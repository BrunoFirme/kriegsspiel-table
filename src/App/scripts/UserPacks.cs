using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public static class UserPacks
{
    public static string IdFor(string name) => Naming.UserPackId(name);

    public static int AddImages(PackageStore store, string packId, string packName, IEnumerable<string> files, double longestSide, string shape, string tags, string author)
    {
        var dir = store.PackageDir(packId);
        var path = Path.Combine(dir, "pack.json");
        var pack = File.Exists(path) ? Json.Load<PackManifest>(path) : new PackManifest
        {
            Id = packId, Name = packName,
            Source = new PackSource { Authors = author.Length > 0 ? new() { author } : new(), License = "made by the author for their own games" },
        };
        if (packName.Length > 0) pack.Name = packName;
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        int added = 0;
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant().TrimStart('.');
            if (ext is not ("png" or "jpg" or "jpeg" or "webp")) continue;
            var bytes = File.ReadAllBytes(f);
            if (bytes.Length > Core.Protocol.UploadAsset.MaxFileBytes || !ContentLimits.PictureAllowed(bytes)) continue;
            var img = new Image();
            var err = ext switch { "png" => img.LoadPngFromBuffer(bytes), "webp" => img.LoadWebpFromBuffer(bytes), _ => img.LoadJpgFromBuffer(bytes) };
            if (err != Error.Ok || img.IsEmpty()) continue;
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var rel = $"assets/{sha}.{(ext == "jpeg" ? "jpg" : ext)}";
            File.WriteAllBytes(Path.Combine(dir, rel), bytes);
            int w = img.GetWidth(), h = img.GetHeight();
            pack.Assets[sha] = new AssetEntry { File = rel, Kind = "image", W = w, H = h };
            var effShape = shape;
            if (shape == "auto")
            {
                int transparent = 0, samples = 0;
                for (int y = 0; y < h; y += Math.Max(1, h / 48)) for (int x = 0; x < w; x += Math.Max(1, w / 48)) { samples++; if (img.GetPixel(x, y).A < 0.5f) transparent++; }
                effShape = transparent > samples * 0.04 ? "alpha" : "box";
            }
            double k = longestSide / Math.Max(w, h);
            var name = Pretty(Path.GetFileNameWithoutExtension(f));
            var id = $"{packId}.{IdFor(name)[PackManifest.UserPrefix.Length..]}-{sha[..8]}";
            pack.Pieces.RemoveAll(p => p.Id == id);
            pack.Pieces.Add(new PieceDef
            {
                Id = id, Name = name, Kind = "flat", Image = sha, Size = new Size2(Math.Round(w * k, 4), Math.Round(h * k, 4)),
                Shape = effShape, Thickness = 0.12, Tags = new[] { "mine" }.Concat(tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().ToList(),
            });
            added++;
        }
        pack.Pieces = pack.Pieces.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Json.Save(path, pack);
        store.Forget(packId);
        return added;
    }

    static string Pretty(string stem)
    {
        var s = stem.Replace('_', ' ').Replace('-', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length == 0 ? "Piece" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static IEnumerable<string> LocalUserPacks(PackageStore store) => store.ListPackIds().Where(PackManifest.IsUserPackId);

    public static void Export(PackageStore store, string packId, string file)
    {
        var dir = store.PackageDir(packId);
        var tmp = $"{file}.{Guid.NewGuid().ToString("N")[..8]}.part";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(dir, "pack.json"), "pack.json");
                var pack = Json.Load<PackManifest>(Path.Combine(dir, "pack.json"));
                foreach (var a in pack.Assets.Values)
                {
                    var src = Path.Combine(dir, a.File);
                    if (File.Exists(src)) zip.CreateEntryFromFile(src, a.File.Replace('\\', '/'), CompressionLevel.Fastest);
                }
                var art = Path.Combine(dir, "art");
                if (Directory.Exists(art))
                    foreach (var f in Directory.EnumerateFiles(art, "*", SearchOption.AllDirectories))
                        if (ArtFile(f)) zip.CreateEntryFromFile(f, Path.GetRelativePath(dir, f).Replace('\\', '/'), CompressionLevel.Fastest);
            }
            File.Move(tmp, file, true);
        }
        finally { BestEffort.DeleteFile(tmp); }
    }

    static bool ArtFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".json" or ".png" or ".jpg" or ".jpeg" or ".webp";

    public static string Import(PackageStore store, string file, List<string>? trashed = null, List<string>? warnings = null)
    {
        using var zip = ZipFile.OpenRead(file);
        var manifestEntry = zip.GetEntry("pack.json") ?? throw new InvalidDataException("not a pack: no pack.json");
        ContentLimits.CheckArchive(zip.Entries, Taken);
        ContentLimits.CheckFreeSpace(store.Root, zip.Entries.Where(Taken).Sum(e => e.Length));
        var pack = Json.Deserialize<PackManifest>(ContentLimits.Read(manifestEntry));
        if (string.IsNullOrWhiteSpace(pack.Id) || !WorkshopContent.SafePackId(pack.Id))
            throw new InvalidDataException("bad pack id");
        var dir = store.PackageDir(pack.Id);
        if (File.Exists(Path.Combine(dir, "pack.json")) && !PackManifest.IsUserPackId(pack.Id))
            throw new InvalidDataException($"pack {pack.Id} is already installed");
        var oldName = WorkshopContent.NameIn(Path.Combine(dir, "pack.json"), pack.Id);
        using var hold = InUse.Claim(new[] { (InUse.Pack(store.Root, pack.Id), $"the pack '{oldName}'") }, "being installed");
        var staged = WorkshopContent.NewStaging(store);
        try
        {
            var root = Path.GetFullPath(staged) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(Path.Combine(staged, "assets"));
            foreach (var e in zip.Entries)
            {
                if (e.FullName == "pack.json" || !Taken(e)) continue;
                var dst = Path.GetFullPath(Path.Combine(staged, e.FullName));
                if (!dst.StartsWith(root)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                ContentLimits.Extract(e, dst);
            }
            Json.Save(Path.Combine(staged, "pack.json"), pack);
            var journal = InstallJournal.Begin(store.Root, SaveStore.ForRoot(store.Root), $"the install of the pack '{pack.Name}'", Repeatable.UtcNow);
            try
            {
                if (WorkshopContent.MoveIntoPlace(staged, journal, "pack", pack.Id)) trashed?.Add($"the pack '{oldName}'");
                if (journal.Finish() is { } warn) warnings?.Add(warn);
            }
            catch (Exception e)
            {
                var undone = journal.RollBack(Repeatable.UtcNow);
                if (undone.Problems.Count > 0) throw new IOException($"{e.Message} Putting back what it had replaced: {string.Join("; ", undone.Problems)}.", e);
                throw;
            }
        }
        finally { BestEffort.DeleteFolder(staged); }
        store.Forget(pack.Id);
        return pack.Id;
    }

    static bool Taken(ZipArchiveEntry e)
    {
        if (e.FullName == "pack.json") return true;
        if (e.FullName.EndsWith('/')) return false;
        if (e.FullName.StartsWith("art/")) return ArtFile(e.FullName);
        return e.FullName.StartsWith("assets/") && Path.GetExtension(e.FullName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".obj" or ".gif" or ".bmp";
    }

    public static string Hash(PackageStore store, string packId)
    {
        var p = Path.Combine(store.PackageDir(packId), "pack.json");
        return File.Exists(p) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant() : "";
    }
}
