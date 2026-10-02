using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCD2SaveEdit;

public sealed class GearDefinition
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Unique { get; set; }
    public string Icon { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Source { get; set; } = "";
    public JsonArray Effects { get; set; } = [];
    public JsonArray Levels { get; set; } = [];
    public string[] Pool { get; set; } = [];
    public override string ToString() => Name;
    public JsonObject Create(int power, int heroLevel)
    {
        return new JsonObject
        {
            ["ItemData"] = new JsonObject
            {
                ["TypeTag"] = Tag, ["RarityTag"] = "SW.Rarity." + (Unique ? "Unique" : Category == "Talisman" ? "None" : "Common"),
                ["Effects"] = Effects.DeepClone(),
                ["ItemProgression"] = new JsonObject { ["CurrentLevel"] = 0, ["CurrentXP"] = 0, ["ItemLevels"] = Levels.DeepClone() },
                ["GeneratorData"] = new JsonObject
                {
                    ["GenesisRandomSeed"] = Random.Shared.NextInt64(0, (long)uint.MaxValue + 1),
                    ["PowerGeneratorValues"] = new JsonObject
                    {
                        ["PlayerLevel"] = heroLevel, ["AreaThreatLevel"] = 1, ["RecommendedThreatLevel"] = 1,
                        ["ThreatSliderOffset"] = 0, ["ItemPowerMin"] = power, ["ItemPowerMax"] = power,
                        ["RNGRoll"] = 0, ["ItemPower"] = Category == "Talisman" ? -1 : power, ["ItemPowerOriginal"] = Category == "Talisman" ? 0 : power
                    }
                },
                ["DynamicPropertyTags"] = new JsonArray(), ["TargetSlotOverride"] = "None",
                ["PickupTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["EffectRerolls"] = 0
            },
            ["StackCount"] = 1, ["EquippedSlot"] = "None", ["MerchantItemSold"] = false, ["MerchantDiscount"] = 0
        };
    }
}

public sealed class EffectDefinition
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Effect { get; set; } = "";
    public string Tier { get; set; } = "";
    public double Value { get; set; }
    public string Icon { get; set; } = "";
    public JsonObject Create() => new()
    {
        ["TypeTag"] = Effect, ["Intensity"] = Value, ["Quality"] = 0, ["EnchantmentPointsInvested"] = 0,
        ["GeneratorData"] = new JsonObject { ["GeneratorParentTemplate"] = Tag, ["Locked"] = false }
    };
    public override string ToString() => Name + "  " + Tier;
}

public static class Catalog
{
    static T Load<T>(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCD2SaveEdit.Data." + file)
            ?? throw new InvalidDataException("The built-in catalogue is missing: " + file);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("Invalid catalogue.");
    }
    public static readonly GearDefinition[] Gear = Load<GearDefinition[]>("gear.json").OrderBy(g => g.Name).ToArray();
    public static readonly EffectDefinition[] Effects = Load<EffectDefinition[]>("effects.json");
    public static readonly EnchantmentDefinition[] Enchantments = Load<EnchantmentDefinition[]>("enchants.json");
    static readonly Dictionary<string, GearDefinition> byTag = Gear.ToDictionary(x => x.Tag, StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Image?> icons = new(StringComparer.Ordinal);
    public static GearDefinition? Find(string tag) => byTag.GetValueOrDefault(tag);
    public static string Name(string tag) => Find(tag)?.Name ?? (BookEnchantment(tag)?.Name is { } name ? name + " Book" : SaveDocument.Label(tag));
    static EnchantmentDefinition? BookEnchantment(string tag) => tag.StartsWith("SW.Item.EnchantmentBook.", StringComparison.OrdinalIgnoreCase)
        ? Enchantments.FirstOrDefault(e => e.Tag == "SW.Enchantment." + tag.Split('.').Last()) : null;
    public static string EffectName(string tag) => Enchantments.FirstOrDefault(x => x.Tag == tag)?.Name ?? Effects.FirstOrDefault(x => x.Effect == tag)?.Name ?? SaveDocument.Label(tag);
    public static Image? Icon(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return null;
        if (icons.TryGetValue(filename, out var image)) return image;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCD2SaveEdit.Data.Icons." + Path.GetFileName(filename));
        if (stream is null) return icons[filename] = null;
        using var original = Image.FromStream(stream);
        return icons[filename] = new Bitmap(original);
    }
    public static Image? ItemIcon(string tag) => Icon(Find(tag)?.Icon ?? BookEnchantment(tag)?.Icon ?? "");
}

public sealed class EnchantmentTier
{
    public string Tier { get; set; } = "";
    public double? Value { get; set; }
}
public sealed class EnchantmentDefinition
{
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Icon { get; set; } = "";
    public string[] Slots { get; set; } = [];
    public EnchantmentTier[] Tiers { get; set; } = [];
    public override string ToString() => Name;
}

public static class ItemEditing
{
    public static void SetEnchantment(JsonObject entry, EnchantmentDefinition enchantment, string tier, double intensity)
    {
        if (!enchantment.Tiers.Any(x => x.Tier == tier) || !double.IsFinite(intensity)) throw new InvalidDataException("Invalid enchantment tier or strength.");
        var gear = Catalog.Find(entry["ItemData"]!["TypeTag"]!.GetValue<string>());
        if (gear is not null && !enchantment.Slots.Contains(gear.Slot)) throw new InvalidDataException("This enchantment does not support the selected equipment slot.");
        var batches = (JsonArray)entry["ItemData"]!["Effects"]!;
        foreach (var b in batches.Where(x => x?["TypeTag"]?.GetValue<string>() == "SW.Item.Effect.Enchantment").ToArray()) batches.Remove(b);
        batches.Add(new JsonObject
        {
            ["TypeTag"] = "SW.Item.Effect.Enchantment",
            ["EffectsInThisBatch"] = new JsonArray(new JsonObject
            {
                ["TypeTag"] = enchantment.Tag, ["Intensity"] = intensity, ["Quality"] = 0, ["EnchantmentPointsInvested"] = 0,
                ["GeneratorData"] = new JsonObject { ["GeneratorParentTemplate"] = enchantment.Tag + "." + tier, ["Locked"] = false }
            })
        });
    }
}
