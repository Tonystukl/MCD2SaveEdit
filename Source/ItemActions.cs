using System.Globalization;
using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed partial class MainForm
{
    void EquipSelected()
    {
        if (!Ready() || selectedItem < 0) return;
        var item = doc!.Items[selectedItem]!; var definition = Catalog.Find(item["ItemData"]!["TypeTag"]!.GetValue<string>());
        string slot = "None";
        if (item["EquippedSlot"]!.GetValue<string>() == "None")
        {
            if (definition is null) throw new InvalidDataException("Use Advanced to edit this item's equipment slot.");
            slot = definition.Slot;
            if (definition.Category is "Artifact" or "Talisman")
            {
                string prefix = "SW.ItemSlot.Equipment." + definition.Category + ".Slot";
                using var picker = new PickerForm("Select equipment slot", Enumerable.Range(1, 3).Select(i => new PickerEntry(definition.Category + " slot " + i, "Equipment", definition.Icon, false, prefix + i)));
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                slot = (string)picker.Selected!.Value;
            }
            if (string.IsNullOrEmpty(slot)) throw new InvalidDataException("No compatible equipment slot is known for this item.");
        }
        doc.Change(root =>
        {
            var entries = (JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!;
            if (slot != "None") foreach (var entry in entries) if (entry?["EquippedSlot"]?.GetValue<string>() == slot) entry["EquippedSlot"] = "None";
            entries[selectedItem]!["EquippedSlot"] = slot; entries[selectedItem]!["ItemData"]!["TargetSlotOverride"] = "None";
        }); RefreshAll();
    }
    void RefreshEffectTiles()
    {
        ClearTiles(effectTiles); if (doc is null || selectedItem < 0) return;
        var item = doc.Items[selectedItem]!; var batches = (JsonArray)item["ItemData"]!["Effects"]!;
        bool hasEnchant = false;
        for (int bi = 0; bi < batches.Count; bi++)
        {
            var batch = batches[bi]!; bool enchant = batch["TypeTag"]?.GetValue<string>() == "SW.Item.Effect.Enchantment";
            hasEnchant |= enchant;
            if (batch["EffectsInThisBatch"] is not JsonArray effects) continue;
            for (int ei = 0; ei < effects.Count; ei++)
            {
                var effect = effects[ei]!; string tag = effect["TypeTag"]?.GetValue<string>() ?? "";
                string template = effect["GeneratorData"]?["GeneratorParentTemplate"]?.GetValue<string>() ?? "";
                string icon = Catalog.Enchantments.FirstOrDefault(x => x.Tag == tag)?.Icon ?? Catalog.Effects.FirstOrDefault(x => x.Effect == tag)?.Icon ?? "";
                var button = new Button { Text = Catalog.EffectName(tag) + "\n" + template.Split('.').Last(), Image = Catalog.Icon(icon), ImageAlign = ContentAlignment.TopCenter,
                    TextAlign = ContentAlignment.BottomCenter, TextImageRelation = TextImageRelation.ImageAboveText, Size = new Size(155, 140), Margin = new Padding(5), FlatStyle = FlatStyle.Flat };
                // Source art may be large; own a small preview for each button.
                if (button.Image is { } image) { button.Image = new Bitmap(image, new Size(72, 72)); button.Disposed += (_, _) => button.Image?.Dispose(); }
                int batchIndex = bi, effectIndex = ei;
                button.Click += (_, _) => Run(() => { if (enchant) EditEnchantment(); else EditSavedEffect(batchIndex, effectIndex); }); effectTiles.Controls.Add(button);
            }
        }
        var def = Catalog.Find(item["ItemData"]!["TypeTag"]!.GetValue<string>());
        if (!hasEnchant && def is not null && Catalog.Enchantments.Any(e => e.Slots.Contains(def.Slot)))
        {
            var add = Btn("Enchant...", EditEnchantment); add.Size = new Size(155, 140); add.AutoSize = false; effectTiles.Controls.Add(add);
        }
        if (def?.Pool.Length > 0) { var roll = Btn("＋ Gear effect", PickGearEffect); effectTiles.Controls.Add(roll); }
    }
    void EditEnchantment()
    {
        if (!Ready() || selectedItem < 0) return;
        var item = doc!.Items[selectedItem]!; var gear = Catalog.Find(item["ItemData"]!["TypeTag"]!.GetValue<string>());
        var current = ((JsonArray)item["ItemData"]!["Effects"]!).FirstOrDefault(x => x?["TypeTag"]?.GetValue<string>() == "SW.Item.Effect.Enchantment")?["EffectsInThisBatch"]?[0];
        var choices = Catalog.Enchantments.Where(e => gear is null || e.Slots.Contains(gear.Slot)).ToArray();
        var selected = choices.FirstOrDefault(e => e.Tag == current?["TypeTag"]?.GetValue<string>()) ?? choices.FirstOrDefault();
        if (selected is null) throw new InvalidDataException("This item has no supported enchantment choices.");
        using var dialog = new Form { Text = "Enchantment", Size = new Size(650, 500), MinimumSize = new Size(600, 480), AutoScaleDimensions = new SizeF(96, 96), AutoScaleMode = AutoScaleMode.Dpi, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var layout = InventoryTable(1, new RowStyle(SizeType.Absolute, 64), new RowStyle(SizeType.Absolute, 48), new RowStyle(SizeType.Absolute, 60), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.Absolute, 72)); layout.Padding = new Padding(16); dialog.Controls.Add(layout);
        var name = new Button { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 17), FlatStyle = FlatStyle.Flat };
        var tierRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        tierRow.Controls.Add(new Label { Text = "Level / tier", Width = 140, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        var tier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 }; tier.Items.AddRange(["I", "II", "III"]); tierRow.Controls.Add(tier);
        var valueRow = new FlowLayoutPanel { Dock = DockStyle.Fill }; valueRow.Controls.Add(new Label { Text = "Strength", Width = 140, Height = 28 });
        var strength = new TextBox { Width = 180 }; valueRow.Controls.Add(strength);
        var note = new Label { Dock = DockStyle.Fill, AutoEllipsis = false };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; var apply = Btn("Apply enchantment", () =>
        {
            if (!double.TryParse(strength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) throw new InvalidDataException("Enter a numeric strength (for example 0.25 for 25%).");
            doc.Change(root => ItemEditing.SetEnchantment((JsonObject)root["CharacterSaveV1"]!["Inventory"]!["Entries"]![selectedItem]!, selected, (string)tier.SelectedItem!, value)); dialog.DialogResult = DialogResult.OK;
        }, true);
        actions.Controls.Add(apply);
        actions.Controls.Add(Btn("Remove enchantment", () =>
        {
            doc.Change(root => { var effects = (JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]![selectedItem]!["ItemData"]!["Effects"]!; foreach (var b in effects.Where(x => x?["TypeTag"]?.GetValue<string>() == "SW.Item.Effect.Enchantment").ToArray()) effects.Remove(b); }); dialog.DialogResult = DialogResult.OK;
        }));
        void RefreshChoice()
        {
            name.Text = selected.Name + "  ▾";
            string level = (string?)tier.SelectedItem ?? "I";
            bool original = current?["TypeTag"]?.GetValue<string>() == selected.Tag && current?["GeneratorData"]?["GeneratorParentTemplate"]?.GetValue<string>() == selected.Tag + "." + level;
            double? value = original ? current?["Intensity"]?.GetValue<double>() : selected.Tiers.First(x => x.Tier == level).Value;
            strength.Text = value?.ToString(CultureInfo.InvariantCulture) ?? "";
            note.Text = value is null ? "This enchantment's tier strength is not verified. Enter a known strength to apply it. Existing effects are preserved until you click Apply."
                : original ? "This strength comes from your saved enchantment. Click the name to choose another enchantment."
                : "Tier strength uses a launch-build preset. You can adjust it above. Enchantment combinations still need an in-game check.";
        }
        name.Click += (_, _) => { using var picker = new PickerForm("Select enchantment", choices.Select(e => new PickerEntry(e.Name, "Enchantment", e.Icon, false, e))); if (picker.ShowDialog(dialog) == DialogResult.OK) { selected = (EnchantmentDefinition)picker.Selected!.Value; RefreshChoice(); } };
        tier.SelectedIndexChanged += (_, _) => RefreshChoice();
        string existingTier = current?["GeneratorData"]?["GeneratorParentTemplate"]?.GetValue<string>().Split('.').Last() ?? "I";
        tier.SelectedItem = tier.Items.Contains(existingTier) ? existingTier : "I";
        layout.Controls.Add(name, 0, 0); layout.Controls.Add(tierRow, 0, 1); layout.Controls.Add(valueRow, 0, 2); layout.Controls.Add(note, 0, 3); layout.Controls.Add(actions, 0, 4); Theme.Apply(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK) RefreshAll();
    }
    void PickGearEffect()
    {
        if (!Ready() || selectedItem < 0) return;
        var definition = Catalog.Find(doc!.Items[selectedItem]!["ItemData"]!["TypeTag"]!.GetValue<string>());
        if (definition is null) return;
        using var picker = new PickerForm("Select gear effect", Catalog.Effects.Where(e => definition.Pool.Contains(e.Tag)).Select(e => new PickerEntry(e.Name + "  " + e.Tier, "Tier " + e.Tier, e.Icon, false, e)));
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected?.Value is not EffectDefinition effect) return;
        doc.Change(root => ((JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]![selectedItem]!["ItemData"]!["Effects"]!).Add(new JsonObject { ["TypeTag"] = "SW.Item.Effect.Rerollable", ["EffectsInThisBatch"] = new JsonArray(effect.Create()) })); RefreshAll();
    }
    void EditSavedEffect(int batchIndex, int effectIndex)
    {
        if (!Ready() || selectedItem < 0) return;
        var effect = doc!.Items[selectedItem]!["ItemData"]!["Effects"]![batchIndex]!["EffectsInThisBatch"]![effectIndex]!;
        string tag = effect["TypeTag"]!.GetValue<string>();
        var choices = Catalog.Effects.Where(e => e.Effect == tag).ToArray();
        if (choices.Length == 0) { EditAdvancedItem(); return; }
        using var picker = new PickerForm("Change effect level", choices.Select(e => new PickerEntry(e.Name + "  " + e.Tier, "Tier " + e.Tier, e.Icon, false, e)));
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected?.Value is not EffectDefinition selected) return;
        doc.Replace(["CharacterSaveV1", "Inventory", "Entries", selectedItem.ToString(), "ItemData", "Effects", batchIndex.ToString(), "EffectsInThisBatch", effectIndex.ToString()], selected.Create()); RefreshAll();
    }
}
