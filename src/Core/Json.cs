using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(PackManifest))]
[JsonSerializable(typeof(Scenario))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(Command))]
[JsonSerializable(typeof(SheetDef))]
[JsonSerializable(typeof(List<SheetDef>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Stroke))]
[JsonSerializable(typeof(PieceInstance))]
[JsonSerializable(typeof(SaveFile))]
[JsonSerializable(typeof(TileIndex))]
[JsonSerializable(typeof(ReliefIndex))]
[JsonSerializable(typeof(List<Command>))]
[JsonSerializable(typeof(InstallJournal.Record))]
[JsonSerializable(typeof(RunMark))]
[JsonSerializable(typeof(SavesInTrash))]
[JsonSerializable(typeof(SavePreview))]
internal partial class KJsonContext : JsonSerializerContext { }

public static class Json
{
    static readonly KJsonContext Ctx = KJsonContext.Default;
    static readonly KJsonContext Pretty = new(new JsonSerializerOptions(KJsonContext.Default.Options) { WriteIndented = true });

    public static string Serialize<T>(T value, bool indented = false) =>
        JsonSerializer.Serialize(value, typeof(T), indented ? Pretty : Ctx);

    public static byte[] SerializeBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, typeof(T), Ctx);

    public static T Deserialize<T>(string json) =>
        (T)(JsonSerializer.Deserialize(json, typeof(T), Ctx) ?? throw new JsonException("null"));

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8) =>
        (T)(JsonSerializer.Deserialize(WithoutBom(utf8), typeof(T), Ctx) ?? throw new JsonException("null"));

    static ReadOnlySpan<byte> WithoutBom(ReadOnlySpan<byte> utf8) =>
        utf8.StartsWith(Encoding.UTF8.Preamble) ? utf8[Encoding.UTF8.Preamble.Length..] : utf8;

    public static T Clone<T>(T value) => Deserialize<T>(SerializeBytes(value));

    public static T Load<T>(string path) => Deserialize<T>(Unpacked(File.ReadAllBytes(path)));

    public static bool IsPacked(ReadOnlySpan<byte> bytes) => bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

    public static byte[] Unpacked(byte[] bytes)
    {
        if (!IsPacked(bytes)) return bytes;
        using var z = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
        using var plain = new MemoryStream();
        z.CopyTo(plain);
        return plain.ToArray();
    }

    public static byte[] Packed(byte[] plain)
    {
        using var packed = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) z.Write(plain);
        return packed.ToArray();
    }

    public static byte[] PackedBytes<T>(T value) => Packed(SerializeBytes(value));

    public static byte[] Head(string path, int bytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var magic = new byte[2];
        int got = fs.Read(magic, 0, 2);
        fs.Position = 0;
        using Stream from = IsPacked(magic.AsSpan(0, got)) ? new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true) : fs;
        var buf = new byte[bytes];
        int n = 0;
        while (n < bytes)
        {
            int r = from.Read(buf, n, bytes - n);
            if (r <= 0) break;
            n += r;
        }
        return buf.AsSpan(0, n).ToArray();
    }

    public sealed record SaveHeadline(string ScenarioId, string Saved, SavePreview Preview);

    public static SaveHeadline? ReadSaveHeadline(string path, int bytes = 1 << 18)
    {
        var head = Head(path, bytes);
        var r = new Utf8JsonReader(WithoutBom(head), isFinalBlock: false, state: default);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) throw new JsonException("a save is an object");
        string scenarioId = "", saved = "";
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            var name = r.GetString();
            if (!r.Read()) return null;
            if (name == "preview")
            {
                var whole = r;
                if (!whole.TrySkip()) return null;
                var preview = (SavePreview?)JsonSerializer.Deserialize(ref r, typeof(SavePreview), Ctx);
                return preview == null ? null : new(scenarioId, saved, preview);
            }
            if (name is "access" or "snapshot" or "commands" or "turns") return null;
            if (name == "scenarioId" && r.TokenType == JsonTokenType.String) scenarioId = r.GetString() ?? "";
            else if (name == "saved" && r.TokenType == JsonTokenType.String) saved = r.GetString() ?? "";
            else if (!r.TrySkip()) return null;
        }
        return null;
    }

    public static (Scenario head, int pieces) ScenarioCard(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var r = new Utf8JsonReader(WithoutBom(bytes));
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) throw new JsonException("a scenario is an object");
        var s = new Scenario();
        int pieces = 0;
        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var name = r.GetString();
            r.Read();
            switch (name)
            {
                case "format":
                    s.Format = r.GetInt32();
                    break;
                case "apiVersion":
                    s.ApiVersion = r.GetInt32();
                    break;
                case "id":
                    s.Id = r.GetString()!;
                    break;
                case "name":
                    s.Name = r.GetString()!;
                    break;
                case "packs":
                    s.Packs = (List<string>)JsonSerializer.Deserialize(ref r, typeof(List<string>), Ctx)!;
                    break;
                case "sheets":
                    s.Sheets = (List<SheetDef>)JsonSerializer.Deserialize(ref r, typeof(List<SheetDef>), Ctx)!;
                    break;
                case "umpireBoard":
                    if (r.TokenType == JsonTokenType.Null) { pieces = -1; break; }
                    if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("the board is a list");
                    pieces = 0;
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray) { pieces++; r.Skip(); }
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        ContentVersions.Check("scenario", s.Name is { Length: > 0 } ? s.Name : s.Id, s.Format, Scenario.CurrentFormat, s.ApiVersion);
        return (s, pieces);
    }

    public static (string? name, string? scenarioId, GameClock? clock, string? mirrorOf) SaveCard(string path)
    {
        var bytes = Unpacked(File.ReadAllBytes(path));
        var r = new Utf8JsonReader(WithoutBom(bytes));
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) throw new JsonException("a save is an object");
        string? name = null, scenarioId = null, mirrorOf = null;
        GameClock? clock = null;
        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "scenarioId":
                    scenarioId = r.GetString();
                    break;
                case "mirrorOf":
                    mirrorOf = r.GetString();
                    break;
                case "snapshot":
                    if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("the table in a save is an object");
                    while (r.Read() && r.TokenType != JsonTokenType.EndObject)
                    {
                        var inner = r.GetString();
                        r.Read();
                        switch (inner)
                        {
                            case "name":
                                name = r.GetString();
                                break;
                            case "clock":
                                clock = (GameClock?)JsonSerializer.Deserialize(ref r, typeof(GameClock), Ctx);
                                break;
                            default:
                                r.Skip();
                                break;
                        }
                    }
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        if (r.TokenType != JsonTokenType.EndObject) throw new JsonException("the save ends early");
        return (name, scenarioId, clock ?? new GameClock(), mirrorOf);
    }

    public static void Save<T>(string path, T value) => WriteAtomic(path, Serialize(value, true));

    public static void SaveBig<T>(string path, T value, string? tmpToken = null) => WriteAtomic(path, SerializeBytes(value), tmpToken);

    public static byte[] BigBytes<T>(T value) => SerializeBytes(value);

    public static string? PeekString(string path, string property, int bytes = 1 << 16)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[Math.Min(bytes, (int)Math.Min(int.MaxValue, fs.Length))];
            int n = fs.Read(buf, 0, buf.Length);
            var head = new UTF8Encoding(false).GetString(buf, 0, n);
            var key = "\"" + property + "\"";
            int i = head.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i = head.IndexOf(':', i + key.Length);
            if (i < 0) return null;
            while (++i < head.Length && char.IsWhiteSpace(head[i])) { }
            if (i >= head.Length || head[i] != '"') return null;
            int end = head.IndexOf('"', i + 1);
            return end < 0 ? null : head[(i + 1)..end];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void WriteAtomic(string path, string text) => WriteAtomic(path, new UTF8Encoding(false).GetBytes(text));

    public static void WriteAtomic(string path, byte[] bytes, string? tmpToken = null)
    {
        var full = Path.GetFullPath(path);
        var tmp = WriteTemp(full, bytes, tmpToken);
        try { Land(tmp, full); }
        catch when (tmpToken != null) { Discard(tmp); throw; }
    }

    public static string WriteTemp(string path, byte[] bytes, string? tmpToken)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = tmpToken == null ? full + ".tmp" : $"{full}.{tmpToken}.tmp";
        try
        {
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        catch when (tmpToken != null) { Discard(tmp); throw; }
        return tmp;
    }

    public static void CopyAtomic(string from, string to, bool overwrite = true)
    {
        var full = Path.GetFullPath(to);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = full + ".tmp";
        try
        {
            using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16))
            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                src.CopyTo(dst);
                dst.Flush(flushToDisk: true);
            }
            File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(from));
            Land(tmp, full, overwrite);
        }
        catch
        {
            Discard(tmp);
            throw;
        }
    }

    public static void Land(string tmp, string path, bool overwrite = true)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, path, overwrite); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 5 && (overwrite || !File.Exists(path))) { Thread.Sleep(40 * (attempt + 1)); }
        }
    }

    public static void Discard(string tmp)
    {
        BestEffort.DeleteFile(tmp);
    }
}
