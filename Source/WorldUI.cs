using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed partial class MainForm
{
    readonly ComboBox worldRegion = new(), worldMode = new();
    readonly TextBox questSearch = new();
    readonly ListView questList = new();
    readonly WorldMapControl worldMap = new();
    readonly TableLayoutPanel worldDetails = new();
    readonly CheckBox worldFog = new() { Text = "Fog preview (experimental)", AutoSize = true };
    TableLayoutPanel? worldColumns;
    string worldSelection = "";
    bool refreshingWorld;
    void BuildWorld()
    {
        var page = Page("Quests / Map");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); page.Controls.Add(layout);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        worldRegion.DropDownStyle = ComboBoxStyle.DropDownList; worldRegion.Width = 160; worldRegion.Items.AddRange(WorldCatalog.Maps); worldRegion.SelectedIndex = 0;
        worldRegion.SelectedIndexChanged += (_, _) => { worldSelection = ""; RefreshWorld(); }; toolbar.Controls.Add(worldRegion);
        worldMode.DropDownStyle = ComboBoxStyle.DropDownList; worldMode.Width = 145; worldMode.Items.AddRange(["Quests", "Map areas"]); worldMode.SelectedIndex = 0;
        worldMode.SelectedIndexChanged += (_, _) => { worldMap.AreaMode = worldMode.SelectedIndex == 1; RefreshWorldList(); worldMap.Invalidate(); }; toolbar.Controls.Add(worldMode);
        toolbar.Controls.Add(Btn("Fit map", worldMap.Fit));
        toolbar.Controls.Add(Btn("Reload game progress", FindSaves));
        worldFog.Margin = new Padding(12, 8, 8, 0); worldFog.CheckedChanged += (_, _) => { worldMap.FogPreview = worldFog.Checked; worldMap.Invalidate(); ShowWorldDetails(); }; toolbar.Controls.Add(worldFog);
        layout.Controls.Add(toolbar, 0, 0);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        worldColumns = columns;
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 345));
        columns.SizeChanged += (_, _) => ResizeWorldColumns();
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 12, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        questSearch.Dock = DockStyle.Fill; questSearch.PlaceholderText = "Search quests or areas…"; questSearch.TextChanged += (_, _) => RefreshWorldList(); left.Controls.Add(questSearch, 0, 0);
        StyleList(questList); questList.Columns.Add("Quest / area", 165); questList.Columns.Add("State", 88); questList.HeaderStyle = ColumnHeaderStyle.None;
        questList.SizeChanged += (_, _) => ResizeWorldList();
        questList.SelectedIndexChanged += (_, _) =>
        {
            if (refreshingWorld || questList.SelectedItems.Count == 0) return;
            worldSelection = (string)questList.SelectedItems[0].Tag!; SelectWorldEntry();
        }; left.Controls.Add(questList, 0, 1); columns.Controls.Add(left, 0, 0);
        worldMap.MarkerSelected += marker => { worldSelection = marker.Quest; SelectWorldEntry(); };
        worldMap.AreaSelected += area => { worldSelection = "area:" + area.Name; SelectWorldEntry(); };
        columns.Controls.Add(worldMap, 1, 0);
        worldDetails.Dock = DockStyle.Fill; worldDetails.AutoScroll = true; worldDetails.ColumnCount = 1; worldDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); worldDetails.Padding = new Padding(16, 0, 0, 0); columns.Controls.Add(worldDetails, 2, 0);
        layout.Controls.Add(columns, 0, 1);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Yellow: story quest   ·   Blue: side quest   ·   ✓: completed   ·   Scroll to zoom, drag to pan   ·   Changes stay in your draft until Save to game", TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 2);
        worldMap.SetMap(WorldCatalog.Maps[0], null);
    }
    void ResizeWorldColumns()
    {
        if (worldColumns is not { } columns || columns.Width <= 0) return;
        float scale = DeviceDpi / 96F;
        float left = Math.Clamp(columns.Width * .19F, 265 * scale, 480 * scale);
        float right = Math.Clamp(columns.Width * .25F, 335 * scale, 640 * scale);
        if (Math.Abs(columns.ColumnStyles[0].Width - left) < 1 && Math.Abs(columns.ColumnStyles[2].Width - right) < 1) return;
        columns.ColumnStyles[0].Width = left; columns.ColumnStyles[2].Width = right;
        ResizeWorldList();
    }
    void ResizeWorldList()
    {
        if (questList.Columns.Count < 2) return;
        int width = Math.Max(1, questList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        int state = Math.Min(width / 3, (int)(125 * DeviceDpi / 96F));
        questList.Columns[0].Width = width - state; questList.Columns[1].Width = state;
    }
    void RefreshWorld()
    {
        if (worldRegion.SelectedItem is not WorldDefinition map) return;
        worldMap.SetMap(map, doc?.Character); RefreshWorldList(); ShowWorldDetails();
    }
    void RefreshWorldList()
    {
        if (worldRegion.SelectedItem is not WorldDefinition map) return;
        refreshingWorld = true; questList.BeginUpdate();
        try
        {
            questList.Items.Clear(); string filter = questSearch.Text.Trim();
            if (worldMode.SelectedIndex == 1)
            {
                foreach (var area in map.Labels.Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                    questList.Items.Add(new ListViewItem([area.Name, "Fog"]) { Tag = "area:" + area.Name });
            }
            else
            {
                foreach (var quest in WorldCatalog.Quests.Where(q => WorldCatalog.InRegion(q, map) && (q.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || q.Area.Contains(filter, StringComparison.OrdinalIgnoreCase))).OrderBy(q => q.Name))
                    questList.Items.Add(new ListViewItem([quest.Name, WorldEditing.QuestState(doc?.Character, quest.Id)]) { Tag = quest.Id });
                foreach (var saved in (doc?.Character["quest"]?["Quests"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    string id = saved["QuestName"]?.GetValue<string>() ?? "";
                    if (map.Id == "overworld" && WorldCatalog.Quest(id) is null && id.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        questList.Items.Add(new ListViewItem([id + " (unlisted)", saved["State"]?.GetValue<string>() ?? "Unknown"]) { Tag = id });
                }
            }
            foreach (ListViewItem item in questList.Items) if ((string)item.Tag! == worldSelection) item.Selected = true;
        }
        finally { questList.EndUpdate(); refreshingWorld = false; }
    }
    void SelectWorldEntry()
    {
        var map = (WorldDefinition)worldRegion.SelectedItem!;
        worldMap.SelectedQuest = worldSelection.StartsWith("area:") ? "" : worldSelection;
        worldMap.SelectedArea = worldSelection.StartsWith("area:") ? map.Labels.FirstOrDefault(a => a.Name == worldSelection[5..]) : null;
        worldMap.Invalidate(); ShowWorldDetails();
    }
    void ShowWorldDetails()
    {
        worldDetails.SuspendLayout();
        foreach (Control c in worldDetails.Controls.Cast<Control>().ToArray()) c.Dispose();
        worldDetails.Controls.Clear(); worldDetails.RowStyles.Clear(); worldDetails.RowCount = 0;
        void Add(Control control, int height)
        {
            int row = worldDetails.RowCount++; worldDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 0, 0, 9); worldDetails.Controls.Add(control, 0, row);
        }
        void TextBlock(string value, int height = 65, bool title = false)
        {
            Add(new Label { Text = value, Font = title ? new Font("Arial", 18, FontStyle.Bold) : Font, AutoEllipsis = true }, height);
        }
        try
        {
            var map = worldRegion.SelectedItem as WorldDefinition; if (map is null) return;
            if (worldSelection.StartsWith("area:"))
            {
                var area = map.Labels.FirstOrDefault(a => a.Name == worldSelection[5..]); if (area is null) return;
                TextBlock(area.Name, 70, true); TextBlock(map.Name + " • map exploration", 35);
                var fog = WorldEditing.Fog(doc?.Character, map.Tag);
                TextBlock(fog is null ? "Visit this region in the game to initialize its map data." : $"{fog["Data"]!.AsArray().Count(n => n?.GetValue<int>() == 255):N0} fully revealed fog cells in this region.", 60);
                TextBlock("Revealing removes map fog. It does not open story gates, activate stations, or complete quests.", 85);
                var local = Btn("Reveal area fog", () => RevealWorld(false), true); local.Enabled = doc is not null && fog is not null && worldFog.Checked; Add(local, 55);
                TextBlock("Local fog alignment and area boundaries are experimental. Enable Fog preview to inspect the approximate coverage first.", 95);
                var all = Btn("Reveal whole region", () => RevealWorld(true)); all.Enabled = doc is not null && fog is not null; Add(all, 55);
                TextBlock("Whole-region reveal uses the complete saved grid and does not depend on preview alignment.", 85);
                return;
            }
            var quest = WorldCatalog.Quest(worldSelection);
            if (quest is null)
            {
                TextBlock(worldSelection.Length > 0 ? worldSelection : "Explore your world", 95, true);
                TextBlock(worldSelection.Length > 0 ? "This saved quest has no verified reward definition. Completion is unavailable to avoid writing invented rewards." : "Choose a yellow or blue quest marker to see its objectives and rewards. Choose Map areas to reveal exploration fog.", 140);
                TextBlock("Experimental quest editing: game scripts, story gates and reward rolls have not been verified in a live game session.", 115);
                return;
            }
            TextBlock(quest.Name, 95, true); string state = WorldEditing.QuestState(doc?.Character, quest.Id);
            TextBlock(quest.Area + " • " + state + (quest.Repeatable ? " • repeatable" : ""), 55);
            if (state is not ("Active" or "Available"))
                TextBlock(state == "Completed" ? "Completed — rewards already claimed." : state == "Not initialized" ? "Cannot complete yet: this quest has no task record in the opened save. Start it in-game, then Reload game progress." : "Cannot complete yet: finish the prerequisites in-game, then Reload game progress.", 115);
            TextBlock(quest.Description, 125);
            var first = new CheckBox { Text = "First time completing this repeatable quest", AutoSize = false, Checked = false };
            if (quest.Repeatable) Add(first, 58);
            var savedLevel = (doc?.Character["Ability"]?["Attributes"] as JsonArray)?.FirstOrDefault(a => a?["AttributeName"]?.GetValue<string>() == "Level")?["CurrentValue"];
            int level = (int)Math.Clamp(savedLevel is null ? 1 : WorldEditing.Number(savedLevel), 1, 100);
            var power = new NumericUpDown { Minimum = 1, Maximum = 100, Value = level };
            TextBlock("Reward gear power (1–100)", 35); Add(power, 42);
            TextBlock("REWARDS", 34);
            var rewardText = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
            void PreviewRewards()
            {
                bool initial = !quest.Repeatable || first.Checked;
                rewardText.Text = $"{(initial ? quest.XPFirst : quest.XPRepeat):N0} XP\r\nEmeralds: {quest.EmeraldBase} drops + up to {quest.EmeraldExtra} extra (random rolls)\r\n\r\n" +
                    string.Join("\r\n", (initial ? quest.FirstRewards : []).Concat(quest.Rewards).Select(r => "• " + r.Label + (r.Rarity.Length > 0 ? " (" + r.Rarity + ")" : "")));
            }
            first.CheckedChanged += (_, _) => PreviewRewards(); PreviewRewards(); Add(rewardText, 185);
            var complete = Btn("Complete quest", () => CompleteWorldQuest(quest, first.Checked, (int)power.Value), true);
            complete.Enabled = doc is not null && (state is "Available" or "Active") && WorldEditing.FindQuest(doc.Character, quest.Id)?["TaskData"] is JsonArray taskData && taskData.Count > 0;
            Add(complete, 60);
            TextBlock(state == "Completed" ? "Already completed. Reward claiming is disabled." : state is not ("Available" or "Active") ? "The button becomes available when the opened save contains an active or available quest with native tasks." : "Adds XP, emeralds and gear rewards together with completion as one undoable draft change. Rolls use the published pools.", 110);
            if (quest.Unlocks.Length > 0) TextBlock("Opens: " + string.Join(", ", quest.Unlocks.Select(id => WorldCatalog.Quest(id)?.Name ?? id)), 95);
            TextBlock("Game quest scripts and physical story gates may require completing the objective in-game. This feature is experimental.", 100);
            TextBlock("OBJECTIVES", 35); TextBlock(string.Join("\r\n", quest.Objectives.Select((o, i) => $"{i + 1}. {o}")), Math.Clamp(quest.Objectives.Length * 65, 90, 800));
        }
        finally { worldDetails.ResumeLayout(); Theme.Apply(worldDetails); }
    }
    void CompleteWorldQuest(QuestDefinition quest, bool first, int power)
    {
        if (!Ready()) return; QuestCompletion? result = null;
        doc!.Change(root => result = WorldEditing.Complete((JsonObject)root["CharacterSaveV1"]!, quest, first, power));
        RefreshAll(); status.Text = $"Draft: {quest.Name} completed • +{result!.XP:N0} XP • +{result.Emeralds:N0} emeralds • {result.Items.Length} gear rewards. Save to game to apply.";
    }
    void RevealWorld(bool entire)
    {
        if (!Ready() || worldRegion.SelectedItem is not WorldDefinition map) return;
        int changed = 0; int[]? cells = null;
        if (!entire)
        {
            if (!worldFog.Checked || worldMap.SelectedArea is null) return;
            cells = worldMap.AreaCells(worldMap.SelectedArea);
            if (cells.Length == 0) throw new InvalidDataException("No saved fog cells overlap this area in the preview. Use whole-region reveal.");
        }
        doc!.Change(root => changed = WorldEditing.Reveal((JsonObject)root["CharacterSaveV1"]!, map.Tag, cells));
        RefreshAll(); status.Text = $"Draft: revealed {changed:N0} fog cells in {map.Name}. Save to game to apply.";
    }
}
