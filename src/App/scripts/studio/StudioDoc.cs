using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using KTable.Client.Studio;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

public static class ArtXf
{
    public static Transform2D Xf(this ArtLayer l) => new Transform2D(Mathf.DegToRad(l.Rot), new Vector2(l.Sx, l.Sy), 0, new Vector2(l.X, l.Y));
    public static void SetXf(this ArtLayer l, Transform2D t)
    {
        l.X = t.Origin.X;
        l.Y = t.Origin.Y;
        l.Rot = Mathf.RadToDeg(t.Rotation);
        var s = t.Scale;
        l.Sx = s.X;
        l.Sy = s.Y;
    }

    public static Vector2[] Pts(this PaintOp op)
    {
        var a = new Vector2[op.P.Count / 2];
        for (int i = 0; i < a.Length; i++) a[i] = new Vector2(op.P[i * 2], op.P[i * 2 + 1]);
        return a;
    }

    public static List<float> Flat(IEnumerable<Vector2> pts) => PaintOp.Flat(pts.Select(p => new System.Numerics.Vector2(p.X, p.Y)));
}

public static class Hex
{
    public static string Of(Color c) => "#" + c.ToHtml(true);
    public static Color To(string? s, Color fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        return Color.HtmlIsValid(s) ? Color.FromHtml(s) : fallback;
    }
}

public static class ArtStore
{
    public static string Dir(PackageStore store, string packId) => Path.Combine(store.PackageDir(packId), "art");
    public static string SrcDir(PackageStore store, string packId) => Path.Combine(Dir(store, packId), "src");
    public static string DocPath(PackageStore store, string packId, string sha) => Path.Combine(Dir(store, packId), sha + ".json");
    public static string SrcPath(PackageStore store, string packId, string name) => Path.Combine(SrcDir(store, packId), name);

    public static string Serialize(ArtDoc d) => ArtJson.Serialize(d);
    public static ArtDoc? Deserialize(string s) => ArtJson.Deserialize(s);

    public static ArtDoc? Load(PackageStore store, string packId, string? sha) =>
        sha == null ? null : Read(store, packId, DocPath(store, packId, sha));

    public static ArtDoc? Read(PackageStore store, string packId, string path)
    {
        if (!File.Exists(path)) return null;
        string why;
        try
        {
            if (Deserialize(File.ReadAllText(path)) is { } doc) return doc;
            why = "it is damaged";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = e.Message; }
        var kept = KeepDamagedCopy(store, packId, path) ? " A copy of the file is in the app's trash." : "";
        Problems.TellOnce("art:" + path, $"The layers of a face ({Path.GetFileName(path)}) could not be read ({why}), so it opens as its flat picture.{kept}");
        return null;
    }

    static readonly HashSet<string> _damagedCopied = new(StringComparer.OrdinalIgnoreCase);

    static bool KeepDamagedCopy(PackageStore store, string packId, string path)
    {
        lock (_damagedCopied) if (!_damagedCopied.Add(path)) return true;
        try
        {
            var dst = Path.Combine(PackEdit.NewBin(packId), Path.GetRelativePath(store.PackageDir(packId), path));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(path, dst, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            lock (_damagedCopied) _damagedCopied.Remove(path);
            return false;
        }
    }

    public static void Save(PackageStore store, string packId, string sha, ArtDoc d) =>
        KTable.Core.Json.WriteAtomic(DocPath(store, packId, sha), Serialize(d));

    public static string AddSrc(PackageStore store, string packId, Image img)
    {
        var png = img.SavePngToBuffer();
        var name = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()[..24] + ".png";
        PackEdit.WriteContent(SrcPath(store, packId, name), png);
        return name;
    }

    public static string AddSrcBytes(PackageStore store, string packId, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..24] + "." + (ext.Length is >= 2 and <= 5 ? ext : "bin");
        PackEdit.WriteContent(SrcPath(store, packId, name), bytes);
        return name;
    }

    public static string? AddSrcFile(PackageStore store, string packId, string path, out Image? img)
    {
        img = ImageFiles.Load(path);
        if (img == null) return null;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (ext is not ("png" or "jpg" or "jpeg" or "webp") || new FileInfo(path).Length > 60_000_000) return AddSrc(store, packId, img);
        var bytes = File.ReadAllBytes(path);
        var name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..24] + "." + (ext == "jpeg" ? "jpg" : ext);
        var p = SrcPath(store, packId, name);
        PackEdit.WriteContent(p, bytes);
        var kept = Image.LoadFromFile(p);
        if (kept != null && !kept.IsEmpty() && (kept.GetWidth() != img.GetWidth() || kept.GetHeight() != img.GetHeight())) return AddSrc(store, packId, img);
        return name;
    }

    public static Image? LoadSrc(PackageStore store, string packId, string name)
    {
        var p = SrcPath(store, packId, name);
        if (!File.Exists(p) || !ContentLimits.PictureAllowed(p)) return null;
        var img = Image.LoadFromFile(p);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    static readonly System.Text.RegularExpressions.Regex SrcName = new("[0-9a-f]{24}\\.[a-z0-9]{2,5}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static int Sweep(PackageStore store, PackManifest pack, IEnumerable<string>? live = null)
    {
        var dir = Dir(store, pack.Id);
        if (!Directory.Exists(dir)) return 0;
        var used = pack.Pieces.SelectMany(PackEdit.AssetsOf).ToHashSet();
        var keepSrc = new HashSet<string>();
        void Names(string? text) { if (text != null) foreach (System.Text.RegularExpressions.Match m in SrcName.Matches(text)) keepSrc.Add(m.Value); }
        if (live != null) foreach (var t in live) Names(t);
        var bin = PackEdit.NewBin(pack.Id);
        bool sure = true;
        int n = 0;
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            var stem = Path.GetFileNameWithoutExtension(f);
            var sha = stem.EndsWith(".model") ? stem[..^6] : stem;
            if (!used.Contains(sha) && PackEdit.ToBin(store, pack.Id, bin, f)) { n++; continue; }
            try { Names(File.ReadAllText(f)); }
            catch (Exception e) when (SaveStore.IsFileTrouble(e)) { sure = false; }
        }
        foreach (var name in keepSrc.Where(x => x.EndsWith(".json")).ToList())
        {
            var path = Path.Combine(SrcDir(store, pack.Id), name);
            if (!File.Exists(path)) continue;
            try { Names(File.ReadAllText(path)); }
            catch (Exception e) when (SaveStore.IsFileTrouble(e)) { sure = false; }
        }
        foreach (var f in Directory.GetFiles(dir, "*.thumb.png"))
            if (!used.Contains(Path.GetFileName(f)[..^".thumb.png".Length]) && PackEdit.ToBin(store, pack.Id, bin, f)) n++;
        var src = SrcDir(store, pack.Id);
        if (sure && Directory.Exists(src))
            foreach (var f in Directory.GetFiles(src))
                if (!keepSrc.Contains(Path.GetFileName(f)) && PackEdit.ToBin(store, pack.Id, bin, f)) n++;
        return n;
    }
}

public sealed class StudioPrefs
{
    public string Ink { get; set; } = "#1a1a1aff";
    public string Ink2 { get; set; } = "#f2f2f2ff";
    public List<string> Recent { get; set; } = new();
    public float Brush { get; set; } = 12;
    public float Soft { get; set; } = 0.3f;
    public float ModelSnap { get; set; }
    public bool ModelOrtho { get; set; }
    public float Eraser { get; set; } = 24;
    public float Line { get; set; } = 6;
    public string ShapeKind { get; set; } = "rect";
    public string ShapeFill { get; set; } = "line";
    public int Sides { get; set; } = 6;
    public float StarInner { get; set; } = 0.45f;
    public bool Grid { get; set; }
    public bool Snap { get; set; }
    public int GridCells { get; set; } = 16;
    public int GridMajor { get; set; } = 4;
    public string Symmetry { get; set; } = "off";
    public int Radial { get; set; } = 6;
    public float FillTolerance { get; set; } = 0.12f;
    public bool FillAllLayers { get; set; } = true;
    public string TextFont { get; set; } = "Segoe UI";
    public float TextSize { get; set; } = 48;
    public bool TextBold { get; set; } = true;
    public double MetresPerUnit { get; set; } = 48.5;
    public bool ShowWidgets { get; set; } = true;
    public bool Rulers { get; set; }
    public Dictionary<string, float[]> Panels { get; set; } = new();

    static string PathOf => ProjectSettings.GlobalizePath("user://studio.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static StudioPrefs Load()
    {
        try { if (File.Exists(PathOf)) return JsonSerializer.Deserialize<StudioPrefs>(File.ReadAllText(PathOf), Opts) ?? new(); }
        catch (Exception e) { Problems.Tell($"The studio's own settings (studio.json) could not be read ({e.Message}), so it starts as new."); }
        return new();
    }

    public void Save()
    {
        try { KTable.Core.Json.WriteAtomic(PathOf, JsonSerializer.Serialize(this, Opts)); }
        catch (Exception e) { Problems.TellOnce("studio.json", $"The studio's own settings could not be saved ({e.Message}): they last until the app closes."); }
    }

    public void Used(Color c)
    {
        var h = Hex.Of(c);
        Recent.Remove(h);
        Recent.Insert(0, h);
        if (Recent.Count > 16) Recent.RemoveRange(16, Recent.Count - 16);
    }
}
