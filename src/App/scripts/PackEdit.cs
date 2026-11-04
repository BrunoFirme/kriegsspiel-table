using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public static class PackEdit
{
    public static string ManifestPath(PackageStore store, string packId) => Path.Combine(store.PackageDir(packId), "pack.json");

    public static string CreatePack(PackageStore store, string name, string author)
    {
        var baseId = UserPacks.IdFor(name);
        var id = baseId;
        for (int i = 2; File.Exists(ManifestPath(store, id)); i++) id = $"{baseId}-{i}";
        var pack = new PackManifest
        {
            Id = id, Name = name.Trim().Length > 0 ? name.Trim() : "My pieces",
            Source = new PackSource { Authors = author.Length > 0 ? new() { author } : new(), License = "made by the author for their own games" },
        };
        Directory.CreateDirectory(Path.Combine(store.PackageDir(id), "assets"));
        Json.Save(ManifestPath(store, id), pack);
        return id;
    }

    public static void Save(PackageStore store, PackManifest pack) => Json.Save(ManifestPath(store, pack.Id), pack);

    public static void WriteContent(string path, byte[] bytes)
    {
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length) return;
        Json.WriteAtomic(path, bytes);
    }

    public static string AddImage(PackageStore store, PackManifest pack, Image img)
    {
        var png = img.SavePngToBuffer();
        var sha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        var rel = $"assets/{sha}.png";
        var abs = Path.Combine(store.PackageDir(pack.Id), rel);
        WriteContent(abs, png);
        pack.Assets[sha] = new AssetEntry { File = rel, Kind = "image", W = img.GetWidth(), H = img.GetHeight() };
        return sha;
    }

    public static void Forget(PackManifest pack, string? hash)
    {
        if (hash == null || !pack.Assets.ContainsKey(hash)) return;
        foreach (var d in pack.Pieces)
        {
            if (AssetsOf(d).Contains(hash)) return;
            if ((d.Script?.Contains(hash, StringComparison.OrdinalIgnoreCase) ?? false) || (d.ScriptState?.Contains(hash, StringComparison.OrdinalIgnoreCase) ?? false)) return;
            if (d.States.Any(s => s.Script?.Contains(hash, StringComparison.OrdinalIgnoreCase) ?? false)) return;
        }
        pack.Assets.Remove(hash);
    }

    public static (string sha, int w, int h, bool seeThrough)? AddImageFile(PackageStore store, PackManifest pack, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
        if (ext is "tif" or "tiff")
        {
            var tiffImg = TiffImages.Load(path);
            if (tiffImg == null) return null;
            var sha0 = AddImage(store, pack, tiffImg);
            return (sha0, tiffImg.GetWidth(), tiffImg.GetHeight(), SeeThrough(tiffImg));
        }
        if (ext is not ("png" or "jpg" or "jpeg" or "webp")) return null;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > Core.Protocol.UploadAsset.MaxFileBytes || !ContentLimits.PictureAllowed(bytes)) return null;
        var img = new Image();
        var err = ext switch { "png" => img.LoadPngFromBuffer(bytes), "webp" => img.LoadWebpFromBuffer(bytes), _ => img.LoadJpgFromBuffer(bytes) };
        if (err != Error.Ok || img.IsEmpty()) return null;
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var rel = $"assets/{sha}.{(ext == "jpeg" ? "jpg" : ext)}";
        var abs = Path.Combine(store.PackageDir(pack.Id), rel);
        WriteContent(abs, bytes);
        pack.Assets[sha] = new AssetEntry { File = rel, Kind = "image", W = img.GetWidth(), H = img.GetHeight() };
        return (sha, img.GetWidth(), img.GetHeight(), SeeThrough(img));
    }

    public static bool SeeThrough(Image img)
    {
        if (img.DetectAlpha() == Image.AlphaMode.None) return false;
        int w = img.GetWidth(), h = img.GetHeight(), transparent = 0, samples = 0;
        for (int y = 0; y < h; y += Math.Max(1, h / 48)) for (int x = 0; x < w; x += Math.Max(1, w / 48)) { samples++; if (img.GetPixel(x, y).A < 0.5f) transparent++; }
        return transparent > samples * 0.04;
    }

    public static string Slug(string name) => Naming.PieceSlug(name);

    public static string NewDefId(PackManifest pack, string name) => Naming.NewPieceId(pack, name);

    public static PieceDef NewDef(PackageStore store, PackManifest pack, string name, string imageSha, int w, int h, double longestSide, string shape, IEnumerable<string>? tags = null)
    {
        double k = longestSide / Math.Max(1, Math.Max(w, h));
        var def = new PieceDef
        {
            Id = NewDefId(pack, name), Name = name, Kind = "flat", Image = imageSha,
            Size = new Size2(Math.Round(w * k, 4), Math.Round(h * k, 4)), Shape = shape, Thickness = 0.12,
            Tags = (tags ?? Array.Empty<string>()).Where(t => t.Length > 0).Distinct().ToList(),
        };
        pack.Pieces.Add(def);
        store.RegisterDef(pack, def);
        return def;
    }

    public static PieceDef Duplicate(PackageStore store, PackManifest pack, PieceDef src)
    {
        var copy = Json.Clone(src);
        copy.Name = src.Name + " copy";
        copy.Id = NewDefId(pack, copy.Name);
        pack.Pieces.Insert(Math.Min(pack.Pieces.Count, pack.Pieces.IndexOf(src) + 1), copy);
        store.RegisterDef(pack, copy);
        return copy;
    }

    public static void Remove(PackageStore store, PackManifest pack, PieceDef def)
    {
        pack.Pieces.Remove(def);
        foreach (var o in pack.Pieces) o.Attachments.RemoveAll(a => a.Piece == def.Id);
        store.UnregisterDef(def.Id);
    }

    public static IEnumerable<string> AssetsOf(PieceDef d) => Naming.AssetsOf(d);

    public static int RemoveUnusedAssets(PackageStore store, PackManifest pack)
    {
        var used = pack.Pieces.SelectMany(AssetsOf).ToHashSet();
        used.UnionWith(MentionedHashes(store, pack));
        var dir = store.PackageDir(pack.Id);
        var bin = NewBin(pack.Id);
        int n = 0;
        var cleared = new Dictionary<string, AssetEntry>();
        foreach (var (hash, a) in pack.Assets.ToList())
        {
            if (used.Contains(hash) || a.Kind != "image") continue;
            var f = Path.Combine(dir, a.File);
            if (File.Exists(f) && !ToBin(store, pack.Id, bin, f)) continue;
            pack.Assets.Remove(hash);
            cleared[hash] = a;
            n++;
        }
        if (cleared.Count > 0) BestEffort.Try(() => Json.Save(Path.Combine(bin, SaveStore.PackFilesListing), cleared));
        var assets = Path.Combine(dir, "assets");
        if (Directory.Exists(assets))
        {
            var listed = pack.Assets.Values.Select(a => Path.GetFullPath(Path.Combine(dir, a.File))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(assets))
            {
                var stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (stem.Length != 64 || !stem.All(Uri.IsHexDigit) || used.Contains(stem) || listed.Contains(Path.GetFullPath(f))) continue;
                if (ToBin(store, pack.Id, bin, f)) n++;
            }
        }
        return n;
    }

    static readonly System.Text.RegularExpressions.Regex HashText = new("[0-9a-fA-F]{64}", System.Text.RegularExpressions.RegexOptions.Compiled);

    static HashSet<string> MentionedHashes(PackageStore store, PackManifest pack)
    {
        var found = new HashSet<string>();
        void Scan(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (System.Text.RegularExpressions.Match m in HashText.Matches(text)) found.Add(m.Value.ToLowerInvariant());
        }
        foreach (var d in pack.Pieces)
        {
            Scan(d.Script);
            Scan(d.ScriptState);
            foreach (var s in d.States) Scan(s.Script);
        }
        var files = new List<string>();
        foreach (var (_, path) in store.ListScenarios())
        {
            files.Add(path);
            files.Add(Path.Combine(Path.GetDirectoryName(path)!, "scenario.start.json"));
        }
        try { var live = SaveStore.Default.LiveDir; if (Directory.Exists(live)) files.AddRange(Directory.GetFiles(live, "*" + SaveStore.Ext, SearchOption.AllDirectories)); }
        catch (Exception e) { throw new IOException($"the games in play could not be listed to see which pictures they use ({e.Message}); nothing was moved", e); }
        foreach (var f in files)
        {
            try
            {
                if (!File.Exists(f)) continue;
                var text = File.ReadAllText(f);
                if (text.Contains(pack.Id, StringComparison.Ordinal)) Scan(text);
            }
            catch (Exception e) { throw new IOException($"{f} could not be read to see which pictures it uses ({e.Message}); nothing was moved", e); }
        }
        return found;
    }

    static void DeleteBinsOlderThanTrashDays(string root)
    {
        if (Directory.Exists(root))
            foreach (var d in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(d);
                if (name.Length >= 15 && DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)
                    && DateTime.UtcNow - at > TimeSpan.FromDays(SaveStore.TrashDays))
                    Directory.Delete(d, true);
            }
    }

    public static string NewBin(string packId)
    {
        var root = Path.Combine(SaveStore.Default.TrashDir, SaveStore.PackFilesDir);
        BestEffort.Try(() => DeleteBinsOlderThanTrashDays(root));
        return Path.Combine(root, $"{Repeatable.UtcNow:yyyyMMdd-HHmmss} {packId}");
    }

    public static bool ToBin(PackageStore store, string packId, string bin, string file)
    {
        try
        {
            var rel = Path.GetRelativePath(store.PackageDir(packId), file);
            if (rel.StartsWith("..")) rel = Path.GetFileName(file);
            var dst = Path.Combine(bin, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Move(file, dst, true);
            return true;
        }
        catch (Exception e) { GD.PrintErr($"[studio] kept {file}: could not move it to the trash ({e.Message})"); return false; }
    }

    public static Image? LoadAsset(PackageStore store, PackManifest pack, string? hash)
    {
        var path = store.AssetPath(pack, hash);
        if (path == null || !File.Exists(path) || !ContentLimits.PictureAllowed(path)) return null;
        var img = Image.LoadFromFile(path);
        if (img == null || img.IsEmpty()) img = ImageFiles.Load(path) ?? Sniffed(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        return img;
    }

    static Image? Sniffed(string path)
    {
        try
        {
            var b = File.ReadAllBytes(path);
            if (b.Length < 12) return null;
            var img = new Image();
            Error err;
            if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') err = img.LoadPngFromBuffer(b);
            else if (b[0] == 0xFF && b[1] == 0xD8) err = img.LoadJpgFromBuffer(b);
            else if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') err = img.LoadWebpFromBuffer(b);
            else if (b[0] == 'B' && b[1] == 'M') err = img.LoadBmpFromBuffer(b);
            else return null;
            return err == Error.Ok && !img.IsEmpty() ? img : null;
        }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return null; }
    }
}
