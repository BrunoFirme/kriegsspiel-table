using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text.Json;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public sealed class WorkshopItemMeta
{
    public int Format { get; set; } = 1;
    public string Kind { get; set; } = "pack";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Packs { get; set; } = new();
    public List<string> BundledPacks { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public List<string> InstalledPacks { get; set; } = new();
    public bool CarriesGame { get; set; }
    public string? GameRun { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? InstalledGame { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public List<string> Trashed { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public List<string> Warnings { get; set; } = new();
}

public sealed class WorkshopEntry
{
    public ulong Id;
    public string Title = "", Description = "", PreviewUrl = "", Tags = "";
    public WorkshopItemMeta? Meta;
    public uint Subscriptions;
    public bool Subscribed;
    public ulong Owner;
    public uint Updated;
}

public static class WorkshopContent
{
    public const string MetaFile = "ktable-item.json";
    public const string GameFile = "game.kstable";
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { ".json", ".png", ".jpg", ".jpeg", ".webp", ".obj", ".gif", ".bmp", SaveStore.Ext };

    public enum PackMode
    {
        None,
        Used,
        Whole,
    }

    public static string Stage(PackageStore store, string kind, string id, PackMode packs = PackMode.None, SavesMode saves = SavesMode.None)
    {
        var src = kind == "pack" ? store.PackageDir(id) : Path.Combine(store.Root, "scenarios", id);
        var manifest = kind == "pack" ? "pack.json" : "scenario.json";
        if (!File.Exists(Path.Combine(src, manifest))) throw new FileNotFoundException($"{kind} {id} is not installed");
        var dst = Path.Combine(Path.GetTempPath(), "ktable-workshop", kind + "-" + id);
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyTree(src, dst);
        var meta = new WorkshopItemMeta { Kind = kind, Id = id };
        if (kind == "pack") meta.Name = Json.Load<PackManifest>(Path.Combine(src, manifest)).Name;
        else
        {
            var sc = Json.Load<Scenario>(Path.Combine(src, manifest));
            meta.Name = sc.Name;
            meta.Packs = sc.Packs.ToList();
            meta.GameRun = SavesHandover.Stage(store, SaveStore.ForRoot(store.Root), id, saves, dst);
            meta.CarriesGame = meta.GameRun != null;
            if (packs != PackMode.None)
            {
                var used = sc.UmpireBoard.Select(p => p.Def).ToHashSet();
                foreach (var pid in sc.Packs)
                {
                    if (store.Pack(pid) is not { } pack) continue;
                    var to = Path.Combine(dst, "packs", pid);
                    if (packs == PackMode.Whole) CopyTree(store.PackageDir(pid), to);
                    else WriteTrimmed(store, pack, used, to);
                    meta.BundledPacks.Add(pid);
                }
            }
        }
        File.WriteAllText(Path.Combine(dst, MetaFile), JsonSerializer.Serialize(meta, Opts));
        return dst;
    }

    static void CopyTree(string src, string dst)
    {
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            if (!Allowed.Contains(Path.GetExtension(f)) || Path.GetFileName(f) == MetaFile || rel == GameFile || rel.StartsWith("packs" + Path.DirectorySeparatorChar)) continue;
            var to = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, true);
        }
    }

    static void WriteTrimmed(PackageStore store, PackManifest pack, HashSet<string> used, string to)
    {
        var keep = Naming.WithParts(pack.Pieces.Where(d => used.Contains(d.Id)), id => pack.Pieces.FirstOrDefault(x => x.Id == id));
        var assets = keep.SelectMany(Naming.AssetsOf).ToHashSet();
        var trimmed = new PackManifest
        {
            Format = pack.Format, Id = pack.Id, Name = pack.Name, Source = pack.Source, Partial = true,
            Pieces = keep.OrderBy(d => d.Id, StringComparer.Ordinal).ToList(),
            Assets = pack.Assets.Where(kv => assets.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        Directory.CreateDirectory(to);
        foreach (var a in trimmed.Assets.Values)
        {
            var src = Path.Combine(store.PackageDir(pack.Id), a.File);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(to, a.File);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, true);
        }
        Json.Save(Path.Combine(to, "pack.json"), trimmed);
    }

    public static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

    static bool SafeId(string id) =>
        id.Length is > 0 and <= 80 && !id.Contains("..") && char.IsAsciiLetterOrDigit(id[0]) && char.IsAsciiLetterOrDigit(id[^1])
        && id.All(c => char.IsLetterOrDigit(c) && c < 128 || c is '.' or '-' or '_')
        && id.Split('.')[0].ToUpperInvariant() is not ("CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7"
            or "COM8" or "COM9" or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9");

    internal static bool SafePackId(string id) =>
        SafeId(id) && !id.Equals("scenarios", StringComparison.OrdinalIgnoreCase) && !id.Equals(SheetImporter.Package, StringComparison.OrdinalIgnoreCase)
        && !StandardPacks.IsStandard(id.ToLowerInvariant());

    public static WorkshopItemMeta Install(PackageStore store, string folder, SaveStore? saves = null)
    {
        lock (InstallGate) return InstallOne(store, folder, saves);
    }

    static readonly object InstallGate = new();

    static WorkshopItemMeta InstallOne(PackageStore store, string folder, SaveStore? saves)
    {
        var metaPath = Path.Combine(folder, MetaFile);
        if (!File.Exists(metaPath)) throw new InvalidDataException("not a Kriegsspiel Table item");
        ContentLimits.CheckFreeSpace(store.Root, ContentLimits.CheckFolder(folder));
        if (new FileInfo(metaPath).Length > ContentLimits.MaxManifestBytes) throw new TooLargeException($"its {MetaFile} is larger than an item's description can be");
        var meta = JsonSerializer.Deserialize<WorkshopItemMeta>(File.ReadAllText(metaPath), Opts) ?? throw new InvalidDataException("bad item");
        if (!SafeId(meta.Id) || meta.Kind is not ("pack" or "scenario")) throw new InvalidDataException("bad item id");
        if (meta.Kind == "pack" && !SafePackId(meta.Id)) throw new InvalidDataException(StandardPacks.IsStandard(meta.Id.ToLowerInvariant()) ? $"{meta.Id} is one of the app's own packs, already installed" : "bad item id");
        saves ??= SaveStore.ForRoot(store.Root);
        meta.Trashed.Clear();
        meta.InstalledPacks.Clear();
        SweepStaging(store);
        var packs = new List<PackStep>();
        var scDir = Path.Combine(store.Root, "scenarios", meta.Id);
        var scName = NameIn(Path.Combine(scDir, "scenario.json"), meta.Id);
        var game = Path.Combine(folder, GameFile);
        if (meta.Kind == "pack") { if (PlanPack(store, folder, meta.Id, carried: false) is { } p) packs.Add(p); }
        else
        {
            Json.Load<Scenario>(Path.Combine(folder, "scenario.json"));
            if (File.Exists(game)) Json.Load<SaveFile>(game);
            var packsDir = Path.Combine(folder, "packs");
            if (Directory.Exists(packsDir))
                foreach (var d in Directory.GetDirectories(packsDir))
                    if (Path.GetFileName(d) is { } pid && SafePackId(pid) && PlanPack(store, d, pid, carried: true) is { } p) packs.Add(p);
        }
        var folders = packs.Select(p => (InUse.Pack(store.Root, p.Id), $"the pack '{(p.Existing?.Name is { Length: > 0 } n ? n : p.Id)}'")).ToList();
        if (meta.Kind == "scenario") folders.Add((InUse.Scenario(store.Root, meta.Id), $"the scenario '{scName}'"));
        using var hold = InUse.Claim(folders, "being installed");
        var journal = InstallJournal.Begin(store.Root, saves, $"the install of the {meta.Kind} '{(meta.Name.Length > 0 ? meta.Name : meta.Id)}'", Repeatable.UtcNow);
        try
        {
            foreach (var p in packs)
            {
                InstallPack(store, p, journal, meta.Trashed);
                if (p.Carried) meta.InstalledPacks.Add(p.Id);
            }
            if (meta.Kind == "scenario")
            {
                if (PutInPlace(store, folder, "scenario", meta.Id, skipPacks: true, null, journal)) meta.Trashed.Add($"the scenario '{scName}'");
                if (SavesHandover.Install(saves, meta.Id, folder, meta.GameRun, Repeatable.UtcNow) is { } run)
                    meta.InstalledGame = saves.RunFolderOf(meta.Id, run) is { } runFolder ? Path.GetFileName(runFolder) : "";
            }
            if (journal.Finish() is { } warn) meta.Warnings.Add(warn);
            return meta;
        }
        catch (Exception e)
        {
            var undone = journal.RollBack(Repeatable.UtcNow);
            if (undone.Problems.Count > 0) throw new IOException($"{e.Message} Putting back what it had replaced: {string.Join("; ", undone.Problems)}.", e);
            throw;
        }
        finally { foreach (var p in packs) store.Forget(p.Id); }
    }

    sealed record PackStep(string Id, string Folder, PackManifest Incoming, PackManifest? Existing, bool Carried);

    static PackStep? PlanPack(PackageStore store, string folder, string id, bool carried)
    {
        var incoming = Json.Load<PackManifest>(Path.Combine(folder, "pack.json"));
        if (incoming.Id != id) throw new InvalidDataException($"pack id {incoming.Id} does not match {id}");
        var existing = Existing(store, id);
        return Replaces(incoming, existing, id, carried) ? new PackStep(id, folder, incoming, existing, carried) : null;
    }

    static PackManifest? Existing(PackageStore store, string id)
    {
        var path = Path.Combine(store.PackageDir(id), "pack.json");
        return File.Exists(path) ? Json.Load<PackManifest>(path) : null;
    }

    static bool Replaces(PackManifest incoming, PackManifest? existing, string id, bool carried) =>
        existing == null || !(incoming.Partial && !existing.Partial) && !(carried && !PackManifest.IsUserPackId(id) && !existing.Partial);

    public static List<string> PacksItWouldPutInPlace(PackageStore store, string file)
    {
        var ids = new List<string>();
        using var zip = ZipFile.OpenRead(file);
        static PackManifest? Read(ZipArchiveEntry? e) => e == null ? null : Json.Deserialize<PackManifest>(ContentLimits.Read(e));
        if (zip.GetEntry(MetaFile) is not { } metaEntry)
        {
            if (Read(zip.GetEntry("pack.json")) is { } bare && PackManifest.IsUserPackId(bare.Id)) ids.Add(bare.Id);
            return ids;
        }
        var meta = JsonSerializer.Deserialize<WorkshopItemMeta>(ContentLimits.Read(metaEntry), Opts);
        if (meta == null) return ids;
        if (meta.Kind == "pack")
        {
            if (SafePackId(meta.Id) && Read(zip.GetEntry("pack.json")) is { } p && p.Id == meta.Id && Replaces(p, Existing(store, meta.Id), meta.Id, carried: false)) ids.Add(meta.Id);
            return ids;
        }
        foreach (var e in zip.Entries)
        {
            var parts = e.FullName.Replace('\\', '/').Split('/');
            if (parts.Length != 3 || parts[0] != "packs" || parts[2] != "pack.json" || !SafePackId(parts[1])) continue;
            if (Read(e) is { } p && p.Id == parts[1] && Replaces(p, Existing(store, p.Id), p.Id, carried: true)) ids.Add(p.Id);
        }
        return ids;
    }

    static void InstallPack(PackageStore store, PackStep p, InstallJournal journal, List<string> trashed)
    {
        var dir = store.PackageDir(p.Id);
        var (incoming, existing) = (p.Incoming, p.Existing);
        Action<string>? merge = existing is { Partial: true } && incoming.Partial ? staged =>
        {
            CopyInto(dir, staged, skipPacks: false, overwrite: false);
            var pieces = incoming.Pieces.ToList();
            var ids = pieces.Select(d => d.Id).ToHashSet();
            pieces.AddRange(existing.Pieces.Where(d => ids.Add(d.Id)));
            incoming.Pieces = pieces.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            foreach (var (sha, a) in existing.Assets) incoming.Assets.TryAdd(sha, a);
            Json.Save(Path.Combine(staged, "pack.json"), incoming);
        } : null;
        if (PutInPlace(store, p.Folder, "pack", p.Id, skipPacks: false, merge, journal))
            trashed.Add($"the pack '{(existing?.Name is { Length: > 0 } n ? n : p.Id)}'");
        store.Forget(p.Id);
    }

    internal static string NameIn(string manifest, string fallback)
    {
        try { return File.Exists(manifest) && Json.PeekString(manifest, "name") is { Length: > 0 } n ? System.Text.RegularExpressions.Regex.Unescape(n) : fallback; }
        catch (ArgumentException) { return fallback; }
    }

    internal const string StagingDir = InstallJournal.Folder;

    internal static string NewStaging(PackageStore store)
    {
        var dir = Path.Combine(store.Root, StagingDir, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void SweepStaging(PackageStore store)
    {
        var dir = Path.Combine(store.Root, StagingDir);
        if (!Directory.Exists(dir)) return;
        foreach (var d in Directory.GetDirectories(dir))
            if (DateTime.UtcNow - Directory.GetCreationTimeUtc(d) > TimeSpan.FromHours(1)) BestEffort.DeleteFolder(d);
    }

    static bool PutInPlace(PackageStore store, string folder, string kind, string id, bool skipPacks, Action<string>? finish, InstallJournal journal)
    {
        var staged = NewStaging(store);
        try
        {
            CopyInto(folder, staged, skipPacks);
            finish?.Invoke(staged);
            return MoveIntoPlace(staged, journal, kind, id);
        }
        finally { BestEffort.DeleteFolder(staged); }
    }

    internal static bool MoveIntoPlace(string staged, InstallJournal journal, string kind, string id)
    {
        var dst = journal.Target(kind, id);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        var entry = journal.MakeWay(kind, id, Repeatable.UtcNow);
        Directory.Move(staged, dst);
        return entry != null;
    }

    static void CopyInto(string folder, string dst, bool skipPacks, bool overwrite = true)
    {
        var root = Path.GetFullPath(dst) + Path.DirectorySeparatorChar;
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(folder, f);
            if (!Allowed.Contains(Path.GetExtension(f)) || Path.GetFileName(f) == MetaFile || rel == GameFile) continue;
            if (skipPacks && rel.StartsWith("packs" + Path.DirectorySeparatorChar)) continue;
            if (rel.StartsWith(SavesHandover.SavesFolder + Path.DirectorySeparatorChar) || rel.StartsWith(SavesHandover.RunsFolder + Path.DirectorySeparatorChar)) continue;
            var to = Path.GetFullPath(Path.Combine(dst, rel));
            if (!to.StartsWith(root)) continue;
            if (!overwrite && File.Exists(to)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, true);
        }
    }

    public static long ExportFile(PackageStore store, string kind, string id, PackMode packs, string file, SavesMode saves = SavesMode.None)
    {
        var staged = Stage(store, kind, id, packs, saves);
        var tmp = $"{file}.{Guid.NewGuid().ToString("N")[..8]}.part";
        try
        {
            ZipFile.CreateFromDirectory(staged, tmp, CompressionLevel.Fastest, false);
            File.Move(tmp, file, true);
        }
        finally
        {
            BestEffort.DeleteFile(tmp);
            BestEffort.DeleteFolder(staged);
        }
        return new FileInfo(file).Length;
    }

    public static WorkshopItemMeta ImportFile(PackageStore store, string file)
    {
        lock (InstallGate) return ImportOne(store, file);
    }

    static WorkshopItemMeta ImportOne(PackageStore store, string file)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ktable-import-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            using (var zip = ZipFile.OpenRead(file))
            {
                static bool Taken(ZipArchiveEntry e) => !e.FullName.EndsWith('/') && Allowed.Contains(Path.GetExtension(e.FullName));
                ContentLimits.CheckArchive(zip.Entries, Taken);
                ContentLimits.CheckFreeSpace(Path.GetTempPath(), 2 * zip.Entries.Where(Taken).Sum(e => e.Length));
                var root = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
                foreach (var e in zip.Entries)
                {
                    if (!Taken(e)) continue;
                    var to = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!to.StartsWith(root)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    ContentLimits.Extract(e, to);
                }
            }
            if (!File.Exists(Path.Combine(tmp, MetaFile)))
            {
                var trashed = new List<string>();
                var warnings = new List<string>();
                var id = UserPacks.Import(store, file, trashed, warnings);
                return new WorkshopItemMeta { Kind = "pack", Id = id, Name = store.Pack(id)?.Name ?? id, Trashed = trashed, Warnings = warnings };
            }
            return Install(store, tmp);
        }
        finally { BestEffort.DeleteFolder(tmp); }
    }

    public static List<string> MissingPacks(PackageStore store, Scenario sc) =>
        sc.Packs.Where(p => !File.Exists(Path.Combine(store.PackageDir(p), "pack.json"))).ToList();

    public static long EstimateBytes(PackageStore store, string scenarioId, PackMode packs)
    {
        long Dir(string d) => Directory.Exists(d) ? Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        var scDir = Path.Combine(store.Root, "scenarios", scenarioId);
        long total = Dir(scDir);
        if (packs == PackMode.None) return total;
        var sc = Json.Load<Scenario>(Path.Combine(scDir, "scenario.json"));
        var used = sc.UmpireBoard.Select(p => p.Def).ToHashSet();
        foreach (var pid in sc.Packs)
        {
            if (store.Pack(pid) is not { } pack) continue;
            if (packs == PackMode.Whole) { total += Dir(store.PackageDir(pid)); continue; }
            var files = pack.Pieces.Where(d => used.Contains(d.Id))
                .SelectMany(Naming.AssetsOf).Distinct().Where(pack.Assets.ContainsKey).Select(h => Path.Combine(store.PackageDir(pid), pack.Assets[h].File));
            total += files.Where(File.Exists).Sum(f => new FileInfo(f).Length);
        }
        return total;
    }

    public static string Preview(PackageStore store, string kind, string id)
    {
        const int S = 512;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        img.Fill(new Color(0.09f, 0.09f, 0.1f));
        if (kind == "pack")
        {
            var pack = store.Pack(id)!;
            var pics = pack.Pieces.Select(d => d.Image).Where(h => h != null && pack.Assets.TryGetValue(h!, out var a) && Math.Max(a.W, a.H) > 64).Distinct().Take(16).ToList();
            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pics.Count))), cell = S / n;
            for (int i = 0; i < pics.Count; i++)
            {
                var picture = Path.Combine(store.PackageDir(id), pack.Assets[pics[i]!].File);
                if (!ContentLimits.PictureAllowed(picture) || Image.LoadFromFile(picture) is not { } p || p.IsEmpty()) continue;
                p.Convert(Image.Format.Rgba8);
                float k = (cell - 12) / (float)Math.Max(p.GetWidth(), p.GetHeight());
                p.Resize(Math.Max(1, (int)(p.GetWidth() * k)), Math.Max(1, (int)(p.GetHeight() * k)), Image.Interpolation.Lanczos);
                int cx = (i % n) * cell + (cell - p.GetWidth()) / 2, cy = (i / n) * cell + (cell - p.GetHeight()) / 2;
                img.BlendRect(p, new Rect2I(0, 0, p.GetWidth(), p.GetHeight()), new Vector2I(cx, cy));
            }
        }
        else
        {
            var sc = Json.Load<Scenario>(Path.Combine(store.Root, "scenarios", id, "scenario.json"));
            var sh = sc.Sheets.FirstOrDefault();
            if (sh != null && File.Exists(Path.Combine(store.TilesDir(sh), "index.json")))
            {
                var idx = Json.Load<TileIndex>(Path.Combine(store.TilesDir(sh), "index.json"));
                var lv = idx.Levels.FirstOrDefault(l => l.W <= 1024) ?? idx.Levels[^1];
                var map = Image.CreateEmpty(lv.W, lv.H, false, Image.Format.Rgba8);
                for (int y = 0; y < lv.Rows; y++) for (int x = 0; x < lv.Cols; x++)
                {
                    var tile = Path.Combine(store.TilesDir(sh), lv.Level.ToString(), $"{x}_{y}.{idx.Ext}");
                    if (!ContentLimits.PictureAllowed(tile) || Image.LoadFromFile(tile) is not { } t) continue;
                    t.Convert(Image.Format.Rgba8);
                    map.BlitRect(t, new Rect2I(0, 0, t.GetWidth(), t.GetHeight()), new Vector2I(x * idx.TileSize, y * idx.TileSize));
                }
                if (Math.Abs(((sh.RotDeg % 360) + 360) % 360 - 180) < 45) { map.FlipX(); map.FlipY(); }
                int side = Math.Min(map.GetWidth(), map.GetHeight());
                map = map.GetRegion(new Rect2I((map.GetWidth() - side) / 2, (map.GetHeight() - side) / 2, side, side));
                map.Resize(S, S, Image.Interpolation.Lanczos);
                img = map;
            }
        }
        var file = Path.Combine(Path.GetTempPath(), "ktable-workshop", $"preview-{kind}-{id}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        img.SavePng(file);
        return file;
    }
}
