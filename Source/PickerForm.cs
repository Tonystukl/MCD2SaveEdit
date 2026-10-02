using System.Drawing.Drawing2D;

namespace MCD2SaveEdit;

public sealed record PickerEntry(string Name, string Group, string Icon, bool Unique, object Value);

public sealed class PickerForm : Form
{
    readonly PickerEntry[] entries;
    readonly TextBox search = new();
    readonly ComboBox group = new();
    readonly ListBox list = new();
    readonly Label count = new();
    readonly Button choose = new();
    public PickerEntry? Selected => list.SelectedItem as PickerEntry;
    internal int VisibleCount => list.Items.Count;
    internal void Filter(string text, string category = "All") { search.Text = text; group.SelectedItem = category; RefreshList(); }
    public PickerForm(string title, IEnumerable<PickerEntry> items, string initialGroup = "All")
    {
        entries = items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        Text = title; Font = new Font("Tahoma", 12); Icon = AppBrand.Icon; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(600, 850); MinimumSize = new Size(440, 480); StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; MinimizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); Controls.Add(root);
        search.Dock = DockStyle.Fill; search.PlaceholderText = "Search by item name…"; search.AccessibleName = "Search catalogue";
        group.Dock = DockStyle.Fill; group.DropDownStyle = ComboBoxStyle.DropDownList;
        group.Items.Add("All"); group.Items.AddRange(entries.Select(x => x.Group).Distinct().Order().Cast<object>().ToArray());
        group.SelectedItem = group.Items.Contains(initialGroup) ? initialGroup : "All";
        list.Dock = DockStyle.Fill; list.IntegralHeight = false; list.BorderStyle = BorderStyle.None;
        list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 96; list.DisplayMember = "Name";
        list.AccessibleName = title; list.DrawItem += DrawEntry;
        search.TextChanged += (_, _) => RefreshList(); group.SelectedIndexChanged += (_, _) => RefreshList();
        list.DoubleClick += (_, _) => Accept(); list.SelectedIndexChanged += (_, _) => choose.Enabled = Selected is not null;
        count.Dock = DockStyle.Fill; count.TextAlign = ContentAlignment.MiddleLeft;
        choose.Text = "Select"; choose.Dock = DockStyle.Fill; choose.FlatStyle = FlatStyle.Flat; choose.Click += (_, _) => Accept();
        root.Controls.Add(search, 0, 0); root.Controls.Add(group, 0, 1); root.Controls.Add(list, 0, 2); root.Controls.Add(count, 0, 3); root.Controls.Add(choose, 0, 4);
        AcceptButton = choose; KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        void ScaleRows() { list.ItemHeight = (int)(96 * DeviceDpi / 96F); }
        DpiChanged += (_, _) => ScaleRows();
        Shown += (_, _) => { ScaleRows(); Theme.Apply(this); search.Focus(); };
        RefreshList(); Theme.Apply(this);
    }
    void Accept() { if (Selected is null) return; DialogResult = DialogResult.OK; Close(); }
    void RefreshList()
    {
        list.BeginUpdate(); list.Items.Clear();
        foreach (var e in entries)
            if ((group.SelectedIndex <= 0 || e.Group == (string?)group.SelectedItem) && e.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)) list.Items.Add(e);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        choose.Enabled = list.Items.Count > 0;
        count.Text = $"{list.Items.Count} of {entries.Length} entries";
        list.EndUpdate();
    }
    void DrawEntry(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var item = (PickerEntry)list.Items[e.Index];
        var bounds = Rectangle.Inflate(e.Bounds, -2, -3); bool selected = e.State.HasFlag(DrawItemState.Selected);
        Color end = selected ? Theme.Raised : Theme.Surface;
        using var fill = new LinearGradientBrush(bounds, item.Unique ? (Theme.Dark ? Color.FromArgb(96, 55, 29) : Color.FromArgb(255, 193, 139)) : end, end, 0F);
        e.Graphics.FillRectangle(fill, bounds);
        using var border = new Pen(selected ? Theme.Accent : item.Unique ? Color.FromArgb(242, 134, 52) : Theme.Border, selected || item.Unique ? 2 : 1);
        e.Graphics.DrawRectangle(border, bounds);
        int Px(int value) => (int)(value * DeviceDpi / 96F);
        if (Catalog.Icon(item.Icon) is { } icon) e.Graphics.DrawImage(icon, new Rectangle(bounds.X + Px(8), bounds.Y + Px(8), Px(72), Px(72)));
        using var title = new Font(Font.FontFamily, Font.SizeInPoints + 1.5F);
        TextRenderer.DrawText(e.Graphics, item.Name, title, new Rectangle(bounds.X + Px(96), bounds.Y + Px(12), bounds.Width - Px(104), Px(32)), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, item.Group + (item.Unique ? " · Unique" : ""), Font, new Rectangle(bounds.X + Px(96), bounds.Y + Px(51), bounds.Width - Px(104), Px(30)), Theme.Muted, TextFormatFlags.EndEllipsis);
    }
}

public sealed partial class MainForm
{
    void PickGear(bool replace = false, string initialGroup = "All")
    {
        if (!Ready() || (replace && selectedItem < 0)) return;
        using var picker = new PickerForm("Select item", Catalog.Gear.Select(x => new PickerEntry(x.Name, x.Category, x.Icon, x.Unique, x)), initialGroup);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected?.Value is not GearDefinition chosen) return;
        int power = Math.Max(1, doc!.Character["MetaData"]?["PowerLevel"]?.GetValue<int>() ?? 1);
        if (replace) power = Math.Max(1, (int)(doc.Items[selectedItem]?["ItemData"]?["GeneratorData"]?["PowerGeneratorValues"]?["ItemPower"]?.GetValue<double>() ?? power));
        var entry = chosen.Create(power, doc.Character["MetaData"]?["Level"]?.GetValue<int>() ?? 1);
        if (replace)
        {
            string oldSlot = doc.Items[selectedItem]!["EquippedSlot"]!.GetValue<string>();
            if (oldSlot == chosen.Slot || (chosen.Category == "Artifact" && oldSlot.Contains(".Artifact.")) || (chosen.Category == "Talisman" && oldSlot.Contains(".Talisman."))) entry["EquippedSlot"] = oldSlot;
        }
        doc.Change(root =>
        {
            var arr = (System.Text.Json.Nodes.JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!;
            if (replace) arr[selectedItem] = entry.DeepClone(); else arr.Add(entry.DeepClone());
        });
        knownTags.Add(chosen.Tag); if (!replace) selectedItem = doc.Items.Count - 1;
        search.Clear(); category.SelectedIndex = 0; RefreshAll();
        status.Text = chosen.Name + (replace ? " selected." : " added to inventory.") + " Click Save to game when ready.";
    }
}
