using System.Drawing.Drawing2D;
using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed partial class MainForm
{
    readonly FlowLayoutPanel itemTiles = new(), armorTiles = new(), utilityTiles = new();
    readonly NumericUpDown emeralds = new(), enchantPoints = new(), heroLevel = new(), echoShards = new();
    readonly Label inventoryCount = new(), powerLabel = new();
    readonly ToolTip tileTips = new();

    void ConfigureTileFlow(FlowLayoutPanel flow) { flow.Dock = DockStyle.Fill; flow.AutoScroll = true; flow.WrapContents = true; flow.Padding = new Padding(3); }
    void ChangeCounter(NumericUpDown control, string name)
    {
        if (loading || doc is null) return;
        Run(() =>
        {
            if (!DiscardPending()) { RefreshClassic(); return; }
            int value = (int)control.Value;
            doc.Change(root =>
            {
                var arr = (JsonArray)root["CharacterSaveV1"]!["Ability"]!["Attributes"]!;
                var attr = arr.FirstOrDefault(x => x?["AttributeName"]?.GetValue<string>() == name) ?? throw new InvalidDataException("Attribute is not present in this save.");
                attr["CurrentValue"] = value;
                if (name == "Level") root["CharacterSaveV1"]!["MetaData"]!["Level"] = value;
            }); RefreshAll();
        });
    }
    void RefreshClassic()
    {
        bool old = loading; loading = true;
        try
        {
            foreach (var (control, name) in new[] { (emeralds, "Emeralds"), (echoShards, "SpringStone"), (enchantPoints, "EnchantmentPoints"), (heroLevel, "Level") })
            {
                var attr = (doc?.Character["Ability"]?["Attributes"] as JsonArray)?.FirstOrDefault(x => x?["AttributeName"]?.GetValue<string>() == name)?["CurrentValue"];
                control.Enabled = attr is not null;
                if (attr is not null && decimal.TryParse(attr.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal v)) control.Value = Math.Clamp(v, control.Minimum, control.Maximum);
            }
            powerLabel.Text = doc is null ? "No character open" : $"EQUIPMENT POWER  {doc.Character["MetaData"]?["PowerLevel"]}\r\n{doc.Character["MetaData"]?["CurrentLocation"]?.GetValue<string>()?.Replace("SW.Area.", "")}";
            tileTips.SetToolTip(powerLabel, "The saved equipment average, separate from character level. Extreme item power does not produce unlimited damage or health. This value can be recalculated by the game.");
            ClearTiles(armorTiles); ClearTiles(utilityTiles);
            if (doc is null) return;
            var armor = new List<int>(); var weapons = new List<int>(); var utilities = new List<int>();
            for (int i = 0; i < doc.Items.Count; i++)
            {
                string slot = doc.Items[i]!["EquippedSlot"]!.GetValue<string>(); if (slot == "None") continue;
                if (slot.Contains(".Armor.")) armor.Add(i);
                else if (slot.Contains("Weapon")) weapons.Add(i);
                else if (slot.Contains(".Artifact.") || slot.Contains(".Talisman.")) utilities.Add(i);
            }
            foreach (int i in weapons) AddTile(armorTiles, i, true);
            foreach (int i in armor) AddTile(armorTiles, i, true);
            for (int i = armor.Count; i < 4; i++) armorTiles.Controls.Add(new GearTile("Armor slot " + (i + 1), "Empty / undiscovered", "Armor", "", "", false) { Width = 121, Height = 115, Enabled = false });
            foreach (int i in utilities) AddTile(utilityTiles, i, true);
            FitTiles(armorTiles); FitTiles(utilityTiles);
        }
        finally { loading = old; }
    }
    void ClearTiles(FlowLayoutPanel flow)
    {
        foreach (Control c in flow.Controls.Cast<Control>().ToArray()) { flow.Controls.Remove(c); c.Dispose(); }
    }
    void AddTile(FlowLayoutPanel parent, int index, bool equipped)
    {
        var e = doc!.Items[index]!; var d = e["ItemData"]!; string tag = d["TypeTag"]!.GetValue<string>();
        string slot = e["EquippedSlot"]!.GetValue<string>(); string subtitleText = equipped ? slot.Replace("SW.ItemSlot.Equipment.", "") : SaveDocument.Category(e);
        var tile = new GearTile(Catalog.Name(tag), subtitleText, SaveDocument.Category(e), tag, d["GeneratorData"]?["PowerGeneratorValues"]?["ItemPower"]?.ToJsonString() ?? "", index == selectedItem)
        { Width = 121, Height = 115, Tag = index, AccessibleName = Catalog.Name(tag) + ", inventory item " + (index + 1) };
        tileTips.SetToolTip(tile, tag + "\n" + slot);
        tile.Click += (_, _) => Run(() => { if (!DiscardPending()) return; ShowItem(index); RefreshTileSelection(); }); parent.Controls.Add(tile);
    }
    void RefreshTiles()
    {
        if (itemTiles.IsDisposed) return;
        itemTiles.SuspendLayout(); ClearTiles(itemTiles);
        if (doc is not null) foreach (ListViewItem row in inventory.Items) AddTile(itemTiles, (int)row.Tag!, false);
        else itemTiles.Controls.Add(new Label { Text = "Open your character\r\n\r\nClick Find Xbox saves above.\r\nYour inventory will appear here.", ForeColor = muted, Size = new Size(300, 160), Padding = new Padding(18), Font = new Font("Tahoma", 13) });
        inventoryCount.Text = doc is null ? "No save open" : $"{inventory.Items.Count} shown / {doc.Items.Count} saved entries";
        FitTiles(itemTiles); itemTiles.ResumeLayout();
    }
    void RefreshTileSelection()
    {
        foreach (var flow in new[] { itemTiles, armorTiles, utilityTiles }) foreach (var tile in flow.Controls.OfType<GearTile>()) { tile.Selected = tile.Tag is int i && i == selectedItem; tile.Invalidate(); }
    }
}

public sealed class GearTile : Button
{
    readonly string title, caption, kind, tag, power;
    public bool Selected;
    public GearTile(string title, string caption, string kind, string tag, string power, bool selected)
    {
        this.title = title; this.caption = caption; this.kind = kind; this.tag = tag; this.power = power; Selected = selected;
        Margin = new Padding(4); FlatStyle = FlatStyle.Flat; BackColor = Color.FromArgb(245, 244, 247); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        bool unique = Catalog.Find(tag)?.Unique == true;
        using var fill = new LinearGradientBrush(bounds, unique ? (Theme.Dark ? Color.FromArgb(100, 59, 30) : Color.FromArgb(255, 191, 132)) : Theme.Surface, Theme.Surface, 90F); g.FillRectangle(fill, bounds);
        using var border = new Pen(Selected ? Theme.Accent : unique ? Color.FromArgb(241, 135, 49) : Theme.Border, Selected || unique ? 2 : 1); g.DrawRectangle(border, bounds);
        float scale = DeviceDpi / 96F;
        using var label = new Font(Font.FontFamily, Math.Max(9.5F, Font.SizeInPoints));
        int inset = (int)(8 * scale);
        int labelHeight = Math.Max((int)(64 * scale), (int)Math.Ceiling(label.GetHeight(g) * 3) + inset);
        int size = Math.Min(Width - 2 * inset, Height - labelHeight - inset);
        if (Catalog.ItemIcon(tag) is { } icon) g.DrawImage(icon, new Rectangle((Width - size) / 2, inset, size, size));
        else { using var placeholder = new Font(Font.FontFamily, 22); TextRenderer.DrawText(g, string.IsNullOrEmpty(tag) ? "+" : "◆", placeholder, new Rectangle(4, 9, Width - 8, size - 8), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
        TextRenderer.DrawText(g, title, label, new Rectangle(inset, Height - labelHeight, Width - 2 * inset, labelHeight - inset), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(power) && power != "-1")
        {
            using var font = new Font(Font.FontFamily, Math.Max(10, Font.SizeInPoints), FontStyle.Bold); var measured = TextRenderer.MeasureText(power, font); var rect = new Rectangle(Width - measured.Width - inset, inset, measured.Width, measured.Height);
            using var backing = new SolidBrush(Theme.Surface); g.FillRectangle(backing, rect); TextRenderer.DrawText(g, power, font, rect, Theme.Text);
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -3, -3));
    }
}
