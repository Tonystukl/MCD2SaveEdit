using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed record QuestCompletion(int XP, int Emeralds, int Levels, string[] Items, string[] Opened);
public static class WorldEditing
{
    public static double Number(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != System.Text.Json.JsonValueKind.Number)
            throw new InvalidDataException("Expected a numeric save value.");
        return double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
    }
    public static JsonObject? FindQuest(JsonObject character, string id) =>
        (character["quest"]?["Quests"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(q => q["QuestName"]?.GetValue<string>() == id);
    public static string QuestState(JsonObject? character, string id) => character is null ? "No save" : FindQuest(character, id)?["State"]?.GetValue<string>() ?? "Not initialized";
    public static JsonObject? Fog(JsonObject? character, string tag) =>
        (character?["WorldExploration"]?["SavedFogOfWarExploration"]?["Items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(f => f["Tag"]?.GetValue<string>() == tag);
    public static void ValidateFog(JsonObject fog)
    {
        int width = fog["Size"]?["X"]?.GetValue<int>() ?? 0, height = fog["Size"]?["Y"]?.GetValue<int>() ?? 0;
        if (width is <= 0 or > 512 || height is <= 0 or > 512 || fog["Data"] is not JsonArray data || data.Count != width * height)
            throw new InvalidDataException("The saved fog grid has invalid dimensions. No map changes were applied.");
        foreach (var n in data)
            if (n is not JsonValue v || !v.TryGetValue<int>(out int value) || value is < 0 or > 255)
                throw new InvalidDataException("The fog grid must contain byte values from 0 to 255.");
        if (fog["WorldPosition"]?["X"] is null || fog["WorldPosition"]?["Y"] is null) throw new InvalidDataException("Fog origin is missing.");
    }
    public static int Reveal(JsonObject character, string tag, IEnumerable<int>? cells = null)
    {
        var fog = Fog(character, tag) ?? throw new InvalidDataException("This region has no fog record in this save. Visit it in the game first.");
        ValidateFog(fog);
        var data = (JsonArray)fog["Data"]!;
        var indices = (cells ?? Enumerable.Range(0, data.Count)).Distinct().ToArray();
        if (indices.Any(i => i < 0 || i >= data.Count)) throw new InvalidDataException("Map section is outside the saved grid.");
        int changed = 0;
        foreach (int i in indices) if (data[i]!.GetValue<int>() != 255) { data[i] = 255; changed++; }
        return changed;
    }
    static JsonObject Attribute(JsonObject character, string name) =>
        (character["Ability"]?["Attributes"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(a => a["AttributeName"]?.GetValue<string>() == name)
        ?? throw new InvalidDataException("The save is missing the " + name + " attribute.");
    public static double StepXP(int level)
    {
        double u = .05 * (level - 1); return 2000 * (1 + 2 * u / (1 + u));
    }
    public static int AddXP(JsonObject character, int reward)
    {
        var levelAttribute = Attribute(character, "Level"); var xpAttribute = Attribute(character, "XP");
        double levelValue = Number(levelAttribute["CurrentValue"]), xp = Number(xpAttribute["CurrentValue"]);
        if (!double.IsFinite(levelValue) || levelValue < 1 || levelValue > int.MaxValue || Math.Truncate(levelValue) != levelValue || !double.IsFinite(xp) || xp < 0)
            throw new InvalidDataException("Character level or XP is invalid.");
        int level = (int)levelValue, original = level;
        xp += reward;
        while (level < 100 && xp >= StepXP(level)) { xp -= StepXP(level); level++; }
        if (level != original)
        {
            levelAttribute["CurrentValue"] = level; character["MetaData"]!["Level"] = level;
            var points = Attribute(character, "EnchantmentPoints");
            double current = Number(points["CurrentValue"]);
            points["CurrentValue"] = Math.Max(current, Math.Min(99, current + level - original));
        }
        xpAttribute["CurrentValue"] = xp;
        return level - original;
    }
    static JsonObject RewardItem(QuestReward reward, int power, int heroLevel, Random random)
    {
        if (reward.Tags.Length == 0 || reward.Tags.Length != reward.Weights.Length || reward.Weights.Any(w => !double.IsFinite(w) || w <= 0))
            throw new InvalidDataException("Invalid quest reward pool.");
        GearDefinition[] Resolve(string tag)
        {
            var baseGear = Catalog.Find(tag) ?? throw new InvalidDataException("Unknown quest reward item.");
            return reward.Rarity == "Unique" && !baseGear.Unique
                ? Catalog.Gear.Where(g => g.Unique && g.Tag.StartsWith(tag + "_Unique", StringComparison.Ordinal)).ToArray()
                : [baseGear];
        }
        var candidates = reward.Tags.Select((tag, i) => (Gear: Resolve(tag), Weight: reward.Weights[i])).Where(p => p.Gear.Length > 0).ToArray();
        if (candidates.Length == 0) throw new InvalidDataException("No verified unique variants are available for this reward pool.");
        double roll = random.NextDouble() * candidates.Sum(p => p.Weight); int chosen = candidates.Length - 1;
        for (int i = 0; i < candidates.Length; i++) { roll -= candidates[i].Weight; if (roll < 0) { chosen = i; break; } }
        var variants = candidates[chosen].Gear; var gear = variants[random.Next(variants.Length)];
        var item = gear.Create(power, heroLevel); var data = item["ItemData"]!;
        string rarity = gear.Unique ? "Unique" : gear.Category == "Talisman" ? "None" : string.IsNullOrEmpty(reward.Rarity) ? "Common" : reward.Rarity;
        if (!new[] { "Common", "Rare", "Special", "Unique", "None" }.Contains(rarity)) throw new InvalidDataException("Unknown reward rarity.");
        data["RarityTag"] = "SW.Rarity." + rarity;
        int rolls = rarity == "Special" ? 2 : rarity is "Rare" or "Unique" ? 1 : 0;
        var effects = Catalog.Effects.Where(e => gear.Pool.Contains(e.Tag)).GroupBy(e => e.Effect).Select(g => g.ToArray()).ToList();
        if (rolls > effects.Count && gear.Category != "Talisman") throw new InvalidDataException("The reward's effect pool is incomplete.");
        var batch = new JsonArray();
        for (int i = 0; i < rolls && effects.Count > 0; i++)
        {
            int index = random.Next(effects.Count); var choices = effects[index]; batch.Add(choices[random.Next(choices.Length)].Create()); effects.RemoveAt(index);
        }
        if (batch.Count > 0) data["Effects"]!.AsArray().Add(new JsonObject { ["TypeTag"] = "SW.Item.Effect.Rerollable", ["EffectsInThisBatch"] = batch });
        return item;
    }
    public static QuestCompletion Complete(JsonObject character, QuestDefinition definition, bool firstCompletion, int rewardPower, Random? random = null)
    {
        if (rewardPower is < 1 or > 100) throw new InvalidDataException("Quest reward power must be between 1 and 100.");
        var quest = FindQuest(character, definition.Id) ?? throw new InvalidDataException("Visit this quest in the game first. Its native task record is not initialized in this save.");
        string state = quest["State"]?.GetValue<string>() ?? "";
        if (state == "Completed") throw new InvalidDataException("This quest is already completed. Its rewards cannot be claimed again here.");
        if (state is not ("Available" or "Active")) throw new InvalidDataException("Finish this quest's prerequisites in the game first.");
        if (quest["TaskData"] is not JsonArray tasks || tasks.Count == 0 || tasks.Any(t => t?["TaskName"] is null || t["State"] is null))
            throw new InvalidDataException("This quest's native task layout is missing or incomplete.");
        random ??= Random.Shared;
        bool first = !definition.Repeatable || firstCompletion;
        int xp = first ? definition.XPFirst : definition.XPRepeat;
        int drops = definition.EmeraldBase;
        for (int i = 0; i < definition.EmeraldExtra; i++) if (random.NextDouble() < definition.EmeraldChance) drops++;
        int emeraldReward = 0;
        for (int i = 0; i < drops; i++) emeraldReward += random.NextDouble() < definition.BlockChance ? 9 : 1;
        var emerald = Attribute(character, "Emeralds"); double current = Number(emerald["CurrentValue"]);
        if (!double.IsFinite(current) || current < 0 || current + emeraldReward > int.MaxValue) throw new InvalidDataException("Emerald reward would overflow this save's currency range.");
        int level = checked((int)Number(Attribute(character, "Level")["CurrentValue"]));
        var groups = (first ? definition.FirstRewards : []).Concat(definition.Rewards);
        var rewards = groups.Select(g => RewardItem(g, rewardPower, level, random)).ToArray();
        var entries = character["Inventory"]!["Entries"]!.AsArray();
        if (entries.Count + rewards.Length > 300) throw new InvalidDataException("Make room in your inventory before claiming the quest's gear rewards (300 entries maximum).");
        int levels = AddXP(character, xp);
        emerald["CurrentValue"] = current + emeraldReward;
        var discovered = character["LootProgression"]?["DiscoveredLoot"] as JsonArray;
        foreach (var item in rewards)
        {
            entries.Add(item);
            string tag = item["ItemData"]!["TypeTag"]!.GetValue<string>();
            if (discovered is not null && !discovered.Any(n => n?.GetValue<string>() == tag)) discovered.Add(tag);
        }
        quest["State"] = "Completed";
        foreach (var task in tasks) task!["State"] = "Completed";
        var opened = new List<string>();
        foreach (string id in definition.Unlocks)
        {
            var next = FindQuest(character, id); var nextDefinition = WorldCatalog.Quest(id);
            if (next?["State"]?.GetValue<string>() == "Unavailable" && nextDefinition is not null &&
                nextDefinition.Prerequisites.All(p => QuestState(character, p) == "Completed"))
            { next["State"] = "Available"; opened.Add(nextDefinition.Name); }
        }
        if (character["quest"]?["FocusedQuestId"]?.GetValue<string>() == definition.Id)
            character["quest"]!["FocusedQuestId"] = character["quest"]!["Quests"]!.AsArray().FirstOrDefault(q => q?["State"]?.GetValue<string>() == "Active")?["QuestName"]?.GetValue<string>() ?? "";
        return new(xp, emeraldReward, levels, rewards.Select(r => Catalog.Name(r["ItemData"]!["TypeTag"]!.GetValue<string>())).ToArray(), opened.ToArray());
    }
}
