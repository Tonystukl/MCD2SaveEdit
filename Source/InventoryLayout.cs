using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed partial class MainForm
{
    readonly Label itemTitle = new();
    readonly PictureBox itemPicture = new();
    readonly FlowLayoutPanel effectTiles = new();
    TableLayoutPanel? inventoryColumns;
    TableLayoutPanel InventoryTable(int columns, params RowStyle[] rows)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows.Length, Margin = Padding.Empty };
        foreach (var r in rows) table.RowStyles.Add(r); return table;
    }
    void BuildInventory()
    {
        var page = Page("Inventory");
        var root = InventoryTable(1, new RowStyle(SizeType.Absolute, 68), new RowStyle(SizeType.Percent, 100)); page.Controls.Add(root);
        var wallet = Flow();
        foreach (var (name, number) in new[] { ("◆ Emeralds", emeralds), ("◈ Echo Shards", echoShards), ("✦ Enchantment points", enchantPoints) })
        {
            var box = InventoryTable(1, new RowStyle(SizeType.Absolute, 29), new RowStyle(SizeType.Percent, 100)); box.Dock = DockStyle.None; box.Size = new Size(225, 62); box.Margin = new Padding(0, 0, 16, 0);
            box.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill }, 0, 0);
            number.Dock = DockStyle.Fill; number.Maximum = int.MaxValue; number.ThousandsSeparator = true; box.Controls.Add(number, 0, 1); wallet.Controls.Add(box);
        }
        emeralds.ValueChanged += (_, _) => ChangeCounter(emeralds, "Emeralds"); enchantPoints.ValueChanged += (_, _) => ChangeCounter(enchantPoints, "EnchantmentPoints");
        echoShards.ValueChanged += (_, _) => ChangeCounter(echoShards, "SpringStone");
        powerLabel.AutoSize = true; powerLabel.Margin = new Padding(6, 16, 0, 0); wallet.Controls.Add(powerLabel); root.Controls.Add(wallet, 0, 0);
        var columns = InventoryTable(3, new RowStyle(SizeType.Percent, 100));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 345)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.Controls.Add(columns, 0, 1);
        inventoryColumns = columns;
        columns.SizeChanged += (_, _) => ResizeInventoryColumns();
        DpiChanged += (_, _) => ResizeInventoryColumns();
        var loadout = InventoryTable(1, new RowStyle(SizeType.Absolute, 24), new RowStyle(SizeType.Percent, 55), new RowStyle(SizeType.Absolute, 64), new RowStyle(SizeType.Absolute, 24), new RowStyle(SizeType.Percent, 45));
        loadout.Margin = new Padding(0, 6, 12, 0); loadout.Controls.Add(new Label { Text = "EQUIPMENT", Dock = DockStyle.Fill }, 0, 0);
        ConfigureTileFlow(armorTiles); armorTiles.Tag = 2; armorTiles.SizeChanged += (_, _) => FitTiles(armorTiles); loadout.Controls.Add(armorTiles, 0, 1);
        var level = InventoryTable(2, new RowStyle(SizeType.Percent, 100)); level.Padding = new Padding(4, 14, 4, 10); level.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); level.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        level.Controls.Add(new Label { Text = "⬡ LEVEL", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        heroLevel.Dock = DockStyle.Fill; heroLevel.Minimum = 1; heroLevel.Maximum = int.MaxValue; heroLevel.ValueChanged += (_, _) => ChangeCounter(heroLevel, "Level"); level.Controls.Add(heroLevel, 1, 0); loadout.Controls.Add(level, 0, 2);
        loadout.Controls.Add(new Label { Text = "ARTIFACTS & TALISMANS", Dock = DockStyle.Fill }, 0, 3);
        ConfigureTileFlow(utilityTiles); utilityTiles.Tag = 3; utilityTiles.SizeChanged += (_, _) => FitTiles(utilityTiles); loadout.Controls.Add(utilityTiles, 0, 4); columns.Controls.Add(loadout, 0, 0);
        var bag = InventoryTable(1, new RowStyle(SizeType.Absolute, 40), new RowStyle(SizeType.Absolute, 44), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.Absolute, 30)); bag.Margin = new Padding(0, 0, 14, 0);
        category.Dock = DockStyle.Fill; category.DropDownStyle = ComboBoxStyle.DropDownList; category.Items.AddRange(["All items", "Weapon", "Armor", "Artifact", "Talisman", "Enchantment", "Cosmetic", "Merchant", "Other"]); category.SelectedIndex = 0; category.SelectedIndexChanged += (_, _) => RefreshInventory(); bag.Controls.Add(category, 0, 0);
        var filters = InventoryTable(2, new RowStyle(SizeType.Percent, 100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 87));
        search.Dock = DockStyle.Fill; search.PlaceholderText = "Search inventory…"; search.TextChanged += (_, _) => RefreshInventory(); filters.Controls.Add(search, 0, 0);
        var add = Btn("+ Add", () => PickGear(), true); add.AutoSize = false; add.Dock = DockStyle.Fill; add.Margin = Padding.Empty; add.Padding = Padding.Empty; filters.Controls.Add(add, 1, 0); bag.Controls.Add(filters, 0, 1);
        ConfigureTileFlow(itemTiles); itemTiles.Tag = 3; itemTiles.SizeChanged += (_, _) => FitTiles(itemTiles); bag.Controls.Add(itemTiles, 0, 2);
        inventoryCount.Dock = DockStyle.Fill; inventoryCount.TextAlign = ContentAlignment.MiddleCenter; bag.Controls.Add(inventoryCount, 0, 3); columns.Controls.Add(bag, 1, 0);
        var detail = InventoryTable(1, new RowStyle(SizeType.Absolute, 52), new RowStyle(SizeType.Percent, 65), new RowStyle(SizeType.Absolute, 29), new RowStyle(SizeType.Percent, 35));
        itemTitle.Dock = DockStyle.Fill; itemTitle.Font = new Font("Arial", 20, FontStyle.Bold); itemTitle.AutoEllipsis = true; detail.Controls.Add(itemTitle, 0, 0);
        var properties = InventoryTable(2, new RowStyle(SizeType.Percent, 100)); properties.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); properties.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        itemFields.Dock = DockStyle.Fill; itemFields.AutoScroll = true; itemFields.WrapContents = true; itemFields.FlowDirection = FlowDirection.LeftToRight; properties.Controls.Add(itemFields, 0, 0);
        var preview = InventoryTable(1, new RowStyle(SizeType.Absolute, 160), new RowStyle(SizeType.Absolute, 44), new RowStyle(SizeType.Absolute, 44), new RowStyle(SizeType.Absolute, 44), new RowStyle(SizeType.Absolute, 44), new RowStyle(SizeType.Percent, 100));
        itemPicture.Dock = DockStyle.Fill; itemPicture.SizeMode = PictureBoxSizeMode.Zoom; itemPicture.Cursor = Cursors.Hand; itemPicture.Click += (_, _) => Run(() => PickGear(true)); preview.Controls.Add(itemPicture, 0, 0);
        int row = 1;
        foreach (var (label, action) in new (string, Action)[] { ("Change item…", () => PickGear(true)), ("Duplicate", DuplicateItem), ("Delete", RemoveItem), ("Advanced…", EditAdvancedItem) })
        { var button = Btn(label, action); button.AutoSize = false; button.Dock = DockStyle.Fill; button.Margin = new Padding(2); button.Padding = Padding.Empty; preview.Controls.Add(button, 0, row++); }
        properties.Controls.Add(preview, 1, 0); detail.Controls.Add(properties, 0, 1); detail.Controls.Add(new Label { Text = "ENCHANTMENTS & EFFECTS", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        effectTiles.Dock = DockStyle.Fill; effectTiles.AutoScroll = true; effectTiles.WrapContents = true; detail.Controls.Add(effectTiles, 0, 3); columns.Controls.Add(detail, 2, 0);
    }
    void FitTiles(FlowLayoutPanel flow)
    {
        double scale = flow.DeviceDpi / 96.0;
        int space = Math.Max(1, flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - (int)(14 * scale));
        int count = flow == itemTiles ? Math.Clamp(space / (int)(160 * scale), 3, 5) : flow.Tag is int n ? n : 3;
        int cap = (int)((flow == armorTiles ? 170 : flow == utilityTiles ? 128 : 152) * scale);
        int width = Math.Clamp(space / count - (int)(8 * scale), (int)(70 * scale), cap);
        flow.SuspendLayout();
        int center = Math.Max(0, (space - count * (width + (int)(8 * scale))) / 2);
        flow.Padding = new Padding((int)(3 * scale) + center, (int)(3 * scale), (int)(3 * scale), (int)(3 * scale));
        var tiles = flow.Controls.OfType<GearTile>().ToArray();
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = new Padding((int)(4 * scale));
            tiles[i].Size = new Size(width, width + (int)(44 * scale));
            flow.SetFlowBreak(tiles[i], (i + 1) % count == 0);
        }
        flow.ResumeLayout();
    }
    void ResizeInventoryColumns()
    {
        if (inventoryColumns is not { } columns || columns.ClientSize.Width <= 0) return;
        float scale = DeviceDpi / 96F;
        int available = columns.ClientSize.Width;
        // Grow the equipment and bag with the window while reserving usable details space.
        float equipment = Math.Clamp(available * 0.20F, 280 * scale, 390 * scale);
        float bag = Math.Clamp(available * 0.30F, 345 * scale, 800 * scale);
        float overflow = equipment + bag + 440 * scale - available;
        if (overflow > 0)
        {
            float shrink = Math.Min(overflow, bag - 345 * scale); bag -= shrink; overflow -= shrink;
            equipment -= Math.Min(overflow, Math.Max(0, equipment - 280 * scale));
        }
        if (Math.Abs(columns.ColumnStyles[0].Width - equipment) < 1 && Math.Abs(columns.ColumnStyles[1].Width - bag) < 1) return;
        columns.SuspendLayout(); columns.ColumnStyles[0].Width = equipment; columns.ColumnStyles[1].Width = bag; columns.ResumeLayout();
        FitTiles(armorTiles); FitTiles(utilityTiles); FitTiles(itemTiles);
    }
    void EditAdvancedItem()
    {
        if (!Ready() || selectedItem < 0) return;
        using var dialog = new Form { Text = "Advanced item", Size = new Size(840, 680), StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox(); StyleText(text, true); text.Dock = DockStyle.Fill; text.Text = doc!.Items[selectedItem]!.ToJsonString(SaveDocument.Pretty);
        var apply = new Button { Text = "Apply item", Dock = DockStyle.Bottom, Height = 44 };
        apply.Click += (_, _) => Run(() => { doc.Replace(["CharacterSaveV1", "Inventory", "Entries", selectedItem.ToString()], SaveDocument.ParseFragment(text.Text)); dialog.DialogResult = DialogResult.OK; });
        dialog.Controls.Add(text); dialog.Controls.Add(apply); Theme.Apply(dialog); if (dialog.ShowDialog(this) == DialogResult.OK) RefreshAll();
    }
}
