using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace MCD2SaveEdit;

public sealed class SaveDocument
{
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    public JsonObject Root { get; private set; }
    public string FilePath { get; private set; }
    public byte[] OriginalBytes { get; private set; }
    public WgsContext? Wgs { get; private set; }
    readonly Stack<string> undo = new(), redo = new();
    string savedJson;
    public bool Dirty => Root.ToJsonString() != savedJson;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public JsonObject Character => (JsonObject)Root["CharacterSaveV1"]!;
    public JsonArray Items => (JsonArray)Character["Inventory"]!["Entries"]!;
    public string CharacterId => Character["MetaData"]?["CharacterId"]?.GetValue<string>() ?? "Unknown";

    public SaveDocument(string path)
    {
        FilePath = Path.GetFullPath(path);
        OriginalBytes = File.ReadAllBytes(FilePath);
        Root = Parse(OriginalBytes);
        savedJson = Root.ToJsonString();
        Wgs = WgsContext.TryOpen(FilePath);
    }

    public static JsonObject Parse(byte[] bytes)
    {
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Save exceeds the 32 MB limit.");
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        RejectDuplicateKeys(text);
        var root = JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Expected a character JSON object.");
        Validate(root);
        return root;
    }

    public static JsonNode? ParseFragment(string text)
    {
        RejectDuplicateKeys(text);
        return JsonNode.Parse(text);
    }

    static void RejectDuplicateKeys(string text)
    {
        using var doc = JsonDocument.Parse(text);
        void Check(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in e.EnumerateObject())
                {
                    if (!names.Add(p.Name)) throw new InvalidDataException($"Duplicate JSON key: {p.Name}");
                    Check(p.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) Check(v);
        }
        Check(doc.RootElement);
    }

    public static void Validate(JsonObject root)
    {
        if (root["SerializeMeta"]?["HardFormat"]?.GetValue<string>() != "FCharacterSaveV1" ||
            root["SerializeMeta"]?["SoftVersion"]?.GetValue<int>() != 5 ||
            root["SerializeMeta"]?["InternalVersion"]?.GetValue<int>() != 0)
            throw new InvalidDataException("Supported format: FCharacterSaveV1, internal version 0, soft version 5. This is not a supported MCD II character save.");
        if (root["CharacterSaveV1"] is not JsonObject c || c["MetaData"] is not JsonObject ||
            c["Inventory"]?["Entries"] is not JsonArray entries || c["Ability"]?["Attributes"] is not JsonArray)
            throw new InvalidDataException("Character metadata, inventory, or attributes are missing.");
        if (!Guid.TryParse(c["MetaData"]?["CharacterId"]?.GetValue<string>(), out _))
            throw new InvalidDataException("CharacterId must be a valid GUID.");
        var slots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is not JsonObject || entry["ItemData"] is not JsonObject data ||
                data["TypeTag"] is not JsonValue || data["Effects"] is not JsonArray)
                throw new InvalidDataException("Each inventory entry needs ItemData, TypeTag and an Effects array.");
            if (!data["TypeTag"]!.GetValue<string>().StartsWith("SW.Item.", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An item type must start with SW.Item.");
            void ValidateEffects(JsonNode? node)
            {
                if (node is JsonObject effect)
                {
                    if (effect.ContainsKey("Intensity") && (effect["Intensity"] is not JsonValue value || !value.TryGetValue<double>(out double strength) || !double.IsFinite(strength)))
                        throw new InvalidDataException($"Item {data["TypeTag"]}: effect strength must be a finite number. Repair missing strengths before saving.");
                    foreach (var field in effect) ValidateEffects(field.Value);
                }
                else if (node is JsonArray array) foreach (var child in array) ValidateEffects(child);
            }
            ValidateEffects(data["Effects"]); ValidateEffects(data["ItemProgression"]?["ItemLevels"]);
            if (entry["StackCount"] is not JsonValue count || !count.TryGetValue<int>(out int n) || n < 1)
                throw new InvalidDataException("Item stack counts must be positive whole numbers.");
            var slot = entry["EquippedSlot"]?.GetValue<string>() ?? throw new InvalidDataException("EquippedSlot is missing.");
            if (slot != "None" && !slots.Add(slot)) throw new InvalidDataException($"Two items occupy {slot}. Unequip one first.");
        }
    }

    public void Change(Action<JsonObject> edit)
    {
        var before = Root.ToJsonString();
        var next = (JsonObject)Root.DeepClone();
        edit(next);
        Validate(next);
        if (next["SerializeMeta"]!.ToJsonString() != Root["SerializeMeta"]!.ToJsonString())
            throw new InvalidDataException("The save format metadata must remain unchanged.");
        if (next["CharacterSaveV1"]!["MetaData"]!["CharacterId"]!.ToJsonString() != Root["CharacterSaveV1"]!["MetaData"]!["CharacterId"]!.ToJsonString())
            throw new InvalidDataException("The character identity must remain unchanged for this save slot.");
        if (next.ToJsonString() == before) return;
        undo.Push(before); redo.Clear(); Root = next;
    }

    public void Replace(string[] path, JsonNode? value) => Change(r => Set(r, path, value?.DeepClone()));
    public static JsonNode? Get(JsonNode? node, IEnumerable<string> path)
    {
        foreach (var key in path) node = node is JsonArray a ? a[int.Parse(key)] : node?[key];
        return node;
    }
    public static void Set(JsonObject root, string[] path, JsonNode? value)
    {
        if (path.Length == 0)
        {
            if (value is not JsonObject obj) throw new InvalidDataException("Root must be an object.");
            root.Clear(); foreach (var p in obj.ToArray()) root[p.Key] = p.Value?.DeepClone(); return;
        }
        var parent = Get(root, path.Take(path.Length - 1));
        if (parent is JsonArray arr) arr[int.Parse(path[^1])] = value;
        else if (parent is JsonObject ob) ob[path[^1]] = value;
        else throw new InvalidDataException("The selected value no longer exists.");
    }
    public void Undo() { if (!CanUndo) return; redo.Push(Root.ToJsonString()); Root = (JsonObject)JsonNode.Parse(undo.Pop())!; }
    public void Redo() { if (!CanRedo) return; undo.Push(Root.ToJsonString()); Root = (JsonObject)JsonNode.Parse(redo.Pop())!; }
    public byte[] Serialize() => Dirty ? Encoding.UTF8.GetBytes(Root.ToJsonString()) : OriginalBytes;
    public void MarkSaved(string? path = null)
    {
        if (path is not null) FilePath = path;
        OriginalBytes = File.ReadAllBytes(FilePath); savedJson = Root.ToJsonString();
        Wgs = WgsContext.TryOpen(FilePath);
    }

    public static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    public static string Label(string tag) => System.Text.RegularExpressions.Regex.Replace(tag.Split('.').Last(), "(?<=[a-z0-9])([A-Z])", " $1");
    public static string Category(JsonNode? entry)
    {
        string tag = entry?["ItemData"]?["TypeTag"]?.GetValue<string>() ?? "";
        string slot = entry?["EquippedSlot"]?.GetValue<string>() ?? "";
        string target = entry?["ItemData"]?["TargetSlotOverride"]?.GetValue<string>() ?? "";
        if (target.Contains("Merchant")) return "Merchant";
        if (Catalog.Find(tag) is { } definition)
            return definition.Category switch { "Melee" or "Ranged" => "Weapon", "Helmet" or "Chest" or "Leggings" or "Boots" => "Armor", _ => definition.Category };
        if (tag.Contains(".Cosmetic.")) return "Cosmetic";
        if (tag.Contains(".Artifact.")) return "Artifact";
        if (tag.Contains("Talisman")) return "Talisman";
        if (tag.Contains("Enchantment")) return "Enchantment";
        if (slot.Contains("Armor") || new[] { "Helmet", "Chestplate", "Boots", "Leggings", "Armor" }.Any(tag.Contains)) return "Armor";
        if (slot.Contains("Weapon") || new[] { "Sword", "Bow", "Axe", "Spear", "Hammer", "Dagger" }.Any(tag.Contains)) return "Weapon";
        return "Other";
    }
}

public static class SaveFiles
{
    public static void EnsureGameClosed()
    {
        foreach (var p in System.Diagnostics.Process.GetProcesses())
            using (p)
                if (p.ProcessName.Contains("Dungeons", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Close Minecraft Dungeons II completely, then click Save to game again. Your edits are still here.");
    }
    public static SaveDocument ResolveGameTarget(SaveDocument source, string? root = null)
    {
        root ??= source.Wgs?.AccountFolder ?? DefaultWgs;
        var matches = WgsContext.ActiveCharacters(root).Select(path => new SaveDocument(path))
            .Where(d => d.CharacterId == source.CharacterId).ToArray();
        if (matches.Length != 1) throw new IOException(matches.Length == 0
            ? "No active Xbox character matches this file. Use Find Xbox saves to open the character first."
            : "More than one Xbox character matches. Open the desired character with Find Xbox saves.");
        if (!JsonNode.DeepEquals(source.Root["SerializeMeta"], matches[0].Root["SerializeMeta"]))
            throw new InvalidDataException("The export and the current game save use different formats.");
        return matches[0];
    }
    public static bool GameContentChanged(SaveDocument source, SaveDocument current) =>
        !JsonNode.DeepEquals(SaveDocument.Parse(source.OriginalBytes), current.Root);
    public static (SaveDocument Document, string Backup) SaveToGame(SaveDocument source, bool replaceChanged = false, bool checkGame = true, string? root = null)
    {
        if (checkGame) EnsureGameClosed();
        var current = ResolveGameTarget(source, root);
        if (source.Wgs is not null && GameContentChanged(source, current) && !replaceChanged)
            throw new SaveConflictException("The game has saved newer progress since you opened this character. Review or replace it with your edits.");
        current.Replace([], source.Root);
        string backup = Save(current, checkGame);
        return (current, backup);
    }
    public static string DefaultWgs => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "Microsoft.MinecraftDungeons2_8wekyb3d8bbwe", "SystemAppData", "wgs");
    public static IEnumerable<string> Discover(string root)
    {
        if (!Directory.Exists(root)) yield break;
        // Never offer inactive blobs left behind by Xbox as editable characters.
        if (Directory.EnumerateFiles(root, "containers.index", SearchOption.AllDirectories).Any())
        {
            foreach (var file in WgsContext.ActiveCharacters(root))
            {
                bool valid = false;
                try { _ = new SaveDocument(file); valid = true; } catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException) { }
                if (valid) yield return file;
            }
            yield break;
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith("container", StringComparison.OrdinalIgnoreCase)) continue;
            bool valid = false;
            try
            {
                var info = new FileInfo(file);
                if (info.Length is > 0 and < 33554432)
                {
                    using var f = File.OpenRead(file);
                    if (f.ReadByte() == '{') { _ = SaveDocument.Parse(File.ReadAllBytes(file)); valid = true; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or FormatException) { }
            if (valid) yield return file;
        }
    }
    public static void AtomicWrite(string path, byte[] bytes)
    {
        string temp = path + ".mcd2-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(bytes); fs.Flush(true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string BackupFolder => Path.Combine(AppContext.BaseDirectory, "Backups");
    public static string Save(SaveDocument doc, bool checkGame = true)
    {
        SaveDocument.Validate(doc.Root);
        if (checkGame) EnsureGameClosed();
        if (!File.ReadAllBytes(doc.FilePath).SequenceEqual(doc.OriginalBytes))
            throw new IOException("The game or another program changed this save. Export your edits, then reopen the current save.");
        doc.Wgs?.VerifyUnchanged();
        string backup = Path.Combine(BackupFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        string sourceRoot = doc.Wgs?.AccountFolder ?? Path.GetDirectoryName(doc.FilePath)!;
        var files = doc.Wgs is null ? new[] { doc.FilePath } : Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        foreach (string src in files)
        {
            string target = Path.Combine(backup, "original", Path.GetRelativePath(sourceRoot, src));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var content = File.ReadAllBytes(src); File.WriteAllBytes(target, content);
            if (!File.ReadAllBytes(target).SequenceEqual(content)) throw new IOException("Backup verification failed.");
        }
        File.WriteAllText(Path.Combine(backup, "restore-info.json"), JsonSerializer.Serialize(new { sourceRoot, characterFile = doc.FilePath, createdUtc = DateTime.UtcNow, format = "MCD2SaveEdit backup v1" }, SaveDocument.Pretty));
        // Recheck after copying: never silently overwrite changes made during backup.
        if (!File.ReadAllBytes(doc.FilePath).SequenceEqual(doc.OriginalBytes)) throw new IOException("Save changed while making the backup. No edits were written.");
        doc.Wgs?.VerifyUnchanged();
        if (doc.Wgs is not null)
        {
            // The game uses .NET ticks for its character revision timestamp.
            // Exporting remains lossless; a game write creates a newer revision.
            long previous = doc.Character["MetaData"]?["GameDataUpdated"]?.GetValue<long>() ?? 0;
            long revision = Math.Max(DateTime.UtcNow.Ticks, checked(previous + 1));
            doc.Change(root => root["CharacterSaveV1"]!["MetaData"]!["GameDataUpdated"] = revision);
        }
        var bytes = doc.Serialize(); _ = SaveDocument.Parse(bytes);
        Guid newBlob = Guid.NewGuid();
        var nextContainer = doc.Wgs?.NextContainer();
        string writeBlob = doc.Wgs is null ? doc.FilePath : Path.Combine(Path.GetDirectoryName(doc.FilePath)!, newBlob.ToString("N").ToUpperInvariant());
        string? writeContainer = nextContainer?.Path;
        var indexBytes = doc.Wgs?.UpdatedIndex(bytes.Length, nextContainer?.Sequence);
        var containerBytes = doc.Wgs?.UpdatedContainer(newBlob);
        byte[] originalBlob = doc.OriginalBytes;
        WgsContext? originalContext = doc.Wgs;
        bool blobWritten = false, containerWritten = false, indexWritten = false;
        try
        {
            AtomicWrite(writeBlob, bytes); blobWritten = true;
            if (containerBytes is not null) { AtomicWrite(writeContainer!, containerBytes); containerWritten = true; }
            if (indexBytes is not null) { AtomicWrite(doc.Wgs!.IndexPath, indexBytes); indexWritten = true; }
            if (!File.ReadAllBytes(writeBlob).SequenceEqual(bytes)) throw new IOException("Save verification failed.");
            if (indexBytes is not null && !File.ReadAllBytes(doc.Wgs!.IndexPath).SequenceEqual(indexBytes)) throw new IOException("Index verification failed.");
            if (containerBytes is not null && !File.ReadAllBytes(writeContainer!).SequenceEqual(containerBytes)) throw new IOException("Blob table verification failed.");
            _ = new SaveDocument(writeBlob);
            doc.MarkSaved(writeBlob); return backup;
        }
        catch (Exception ex)
        {
            try
            {
                if (indexWritten) AtomicWrite(originalContext!.IndexPath, originalContext.OriginalIndex);
                if (containerWritten) File.Delete(writeContainer!);
                if (blobWritten) { if (originalContext is null) AtomicWrite(doc.FilePath, originalBlob); else File.Delete(writeBlob); }
            }
            catch (Exception rollback) { throw new IOException($"Save and rollback failed. Restore the backup at {backup}. {rollback.Message}", ex); }
            throw new IOException($"Save failed; original files restored. Backup: {backup}. {ex.Message}", ex);
        }
    }
}

public sealed class SaveConflictException(string message) : IOException(message);
