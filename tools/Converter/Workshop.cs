using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KTable.Converter;

public static class Workshop
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string? IdOf(string s)
    {
        var m = Regex.Match(s, @"(?:id=)?(\d{6,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static async Task<string> Resolve(Config cfg, string src)
    {
        if (File.Exists(src)) return Path.GetFullPath(src);
        bool fresh = src.StartsWith("steam:");
        var id = IdOf(src) ?? throw new FileNotFoundException($"'{src}' is neither a file nor a Steam Workshop id/link");
        var local = Path.Combine(cfg.WorkshopDir, id + ".json");
        if (!fresh && File.Exists(local)) return local;
        var cached = Path.Combine(cfg.Cache, "workshop", id + ".json");
        if (!fresh && File.Exists(cached)) return cached;

        Console.WriteLine($"Downloading Steam Workshop item {id}…");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["itemcount"] = "1", ["publishedfileids[0]"] = id });
        var resp = await Http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", form);
        resp.EnsureSuccessStatusCode();
        var details = JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["response"]?["publishedfiledetails"]?[0];
        if (details?["result"]?.GetValue<int>() != 1) throw new InvalidDataException($"Steam will not hand out workshop item {id}: it is unlisted, friends-only, private or removed. Subscribe to it in your digital tabletop, then import it from this PC.");
        if (details["consumer_app_id"]?.GetValue<int>() is int app && app != 286160) throw new InvalidDataException($"workshop item {id} is not a digital tabletop module (app {app})");
        var url = details["file_url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(url)) throw new InvalidDataException($"Steam gives no download link for {id}; subscribe to it once in your digital tabletop, then import it from this PC");
        var title = details["title"]?.GetValue<string>();
        var size = long.TryParse(details["file_size"]?.ToString(), out var stated) ? stated : 0;
        Progress.Stage("module", 1, size);
        Progress.On(title);
        var bytes = await Fetch(url);
        Progress.Step();
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
        var text = LooksLikeJson(bytes) ? Encoding.UTF8.GetString(bytes) : Bson.ToJson(bytes).ToJsonString();
        var tmp = cached + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        try
        {
            await File.WriteAllTextAsync(tmp, text);
            File.Move(tmp, cached, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        Console.WriteLine($"  {title} ({bytes.Length / 1024} KB)");
        return cached;
    }

    static async Task<byte[]> Fetch(string url)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buf = new byte[1 << 16];
        for (int got; (got = await s.ReadAsync(buf)) > 0; )
        {
            ms.Write(buf, 0, got);
            Progress.Bytes(got);
            if (ms.Length > KTable.Core.ContentLimits.MaxFileBytes) throw new InvalidDataException($"the Workshop item is over {KTable.Core.ContentLimits.Describe(KTable.Core.ContentLimits.MaxFileBytes)}");
        }
        return ms.ToArray();
    }

    static bool LooksLikeJson(byte[] b)
    {
        foreach (var c in b.Take(64)) { if (c is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0xEF or 0xBB or 0xBF) continue; return c == '{'; }
        return false;
    }
}

public static class Bson
{
    public static JsonObject ToJson(byte[] data)
    {
        int pos = 0;
        return (JsonObject)ReadDoc(data, ref pos, isArray: false);
    }

    static JsonNode ReadDoc(byte[] d, ref int p, bool isArray)
    {
        int start = p, len = BitConverter.ToInt32(d, p);
        if (len < 5 || len > d.Length - start) throw new InvalidDataException($"damaged BSON: a document at byte {start} claims {len} bytes");
        p += 4;
        var obj = new JsonObject();
        var arr = new JsonArray();
        while (p < start + len - 1)
        {
            byte type = d[p++];
            string name = CString(d, ref p);
            JsonNode? v = type switch
            {
                0x01 => JsonValue.Create(BitConverter.ToDouble(d, Adv(ref p, 8))),
                0x02 => JsonValue.Create(Str(d, ref p)),
                0x03 => ReadDoc(d, ref p, false),
                0x04 => ReadDoc(d, ref p, true),
                0x08 => JsonValue.Create(d[Adv(ref p, 1)] != 0),
                0x0A => null,
                0x10 => JsonValue.Create(BitConverter.ToInt32(d, Adv(ref p, 4))),
                0x12 => JsonValue.Create(BitConverter.ToInt64(d, Adv(ref p, 8))),
                0x11 => JsonValue.Create(BitConverter.ToInt64(d, Adv(ref p, 8))),
                0x09 => JsonValue.Create(BitConverter.ToInt64(d, Adv(ref p, 8))),
                _ => throw new InvalidDataException($"unsupported BSON type 0x{type:X2}"),
            };
            if (isArray) arr.Add(v);
            else obj[name] = v;
        }
        p = start + len;
        return isArray ? arr : obj;
    }

    static int Adv(ref int p, int n)
    {
        int at = p;
        p += n;
        return at;
    }
    static string CString(byte[] d, ref int p)
    {
        int s = p;
        while (d[p] != 0) p++;
        var r = Encoding.UTF8.GetString(d, s, p - s);
        p++;
        return r;
    }
    static string Str(byte[] d, ref int p)
    {
        int n = BitConverter.ToInt32(d, p);
        p += 4;
        var r = Encoding.UTF8.GetString(d, p, n - 1);
        p += n;
        return r;
    }
}
