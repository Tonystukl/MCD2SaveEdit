using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed partial class MainForm : Form
{
    Color bg => Theme.Background; Color panel => Theme.Surface; Color ink => Theme.Text; Color muted => Theme.Muted; Color accent => Theme.Accent;
    SaveDocument? doc;
    readonly Label heading = new(), subtitle = new(), status = new();
    readonly TabControl tabs = new();
    readonly ListView inventory = new();
    readonly TextBox search = new();
    readonly ComboBox category = new();
    readonly FlowLayoutPanel itemFields = new(), heroFields = new();
    readonly TextBox itemJson = new(), rawJson = new(), treeSearch = new();
    readonly TreeView tree = new();
    readonly Label rawPath = new();
    readonly ListView changes = new();
    readonly Button undoButton = new ModernButton(), redoButton = new ModernButton(), saveButton = new ModernButton(), exportButton = new ModernButton();
    readonly HashSet<string> knownTags = new(StringComparer.Ordinal);
    readonly HashSet<string> knownSlots = new(StringComparer.Ordinal) { "None" };
    readonly HashSet<string> knownRarities = new(StringComparer.Ordinal);
    bool loading, rawDirty, itemDirty, itemFieldsDirty, heroDirty;
    int selectedItem = -1;
    string[] selectedPath = [];

    public MainForm(string? path)
    {
        Text = "Minecraft Dungeons II Save Editor 0.3.1"; Font = new Font("Arial", 10.5F); Icon = AppBrand.Icon;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Size = new Size(1640, 1000); MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18, 0, 18, 8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); Controls.Add(layout);
        var menu = new MenuStrip { Dock = DockStyle.Fill, Font = new Font("Arial", 9F) }; MainMenuStrip = menu;
        var fileMenu = new ToolStripMenuItem("File"); menu.Items.Add(fileMenu);
        void MenuAction(ToolStripMenuItem parent, string name, Action action) { parent.DropDownItems.Add(name, null, (_, _) => Run(action)); }
        MenuAction(fileMenu, "Find Xbox saves", FindSaves); MenuAction(fileMenu, "Open file…", OpenFile); MenuAction(fileMenu, "Import character…", ImportCharacter); MenuAction(fileMenu, "Save to game", Save); MenuAction(fileMenu, "Export copy…", Export);
        MenuAction(fileMenu, "Import item…", ImportItem); MenuAction(fileMenu, "Export selected item…", ExportItem);
        var view = new ToolStripMenuItem("View"); menu.Items.Add(view);
        var dark = new ToolStripMenuItem("Dark mode") { Checked = Theme.Dark, CheckOnClick = true };
        dark.CheckedChanged += (_, _) => { Theme.Dark = dark.Checked; Theme.Apply(this); }; view.DropDownItems.Add(dark);
        MenuAction(view, "Backups", () => { Directory.CreateDirectory(SaveFiles.BackupFolder); Process.Start(new ProcessStartInfo(SaveFiles.BackupFolder) { UseShellExecute = true }); });
        layout.Controls.Add(menu, 0, 0);
        layout.Controls.Add(BuildBrandHeader(), 0, 1);
        var tools = Flow(); tools.Controls.Add(Btn("Find Xbox saves", FindSaves)); tools.Controls.Add(Btn("Import character…", ImportCharacter));
        SetupButton(saveButton, "Save to game", Save, true); tools.Controls.Add(saveButton); SetupButton(exportButton, "Export copy…", Export); tools.Controls.Add(exportButton);
        SetupButton(undoButton, "Undo", () => { if (Ready()) { doc?.Undo(); RefreshAll(); } }); tools.Controls.Add(undoButton);
        SetupButton(redoButton, "Redo", () => { if (Ready()) { doc?.Redo(); RefreshAll(); } }); tools.Controls.Add(redoButton);
        subtitle.AutoSize = true; subtitle.Margin = new Padding(10, 12, 0, 0); tools.Controls.Add(subtitle); layout.Controls.Add(tools, 0, 2);
        tabs.Dock = DockStyle.Fill; tabs.Padding = new Point(13, 7); tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            using var brush = new SolidBrush(selected ? Theme.Raised : Theme.Background); e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, e.Bounds, selected ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected) { using var marker = new SolidBrush(Theme.Accent); e.Graphics.FillRectangle(marker, e.Bounds.X + 4, e.Bounds.Bottom - 3, e.Bounds.Width - 8, 3); }
        };
        BuildNavigationHost(layout);
        knownTags.UnionWith(Catalog.Gear.Select(x => x.Tag)); knownRarities.UnionWith(new[] { "SW.Rarity.Common", "SW.Rarity.Rare", "SW.Rarity.Special", "SW.Rarity.Unique", "SW.Rarity.None" });
        BuildInventory(); BuildHero(); BuildEffects(); BuildWorld(); BuildRaw(); BuildChanges(); BuildHelp();
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.Tag = "muted"; status.Margin = Padding.Empty; layout.Controls.Add(status, 0, 4);
        FormClosing += (_, e) => { if (!CanLeave()) e.Cancel = true; }; KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { Run(Save); e.SuppressKeyPress = true; } };
        RefreshAll();
        Shown += (_, _) => Run(() =>
        {
            Theme.Apply(this);
            if (path is not null) { LoadDocument(path); return; }
            var found = SaveFiles.Discover(SaveFiles.DefaultWgs).ToArray();
            if (found.Length == 1) LoadDocument(found[0]); else if (found.Length > 1) FindSaves();
            else status.Text = "No character found. Click Find Xbox saves or use File > Open file.";
        });
    }
    FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, WrapContents = false, BackColor = bg, Margin = Padding.Empty };
    Button Btn(string text, Action click, bool primary = false) { var b = new ModernButton(); SetupButton(b, text, click, primary); return b; }
    void SetupButton(Button b, string text, Action click, bool primary = false)
    {
        b.Text = text; b.AutoSize = true; b.Height = 34; b.Padding = new Padding(10, 4, 10, 4); b.Margin = new Padding(0, 3, 8, 3);
        b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(186, 187, 192);
        if (b is ModernButton modern) modern.Primary = primary;
        b.BackColor = primary ? accent : panel; b.ForeColor = primary ? Color.White : ink;
        b.Click += (_, _) => Run(click);
    }
    TabPage Page(string name) { var p = new TabPage(name) { BackColor = bg, ForeColor = ink, Padding = new Padding(12) }; tabs.TabPages.Add(p); AddNavigation(p); return p; }
    void StyleText(TextBox t, bool multi = false)
    {
        t.BackColor = panel; t.ForeColor = ink; t.BorderStyle = BorderStyle.FixedSingle;
        if (multi) { t.Multiline = true; t.AcceptsReturn = true; t.AcceptsTab = true; t.ScrollBars = ScrollBars.Both; t.WordWrap = false; t.Font = new Font("Consolas", 10); t.MaxLength = 32 * 1024 * 1024; }
    }
    void StyleList(ListView v)
    {
        v.Dock = DockStyle.Fill; v.View = View.Details; v.FullRowSelect = true; v.MultiSelect = false; v.HideSelection = false;
        v.BackColor = panel; v.ForeColor = ink; v.BorderStyle = BorderStyle.None;
    }
    void BuildInventoryLegacy()
    {
        var page = Page("Inventory");
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 9, BackColor = bg, Size = new Size(1200, 580), SplitterDistance = 680, Panel1MinSize = 430, Panel2MinSize = 340 };
        page.Controls.Add(split);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        split.Panel1.Controls.Add(left);
        var filters = Flow(); StyleText(search); search.Width = 280; search.PlaceholderText = "Search item, tag or slot…";
        search.TextChanged += (_, _) => RefreshInventory(); filters.Controls.Add(search);
        category.DropDownStyle = ComboBoxStyle.DropDownList; category.Width = 135;
        category.Items.AddRange(["All items", "Weapon", "Armor", "Artifact", "Talisman", "Enchantment", "Cosmetic", "Merchant", "Other"]); category.SelectedIndex = 0;
        category.SelectedIndexChanged += (_, _) => RefreshInventory(); filters.Controls.Add(category); left.Controls.Add(filters, 0, 0);
        StyleList(inventory); inventory.Columns.Add("Item", 230); inventory.Columns.Add("Group*", 90); inventory.Columns.Add("Power", 60); inventory.Columns.Add("Rarity", 82); inventory.Columns.Add("Equipped / stock", 160);
        inventory.SelectedIndexChanged += (_, _) => { if (loading || inventory.SelectedItems.Count == 0) return; if (!DiscardPending()) { RefreshInventory(); return; } ShowItem((int)inventory.SelectedItems[0].Tag!); };
        left.Controls.Add(inventory, 0, 1);
        var actions = Flow(); actions.Controls.Add(Btn("Duplicate", DuplicateItem)); actions.Controls.Add(Btn("Import item…", ImportItem)); actions.Controls.Add(Btn("Export item…", ExportItem)); actions.Controls.Add(Btn("Remove", RemoveItem));
        left.Controls.Add(actions, 0, 2);
        var right = new TabControl { Dock = DockStyle.Fill };
        var simple = new TabPage("Item fields") { BackColor = bg }; var advanced = new TabPage("Full item / effects") { BackColor = bg, Padding = new Padding(8) };
        right.TabPages.AddRange([simple, advanced]); split.Panel2.Controls.Add(right);
        itemFields.Dock = DockStyle.Fill; itemFields.AutoScroll = true; itemFields.FlowDirection = FlowDirection.TopDown; itemFields.WrapContents = false; simple.Controls.Add(itemFields);
        var rawLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        rawLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); rawLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rawLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        rawLayout.Controls.Add(new Label { Text = "Edit every item property, including Effects and ItemProgression. Apply when ready.", Dock = DockStyle.Fill, ForeColor = muted }, 0, 0);
        StyleText(itemJson, true); itemJson.Dock = DockStyle.Fill; itemJson.TextChanged += (_, _) => { if (!loading) itemDirty = true; }; rawLayout.Controls.Add(itemJson, 0, 1);
        rawLayout.Controls.Add(Btn("Apply full item", ApplyItem), 0, 2); advanced.Controls.Add(rawLayout);
    }
    void BuildHero()
    {
        var page = Page("Stats / Counters");
        heroFields.Dock = DockStyle.Fill; heroFields.AutoScroll = true; heroFields.FlowDirection = FlowDirection.TopDown; heroFields.WrapContents = false;
        page.Controls.Add(heroFields);
    }
    void BuildRaw()
    {
        var page = Page("All save data");
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1200, 580), SplitterDistance = 450, Panel1MinSize = 260, Panel2MinSize = 360, SplitterWidth = 9 };
        page.Controls.Add(split);
        tree.BackColor = panel; tree.ForeColor = ink; tree.BorderStyle = BorderStyle.None; tree.Dock = DockStyle.Fill; tree.HideSelection = false;
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var find = Flow(); StyleText(treeSearch); treeSearch.Width = 250; treeSearch.PlaceholderText = "Find field or value…"; find.Controls.Add(treeSearch); find.Controls.Add(Btn("Find next", FindNode));
        left.Controls.Add(find, 0, 0); left.Controls.Add(tree, 0, 1); split.Panel1.Controls.Add(left);
        tree.BeforeSelect += (_, e) => { if (!loading && rawDirty && !DiscardPending()) e.Cancel = true; };
        tree.AfterSelect += (_, _) => { if (!loading && tree.SelectedNode?.Tag is string[] path) ShowRaw(path); };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        rawPath.Dock = DockStyle.Fill; rawPath.ForeColor = accent; rawPath.AutoEllipsis = true; right.Controls.Add(rawPath, 0, 0);
        StyleText(rawJson, true); rawJson.Dock = DockStyle.Fill; rawJson.TextChanged += (_, _) => { if (!loading) rawDirty = true; }; right.Controls.Add(rawJson, 0, 1);
        var actions = Flow(); actions.Controls.Add(Btn("Apply selected JSON", ApplyRaw, true)); actions.Controls.Add(Btn("Reload selected", () => ShowRaw(selectedPath)));
        right.Controls.Add(actions, 0, 2); split.Panel2.Controls.Add(right);
    }
    void BuildChanges()
    {
        var page = Page("Review changes"); StyleList(changes); changes.Columns.Add("Field", 530); changes.Columns.Add("Original", 280); changes.Columns.Add("Edited", 280); page.Controls.Add(changes);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab?.Text == "Review changes") RefreshChanges(); };
    }
    void BuildHelp()
    {
        var page = Page("Read me");
        var help = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = new Font("Tahoma", 11), Text = """
MCD II Save Editor 0.3.1

Close the game, open your character, and select an inventory tile. Apply gear-field changes before saving. Currency controls apply immediately to the editor's draft.

Items: + Add opens a searchable catalogue of 296 gear definitions: 80 weapons, 152 armor pieces, 40 artifacts and 24 talismans. Change item opens the same picker for replacements. Four armor slots are separate. Advanced opens all item fields.

Enchantments: click an enchantment tile or the Enchantment button beneath the selected item. Click the enchantment name to choose another compatible enchantment, select tier I, II or III, then Apply enchantment. Strength is editable. Existing strengths come from the save; new presets come from launch-build data. Some tier strengths need manual input because their exact values are unverified. The Enchantments tab also exposes the saved fields and full arrays.

Currencies: Emeralds, Echo Shards and enchantment points are on the Inventory bar. Stats / Counters includes other saved character attributes.

Quests / Map: switch between Overworld and The Sift, click a quest marker or choose a quest in the list, then complete it and add published rewards to your draft. Available/active native quest records are required. Already-completed quests cannot claim rewards again. For repeatable quests, select First time only if you have never completed that quest before. Quest scripts and story gates have not been verified in-game.

Map areas: reveal the whole region's saved fog grid, or enable the experimental fog preview and reveal fog near an area. Local grid alignment and area boundaries are approximate; map revealing does not activate stations or open story gates. Both actions support Undo and require Save to game.

View > Dark mode switches the theme.

Save to game creates a verified backup before writing the active Xbox character. Import character opens an exported character JSON for saving back to its matching character. If newer character progress exists, replacement requires an explicit choice. Export copy writes a separate JSON.

This build supports FCharacterSaveV1, soft version 5, observed in game 1.1.1.0. Catalogue records come from public game-file databases and may vary between game builds. Save round-trip tests pass, but all gear combinations and in-game cloud synchronization have not been verified.

Backups are beside the program. Each contains restore-info.json and an original folder. To restore, close the game, pause cloud synchronization, keep a copy of the current account save folder, then restore the whole original folder to the recorded sourceRoot. Keep full Xbox backups together and private.

The program runs locally and uploads nothing. Unofficial fan utility; not affiliated with Mojang or Microsoft. See README.md for data sources and validation details.
""" };

        page.Controls.Add(help);
    }
    void Run(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCD2SaveEdit", "last-error.txt");
            try { Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, DateTime.UtcNow + "\n" + ex); } catch { log = "Could not write error details."; }
            MessageBox.Show(this, ex.Message + "\n\nYour edits are still open.\nError details: " + log, "Could not complete action", MessageBoxButtons.OK, MessageBoxIcon.Warning); status.Text = ex.Message;
        }
    }
    bool DiscardPending(string except = "")
    {
        bool pending = (rawDirty && except != "raw") || (itemDirty && except != "itemJson") || (itemFieldsDirty && except != "itemFields") || (heroDirty && except != "hero") || (effectsDirty && except != "effects");
        if (!pending) return true;
        if (MessageBox.Show(this, "Discard unapplied edits in other controls? Click No to return and use their Apply button first.", "Unapplied edits", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        if (except != "raw") rawDirty = false;
        if (except != "itemJson") itemDirty = false;
        if (except != "itemFields") itemFieldsDirty = false;
        if (except != "hero") heroDirty = false;
        if (except != "effects") effectsDirty = false;
        return true;
    }
    bool Ready() => doc is not null && DiscardPending();
    bool CanLeave()
    {
        if (!DiscardPending()) return false;
        return doc?.Dirty != true || MessageBox.Show(this, "Discard your unsaved character edits? Export or save first to keep them.", "Unsaved edits", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
    void LoadDocument(string path)
    {
        var candidate = new SaveDocument(path);
        if (!CanLeave()) return;
        doc = candidate; selectedItem = candidate.Items.Count > 0 ? 0 : -1; selectedPath = []; itemDirty = rawDirty = false;
        foreach (var entry in doc.Items)
        {
            knownTags.Add(entry!["ItemData"]!["TypeTag"]!.GetValue<string>());
            knownSlots.Add(entry["EquippedSlot"]!.GetValue<string>());
            if (entry["ItemData"]!["RarityTag"] is JsonValue rarity) knownRarities.Add(rarity.GetValue<string>());
        }
        RefreshAll(); status.Text = $"Character loaded • {doc.Items.Count} inventory entries • {(doc.Wgs is null ? "Imported JSON" : "Xbox / Game Pass")} • v0.3.1";
    }
    void FindSaves()
    {
        var found = SaveFiles.Discover(SaveFiles.DefaultWgs).ToArray();
        if (found.Length == 0)
        {
            using var folder = new FolderBrowserDialog { Description = "Select a folder containing MCD II character saves", UseDescriptionForTitle = true };
            if (folder.ShowDialog(this) != DialogResult.OK) return;
            found = SaveFiles.Discover(folder.SelectedPath).ToArray();
        }
        if (found.Length == 0) { status.Text = "No supported character saves found. Create a character in the game, exit, then try again."; return; }
        using var picker = new Form { Text = "Choose a character", Size = new Size(850, 350), StartPosition = FormStartPosition.CenterParent, BackColor = bg, ForeColor = ink, Font = Font };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = panel, ForeColor = ink, HorizontalScrollbar = true };
        foreach (string file in found)
        {
            var d = new SaveDocument(file);
            list.Items.Add($"Level {d.Character["MetaData"]?["Level"]} • {d.Items.Count} entries • {d.CharacterId} • {File.GetLastWriteTime(file):g}");
        }
        list.SelectedIndex = 0; picker.Controls.Add(list);
        var open = new Button { Text = "Open selected character", Dock = DockStyle.Bottom, Height = 45, DialogResult = DialogResult.OK };
        picker.Controls.Add(open); picker.AcceptButton = open; list.DoubleClick += (_, _) => picker.DialogResult = DialogResult.OK;
        if (picker.ShowDialog(this) == DialogResult.OK) LoadDocument(found[list.SelectedIndex]);
    }
    void OpenFile()
    {
        using var file = new OpenFileDialog { Title = "Open an MCD II character save", Filter = "All save files|*.*|JSON files|*.json", InitialDirectory = Directory.Exists(SaveFiles.DefaultWgs) ? SaveFiles.DefaultWgs : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (file.ShowDialog(this) == DialogResult.OK) LoadDocument(file.FileName);
    }
    void Save()
    {
        if (doc is null) return;
        if (rawDirty || itemDirty || itemFieldsDirty || heroDirty || effectsDirty)
        { MessageBox.Show(this, "Click Apply in the editor containing your pending changes, then Save to game. Nothing has been discarded.", "Apply pending changes"); return; }
        SaveFiles.EnsureGameClosed();
        var current = SaveFiles.ResolveGameTarget(doc);
        bool conflict = doc.Wgs is not null && SaveFiles.GameContentChanged(doc, current);
        if (conflict || doc.Wgs is null)
        {
            string message = $"Replace the current game character (level {current.Character["MetaData"]?["Level"]}) with this character (level {doc.Character["MetaData"]?["Level"]})?\n\n" +
                (conflict ? "The game has saved newer progress since you opened this file. " : "") + "A full backup of the current save will be created first.";
            if (MessageBox.Show(this, message, "Apply character to game", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        }
        var result = SaveFiles.SaveToGame(doc, conflict);
        doc = result.Document; RefreshAll(); status.Text = "Written locally and verified. Backup: " + result.Backup;
    }
    void ImportCharacter()
    {
        using var file = new OpenFileDialog { Title = "Import an exported character, then click Save to game", Filter = "Character JSON|*.json|All files|*.*" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        var imported = new SaveDocument(file.FileName);
        _ = SaveFiles.ResolveGameTarget(imported);
        LoadDocument(file.FileName);
        status.Text = "Imported character ready. Review it, then click Save to game to apply it with a backup.";
    }
    void Export()
    {
        if (!Ready()) return;
        using var file = new SaveFileDialog { Title = "Export an edited copy", Filter = "JSON character save|*.json", FileName = "MCD2-character-edited.json" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        string dest = Path.GetFullPath(file.FileName);
        if (dest.Equals(doc!.FilePath, StringComparison.OrdinalIgnoreCase) || dest.StartsWith(Path.GetFullPath(SaveFiles.DefaultWgs) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(dest))!, "containers.index")))
            throw new IOException("Export outside the Xbox save folders. Use Save to game to update the opened save with its container metadata.");
        SaveFiles.AtomicWrite(dest, doc.Serialize()); status.Text = "Exported copy: " + dest;
    }
    void RefreshAll()
    {
        loading = true;
        try
        {
            saveButton.Enabled = doc is not null; exportButton.Enabled = doc is not null; undoButton.Enabled = doc?.CanUndo == true; redoButton.Enabled = doc?.CanRedo == true;
            subtitle.Text = doc is null ? "Open a character to begin. Your files stay on this computer." : $"{(doc.Dirty ? "Unsaved changes" : "Saved")} · {doc.Items.Count} items";
            Text = "Minecraft Dungeons II Save Editor 0.3.1" + (doc?.Dirty == true ? " *" : "");
            RefreshInventory(); RefreshHero(); RefreshEffects(); RefreshTree(); RefreshChanges(); RefreshClassic(); RefreshWorld();
            if (doc is not null && selectedItem >= 0 && selectedItem < doc.Items.Count) ShowItem(selectedItem);
            else { itemFields.Controls.Clear(); itemJson.Text = ""; itemDirty = false; itemTitle.Text = "Select an item"; itemPicture.Image = null; ClearTiles(effectTiles); }
            Theme.Apply(this); FitTiles(itemTiles); FitTiles(armorTiles); FitTiles(utilityTiles);
        }
        finally { loading = false; }
    }
    void RefreshInventory()
    {
        bool before = loading; loading = true; inventory.BeginUpdate(); inventory.Items.Clear();
        try
        {
            if (doc is null) return;
            for (int i = 0; i < doc.Items.Count; i++)
            {
                var e = doc.Items[i]!; var d = e["ItemData"]!;
                string type = d["TypeTag"]!.GetValue<string>(), group = SaveDocument.Category(e), slot = e["EquippedSlot"]!.GetValue<string>();
                if (category.SelectedIndex > 0 && (string)category.SelectedItem! != group) continue;
                if (!(type + " " + slot + " " + Catalog.Name(type)).Contains(search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                var row = new ListViewItem(Catalog.Name(type)) { Tag = i };
                row.SubItems.Add(group); row.SubItems.Add(d["GeneratorData"]?["PowerGeneratorValues"]?["ItemPower"]?.ToJsonString() ?? "—");
                row.SubItems.Add(SaveDocument.Label(d["RarityTag"]?.GetValue<string>() ?? ""));
                row.SubItems.Add(slot == "None" ? (group == "Merchant" ? "Merchant stock" : "Inventory") : SaveDocument.Label(slot));
                inventory.Items.Add(row); if (i == selectedItem) row.Selected = true;
            }
        }
        finally { inventory.EndUpdate(); loading = before; RefreshTiles(); }
    }
    Control InputRow(string label, string value, IEnumerable<string>? suggestions, out Control input)
    {
        var row = new Panel { Width = 355, Height = 54, Margin = new Padding(8, 2, 8, 2) };
        var text = new Label { Text = label, ForeColor = muted, Left = 0, Top = 0, Width = 350, Height = 21 };
        if (suggestions is null) { var box = new TextBox { Text = value, Left = 0, Top = 22, Width = 345 }; StyleText(box); input = box; }
        else
        {
            var combo = new ComboBox { Left = 0, Top = 22, Width = 345, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            combo.Items.AddRange(suggestions.Order().Cast<object>().ToArray()); combo.Text = value; input = combo;
        }
        row.Controls.Add(text); row.Controls.Add(input); return row;
    }
    void ShowItem(int index)
    {
        if (doc is null || index < 0 || index >= doc.Items.Count) return;
        selectedItem = index; bool old = loading; loading = true;
        try
        {
            ClearTiles(itemFields); var entry = doc.Items[index]!; var data = entry["ItemData"]!; string tag = data["TypeTag"]!.GetValue<string>();
            itemTitle.Text = Catalog.Name(tag); itemPicture.Image = Catalog.ItemIcon(tag);
            var edits = new List<(string[] Path, Control Control, bool Numeric)>();
            var combatEdits = new List<(string Effect, TextBox Input, double Original)>();
            void AddField(string name, Control input)
            {
                int Px(int size) => (int)Math.Round(size * DeviceDpi / 96F);
                int labelHeight = Math.Max(Px(24), Font.Height + Px(4));
                var row = new Panel { Width = Px(180), Margin = new Padding(Px(3), Px(3), Px(8), Px(3)) };
                row.Controls.Add(new Label { Text = name, Location = Point.Empty, Size = new Size(Px(180), labelHeight) });
                input.Font = Font;
                input.SetBounds(0, labelHeight + Px(5), Px(176), Math.Max(Px(29), input.PreferredSize.Height));
                row.Height = input.Bottom + Px(8);
                input.TextChanged += (_, _) => { if (!loading) itemFieldsDirty = true; };
                row.Controls.Add(input); itemFields.Controls.Add(row);
            }
            void Field(string name, string[] path, string[]? choices = null)
            {
                var value = SaveDocument.Get(entry, path); if (value is null) return;
                Control input;
                if (choices is null) input = new TextBox { Text = value.ToJsonString() };
                else { var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; combo.Items.AddRange(choices); combo.SelectedItem = value.GetValue<string>().Replace("SW.Rarity.", ""); input = combo; }
                AddField(name, input); edits.Add((path, input, choices is null));
            }
            Field("Power", ["ItemData", "GeneratorData", "PowerGeneratorValues", "ItemPower"]);
            Field("Rarity", ["ItemData", "RarityTag"], ["Common", "Rare", "Special", "Unique", "None"]);
            Field("Original power", ["ItemData", "GeneratorData", "PowerGeneratorValues", "ItemPowerOriginal"]);
            Field("Gear level", ["ItemData", "ItemProgression", "CurrentLevel"]);
            Field("Gear XP", ["ItemData", "ItemProgression", "CurrentXP"]);
            Field("Stack count", ["StackCount"]);
            foreach (var (effect, label) in new[] { (CombatEditing.Sharpness, "Sharpness bonus (%)"), (CombatEditing.Protection, "Protection strength (%)") })
            {
                if (!CombatEditing.Supports(entry, effect)) continue;
                double original = CombatEditing.Percent(entry, effect);
                var input = new TextBox { Text = original.ToString("G", System.Globalization.CultureInfo.InvariantCulture) };
                AddField(label, input); combatEdits.Add((effect, input, original));
                tileTips.SetToolTip(input, effect == CombatEditing.Sharpness
                    ? "Edits rolled Sharpness directly. Normal tiers: 10 / 20 / 30%. Above-normal bonuses need in-game testing; the game may limit them. 0 removes the rolled bonus."
                    : "Edits rolled Protection directly. Normal tiers: 10 / 15 / 20%. Above-normal strength and the final damage reduction need in-game testing. Fixed traits stay intact; 0 removes the roll.");
            }
            itemFields.Controls.Add(Btn("Apply changes", () =>
            {
                if (doc is null || !DiscardPending("itemFields")) return;
                doc.Change(root =>
                {
                    var item = (JsonObject)root["CharacterSaveV1"]!["Inventory"]!["Entries"]![index]!;
                    foreach (var edit in edits)
                    {
                        JsonNode? value = edit.Numeric ? SaveDocument.ParseFragment(edit.Control.Text) : JsonValue.Create("SW.Rarity." + edit.Control.Text);
                        if (edit.Numeric && (value is not JsonValue v || v.GetValueKind() != JsonValueKind.Number)) throw new InvalidDataException("Enter a number for each numeric field.");
                        SaveDocument.Set(item, edit.Path, value);
                    }
                    foreach (var edit in combatEdits)
                    {
                        if (!double.TryParse(edit.Input.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                            throw new InvalidDataException("Enter a number for the combat bonus percentage.");
                        if (percent != edit.Original) CombatEditing.SetPercent(item, edit.Effect, percent);
                    }
                    var levels = item["ItemData"]?["ItemProgression"]?["ItemLevels"] as JsonArray;
                    if (levels?.Count > 0 && item["ItemData"]?["ItemProgression"]?["CurrentLevel"] is JsonValue level)
                    {
                        int n = level.GetValue<int>(); if (n < 0 || n >= levels.Count) throw new InvalidDataException($"This talisman has gear levels 0–{levels.Count - 1} (tiers I–III).");
                        var batches = (JsonArray)item["ItemData"]!["Effects"]!;
                        var upgradable = batches.FirstOrDefault(x => x?["TypeTag"]?.GetValue<string>() == "SW.Item.Effect.Upgradable");
                        if (upgradable is not null) upgradable["EffectsInThisBatch"] = levels[n]!["LevelEffects"]!.DeepClone();
                    }
                }); itemFieldsDirty = false; RefreshAll(); status.Text = "Item updated. Click Save to game to keep your changes.";
            }, true));
            itemFields.Controls.Add(Btn("Equip / unequip", EquipSelected));
            itemJson.Text = entry.ToJsonString(SaveDocument.Pretty); itemDirty = itemFieldsDirty = false;
            RefreshEffectTiles(); Theme.Apply(itemFields); Theme.Apply(effectTiles);
        }
        finally { loading = old; RefreshTileSelection(); }
    }
    void ApplyItem()
    {
        if (doc is null || selectedItem < 0 || !DiscardPending("itemJson")) return;
        var parsed = SaveDocument.ParseFragment(itemJson.Text);
        doc.Replace(["CharacterSaveV1", "Inventory", "Entries", selectedItem.ToString()], parsed);
        itemDirty = false; RefreshAll(); status.Text = "Applied full item, including effects and progression.";
    }
    void DuplicateItem()
    {
        if (!Ready() || selectedItem < 0) return;
        doc!.Change(root =>
        {
            var arr = (JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!;
            var clone = (JsonObject)arr[selectedItem]!.DeepClone(); clone["EquippedSlot"] = "None"; clone["MerchantItemSold"] = false; clone["MerchantDiscount"] = 0;
            clone["ItemData"]!["TargetSlotOverride"] = "None"; clone["ItemData"]!["PickupTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); arr.Add(clone);
        });
        selectedItem = doc.Items.Count - 1; search.Clear(); category.SelectedIndex = 0; RefreshAll();
    }
    void RemoveItem()
    {
        if (!Ready() || selectedItem < 0) return;
        if (MessageBox.Show(this, "Remove this inventory entry? You can undo before saving.", "Remove item", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        doc!.Change(root => ((JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!).RemoveAt(selectedItem)); selectedItem = -1; RefreshAll();
    }
    void ExportItem()
    {
        if (!Ready() || selectedItem < 0) return;
        using var file = new SaveFileDialog { Filter = "Item template|*.json", FileName = Catalog.Name(doc!.Items[selectedItem]!["ItemData"]!["TypeTag"]!.GetValue<string>()) + ".json" };
        if (file.ShowDialog(this) == DialogResult.OK) File.WriteAllText(file.FileName, doc.Items[selectedItem]!.ToJsonString(SaveDocument.Pretty));
    }
    void ImportItem()
    {
        if (!Ready()) return;
        using var file = new OpenFileDialog { Title = "Import an exported MCD II inventory entry", Filter = "Item templates|*.json" };
        if (file.ShowDialog(this) != DialogResult.OK) return;
        var item = SaveDocument.ParseFragment(File.ReadAllText(file.FileName)) as JsonObject ?? throw new InvalidDataException("An item template must be a JSON object.");
        item["EquippedSlot"] = "None"; item["MerchantItemSold"] = false; item["MerchantDiscount"] = 0;
        if (item["ItemData"] is JsonObject data) data["TargetSlotOverride"] = "None";
        doc!.Change(root => ((JsonArray)root["CharacterSaveV1"]!["Inventory"]!["Entries"]!).Add(item.DeepClone()));
        knownTags.Add(item["ItemData"]!["TypeTag"]!.GetValue<string>()); selectedItem = doc.Items.Count - 1; search.Clear(); category.SelectedIndex = 0; RefreshAll();
    }
    void RefreshHero()
    {
        heroFields.Controls.Clear(); if (doc is null) return;
        heroFields.Controls.Add(new Label { Text = "Character attributes", ForeColor = accent, Font = new Font("Arial", 19, FontStyle.Bold), Width = 600, Height = 40 });
        heroFields.Controls.Add(new Label { Text = "These are the actual attributes in your save. Other sections are available under All save data.", ForeColor = muted, Width = 900, Height = 30 });
        var arr = (JsonArray)doc.Character["Ability"]!["Attributes"]!;
        var fields = new List<(int index, Control input)>();
        for (int i = 0; i < arr.Count; i++)
        {
            var item = arr[i]; if (item?["AttributeName"] is null || item["CurrentValue"] is null) continue;
            heroFields.Controls.Add(InputRow(SaveDocument.Label(item["AttributeName"]!.GetValue<string>()), item["CurrentValue"]!.ToJsonString(), null, out var input)); fields.Add((i, input));
            input.TextChanged += (_, _) => { if (!loading) heroDirty = true; };
        }
        heroFields.Controls.Add(Btn("Apply hero attributes", () =>
        {
            if (doc is null || !DiscardPending("hero")) return;
            doc.Change(root =>
            {
                var a = (JsonArray)root["CharacterSaveV1"]!["Ability"]!["Attributes"]!;
                foreach (var (i, input) in fields)
                {
                    var value = SaveDocument.ParseFragment(input.Text);
                    if (value is not JsonValue v || v.GetValueKind() != JsonValueKind.Number) throw new InvalidDataException("Attributes must be numeric.");
                    a[i]!["CurrentValue"] = value;
                    if (a[i]!["AttributeName"]!.GetValue<string>() == "Level") root["CharacterSaveV1"]!["MetaData"]!["Level"] = value.DeepClone();
                }
            }); heroDirty = false; RefreshAll(); status.Text = "Applied character attributes.";
        }, true));
        heroDirty = false;
    }
    void RefreshTree()
    {
        tree.BeginUpdate(); tree.Nodes.Clear();
        if (doc is not null)
        {
            var root = MakeNode("Character save", doc.Root, []); tree.Nodes.Add(root); root.Expand();
            foreach (TreeNode n in root.Nodes) if (n.Text.StartsWith("CharacterSaveV1")) n.Expand();
            ShowRaw(selectedPath);
        }
        tree.EndUpdate();
    }
    TreeNode MakeNode(string name, JsonNode? value, string[] path)
    {
        string preview = value switch { JsonObject o => $"  {{{o.Count}}}", JsonArray a => $"  [{a.Count}]", _ => "  = " + (value?.ToJsonString() ?? "null") };
        if (preview.Length > 110) preview = preview[..110] + "…";
        var node = new TreeNode(name + preview) { Tag = path };
        if (value is JsonObject ob) foreach (var p in ob) node.Nodes.Add(MakeNode(p.Key, p.Value, [.. path, p.Key]));
        else if (value is JsonArray ar) for (int i = 0; i < ar.Count; i++) node.Nodes.Add(MakeNode(i.ToString(), ar[i], [.. path, i.ToString()]));
        return node;
    }
    void ShowRaw(string[] path)
    {
        if (doc is null) return;
        bool old = loading; loading = true;
        try
        {
            JsonNode? node;
            try { node = SaveDocument.Get(doc.Root, path); } catch { path = []; node = doc.Root; }
            selectedPath = path; rawPath.Text = path.Length == 0 ? "Whole character save" : string.Join(" / ", path);
            rawJson.Text = node?.ToJsonString(SaveDocument.Pretty) ?? "null"; rawDirty = false;
        }
        finally { loading = old; }
    }
    void ApplyRaw()
    {
        if (doc is null || !DiscardPending("raw")) return;
        doc.Replace(selectedPath, SaveDocument.ParseFragment(rawJson.Text)); rawDirty = false; RefreshAll(); status.Text = "Applied selected data. Review changes before saving.";
    }
    void FindNode()
    {
        if (string.IsNullOrWhiteSpace(treeSearch.Text)) return;
        var nodes = new List<TreeNode>();
        void Walk(TreeNodeCollection col) { foreach (TreeNode n in col) { nodes.Add(n); Walk(n.Nodes); } }
        Walk(tree.Nodes); if (nodes.Count == 0) return;
        int start = Math.Max(-1, nodes.IndexOf(tree.SelectedNode!));
        for (int i = 1; i <= nodes.Count; i++)
        {
            var n = nodes[(start + i) % nodes.Count];
            if (n.Text.Contains(treeSearch.Text, StringComparison.OrdinalIgnoreCase)) { tree.SelectedNode = n; n.EnsureVisible(); return; }
        }
        status.Text = "No matching field or value.";
    }
    void RefreshChanges()
    {
        changes.BeginUpdate(); changes.Items.Clear();
        if (doc is not null)
        {
            var original = SaveDocument.Parse(doc.OriginalBytes);
            void Diff(JsonNode? a, JsonNode? b, string path)
            {
                if (JsonNode.DeepEquals(a, b)) return;
                if (a is JsonObject ao && b is JsonObject bo) { foreach (var k in ao.Select(x => x.Key).Union(bo.Select(x => x.Key))) Diff(ao[k], bo[k], path + "/" + k); }
                else if (a is JsonArray aa && b is JsonArray ba) { for (int i = 0; i < Math.Max(aa.Count, ba.Count); i++) Diff(i < aa.Count ? aa[i] : null, i < ba.Count ? ba[i] : null, path + "/" + i); }
                else
                {
                    string Short(JsonNode? n) { string s = n?.ToJsonString() ?? "(missing / null)"; return s.Length > 200 ? s[..200] + "…" : s; }
                    changes.Items.Add(new ListViewItem([path, Short(a), Short(b)]));
                }
            }
            Diff(original, doc.Root, "");
        }
        changes.EndUpdate();
    }
}
