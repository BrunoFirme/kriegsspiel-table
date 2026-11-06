using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

public sealed class AlphaMask
{
    public const int N = 64;
    public readonly bool[] Solid = new bool[N * N];
    public bool HasAlpha;
    public float TransparentFraction;
    public (float y, float h)? DarkBand;
    public bool At(float u, float v)
    {
        if (!HasAlpha) return true;
        int x = Math.Clamp((int)(u * N), 0, N - 1), y = Math.Clamp((int)(v * N), 0, N - 1);
        return Solid[y * N + x];
    }
}

public sealed class Assets
{
    public PackageStore Store { get; private set; }
    public readonly AssetWaits<Texture2D> Waits = new();
    public Action<string, string>? RequestRemote { get => Waits.RequestRemote; set => Waits.RequestRemote = value; }
    public Action<string, string>? RequestInBackground { get => Waits.RequestInBackground; set => Waits.RequestInBackground = value; }

    public static string CacheDir => Caches.Display;

    readonly Dictionary<string, Texture2D> _tex = new();
    readonly ConcurrentDictionary<string, AlphaMask> _masks = new();
    readonly Dictionary<string, long> _lastUse = new();
    readonly ConcurrentQueue<(string key, Image? img)> _ready = new();
    static readonly SemaphoreSlim Decoders = new(3);
    public static volatile bool Quitting;

    public static void Quiesce()
    {
        Quitting = true;
        int got = 0;
        for (int i = 0; i < 3; i++) if (Decoders.Wait(700)) got++;
        for (int i = 0; i < got; i++) Decoders.Release();
    }
    long _frame;
    public int TileCap { get; set; } = 1400;
    public int TexCount => _tex.Count;
    public int WaitingCount => Waits.PendingTextures;
    public int Uploaded;
    public int Loading => Waits.PendingTextures;
    public int PendingTextures => Waits.PendingTextures;
    public int PendingFiles => Waits.PendingFiles;
    public bool Busy => Waits.Busy;
    public bool Shows => Waits.Shows;
    public IReadOnlyList<string> Jobs => Waits.Jobs;
    public void BeginJob(string name) => Waits.BeginJob(name);
    public void EndJob(string name) => Waits.EndJob(name);
    public double JobProgress { get => Waits.JobProgress; set => Waits.JobProgress = value; }
    public int BatchDone => Waits.BatchDone;
    public int BatchTotal => Waits.BatchTotal;

    public Assets(PackageStore store) { Store = store; Directory.CreateDirectory(CacheDir); }

    public string Abs(string package, string rel) => Path.Combine(Store.Root, package, rel);

    public void EnsureFile(string package, string rel, Action onReady, Action? onMissing = null) => Waits.EnsureFile(package, rel, Abs(package, rel), onReady, onMissing);

    public void Prefetch(string package, string rel) => Waits.Prefetch(package, rel, Abs(package, rel));

    public void FileArrived(string package, string rel) => Waits.FileArrived(Abs(package, rel));
    public void FileMissing(string package, string rel) => Waits.FileMissing(Abs(package, rel));

    static string Key(string abs, int maxSize) => maxSize > 0 ? abs + "@" + maxSize : abs;

    public Texture2D? Texture(string package, string rel, Action<Texture2D>? onReady, bool mipmaps = true, int maxSize = 0, bool quiet = false)
    {
        var abs = Abs(package, rel);
        var key = Key(abs, maxSize);
        _lastUse[key] = _frame;
        if (_tex.TryGetValue(key, out var t)) return t;
        if (Waits.KnownMissing(abs)) return null;
        if (!Waits.StartTexture(key, onReady, quiet)) return null;
        EnsureFile(package, rel, () => Task.Run(async () =>
        {
            Image? img = null;
            await Decoders.WaitAsync();
            try { if (!Quitting) img = LoadScaled(abs, key, maxSize, mipmaps); }
            catch (Exception e) { img = null; Diagnostics.Write(LogLevel.Warning, $"[assets] {abs} could not be decoded: {e.Message}"); }
            finally { Decoders.Release(); }
            if (!Quitting) _ready.Enqueue((key, img));
        }), () => _ready.Enqueue((key, null)));
        return null;
    }

    public AlphaMask? Mask(string package, string rel, int maxSize) => _masks.GetValueOrDefault(Key(Abs(package, rel), maxSize));

    Image? LoadScaled(string abs, string key, int maxSize, bool mipmaps)
    {
        Image? img = null;
        string? cached = null;
        if (maxSize > 0)
        {
            var stem = Path.GetFileNameWithoutExtension(abs);
            if (stem.Length != 64) stem = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(abs)))[..32];
            cached = Path.Combine(CacheDir, $"{stem}_{maxSize}.png");
            if (File.Exists(cached)) img = Image.LoadFromFile(cached);
        }
        if (img == null || img.IsEmpty())
        {
            img?.Dispose();
            if (!ContentLimits.PictureAllowed(abs)) { GD.PrintErr($"[assets] not decoded, larger than a picture may be: {abs}"); return null; }
            img = Image.LoadFromFile(abs);
            if (img == null || img.IsEmpty()) return null;
            if (img.IsCompressed()) img.Decompress();
            int w = img.GetWidth(), h = img.GetHeight();
            if (maxSize > 0 && Math.Max(w, h) > maxSize)
            {
                float k = maxSize / (float)Math.Max(w, h);
                img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), Image.Interpolation.Lanczos);
            }
            if (cached != null) BestEffort.Try(() => img.SavePng(cached));
        }
        if (maxSize > 0) _masks[key] = BuildMask(img);
        if (mipmaps) img.GenerateMipmaps();
        return img;
    }

    internal static AlphaMask BuildMask(Image img)
    {
        var m = new AlphaMask();
        int w = img.GetWidth(), h = img.GetHeight();
        if (img.DetectAlpha() != Image.AlphaMode.None)
        {
            m.HasAlpha = true;
            for (int y = 0; y < AlphaMask.N; y++)
                for (int x = 0; x < AlphaMask.N; x++)
                    m.Solid[y * AlphaMask.N + x] = img.GetPixel(Math.Min(w - 1, (int)((x + 0.5f) / AlphaMask.N * w)), Math.Min(h - 1, (int)((y + 0.5f) / AlphaMask.N * h))).A > 0.5f;
            m.TransparentFraction = m.Solid.Count(v => !v) / (float)m.Solid.Length;
        }
        else Array.Fill(m.Solid, true);
        m.DarkBand = DarkBand(img, w, h);
        return m;
    }

    static (float y, float h)? DarkBand(Image img, int w, int h)
    {
        const int R = 48, C = 24;
        var dark = new float[R];
        for (int r = 0; r < R; r++)
        {
            int cnt = 0, tot = 0;
            int y = Math.Min(h - 1, (int)((r + 0.5f) / R * h));
            for (int c = 0; c < C; c++)
            {
                var px = img.GetPixel(Math.Min(w - 1, (int)((0.1f + 0.8f * (c + 0.5f) / C) * w)), y);
                if (px.A < 0.5f) continue;
                tot++;
                if (px.R * 0.3f + px.G * 0.59f + px.B * 0.11f < 0.22f) cnt++;
            }
            dark[r] = tot >= C / 2 ? cnt / (float)tot : 0;
        }
        int bestStart = -1, bestLen = 0, darkRows = 0;
        for (int r = 0; r < R;)
        {
            if (dark[r] < 0.75f) { r++; continue; }
            int s = r;
            while (r < R && dark[r] >= 0.75f) r++;
            int len = r - s;
            darkRows += len;
            if (len > bestLen) { bestLen = len; bestStart = s; }
        }
        if (bestLen >= 3 && bestLen <= R * 0.45f && darkRows <= R * 0.6f) return (bestStart / (float)R, bestLen / (float)R);
        return null;
    }

    public void Process()
    {
        _frame++;
        var budget = 24;
        while (budget-- > 0 && _ready.TryDequeue(out var r))
        {
            var cbs = Waits.TextureDone(r.key);
            if (r.img == null || r.img.IsEmpty()) continue;
            var tex = ImageTexture.CreateFromImage(r.img);
            Uploaded++;
            r.img.Dispose();
            _tex[r.key] = tex;
            foreach (var cb in cbs) cb(tex);
        }
        if (_frame % 120 == 0) Evict();
    }

    void Evict()
    {
        var tiles = new List<(string k, long f)>();
        foreach (var (k, _) in _tex)
            if (k.Contains(Path.DirectorySeparatorChar + "tiles" + Path.DirectorySeparatorChar) || k.Contains("/tiles/"))
                tiles.Add((k, _lastUse.GetValueOrDefault(k)));
        if (tiles.Count <= TileCap) return;
        tiles.Sort((a, b) => a.f.CompareTo(b.f));
        for (int i = 0; i < tiles.Count - TileCap; i++)
        {
            const int FramesStillInUse = 30;
            if (_frame - tiles[i].f < FramesStillInUse) break;
            _tex.Remove(tiles[i].k);
        }
    }

    public void Touch(string package, string rel) => _lastUse[Abs(package, rel)] = _frame;

    public static int PieceTexSize(double longestUnits) => longestUnits <= 4 ? 512 : longestUnits <= 16 ? 1024 : 2048;
}
