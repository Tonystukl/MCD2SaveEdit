using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public static class WorldTests
{
    public static void Run(SaveDocument document, Action<bool, string> check, Action<Action, string> reject)
    {
        check(WorldCatalog.Maps.Length == 2 && WorldCatalog.Quests.Length == 42 && WorldCatalog.Quests.Select(q => q.Id).Distinct().Count() == 42, "World catalogue has Overworld, Sift and 42 distinct published quests");
        check(WorldCatalog.Maps.All(m => WorldCatalog.MapImage(m.Image).Width > 3000 && m.Markers.All(k => WorldCatalog.MapImage(k.Icon).Width > 0)), "Both original map textures and all marker icons are embedded");
        check(WorldCatalog.Quests.All(q => q.Rewards.Concat(q.FirstRewards).All(r => r.Tags.Length == r.Weights.Length && r.Tags.All(t => Catalog.Find(t) is not null))), "Every fixed and regional quest reward resolves to a native gear definition");
        var baseline = document.Root.ToJsonString();
        JsonObject FixtureQuest(QuestDefinition definition) => new()
        {
            ["QuestName"] = definition.Id, ["State"] = "Active",
            ["TaskData"] = new JsonArray((definition.Tasks.Length > 0 ? definition.Tasks : new[] { "FixtureOnly_NotAGameTask" }).Select(t => (JsonNode)new JsonObject { ["TaskName"] = t, ["State"] = "Active", ["PartialProgress"] = 1 }).ToArray())
        };
        // Reward construction is tested for all definitions; missing task layouts use explicitly
        // synthetic fixture IDs here only. The production editor never synthesizes task records.
        foreach (var definition in WorldCatalog.Quests)
        {
            var root = (JsonObject)document.Root.DeepClone(); var character = (JsonObject)root["CharacterSaveV1"]!;
            character["Inventory"]!["Entries"] = new JsonArray();
            character["quest"] = new JsonObject { ["Quests"] = new JsonArray(FixtureQuest(definition)), ["FocusedQuestId"] = definition.Id };
            var result = WorldEditing.Complete(character, definition, true, 45, new Random(23));
            SaveDocument.Validate(root);
            check(character["Inventory"]!["Entries"]!.AsArray().All(e => e!["ItemData"]!["RarityTag"]!.GetValue<string>() != "SW.Rarity.Unique" || Catalog.Find(e["ItemData"]!["TypeTag"]!.GetValue<string>())?.Unique == true), "Unique quest rewards use native unique variants: " + definition.Id);
            check(result.Items.Length == definition.Rewards.Length + definition.FirstRewards.Length && WorldEditing.QuestState(character, definition.Id) == "Completed", "Reward construction and quest-state round-trip: " + definition.Name);
            check(character["quest"]!["Quests"]![0]!["TaskData"]!.AsArray().All(t => t!["State"]!.GetValue<string>() == "Completed"), "All initialized tasks finish: " + definition.Id);
            var before = root.ToJsonString(); reject(() => WorldEditing.Complete(character, definition, true, 45), "Second claim rejected: " + definition.Id);
            check(root.ToJsonString() == before, "Second claim leaves XP, currency and items unchanged: " + definition.Id);
        }
        var target = WorldCatalog.Quest("MEa1_S06_A")!;
        document.Change(root =>
        {
            root["CharacterSaveV1"]!["quest"] = new JsonObject { ["Quests"] = new JsonArray(FixtureQuest(target)), ["FocusedQuestId"] = target.Id };
            root["CharacterSaveV1"]!["Inventory"]!["Entries"] = new JsonArray();
        });
        string prepared = document.Root.ToJsonString();
        document.Change(root => WorldEditing.Complete((JsonObject)root["CharacterSaveV1"]!, target, false, 45, new Random(4)));
        var completed = SaveDocument.Parse(document.Serialize());
        check(WorldEditing.QuestState((JsonObject)completed["CharacterSaveV1"]!, target.Id) == "Completed", "Native quest record and rewards survive JSON serialization");
        document.Undo(); check(document.Root.ToJsonString() == prepared, "One Undo reverses quest completion, XP and all rewards together");
        document.Undo(); check(document.Root.ToJsonString() == baseline, "Quest test setup restores the original copied character");
        var missingRoot = (JsonObject)document.Root.DeepClone(); missingRoot["CharacterSaveV1"]!["quest"] = new JsonObject { ["Quests"] = new JsonArray() };
        reject(() => WorldEditing.Complete((JsonObject)missingRoot["CharacterSaveV1"]!, target, true, 45), "Uninitialized quest is rejected without invented task IDs");
        document.Change(root =>
        {
            root["CharacterSaveV1"]!["quest"] = new JsonObject { ["Quests"] = new JsonArray(FixtureQuest(target)) };
            var emeralds = root["CharacterSaveV1"]!["Ability"]!["Attributes"]!.AsArray().First(a => a!["AttributeName"]!.GetValue<string>() == "Emeralds")!;
            emeralds["CurrentValue"] = int.MaxValue;
        });
        var overflowBefore = document.Root.ToJsonString();
        reject(() => document.Change(root => WorldEditing.Complete((JsonObject)root["CharacterSaveV1"]!, target, true, 45, new Random(4))), "Currency overflow rejects the complete transaction");
        check(document.Root.ToJsonString() == overflowBefore, "Failed claim leaves the complete draft unchanged"); document.Undo();
        var xpRoot = (JsonObject)document.Root.DeepClone(); var xpCharacter = (JsonObject)xpRoot["CharacterSaveV1"]!;
        var attributes = xpCharacter["Ability"]!["Attributes"]!.AsArray();
        attributes.First(a => a!["AttributeName"]!.GetValue<string>() == "Level")!["CurrentValue"] = 45;
        attributes.First(a => a!["AttributeName"]!.GetValue<string>() == "XP")!["CurrentValue"] = WorldEditing.StepXP(45) - 50;
        check(WorldEditing.AddXP(xpCharacter, 60) == 1 && xpCharacter["MetaData"]!["Level"]!.GetValue<int>() == 46 && Math.Abs(attributes.First(a => a!["AttributeName"]!.GetValue<string>() == "XP")!["CurrentValue"]!.GetValue<double>() - 10) < .001,
            "Quest XP uses per-level threshold, carries XP and synchronizes metadata");
        var worldRoot = (JsonObject)document.Root.DeepClone(); var worldCharacter = (JsonObject)worldRoot["CharacterSaveV1"]!;
        var fogs = new JsonArray();
        foreach (var map in WorldCatalog.Maps) fogs.Add(new JsonObject { ["Tag"] = map.Tag, ["Size"] = new JsonObject { ["X"] = 3, ["Y"] = 2 }, ["WorldPosition"] = new JsonObject { ["X"] = 0, ["Y"] = 0 }, ["Data"] = new JsonArray(0, 10, 0, 255, 30, 0) });
        worldCharacter["WorldExploration"]!["SavedFogOfWarExploration"] = new JsonObject { ["Items"] = fogs };
        string questsBefore = worldCharacter["quest"]!.ToJsonString(), siftBefore = fogs[1]!.ToJsonString();
        check(WorldEditing.Reveal(worldCharacter, WorldCatalog.Maps[0].Tag, [0, 4]) == 2 && fogs[0]!["Data"]!.AsArray().Select(n => n!.GetValue<int>()).SequenceEqual(new[] { 255, 10, 0, 255, 255, 0 }), "Selected map reveal changes only the requested fog cells");
        check(fogs[1]!.ToJsonString() == siftBefore && worldCharacter["quest"]!.ToJsonString() == questsBefore, "Map revealing preserves the other region and every quest");
        check(WorldEditing.Reveal(worldCharacter, WorldCatalog.Maps[1].Tag) == 5 && fogs[1]!["Data"]!.AsArray().All(n => n!.GetValue<int>() == 255), "Whole Sift reveal fills the saved region grid");
        reject(() => WorldEditing.Reveal(worldCharacter, WorldCatalog.Maps[0].Tag, [999]), "Out-of-range map reveal is rejected");
        fogs[0]!["Data"]![0] = 256;
        reject(() => WorldEditing.Reveal(worldCharacter, WorldCatalog.Maps[0].Tag), "Malformed fog byte is rejected");
        check(document.Root.ToJsonString() == baseline, "World tests never modify the supplied character fixture");
    }
}
