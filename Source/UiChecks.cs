namespace MCD2SaveEdit;

public sealed partial class MainForm
{
    // In-process control tests only: no window is shown and no desktop input is injected.
    internal void CheckControls(string fixture, Action<bool, string> check)
    {
        LoadDocument(fixture);
        check(doc is not null && inventory.Items.Count == doc.Items.Count, "UI model lists all real inventory entries");
        check(itemTiles.Controls.OfType<GearTile>().Count() == doc!.Items.Count, "Classic inventory displays a tile for every saved item");
        check(armorTiles.Controls.OfType<GearTile>().Count() >= 4, "Classic loadout displays four separate armor positions");
        var expectedEmeralds = ((System.Text.Json.Nodes.JsonArray)doc.Character["Ability"]!["Attributes"]!).First(x => x!["AttributeName"]!.GetValue<string>() == "Emeralds")!["CurrentValue"]!;
        check(emeralds.Enabled && emeralds.Value == decimal.Parse(expectedEmeralds.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture), "Classic currency control reads real emeralds");
        ShowItem(0);
        check(itemFields.Controls.Count >= 8 && itemJson.Text.Contains("ItemProgression"), "UI builds compact gear power, level and full-item controls");
        check(heroFields.Controls.Count >= 10, "UI builds character attributes including emeralds");
        check(armorTiles.Controls.OfType<GearTile>().Count() >= 4, "UI keeps the four-piece armor loadout");
        check(effectItem.Items.Count == doc!.Items.Count && effectsJson.Text == "[]", "UI opens real empty effects without fabricating enchantments");
        check(tabs.TabPages.Cast<TabPage>().Any(p => p.Text == "Quests / Map"), "Quest and map editor is available");
        worldRegion.SelectedIndex = 1; RefreshWorld();
        check(worldMap.Map?.Id == "sift" && questList.Items.Count > 0, "Sift map and quest list open without changing the save");
        worldSelection = "CA09"; SelectWorldEntry();
        check(worldDetails.Controls.OfType<Label>().Any(l => l.Text == "Wrath of the Sculkers"), "Quest details resolve the actual displayed game name");
        worldMode.SelectedIndex = 1;
        worldSelection = "area:Singer's Meadow"; SelectWorldEntry();
        check(worldDetails.Controls.OfType<Button>().Any(b => b.Text.Contains("Reveal whole region")), "Map area details include a separate region reveal action");
        worldRegion.SelectedIndex = 0; RefreshWorld();
        check(echoShards.Enabled, "Echo Shards control binds to the saved currency");
        using var picker = new PickerForm("Select item", Catalog.Gear.Select(x => new PickerEntry(x.Name, x.Category, x.Icon, x.Unique, x)));
        check(picker.VisibleCount == 296, "Picker contains all 296 catalogue gear records");
        picker.Filter("The Burning Blade"); check(picker.VisibleCount == 1, "Picker searches by the in-game unique name");
        picker.Filter("", "Helmet"); check(picker.VisibleCount == 38, "Picker filters all helmet definitions");
        check(itemTitle.Text == Catalog.Name(doc.Items[0]!["ItemData"]!["TypeTag"]!.GetValue<string>()), "Selected item uses its in-game display name");
        check(tree.Nodes.Count == 1 && changes.Items.Count == 0 && !doc.Dirty, "Opening every editor leaves the save unchanged");
        foreach (var size in new[] { MinimumSize, new Size(1640, 1000), new Size(3000, 1800) })
        {
            Size = size; PerformLayout(); ResizeInventoryColumns(); ShowItem(0);
            var fields = itemFields.Controls.OfType<Panel>().ToArray();
            check(fields.All(p => p.Controls.Count == 2 && p.Controls[0].Bottom <= p.Controls[1].Top &&
                p.Controls[1].Bottom <= p.ClientSize.Height && p.Controls[1].Right <= p.ClientSize.Width),
                $"Item labels and inputs have separate, contained bounds at window width {size.Width}");
            check(itemTiles.Controls.OfType<GearTile>().All(t => t.Width <= (int)(152 * t.DeviceDpi / 96.0)),
                $"Inventory cards stay within the size cap at window width {size.Width}");
            IEnumerable<Control> Descendants(Control parent)
            {
                foreach (Control child in parent.Controls)
                {
                    yield return child;
                    foreach (var nested in Descendants(child)) yield return nested;
                }
            }
            var captions = Descendants(tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Inventory")).OfType<Label>().Where(l => new[]
                { "Emeralds", "Echo Shards", "Enchantment points", "EQUIPMENT", "ARTIFACTS / TALISMANS", "ENCHANTMENTS / EFFECTS" }.Contains(l.Text)).ToArray();
            check(captions.Length == 6 && captions.All(l => !l.UseMnemonic && l.ClientSize.Height >=
                TextRenderer.MeasureText(l.Text, l.Font, new Size(10000, 10000), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height),
                $"Inventory captions have enough height to render without clipping at window width {size.Width}: " +
                string.Join(", ", captions.Select(l => $"{l.Text}={l.ClientSize.Height}px/{l.Font.Height}px")));
            check(inventoryCount.Top >= itemTiles.Bottom && inventoryCount.Height >= inventoryCount.Font.Height + 8,
                $"Inventory count is separate from the scrolling cards at window width {size.Width}");
        }
        check(navigationButtons.Any(p => p.Key.Text == "Quests / Map" && p.Value.Text == "Quests / Map" && !p.Value.UseMnemonic),
            "Navigation displays Quests / Map without a shortcut underline");
        check(!doc.Dirty, "Resizing and rebuilding item fields leaves character data unchanged");
    }
}
