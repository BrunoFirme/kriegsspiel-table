using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;
using KTable.Core;

namespace KTable.Converter;

public sealed class CacheEntry
{
    public string? Sha { get; set; }
    public string? Ext { get; set; }
    public string? Src { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public string Status { get; set; } = "ok";
    public string? Error { get; set; }
    public string? Url { get; set; }
}

public sealed partial class AssetStore
{
    readonly Config _cfg;
    readonly Dictionary<string, string> _ttsFiles = new();
    readonly ConcurrentDictionary<string, CacheEntry> _index = new();
    readonly string _indexPath;
    readonly object _saveLock = new();
    public Report Report { get; }

    public AssetStore(Config cfg, Report report)
    {
        _cfg = cfg; Report = report;
        Directory.CreateDirectory(cfg.Cache);
        _indexPath = Path.Combine(cfg.Cache, "index.json");
        if (File.Exists(_indexPath))
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_indexPath), Config.Opts) ?? new())
                _index[k] = v;
        foreach (var sub in new[] { "Images", "Models", "Assetbundles", "Audio", "PDF", "Text" })
        {
            var dir = Path.Combine(cfg.TtsModsPath, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir))
                _ttsFiles.TryAdd(KeyFromStem(Path.GetFileNameWithoutExtension(f)), f);
        }
    }

    public void Save()
    {
        lock (_saveLock)
        {
            if (!_dirty) return;
            try
            {
                var text = JsonSerializer.Serialize(new SortedDictionary<string, CacheEntry>(_index), Config.Opts);
                Json.WriteAtomic(_indexPath, new UTF8Encoding(false).GetBytes(text), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                _dirty = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"The download cache's index could not be written this time ({e.Message}); it is tried again.");
            }
        }
    }

    volatile bool _dirty;

    void Set(string key, CacheEntry e) { _index[key] = e; _dirty = true; }

    [GeneratedRegex(@"ugc/(\d+)/([0-9A-Fa-f]+)")] private static partial Regex UgcRx();
    [GeneratedRegex(@"[^A-Za-z0-9]")] private static partial Regex NonAlnum();

    static string KeyFromStem(string stem)
    {
        int i = stem.IndexOf("ugc", StringComparison.Ordinal);
        if (i >= 0 && (stem.Contains("steamusercontent") || stem.Contains("steamuserimages"))) return stem[i..];
        return stem;
    }

    public static string Key(string url) => KeyFromStem(NonAlnum().Replace(url.Trim(), ""));

    public static bool IsSteam(string url) => UgcRx().IsMatch(url) && (url.Contains("steamusercontent") || url.Contains("steamuserimages"));

    string FetchUrl(string url)
    {
        var m = UgcRx().Match(url);
        return m.Success && IsSteam(url) ? $"{_cfg.SteamHost.TrimEnd('/')}/ugc/{m.Groups[1].Value}/{m.Groups[2].Value}/" : url.Trim();
    }

    public string? FilePath(CacheEntry e) => e.Sha == null ? null : e.Src ?? Path.Combine(_cfg.Cache, e.Sha + "." + e.Ext);

    public CacheEntry? TryLocal(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var key = Key(url);
        if (_index.TryGetValue(key, out var e))
        {
            if (e.Status != "ok") return e;
            var p = FilePath(e);
            if (p != null && File.Exists(p)) return e;
        }
        string? src = null;
        if (_ttsFiles.TryGetValue(key, out var f)) src = f;
        else if (LocalPath(url) is { } lp && File.Exists(lp)) src = lp;
        if (src == null) return null;
        var bytes = File.ReadAllBytes(src);
        e = Describe(bytes, url);
        e.Src = src;
        Set(key, e);
        return e;
    }

    static string? LocalPath(string url)
    {
        var u = url.Trim();
        if (u.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) u = Uri.UnescapeDataString(u[8..]);
        else if (u.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) u = Uri.UnescapeDataString(u[7..]);
        return u.Length > 2 && u[1] == ':' ? u.Replace('/', '\\') : null;
    }

    static CacheEntry Describe(byte[] bytes, string url)
    {
        var e = new CacheEntry { Sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Ext = Sniff(bytes), Url = url };
        if (e.Ext is "png" or "jpg" or "gif" or "bmp" or "webp" or "tga")
        {
            var info = BestEffort.Read(() => SKBitmap.DecodeBounds(bytes), fallback: default);
            e.W = info.Width; e.H = info.Height;
        }
        return e;
    }

    public static string Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 4 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return "gif";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return "bmp";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        if (b.Length >= 7 && Encoding.ASCII.GetString(b[..7]) is "UnityFS" or "UnityWe" or "UnityRa") return "unity3d";
        if (b.Length >= 5 && Encoding.ASCII.GetString(b[..5]) == "<!DOC" || b.Length >= 5 && Encoding.ASCII.GetString(b[..5]).StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "html";
        var head = Encoding.ASCII.GetString(b[..Math.Min(b.Length, 4096)]);
        if (Regex.IsMatch(head, @"(^|\n)\s*(v|vt|vn|f|o|g|mtllib|usemtl)\s", RegexOptions.None)) return "obj";
        return "bin";
    }

    static bool Retryable(CacheEntry e) => e.Status switch
    {
        "ok" => false,
        "dead" => !(e.Error is { } why && (why.StartsWith("HTTP 404") || why.StartsWith("HTTP 410") || why.StartsWith("got an HTML page")
                                           || why.StartsWith("local file path") || why.StartsWith("not a web address"))),
        _ => true,
    };

    readonly ConcurrentDictionary<string, byte> _tried = new();

    public async Task FetchAll(IEnumerable<string> urls, bool allowNonSteam = true)
    {
        var todo = new List<string>();
        foreach (var u in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct())
        {
            var e = TryLocal(u);
            if (e != null && !Retryable(e)) continue;
            if (LocalPath(u) != null) { Set(Key(u), new CacheEntry { Status = "dead", Error = "local file path not present on this machine", Url = u }); continue; }
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            { Set(Key(u), new CacheEntry { Status = "dead", Error = "not a web address (no http/https)", Url = u }); continue; }
            if (!IsSteam(u) && !allowNonSteam) continue;
            if (!_tried.TryAdd(Key(u), 0)) continue;
            todo.Add(u);
        }
        if (todo.Count == 0) { Save(); return; }
        Console.WriteLine($"  downloading {todo.Count} assets ({_cfg.MaxParallelDownloads} parallel)...");
        using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KriegsspielTable-Converter/1.0");
        using var gate = new SemaphoreSlim(Math.Max(1, _cfg.MaxParallelDownloads));
        int done = 0, failed = 0;
        var tasks = todo.Select(async u =>
        {
            await gate.WaitAsync();
            try
            {
                var ok = await Download(http, u);
                if (!ok) Interlocked.Increment(ref failed);
                var n = Interlocked.Increment(ref done);
                Console.WriteLine($"PROGRESS {n}/{todo.Count}");
                if (n % 50 == 0) { Console.WriteLine($"  {n}/{todo.Count} ({failed} failed)"); Save(); }
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        Console.WriteLine($"  downloads done: {done - failed} ok, {failed} failed");
        Save();
    }

    static readonly TimeSpan Stall = TimeSpan.FromSeconds(60);

    async Task<bool> Download(HttpClient http, string url)
    {
        var key = Key(url);
        bool steam = IsSteam(url);
        const int SteamAttempts = 5, OtherHostAttempts = 1;
        int attempts = steam ? SteamAttempts : OtherHostAttempts;
        string? err = null;
        bool definite = false;
        for (int i = 0; i < attempts; i++)
        {
            if (i > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
            try
            {
                using var cts = new CancellationTokenSource();
                using var resp = await http.GetAsync(FetchUrl(url), HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500) { err = $"HTTP {(int)resp.StatusCode}"; continue; }
                if (!resp.IsSuccessStatusCode) { err = $"HTTP {(int)resp.StatusCode}"; definite = (int)resp.StatusCode is 404 or 410; break; }
                var bytes = await ReadBody(resp, cts);
                if (bytes.Length == 0) { err = "empty body"; continue; }
                var e = Describe(bytes, url);
                if (e.Ext == "html") { err = "got an HTML page, not an asset"; definite = true; break; }
                var path = Path.Combine(_cfg.Cache, e.Sha + "." + e.Ext);
                if (!File.Exists(path))
                {
                    var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
                    await File.WriteAllBytesAsync(tmp, bytes);
                    try { File.Move(tmp, path, false); } catch (IOException) { File.Delete(tmp); }
                }
                Set(key, e);
                return true;
            }
            catch (OperationCanceledException)
            {
                err = $"stalled: nothing arrived for {Stall.TotalSeconds:0} s, or no answer in time";
            }
            catch (Exception ex)
            {
                err = ex.GetType().Name + ": " + ex.Message;
            }
        }
        Set(key, new CacheEntry { Status = steam || !definite ? "missing" : "dead", Error = err, Url = url });
        return false;
    }

    static async Task<byte[]> ReadBody(HttpResponseMessage resp, CancellationTokenSource cts)
    {
        cts.CancelAfter(Stall);
        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var ms = resp.Content.Headers.ContentLength is long n and > 0 and < int.MaxValue ? new MemoryStream((int)n) : new MemoryStream();
        var buf = new byte[1 << 16];
        for (int got; (got = await s.ReadAsync(buf, cts.Token)) > 0; )
        {
            ms.Write(buf, 0, got);
            cts.CancelAfter(Stall);
        }
        return ms.Length == ms.Capacity ? ms.GetBuffer() : ms.ToArray();
    }

    public CacheEntry? Get(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var e = TryLocal(url);
        return e is { Status: "ok" } ? e : null;
    }

    public string? Why(string url) => _index.TryGetValue(Key(url), out var e) ? $"{e.Status}: {e.Error}" : "not fetched";
}
