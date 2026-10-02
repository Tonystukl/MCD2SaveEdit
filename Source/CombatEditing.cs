using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public static class CombatEditing
{
    public const string Sharpness = "SW.Effect.Sharpness";
    public const string Protection = "SW.Effect.Protection";
    const string RolledBatch = "SW.Item.Effect.Rerollable";

    static EffectDefinition Template(JsonNode item, string effect)
    {
        if (effect is not (Sharpness or Protection)) throw new InvalidDataException("Unsupported combat bonus.");
        var gear = Catalog.Find(item["ItemData"]!["TypeTag"]!.GetValue<string>());
        return Catalog.Effects.FirstOrDefault(e => e.Effect == effect && e.Tier == "III" && gear?.Pool.Contains(e.Tag) == true)
            ?? throw new InvalidDataException("This gear cannot roll that combat effect.");
    }
    public static bool Supports(JsonNode item, string effect)
    {
        try { Template(item, effect); return true; } catch (InvalidDataException) { return false; }
    }
    public static double Percent(JsonNode item, string effect)
    {
        var batches = (JsonArray)item["ItemData"]!["Effects"]!;
        double total = batches.Where(b => b?["TypeTag"]?.GetValue<string>() == RolledBatch)
            .SelectMany(b => (JsonArray)b!["EffectsInThisBatch"]!)
            .Where(e => e?["TypeTag"]?.GetValue<string>() == effect)
            .Sum(e => e!["Intensity"]!.GetValue<double>());
        return total * (effect == Protection ? -100 : 100);
    }
    public static void SetPercent(JsonObject item, string effect, double percent)
    {
        var template = Template(item, effect);
        if (!double.IsFinite(percent) || percent < 0 || percent > (effect == Protection ? 80 : 10000))
            throw new InvalidDataException(effect == Protection ? "Protection strength must be between 0 and 80%." : "Sharpness bonus must be between 0 and 10,000%.");
        var batches = (JsonArray)item["ItemData"]!["Effects"]!;
        // Replace only this rolled effect. Fixed traits, enchantments and other rolls survive.
        foreach (var batch in batches.Where(b => b?["TypeTag"]?.GetValue<string>() == RolledBatch).ToArray())
        {
            var effects = (JsonArray)batch!["EffectsInThisBatch"]!;
            foreach (var record in effects.Where(e => e?["TypeTag"]?.GetValue<string>() == effect).ToArray()) effects.Remove(record);
            if (effects.Count == 0) batches.Remove(batch);
        }
        if (percent == 0) return;
        var created = template.Create(); created["Intensity"] = percent / (effect == Protection ? -100 : 100);
        batches.Add(new JsonObject { ["TypeTag"] = RolledBatch, ["EffectsInThisBatch"] = new JsonArray(created) });
    }
}
