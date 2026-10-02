using System.Reflection;
using System.Text.Json;

namespace MCD2SaveEdit;

public sealed class WorldMarker
{
    public string Id { get; set; } = "";
    public string Quest { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}
public sealed class WorldLabel
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}
public sealed class WorldDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Image { get; set; } = "";
    public double CropX { get; set; }
    public double CropY { get; set; }
    public double Divisor { get; set; }
    public double ScaleX { get; set; }
    public double ScaleY { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public WorldMarker[] Markers { get; set; } = [];
    public WorldLabel[] Labels { get; set; } = [];
    public override string ToString() => Name;
    public PointF ImagePoint(double x, double y) => new((float)(x / Divisor - CropX), (float)(y / Divisor - CropY));
    public PointF WorldPoint(double x, double y) => ImagePoint(y * ScaleX + OffsetX, x * ScaleY + OffsetY);
}
public sealed class QuestReward
{
    public string[] Tags { get; set; } = [];
    public double[] Weights { get; set; } = [];
    public string Rarity { get; set; } = "";
    public string Label { get; set; } = "";
}
public sealed class QuestDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Area { get; set; } = "";
    public bool Repeatable { get; set; }
    public int XPFirst { get; set; }
    public int XPRepeat { get; set; }
    public int EmeraldBase { get; set; }
    public int EmeraldExtra { get; set; }
    public double EmeraldChance { get; set; }
    public double BlockChance { get; set; }
    public string[] Prerequisites { get; set; } = [];
    public string[] Unlocks { get; set; } = [];
    public string[] Tasks { get; set; } = [];
    public string[] Objectives { get; set; } = [];
    public QuestReward[] FirstRewards { get; set; } = [];
    public QuestReward[] Rewards { get; set; } = [];
    public string Source { get; set; } = "";
    public override string ToString() => Name;
}
public static class WorldCatalog
{
    static T Load<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCD2SaveEdit.Data." + name)
            ?? throw new InvalidDataException("Missing world catalogue: " + name);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("Invalid world catalogue.");
    }
    public static readonly WorldDefinition[] Maps = Load<WorldDefinition[]>("maps.json");
    public static readonly QuestDefinition[] Quests = Load<QuestDefinition[]>("quests.json");
    static readonly Dictionary<string, Image> images = new();
    public static Image MapImage(string name)
    {
        if (images.TryGetValue(name, out var result)) return result;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCD2SaveEdit.Data.Maps." + Path.GetFileName(name))
            ?? throw new InvalidDataException("Missing map image: " + name);
        using var source = Image.FromStream(stream);
        return images[name] = new Bitmap(source);
    }
    public static QuestDefinition? Quest(string id) => Quests.FirstOrDefault(q => q.Id == id);
    public static bool InRegion(QuestDefinition quest, WorldDefinition map) => map.Markers.Any(m => m.Quest == quest.Id)
        || (map.Id == "sift" ? new[] { "Singer's Meadow", "Humbler Huskland", "Lullaby Hills" }.Contains(quest.Area)
        : !new[] { "Singer's Meadow", "Humbler Huskland", "Lullaby Hills" }.Contains(quest.Area));
}
