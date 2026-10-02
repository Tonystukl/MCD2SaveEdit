using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed partial class MainForm
{
    readonly ListView equipment = new();
    readonly Label armorSummary = new();
    readonly ComboBox effectItem = new();
    readonly TextBox effectsJson = new();
    readonly DataGridView effectGrid = new();
    readonly Label effectsNote = new();
    bool effectsDirty;
    int currentEffectSelection = -1;
    readonly List<string[]> effectPaths = [];
    readonly List<int> effectIndexes = [];

    void BuildEquipment()
    {
        var page = Page("Equipment");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        armorSummary.Dock = DockStyle.Fill; armorSummary.ForeColor = accent;
        layout.Controls.Add(armorSummary, 0, 0); StyleList(equipment);
        equipment.Columns.Add("Equipment slot", 380); equipment.Columns.Add("Item", 270); equipment.Columns.Add("Power", 90); equipment.Columns.Add("Gear level", 100); equipment.Columns.Add("Effects", 90);
        equipment.DoubleClick += (_, _) => GoToEquipment(); layout.Controls.Add(equipment, 0, 1);
        var actions = Flow(); actions.Controls.Add(Btn("Edit selected piece", GoToEquipment, true));
        actions.Controls.Add(Btn("View all armor", () => { if (!DiscardPending()) return; category.SelectedItem = "Armor"; search.Clear(); tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Inventory"); }));
        actions.Controls.Add(Btn("View talismans", () => { if (!DiscardPending()) return; category.SelectedItem = "Talisman"; search.Clear(); tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Inventory"); }));
        actions.Controls.Add(Btn("View artifacts", () => { if (!DiscardPending()) return; category.SelectedItem = "Artifact"; search.Clear(); tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Inventory"); }));
        layout.Controls.Add(actions, 0, 2); page.Controls.Add(layout);
    }
    void GoToEquipment()
    {
        if (!DiscardPending() || equipment.SelectedItems.Count == 0) return;
        selectedItem = (int)equipment.SelectedItems[0].Tag!; category.SelectedIndex = 0; search.Clear();
        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(x => x.Text == "Inventory"); RefreshAll();
    }
    void RefreshEquipment()
    {
        equipment.Items.Clear(); int armor = 0;
        if (doc is not null) for (int i = 0; i < doc.Items.Count; i++)
        {
            var e = doc.Items[i]!; string slot = e["EquippedSlot"]!.GetValue<string>();
            if (slot == "None") continue;
            if (slot.StartsWith("SW.ItemSlot.Equipment.Armor.", StringComparison.Ordinal)) armor++;
            var d = e["ItemData"]!;
            equipment.Items.Add(new ListViewItem([slot.Replace("SW.ItemSlot.Equipment.", ""), Catalog.Name(d["TypeTag"]!.GetValue<string>()),
                d["GeneratorData"]?["PowerGeneratorValues"]?["ItemPower"]?.ToJsonString() ?? "—", d["ItemProgression"]?["CurrentLevel"]?.ToJsonString() ?? "—", (d["Effects"] as JsonArray)?.Count.ToString() ?? "0"]) { Tag = i });
        }
        armorSummary.Text = $"FOUR-PIECE ARMOR  •  {armor} armor pieces currently equipped\r\nEvery armor slot is stored and edited independently. Empty slots need a real item / slot tag; they are not fabricated.";
    }

    void BuildEffects()
    {
        var page = Page("Enchantments");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        effectItem.Width = 760; effectItem.DropDownStyle = ComboBoxStyle.DropDownList;
        effectItem.SelectedIndexChanged += (_, _) => { if (!loading) { if (!DiscardPending()) { loading = true; effectItem.SelectedIndex = currentEffectSelection; loading = false; return; } LoadEffects(); } }; layout.Controls.Add(effectItem, 0, 0);
        effectsNote.Dock = DockStyle.Fill; effectsNote.ForeColor = muted; layout.Controls.Add(effectsNote, 0, 1);
        var nested = new TabControl { Dock = DockStyle.Fill };
        var fields = new TabPage("Saved effect fields") { BackColor = bg }; var json = new TabPage("Full effects array") { BackColor = bg };
        effectGrid.Dock = DockStyle.Fill; effectGrid.BackgroundColor = panel; effectGrid.BorderStyle = BorderStyle.None; effectGrid.AllowUserToAddRows = false; effectGrid.AllowUserToDeleteRows = false; effectGrid.RowHeadersVisible = false;
        effectGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        effectGrid.DefaultCellStyle.BackColor = panel; effectGrid.DefaultCellStyle.ForeColor = ink;
        effectGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(56, 77, 91); effectGrid.DefaultCellStyle.SelectionForeColor = ink;
        effectGrid.Columns.Add("path", "Effect field"); effectGrid.Columns[0].ReadOnly = true; effectGrid.Columns.Add("value", "Value (JSON: strings in quotes)");
        effectGrid.CellValueChanged += (_, _) => { if (!loading) effectsDirty = true; };
        fields.Controls.Add(effectGrid); StyleText(effectsJson, true); effectsJson.Dock = DockStyle.Fill; effectsJson.TextChanged += (_, _) => { if (!loading) effectsDirty = true; }; json.Controls.Add(effectsJson);
        nested.TabPages.AddRange([fields, json]); layout.Controls.Add(nested, 0, 2);
        nested.Selecting += (_, e) =>
        {
            if (!loading && effectsDirty) { MessageBox.Show(this, "Apply or reload your effects before switching between the field and JSON editors.", "Unapplied effects"); e.Cancel = true; }
        };
        var actions = Flow();
        actions.Controls.Add(Btn("Choose enchantment / level...", () =>
        {
            if (!Ready() || effectItem.SelectedIndex < 0) return;
            ShowItem(effectIndexes[effectItem.SelectedIndex]); EditEnchantment();
        }));
        actions.Controls.Add(Btn("Apply effects", () =>
        {
            if (doc is null || effectItem.SelectedIndex < 0 || !DiscardPending("effects")) return;
            effectGrid.EndEdit();
            int idx = effectIndexes[effectItem.SelectedIndex];
            JsonNode? value;
            if (nested.SelectedTab == json) value = SaveDocument.ParseFragment(effectsJson.Text);
            else
            {
                var wrapper = new JsonObject { ["effects"] = doc.Items[idx]!["ItemData"]!["Effects"]!.DeepClone() };
                for (int n = 0; n < effectPaths.Count; n++) SaveDocument.Set(wrapper, ["effects", .. effectPaths[n]], SaveDocument.ParseFragment(Convert.ToString(effectGrid.Rows[n].Cells[1].Value) ?? "null"));
                value = wrapper["effects"]!.DeepClone();
            }
            if (value is not JsonArray) throw new InvalidDataException("Effects must be an array of real effect records.");
            doc.Replace(["CharacterSaveV1", "Inventory", "Entries", idx.ToString(), "ItemData", "Effects"], value); effectsDirty = false; RefreshAll(); status.Text = "Applied effects. Effect validity still needs in-game verification.";
        }, true));
        actions.Controls.Add(Btn("Import effects…", () =>
        {
            if (doc is null || effectItem.SelectedIndex < 0) return;
            using var file = new OpenFileDialog { Filter = "Effects or exported item|*.json" };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            var value = SaveDocument.ParseFragment(File.ReadAllText(file.FileName));
            if (value is JsonObject obj) value = obj["ItemData"]?["Effects"]?.DeepClone();
            if (value is not JsonArray) throw new InvalidDataException("Import a real effects array or an exported item with ItemData.Effects.");
            if (effectsDirty && MessageBox.Show(this, "Replace the unapplied effects draft with this import?", "Import effects", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            effectsDirty = false; nested.SelectedTab = json; effectsJson.Text = value.ToJsonString(SaveDocument.Pretty);
        }));
        actions.Controls.Add(Btn("Reload effects", LoadEffects)); layout.Controls.Add(actions, 0, 3); page.Controls.Add(layout);
    }
    void RefreshEffects()
    {
        int previous = effectItem.SelectedIndex; effectItem.Items.Clear(); effectIndexes.Clear();
        if (doc is not null) for (int i = 0; i < doc.Items.Count; i++)
        {
            var d = doc.Items[i]!["ItemData"]!; effectIndexes.Add(i);
            effectItem.Items.Add($"{i + 1}. {Catalog.Name(d["TypeTag"]!.GetValue<string>())} — {(d["Effects"] as JsonArray)?.Count ?? 0} saved effects");
        }
        if (effectItem.Items.Count > 0) effectItem.SelectedIndex = Math.Clamp(previous, 0, effectItem.Items.Count - 1);
        LoadEffects();
    }
    void LoadEffects()
    {
        bool old = loading; loading = true;
        try
        {
            currentEffectSelection = effectItem.SelectedIndex; effectPaths.Clear(); effectGrid.Rows.Clear(); effectsJson.Text = "";
            if (doc is null || effectItem.SelectedIndex < 0) return;
            var effects = (JsonArray)doc.Items[effectIndexes[effectItem.SelectedIndex]]!["ItemData"]!["Effects"]!;
            effectsJson.Text = effects.ToJsonString(SaveDocument.Pretty);
            void Walk(JsonNode? n, string[] path)
            {
                if (n is JsonObject o) foreach (var p in o) Walk(p.Value, [.. path, p.Key]);
                else if (n is JsonArray a) for (int i = 0; i < a.Count; i++) Walk(a[i], [.. path, i.ToString()]);
                else { effectPaths.Add(path); effectGrid.Rows.Add(string.Join(" / ", path), n?.ToJsonString() ?? "null"); }
            }
            Walk(effects, []);
            effectsNote.Text = effects.Count == 0 ? "Choose enchantment / level to add a named enchantment, or use the full effects array for advanced editing." : "Edit the saved enchantment tags, levels and other values below. The full array also supports adding / removing real effect records.";
            effectsDirty = false;
        }
        finally { loading = old; }
    }
}
