using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public static class SelfTest
{
    public static int Run(string[] args)
    {
        string logPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "test-results.txt");
        var log = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); log.Add("PASS " + name); }
        void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
        try
        {
            if (args.Length == 0) throw new ArgumentException("Supply a COPIED character save path and optional test log path.");
            // The test runner never uses live WGS. Its caller must provide a workspace fixture.
            string fixture = Path.GetFullPath(args[0]);
            if (fixture.StartsWith(Path.GetFullPath(SaveFiles.DefaultWgs), StringComparison.OrdinalIgnoreCase)) throw new Exception("Tests refuse live game saves.");
            var d = new SaveDocument(fixture);
            WorldTests.Run(d, Check, Reject);
            Check(Catalog.Gear.Length == 296 && Catalog.Gear.Select(x => x.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 296, "Catalogue contains 296 distinct game item IDs");
            Check(Catalog.Gear.Count(g => g.Category is "Melee" or "Ranged") == 80, "All 80 catalogue weapons present");
            Check(Catalog.Gear.Count(g => g.Category is "Helmet" or "Chest" or "Leggings" or "Boots") == 152, "All 152 catalogue armor pieces present");
            Check(Catalog.Gear.Count(g => g.Category == "Artifact") == 40 && Catalog.Gear.Count(g => g.Category == "Talisman") == 24, "All 40 artifacts and 24 talismans present");
            Check(Catalog.Gear.All(g => Catalog.ItemIcon(g.Tag) is not null), "Every catalogue item has an embedded icon");
            Check(Catalog.Name("SW.Item.Hammer") != "Hammer", "Internal weapon ID resolves to the displayed game name");
            var catalogueRoot = (JsonObject)d.Root.DeepClone();
            catalogueRoot["CharacterSaveV1"]!["Inventory"]!["Entries"] = new JsonArray(Catalog.Gear.Select(g => (JsonNode)g.Create(30, 10)).ToArray());
            SaveDocument.Validate(catalogueRoot);
            Check(SaveDocument.Parse(System.Text.Encoding.UTF8.GetBytes(catalogueRoot.ToJsonString()))["CharacterSaveV1"]!["Inventory"]!["Entries"]!.AsArray().Count == 296, "Every catalogue item builds and round-trips through the save format");
            Check(Catalog.Gear.Where(g => g.Category == "Talisman").All(g => g.Levels.Count == 3 && g.Effects.Count > 0), "All talismans retain their three tiers and initial effects");
            var sword = Catalog.Find("SW.Item.Sword")!.Create(10, 10);
            var combatItem = Catalog.Find("SW.Item.Dagger_Unique1")!.Create(1000, 45);
            var fixedCombat = combatItem["ItemData"]!["Effects"]!.DeepClone();
            CombatEditing.SetPercent(combatItem, CombatEditing.Sharpness, 900);
            CombatEditing.SetPercent(combatItem, CombatEditing.Sharpness, 900);
            var combatBatches = combatItem["ItemData"]!["Effects"]!.AsArray();
            Check(combatBatches.Count == fixedCombat.AsArray().Count + 1 && CombatEditing.Percent(combatItem, CombatEditing.Sharpness) == 900,
                "Repeated direct damage edits replace one Sharpness roll instead of stacking duplicates");
            Check(combatBatches.Take(fixedCombat.AsArray().Count).Select((b, i) => JsonNode.DeepEquals(b, fixedCombat[i])).All(x => x),
                "Direct damage editing preserves Sculker's Bane fixed soul trait");
            Check(combatBatches.Last()!["EffectsInThisBatch"]![0]!["GeneratorData"]!["GeneratorParentTemplate"]!.GetValue<string>() == "SW.EffectTemplate.Sharpness.III",
                "Direct bonus uses a known game effect and tier template");
            CombatEditing.SetPercent(combatItem, CombatEditing.Sharpness, 0);
            Check(JsonNode.DeepEquals(combatItem["ItemData"]!["Effects"], fixedCombat), "Removing direct damage bonus restores original fixed batches");
            var boots = Catalog.Find("SW.Item.CaveCrawlerBoots_Unique")!.Create(100, 45);
            CombatEditing.SetPercent(boots, CombatEditing.Protection, 20);
            Check(CombatEditing.Percent(boots, CombatEditing.Protection) == 20 && boots["ItemData"]!["Effects"]!.AsArray().Last()!["EffectsInThisBatch"]![0]!["Intensity"]!.GetValue<double>() == -0.2,
                "Protection UI percentage retains the native negative effect strength");
            Reject(() => CombatEditing.SetPercent(combatItem, CombatEditing.Protection, 20), "Incompatible direct combat bonus rejected");
            Reject(() => CombatEditing.SetPercent(combatItem, CombatEditing.Sharpness, double.NaN), "Nonfinite direct damage bonus rejected");
            var fire = Catalog.Enchantments.First(x => x.Tag == "SW.Enchantment.FireAspect");
            ItemEditing.SetEnchantment(sword, fire, "II", fire.Tiers.First(t => t.Tier == "II").Value!.Value);
            Check(sword["ItemData"]!["Effects"]![0]!["EffectsInThisBatch"]![0]!["GeneratorData"]!["GeneratorParentTemplate"]!.GetValue<string>() == "SW.Enchantment.FireAspect.II", "Enchantment picker writes the chosen tier template");
            var poison = Catalog.Enchantments.First(x => x.Tag == "SW.Enchantment.PoisonFog");
            ItemEditing.SetEnchantment(sword, poison, "III", 0.3);
            Check(sword["ItemData"]!["Effects"]!.AsArray().Count == 1 && sword["ItemData"]!["Effects"]![0]!["EffectsInThisBatch"]![0]!["TypeTag"]!.GetValue<string>() == poison.Tag, "Changing enchantment replaces its batch without stacking another enchantment");
            var fixedEffects = new JsonObject { ["TypeTag"] = "SW.Item.Effect.Static", ["EffectsInThisBatch"] = new JsonArray(Catalog.Effects.First().Create()) };
            sword["ItemData"]!["Effects"]!.AsArray().Add(fixedEffects.DeepClone());
            ItemEditing.SetEnchantment(sword, fire, "III", 0.5);
            Check(sword["ItemData"]!["Effects"]!.AsArray().Any(e => JsonNode.DeepEquals(e, fixedEffects)), "Changing enchantment preserves fixed gear effects");
            var invalidEffectsRoot = (JsonObject)catalogueRoot.DeepClone();
            var invalidEffectItem = sword.DeepClone();
            invalidEffectItem["ItemData"]!["Effects"]![0]!["EffectsInThisBatch"]![0]!["Intensity"] = null;
            invalidEffectsRoot["CharacterSaveV1"]!["Inventory"]!["Entries"] = new JsonArray(invalidEffectItem);
            Reject(() => SaveDocument.Validate(invalidEffectsRoot), "Null enchantment strength rejected before writing a save");
            Check(d.Items.Count > 0, "Real character fixture opens");
            Check(d.Serialize().SequenceEqual(File.ReadAllBytes(fixture)), "Untouched export is byte-for-byte identical");
            long timestamp = d.Character["MetaData"]!["GameDataUpdated"]!.GetValue<long>();
            var raw = d.Root.ToJsonString();
            d.Replace(["CharacterSaveV1", "Inventory", "Entries", "0", "ItemData", "GeneratorData", "PowerGeneratorValues", "ItemPower"], JsonValue.Create(23));
            Check(d.Dirty && d.Items[0]!["ItemData"]!["GeneratorData"]!["PowerGeneratorValues"]!["ItemPower"]!.GetValue<int>() == 23, "Gear power changes");
            Check(SaveDocument.Parse(d.Serialize())["CharacterSaveV1"]!["MetaData"]!["GameDataUpdated"]!.GetValue<long>() == timestamp, "Exact 64-bit metadata survives an edit");
            d.Undo(); Check(d.Root.ToJsonString() == raw && !d.Dirty, "Undo restores all original data");
            d.Redo(); Check(d.Dirty, "Redo restores edit"); d.Undo();
            Reject(() => d.Replace(["SerializeMeta", "SoftVersion"], JsonValue.Create(999)), "Unknown format / metadata edits rejected");
            Reject(() => d.Replace(["CharacterSaveV1", "MetaData", "CharacterId"], JsonValue.Create(Guid.NewGuid().ToString())), "Character identity protected");
            Reject(() => d.Replace(["CharacterSaveV1", "Inventory", "Entries", "0", "StackCount"], JsonValue.Create(-1)), "Invalid stack rejected atomically");
            Check(d.Root.ToJsonString() == raw, "Rejected changes leave no partial edits");
            Reject(() => SaveDocument.ParseFragment("{\"a\":1,\"a\":2}"), "Duplicate JSON keys rejected");
            Reject(() => SaveDocument.Parse([0, 1, 2, 3]), "Non-character binary files rejected");
            d.Change(root =>
            {
                var arr = (JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!;
                foreach (var e in arr) e!["EquippedSlot"] = "None";
                // Synthetic slot suffixes intentionally avoid claiming a verified game catalogue.
                for (int i = 0; i < 4; i++) { var e = arr[0]!.DeepClone(); e["EquippedSlot"] = "SW.ItemSlot.Equipment.Armor.TestSlot" + i; e["ItemData"]!["ItemProgression"]!["CurrentLevel"] = i + 1; arr.Add(e); }
            });
            Check(d.Items.Where(e => e!["EquippedSlot"]!.GetValue<string>().Contains("Armor.TestSlot")).Count() == 4, "Four independent armor slots round-trip without merging");
            Reject(() => d.Change(root => ((JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!).Add(d.Items[^1]!.DeepClone())), "Duplicate equipped slot rejected"); d.Undo();
            d.Replace(["CharacterSaveV1", "Inventory", "Entries", "0", "ItemData", "Effects"], JsonNode.Parse("[{\"SyntheticEffect\":\"test-only\",\"Level\":3,\"UnknownField\":1234567890123456789}]"));
            Check(SaveDocument.Parse(d.Serialize())["CharacterSaveV1"]!["Inventory"]!["Entries"]![0]!["ItemData"]!["Effects"]![0]!["UnknownField"]!.GetValue<long>() == 1234567890123456789, "Effect records, level and unknown fields round-trip (synthetic)"); d.Undo();
            d.Replace(["CharacterSaveV1", "Ability", "Attributes", "4", "CurrentValue"], JsonValue.Create(1000));
            d.Replace(["CharacterSaveV1", "WorldExploration", "LastMinecartStation"], JsonValue.Create("None"));
            Check(SaveDocument.Parse(d.Serialize())["CharacterSaveV1"]!["Ability"]!["Attributes"]![4]!["CurrentValue"]!.GetValue<int>() == 1000, "Currency edit survives serialization"); d.Undo();
            Check(d.Wgs is not null, "Version-14 Xbox container linked to active character blob");
            string testRoot = Path.Combine(Path.GetDirectoryName(logPath)!, "transaction-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            foreach (var f in Directory.GetFiles(d.Wgs!.AccountFolder, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(testRoot, Path.GetRelativePath(d.Wgs.AccountFolder, f)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(f, dest);
            }
            string testBlob = Path.Combine(testRoot, Path.GetRelativePath(d.Wgs.AccountFolder, fixture));
            var test = new SaveDocument(testBlob); var originals = Directory.GetFiles(testRoot, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
            test.Replace(["CharacterSaveV1", "Inventory", "Entries", "0", "ItemData", "ItemProgression", "CurrentLevel"], JsonValue.Create(7));
            string backup = SaveFiles.Save(test, false);
            string previousBlob = testBlob; testBlob = test.FilePath;
            var reopened = new SaveDocument(testBlob);
            Check(reopened.Items[0]!["ItemData"]!["ItemProgression"]!["CurrentLevel"]!.GetValue<int>() == 7, "Copied Xbox save: edit, backup, write, reopen");
            Check(reopened.Character["MetaData"]!["GameDataUpdated"]!.GetValue<long>() > timestamp, "Game write advances the character revision timestamp");
            Check(reopened.Wgs!.OriginalContainer[136..152].All(b => b == 0), "Pending local edit clears the old cloud blob identity");
            Check(!testBlob.Equals(previousBlob, StringComparison.OrdinalIgnoreCase) && File.ReadAllBytes(previousBlob).SequenceEqual(originals[previousBlob]), "Game write publishes a fresh blob and preserves the prior revision");
            Check(WgsContext.ActiveCharacters(testRoot).Single() == testBlob, "Xbox index publishes the new active character revision");
            Check(!test.Dirty, "Successful save clears dirty state");
            foreach (var pair in originals)
            {
                string relative = Path.GetRelativePath(testRoot, pair.Key);
                Check(File.ReadAllBytes(Path.Combine(backup, "original", relative)).SequenceEqual(pair.Value), "Verified original backup: " + Path.GetFileName(pair.Key));
                if (pair.Key != testBlob && pair.Key != test.Wgs!.IndexPath && pair.Key != test.Wgs.ContainerPath) Check(File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value), "Unrelated container untouched: " + Path.GetFileName(pair.Key));
            }
            File.AppendAllText(testBlob, " ");
            Reject(() => SaveFiles.Save(test, false), "Concurrent blob change blocks overwrite");
            File.WriteAllBytes(testBlob, test.OriginalBytes);
            File.AppendAllText(test.Wgs!.IndexPath, " ");
            Reject(() => SaveFiles.Save(test, false), "Concurrent Xbox index change blocks overwrite");
            File.WriteAllBytes(test.Wgs.IndexPath, test.Wgs.OriginalIndex);
            test.Replace(["CharacterSaveV1", "MetaData", "Level"], JsonValue.Create(12));
            File.WriteAllBytes(test.Wgs.IndexPath, test.Wgs.UpdatedIndex(test.OriginalBytes.Length));
            var updated = SaveFiles.SaveToGame(test, checkGame: false, root: testRoot);
            Check(updated.Document.Character["MetaData"]!["Level"]!.GetValue<int>() == 12, "Metadata-only Xbox index refresh no longer blocks saving");
            test = updated.Document;
            byte[] tableBytes = File.ReadAllBytes(test.Wgs!.ContainerPath); var rotatedId = Guid.NewGuid(); rotatedId.ToByteArray().CopyTo(tableBytes, 152);
            string rotatedBlob = Path.Combine(Path.GetDirectoryName(test.FilePath)!, rotatedId.ToString("N").ToUpperInvariant());
            File.WriteAllBytes(rotatedBlob, test.OriginalBytes); File.WriteAllBytes(test.Wgs.ContainerPath, tableBytes); File.Delete(test.FilePath);
            test.Replace(["CharacterSaveV1", "MetaData", "Level"], JsonValue.Create(13));
            updated = SaveFiles.SaveToGame(test, checkGame: false, root: testRoot);
            Check(updated.Document.FilePath != rotatedBlob && WgsContext.ActiveCharacters(testRoot).Single() == updated.Document.FilePath, "Saving resolves and publishes from the rotated active Xbox blob");
            test = updated.Document;
            var concurrent = new SaveDocument(test.FilePath); concurrent.Replace(["CharacterSaveV1", "MetaData", "Level"], JsonValue.Create(14)); SaveFiles.Save(concurrent, false);
            Reject(() => SaveFiles.SaveToGame(test, checkGame: false, root: testRoot), "Newer character progress requires explicit replacement");
            Check(new SaveDocument(concurrent.FilePath).Character["MetaData"]!["Level"]!.GetValue<int>() == 14, "Conflict leaves newer progress intact");
            string exportedFixture = Path.Combine(Path.GetDirectoryName(logPath)!, "import-fixture.json"); File.WriteAllBytes(exportedFixture, test.Serialize());
            var imported = SaveFiles.SaveToGame(new SaveDocument(exportedFixture), checkGame: false, root: testRoot);
            Check(imported.Document.Character["MetaData"]!["Level"]!.GetValue<int>() == 13, "Unmodified exported JSON can be imported to its matching character with backup");
            var talismanIndex = d.Items.Select((v, i) => (v, i)).Where(x => x.v?["ItemData"]?["TypeTag"]?.GetValue<string>().Contains(".Talisman.") == true).Select(x => x.i).DefaultIfEmpty(-1).First();
            if (talismanIndex >= 0)
            {
                var talisman = d.Items[talismanIndex]!["ItemData"]!;
                var realEffects = talisman["Effects"]!.ToJsonString();
                var realLevels = talisman["ItemProgression"]!["ItemLevels"]!.ToJsonString();
                d.Replace(["CharacterSaveV1", "Inventory", "Entries", talismanIndex.ToString(), "ItemData", "ItemProgression", "CurrentXP"], JsonValue.Create(500));
                var roundtrip = SaveDocument.Parse(d.Serialize())["CharacterSaveV1"]!["Inventory"]!["Entries"]![talismanIndex]!["ItemData"]!;
                Check(roundtrip["Effects"]!.ToJsonString() == realEffects && roundtrip["ItemProgression"]!["ItemLevels"]!.ToJsonString() == realLevels, "Real talisman XP edit preserves all nested effects and three tier definitions");
                d.Undo();
            }
            ApplicationConfiguration.Initialize();
            using (var form = new MainForm(null)) form.CheckControls(fixture, Check);
            log.Add("All checks passed. No live game files were changed. In-game loading / cloud synchronization are not tested.");
            File.WriteAllLines(logPath, log); return 0;
        }
        catch (Exception ex) { log.Add("FAIL " + ex); File.WriteAllLines(logPath, log); return 1; }
    }
}
