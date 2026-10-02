using System.Text;

namespace MCD2SaveEdit;

// Narrow, lossless parser for the version-14 index / version-4 blob table observed locally.
// Unrelated entries and opaque header fields are preserved as raw bytes.
public sealed class WgsContext
{
    // Resolve the active blob from the index; Xbox rotates blob names after game saves.
    public static IEnumerable<string> ActiveCharacters(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (string index in Directory.EnumerateFiles(root, "containers.index", SearchOption.AllDirectories))
        {
            var found = new List<string>();
            try
            {
                using var r = new BinaryReader(new MemoryStream(File.ReadAllBytes(index)));
                if (r.ReadInt32() != 14) continue;
                int count = r.ReadInt32(); if (count is < 0 or > 10000) continue;
                r.ReadInt32(); if (!ReadString(r).StartsWith("Microsoft.MinecraftDungeons2_", StringComparison.Ordinal)) continue;
                r.ReadInt64(); r.ReadInt32(); ReadString(r); r.ReadInt64();
                for (int i = 0; i < count; i++)
                {
                    string name = ReadString(r); ReadString(r); ReadString(r);
                    int sequence = r.ReadByte(); r.ReadInt32();
                    string folder = new Guid(r.ReadBytes(16)).ToString("N").ToUpperInvariant();
                    r.ReadInt64(); r.ReadInt64(); r.ReadInt64();
                    if (!name.StartsWith("Character", StringComparison.Ordinal)) continue;
                    string dir = Path.Combine(Path.GetDirectoryName(index)!, folder);
                    byte[] table = File.ReadAllBytes(Path.Combine(dir, "container." + sequence));
                    if (table.Length != 168 || BitConverter.ToInt32(table, 0) != 4 || BitConverter.ToInt32(table, 4) != 1) continue;
                    string blob = Path.Combine(dir, new Guid(table[152..168]).ToString("N").ToUpperInvariant());
                    if (File.Exists(blob)) found.Add(blob);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            foreach (string file in found) yield return file;
        }
    }
    public string AccountFolder { get; private init; } = "";
    public string IndexPath => Path.Combine(AccountFolder, "containers.index");
    public byte[] OriginalIndex { get; private init; } = [];
    public string ContainerPath { get; private init; } = "";
    public byte[] OriginalContainer { get; private init; } = [];
    public string Name { get; private init; } = "";
    int headerTime, cloudStart, dataStart, dataEnd;

    static string ReadString(BinaryReader reader)
    {
        int len = reader.ReadInt32();
        if (len < 0 || len > 65536) throw new InvalidDataException("Unsupported Xbox index string.");
        byte[] b = reader.ReadBytes(len * 2);
        if (b.Length != len * 2) throw new EndOfStreamException();
        return Encoding.Unicode.GetString(b);
    }
    public static WgsContext? TryOpen(string blob)
    {
        string folder = Path.GetDirectoryName(blob)!;
        string account = Path.GetDirectoryName(folder)!;
        string index = Path.Combine(account, "containers.index");
        if (!File.Exists(index)) return null;
        var bytes = File.ReadAllBytes(index);
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (r.ReadInt32() != 14) throw new InvalidDataException("Unsupported Xbox save index version.");
        int count = r.ReadInt32();
        if (count < 0 || count > 10000) throw new InvalidDataException("Invalid Xbox container count.");
        r.ReadInt32(); string package = ReadString(r);
        if (!package.StartsWith("Microsoft.MinecraftDungeons2_", StringComparison.Ordinal)) throw new InvalidDataException("This Xbox index belongs to another game.");
        int headerTime = (int)r.BaseStream.Position;
        r.ReadInt64(); r.ReadInt32(); ReadString(r); r.ReadInt64();
        WgsContext? match = null;
        for (int i = 0; i < count; i++)
        {
            string name = ReadString(r), name2 = ReadString(r);
            if (name != name2) throw new InvalidDataException("Xbox container names do not match.");
            int cloudStart = (int)r.BaseStream.Position; ReadString(r);
            int dataStart = (int)r.BaseStream.Position;
            int seq = r.ReadByte(); r.ReadInt32();
            var guid = r.ReadBytes(16);
            if (guid.Length != 16) throw new EndOfStreamException();
            string directory = new Guid(guid).ToString("N").ToUpperInvariant();
            r.ReadInt64(); r.ReadInt64(); long size = r.ReadInt64();
            int dataEnd = (int)r.BaseStream.Position;
            if (!string.Equals(directory, Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase)) continue;
            string container = Path.Combine(folder, "container." + seq);
            byte[] table = File.ReadAllBytes(container);
            using var cr = new BinaryReader(new MemoryStream(table));
            if (cr.ReadInt32() != 4 || cr.ReadInt32() != 1 || table.Length != 168)
                throw new InvalidDataException("Only single-blob Xbox character containers are supported.");
            string label = Encoding.Unicode.GetString(cr.ReadBytes(128)).TrimEnd('\0');
            cr.ReadBytes(16); var localGuid = cr.ReadBytes(16);
            if (label != "Data" || !string.Equals(new Guid(localGuid).ToString("N"), Path.GetFileName(blob), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This file is not the active Data blob for this character.");
            if (!name.StartsWith("Character", StringComparison.Ordinal) || size != new FileInfo(blob).Length)
                throw new InvalidDataException("Xbox character container name or size does not match.");
            match = new WgsContext { AccountFolder = account, OriginalIndex = bytes, ContainerPath = container,
                OriginalContainer = table, Name = name, headerTime = headerTime, cloudStart = cloudStart, dataStart = dataStart, dataEnd = dataEnd };
        }
        if (r.BaseStream.Position != bytes.Length) throw new InvalidDataException("Unexpected Xbox index trailer.");
        return match ?? throw new InvalidDataException("Character container was not found in the Xbox index.");
    }
    public void VerifyUnchanged()
    {
        if (!File.ReadAllBytes(IndexPath).SequenceEqual(OriginalIndex) || !File.ReadAllBytes(ContainerPath).SequenceEqual(OriginalContainer))
            throw new IOException("Xbox changed the save container. Export your edits and reopen the current character.");
    }
    public byte[] UpdatedIndex(int length, byte? sequence = null)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m, Encoding.UTF8, true);
        w.Write(OriginalIndex, 0, cloudStart);
        w.Write(0); // Clear old cloud revision; mark this local container pending synchronization.
        byte[] data = OriginalIndex[dataStart..dataEnd];
        if (sequence is not null) data[0] = sequence.Value;
        BitConverter.GetBytes(5).CopyTo(data, 1);
        long now = DateTime.UtcNow.ToFileTimeUtc();
        BitConverter.GetBytes(now).CopyTo(data, 21);
        BitConverter.GetBytes((long)length).CopyTo(data, 37);
        w.Write(data); w.Write(OriginalIndex, dataEnd, OriginalIndex.Length - dataEnd);
        byte[] result = m.ToArray(); BitConverter.GetBytes(now).CopyTo(result, headerTime); return result;
    }
    public byte[] UpdatedContainer(Guid? localBlob = null)
    {
        // A pending local edit must not retain the cloud blob identity from the
        // previous synchronized revision. Preserve the active local blob UUID.
        byte[] result = (byte[])OriginalContainer.Clone();
        Array.Clear(result, 136, 16);
        if (localBlob is not null) localBlob.Value.ToByteArray().CopyTo(result, 152);
        return result;
    }
    public (string Path, byte Sequence) NextContainer()
    {
        int current = int.Parse(System.IO.Path.GetExtension(ContainerPath)[1..]);
        for (int offset = 1; offset < 256; offset++)
        {
            byte sequence = (byte)((current + offset) % 256);
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ContainerPath)!, "container." + sequence);
            if (!File.Exists(path)) return (path, sequence);
        }
        throw new IOException("No free Xbox container revision. Reload after the game finishes synchronizing.");
    }
}
