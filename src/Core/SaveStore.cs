using System.Globalization;
using System.Text.RegularExpressions;

namespace KTable.Core;

public sealed partial class SaveStore
{
    const int ErrorNotSameDevice = unchecked((int)0x80070011);

    public const string Ext = ".kstable";
    public const int KeepTurns = 10, KeepTimed = 5, KeepStartOver = 3, TrashDays = 30;
    public static readonly TimeSpan TimedEvery = TimeSpan.FromMinutes(10);
    const string StampFormat = "yyyyMMdd-HHmmss";

    static bool TryStamp(string s, out DateTime t) =>
        DateTime.TryParseExact(s, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t);

    public static bool IsDamage(Exception e) =>
        e is System.Text.Json.JsonException or InvalidDataException or FormatException or System.Text.DecoderFallbackException or EndOfStreamException;

    public static bool SamePath(string? a, string? b)
    {
        if (a == null || b == null) return a == b;
        static string Full(string p) { try { return Path.GetFullPath(p); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return p; } }
        return string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnreachable(Exception e) => !IsDamage(e) && e is IOException or UnauthorizedAccessException;

    public static bool IsFileTrouble(Exception e) => IsDamage(e) || IsUnreachable(e);

    public static bool IsHeld(Exception e) =>
        e is UnauthorizedAccessException || e is IOException && (e.HResult & 0xFFFF) is 0x20 or 0x21;
    static readonly Regex CheckpointName = new($@"^(?<kind>{string.Join("|", CheckpointKind.Named)})-(?<turn>\d{{4}})_(?<stamp>\d{{8}}-\d{{6}})(?:-(?<n>\d+))?\.kstable$", RegexOptions.IgnoreCase);
    static readonly Regex TrashName = new(@"^(?<stamp>\d{8}-\d{6}) (?<name>.+?)(?: \((?<n>\d+)\))?$");
    const string GameSuffix = " (game)", MirrorSuffix = " (mirror)", PieceListSuffix = " (piece list)";
    const string PackManifestFile = "pack.json";

    public string LiveDir { get; }
    public string HistoryDir { get; }
    public string TrashDir { get; }

    public SaveStore(string liveDir, string historyDir, string trashDir)
    {
        LiveDir = liveDir;
        HistoryDir = historyDir;
        TrashDir = trashDir;
    }

    public static SaveStore Default =>
        Environment.GetEnvironmentVariable("KTABLE_PACKAGES") is { Length: > 0 } env
            ? new(Path.Combine(env, "_saves", "live"), Path.Combine(env, "_saves", "history"), Path.Combine(env, "_saves", "trash"))
            : new(Path.Combine(DataPaths.Local, "saves"), Path.Combine(DataPaths.Documents, "saves"), Path.Combine(DataPaths.Local, "trash"));

    public static SaveStore ForRoot(string packagesRoot)
    {
        static string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Norm(packagesRoot), Norm(PackageStore.DefaultRoot), StringComparison.OrdinalIgnoreCase)
            ? Default
            : new(Path.Combine(packagesRoot, "_saves", "live"), Path.Combine(packagesRoot, "_saves", "history"), Path.Combine(packagesRoot, "_saves", "trash"));
    }

    public string CheckpointDir(string savePath)
    {
        if (RunOf(savePath) is var (scenarioDir, run)) return Path.Combine(SavesRoot(scenarioDir), SafetyFolder, run);
        var full = Path.GetFullPath(savePath);
        var live = Path.GetFullPath(LiveDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (full.StartsWith(live, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(HistoryDir, Path.ChangeExtension(full[live.Length..], null));
        return Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full));
    }

    public string MirrorDir => Path.Combine(LiveDir, "mirrors");

    public string MirrorPath(string scenarioId) => Path.Combine(MirrorDir, Slug(scenarioId) + Ext);

    public IReadOnlyList<string> Mirrors() =>
        Directory.Exists(MirrorDir) ? Directory.GetFiles(MirrorDir, "*" + Ext).OrderByDescending(File.GetLastWriteTimeUtc).ToList() : Array.Empty<string>();

    public string TrashMirror(string mirrorPath, DateTime nowUtc)
    {
        var entry = NewTrashEntry(Path.GetFileNameWithoutExtension(mirrorPath) + MirrorSuffix, nowUtc);
        if (File.Exists(mirrorPath)) { Directory.CreateDirectory(entry); File.Move(mirrorPath, Path.Combine(entry, Path.GetFileName(mirrorPath)), true); }
        MoveInto(CheckpointDir(mirrorPath), Path.Combine(entry, "checkpoints"));
        return entry;
    }

    static string Slug(string s)
    {
        var t = new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray()).Trim('-');
        while (t.Contains("--")) t = t.Replace("--", "-");
        return t.Length == 0 ? "game" : t.Length > 60 ? t[..60].Trim('-') : t;
    }

    public IReadOnlyList<Checkpoint> Checkpoints(string savePath)
    {
        var dir = CheckpointDir(savePath);
        var list = new List<Checkpoint>();
        if (RunOf(savePath) is var (scenarioDir, run) && RunFolderOf(scenarioDir, run) is { } folder)
            foreach (var f in Directory.EnumerateFiles(folder, "*" + Ext))
                list.Add(new Checkpoint(f, CheckpointKind.Saved, 0, File.GetLastWriteTimeUtc(f)));
        if (!Directory.Exists(dir)) return list.OrderByDescending(c => c.Stamp).ToList();
        foreach (var f in Directory.EnumerateFiles(dir, "*" + Ext))
        {
            var m = CheckpointName.Match(Path.GetFileName(f));
            if (m.Success && TryStamp(m.Groups["stamp"].Value, out var stamp))
                list.Add(new Checkpoint(f, m.Groups["kind"].Value.ToLowerInvariant(), int.Parse(m.Groups["turn"].Value, CultureInfo.InvariantCulture), stamp));
            else list.Add(new Checkpoint(f, CheckpointKind.Other, 0, File.GetLastWriteTimeUtc(f)));
        }
        static int CountOf(Checkpoint c) => CheckpointName.Match(System.IO.Path.GetFileName(c.Path)) is { Success: true } m && m.Groups["n"].Success
            && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        return list.OrderByDescending(c => c.Stamp).ThenByDescending(CountOf).ThenByDescending(c => c.Path, StringComparer.Ordinal).ToList();
    }

    public string Checkpoint(string savePath, string kind, int turn, DateTime nowUtc)
    {
        var dir = CheckpointDir(savePath);
        Directory.CreateDirectory(dir);
        var dst = FreeName(Path.Combine(dir, $"{kind}-{Math.Clamp(turn, 0, 9999):D4}_{Stamp(nowUtc)}"), to => Json.CopyAtomic(savePath, to, overwrite: false));
        Prune(savePath);
        return dst;
    }

    static string FreeName(string stem, Action<string> make)
    {
        for (int n = 1; ; n++)
        {
            var dst = n == 1 ? stem + Ext : $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}{Ext}";
            if (File.Exists(dst)) continue;
            try { make(dst); return dst; }
            catch (IOException) when (File.Exists(dst) && n < 1000) { continue; }
        }
    }

    public void Prune(string savePath)
    {
        var all = Checkpoints(savePath);
        void Keep(Func<Checkpoint, bool> of, int n) { foreach (var c in all.Where(of).Skip(n)) BestEffort.DeleteFile(c.Path); }
        Keep(c => c.Kind == CheckpointKind.Turn, KeepTurns);
        Keep(c => c.Kind is CheckpointKind.Time or CheckpointKind.Close, KeepTimed);
        Keep(c => c.Kind == CheckpointKind.StartOver, KeepStartOver);
    }

    public static string? SetAside(string path, string reason, DateTime nowUtc)
    {
        if (!File.Exists(path)) return null;
        return FreeName(Path.ChangeExtension(path, null) + $".{reason}-{Stamp(nowUtc)}", to => File.Move(path, to, false));
    }

    public static string? KeepCopy(string path, string reason, DateTime nowUtc)
    {
        if (!File.Exists(path)) return null;
        return FreeName(Path.ChangeExtension(path, null) + $".{reason}-{Stamp(nowUtc)}", to => Json.CopyAtomic(path, to, overwrite: false));
    }

    static string Stamp(DateTime utc) => utc.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture);

    public string TrashScenario(string packagesRoot, string scenarioDir, DateTime nowUtc)
    {
        var entry = NewTrashEntry(scenarioDir, nowUtc);
        MoveInto(Path.Combine(packagesRoot, "scenarios", scenarioDir), Path.Combine(entry, "scenario"));
        TrashGameInto(scenarioDir, entry);
        return entry;
    }

    public string TrashScenarioFolder(string packagesRoot, string scenarioDir, DateTime nowUtc) =>
        TrashScenarioFolderAs(packagesRoot, scenarioDir, NewTrashEntry(scenarioDir, nowUtc));

    internal string TrashScenarioFolderAs(string packagesRoot, string scenarioDir, string entry)
    {
        MoveWhole(Path.Combine(packagesRoot, "scenarios", scenarioDir), entry, "scenario");
        return entry;
    }

    static void MoveWhole(string from, string entry, string sub)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(entry);
        try { MoveWhileHeld(from, Path.Combine(entry, sub)); }
        catch (IOException e) when (OtherDevice(e, from, entry))
        {
            MoveInto(from, Path.Combine(entry, sub));
        }
        catch
        {
            BestEffort.DeleteEmptyFolder(entry);
            throw;
        }
    }

    const int MoveTries = 5;
    const int MoveBackoffMs = 200;

    static bool OtherDevice(IOException e, string from, string to) =>
        e.HResult == ErrorNotSameDevice || !string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase);

    static void MoveWhileHeld(string from, string to)
    {
        for (int tries = 1; ; tries++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception e) when (tries < MoveTries && (e is UnauthorizedAccessException || e is IOException io && !OtherDevice(io, from, to)))
            {
                Thread.Sleep(MoveBackoffMs * tries);
            }
        }
    }

    public string PutBack(TrashEntry e, string packagesRoot, DateTime nowUtc)
    {
        if (e.Kind is not (TrashKind.Pack or TrashKind.Scenario)) throw new IOException("only a pack or a scenario folder is put back");
        TrashHalfMade(packagesRoot, e.Kind, e.Id, nowUtc);
        return Restore(e, packagesRoot, nowUtc);
    }

    public string? TrashHalfMade(string packagesRoot, string kind, string id, DateTime nowUtc)
    {
        var target = kind == TrashKind.Pack ? Path.Combine(packagesRoot, id) : Path.Combine(packagesRoot, "scenarios", id);
        if (!Directory.Exists(target)) return null;
        return kind == TrashKind.Pack ? TrashPackFolder(packagesRoot, id, nowUtc) : TrashScenarioFolder(packagesRoot, id, nowUtc);
    }

    public string TrashGame(string scenarioDir, DateTime nowUtc)
    {
        var entry = NewGameEntry(scenarioDir, nowUtc);
        TrashGameInto(scenarioDir, entry);
        return entry;
    }

    public string NewGameEntry(string scenarioDir, DateTime nowUtc) => NewTrashEntry(scenarioDir + GameSuffix, nowUtc);

    public IReadOnlyList<TrashEntry> TrashEntries()
    {
        if (!Directory.Exists(TrashDir)) return Array.Empty<TrashEntry>();
        var list = new List<TrashEntry>();
        foreach (var d in Directory.GetDirectories(TrashDir))
        {
            var m = TrashName.Match(Path.GetFileName(d));
            if (!m.Success) continue;
            if (!TryStamp(m.Groups["stamp"].Value, out var stamp)) stamp = Directory.GetLastWriteTimeUtc(d);
            var name = m.Groups["name"].Value;
            string kind, id;
            if (Directory.Exists(Path.Combine(d, "scenario"))) { kind = TrashKind.Scenario; id = name; }
            else if (Directory.Exists(Path.Combine(d, "pack"))) { kind = TrashKind.Pack; id = name; }
            else if (name.EndsWith(GameSuffix)) { kind = TrashKind.Game; id = name[..^GameSuffix.Length]; }
            else if (name.EndsWith(SavesSuffix)) { kind = TrashKind.Saves; id = name[..^SavesSuffix.Length]; }
            else if (name.EndsWith(MirrorSuffix)) { kind = TrashKind.Mirror; id = name[..^MirrorSuffix.Length]; }
            else if (name.EndsWith(PieceListSuffix)) { kind = TrashKind.PieceList; id = name[..^PieceListSuffix.Length]; }
            else { kind = TrashKind.Other; id = name; }
            list.Add(new TrashEntry(d, kind, id, stamp));
        }
        var bins = Path.Combine(TrashDir, PackFilesDir);
        if (Directory.Exists(bins))
            foreach (var d in Directory.GetDirectories(bins))
            {
                var m = TrashName.Match(Path.GetFileName(d));
                if (!m.Success) continue;
                if (!TryStamp(m.Groups["stamp"].Value, out var stamp)) stamp = Directory.GetLastWriteTimeUtc(d);
                list.Add(new TrashEntry(d, TrashKind.PackFiles, m.Groups["name"].Value, stamp));
            }
        return list.OrderByDescending(e => e.Stamp).ThenBy(e => e.Path, StringComparer.Ordinal).ToList();
    }

    public const string PackFilesDir = TrashKind.PackFiles;

    public string Restore(TrashEntry e, string packagesRoot, DateTime nowUtc)
    {
        switch (e.Kind)
        {
            case TrashKind.Scenario:
            {
                var target = Path.Combine(packagesRoot, "scenarios", e.Id);
                if (Directory.Exists(target)) throw new IOException($"a scenario folder named {e.Id} exists again; delete or rename it first");
                var from = Path.Combine(e.Path, "scenario");
                MoveInto(from, target);
                string note;
                try { note = RestoreGameFrom(e, e.Id, nowUtc); }
                catch (Exception) when (Directory.Exists(target) && !Directory.Exists(from))
                {
                    BestEffort.Try(() => MoveInto(target, from));
                    throw;
                }
                Directory.Delete(e.Path, true);
                return $"The scenario {e.Id} is back under Scenarios." + note;
            }
            case TrashKind.Pack:
            {
                var target = Path.Combine(packagesRoot, e.Id);
                if (Directory.Exists(target)) throw new IOException($"a pack with the id {e.Id} is installed again; remove it first");
                MoveInto(Path.Combine(e.Path, "pack"), target);
                Directory.Delete(e.Path, true);
                return $"The pack {e.Id} is installed again.";
            }
            case TrashKind.Game:
            {
                var note = RestoreGameFrom(e, e.Id, nowUtc);
                Directory.Delete(e.Path, true);
                return $"The saves of {e.Id} are back; Resume continues the game that was in play." + note;
            }
            case TrashKind.Saves: return RestoreSaves(e, nowUtc);
            case TrashKind.Mirror:
            {
                var path = Path.Combine(MirrorDir, e.Id + Ext);
                string note = "";
                if (File.Exists(path) || Directory.Exists(CheckpointDir(path))) note = $" The mirror that was there went to the trash: {TrashMirror(path, nowUtc)}";
                Directory.CreateDirectory(MirrorDir);
                var f = Path.Combine(e.Path, e.Id + Ext);
                if (File.Exists(f)) File.Move(f, path, true);
                MoveInto(Path.Combine(e.Path, "checkpoints"), CheckpointDir(path));
                Directory.Delete(e.Path, true);
                return $"The mirror of {e.Id} is back under Scenarios." + note;
            }
            case TrashKind.PackFiles: return RestorePackFiles(e, packagesRoot);
            case TrashKind.PieceList:
            {
                var manifest = Path.Combine(packagesRoot, e.Id, PackManifestFile);
                if (!File.Exists(manifest)) throw new IOException($"the pack {e.Id} is not installed here; restore or install it first");
                KeepPieceList(packagesRoot, e.Id, nowUtc);
                File.Move(Path.Combine(e.Path, PackManifestFile), manifest, true);
                Directory.Delete(e.Path, true);
                return $"The pack {e.Id} has the piece list it had before. The list this replaced is in the trash.";
            }
            default: throw new IOException("this entry is not one the app put there");
        }
    }

    string RestorePackFiles(TrashEntry e, string packagesRoot)
    {
        var dir = Path.Combine(packagesRoot, e.Id);
        var manifest = Path.Combine(dir, "pack.json");
        if (!File.Exists(manifest)) throw new IOException($"the pack {e.Id} is not installed here; restore or install it first");
        var pack = Json.Load<Model.PackManifest>(manifest);
        var listing = Path.Combine(e.Path, PackFilesListing);
        var listed = File.Exists(listing) ? Json.Load<Dictionary<string, Model.AssetEntry>>(listing) : new();
        int back = 0, kept = 0;
        foreach (var f in Directory.GetFiles(e.Path, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(e.Path, f);
            if (rel == PackFilesListing) continue;
            var dst = Path.Combine(dir, rel);
            if (File.Exists(dst)) { kept++; continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Move(f, dst);
            back++;
        }
        bool relist = false;
        foreach (var (hash, a) in listed)
            if (!pack.Assets.ContainsKey(hash) && File.Exists(Path.Combine(dir, a.File))) { pack.Assets[hash] = a; relist = true; }
        if (relist) Json.Save(manifest, pack);
        if (kept == 0) Directory.Delete(e.Path, true);
        else if (File.Exists(listing)) File.Delete(listing);
        return $"{back} file{(back == 1 ? "" : "s")} back in the pack {(pack.Name.Length > 0 ? pack.Name : e.Id)}."
            + (kept > 0 ? $" {kept} the pack has again under the same name stayed as {(kept == 1 ? "it is" : "they are")} now; the old {(kept == 1 ? "copy is" : "copies are")} still in the trash." : "");
    }

    public const string PackFilesListing = "_listed.json";

    string RestoreGameFrom(TrashEntry e, string scenarioDir, DateTime nowUtc)
    {
        var save = Path.Combine(e.Path, scenarioDir + Ext);
        var cps = Path.Combine(e.Path, "checkpoints");
        if (File.Exists(save) || Directory.Exists(cps)) AdoptLegacyGame(scenarioDir, save, cps, "Restored", nowUtc);
        MergeSaves(scenarioDir, Path.Combine(e.Path, TrashedSaves), Path.Combine(e.Path, TrashedLive), takeCurrent: true, nowUtc);
        return "";
    }

    public void DeleteTrashEntry(TrashEntry e) => Directory.Delete(e.Path, true);

    public void PutGameBack(string entry, string scenarioDir, DateTime nowUtc)
    {
        RestoreGameFrom(new TrashEntry(entry, TrashKind.Game, scenarioDir, nowUtc), scenarioDir, nowUtc);
        if (Directory.Exists(entry) && !Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories).Any()) Directory.Delete(entry, true);
    }

    internal void TrashGameInto(string scenarioDir, string entry)
    {
        var live = LegacyLivePath(scenarioDir);
        if (File.Exists(live)) { Directory.CreateDirectory(entry); File.Move(live, Path.Combine(entry, Path.GetFileName(live)), true); }
        foreach (var log in new[] { SaveLog.PathFor(live), SaveLog.OldPathFor(live) })
            if (File.Exists(log)) { Directory.CreateDirectory(entry); File.Move(log, Path.Combine(entry, Path.GetFileName(log)), true); }
        MoveInto(RunsLiveDir(scenarioDir), Path.Combine(entry, TrashedLive));
        MoveInto(SavesRoot(scenarioDir), Path.Combine(entry, TrashedSaves));
    }

    public string TrashPack(string packagesRoot, string packId, DateTime nowUtc)
    {
        var entry = NewTrashEntry(packId, nowUtc);
        MoveInto(Path.Combine(packagesRoot, packId), Path.Combine(entry, "pack"));
        return entry;
    }

    public string KeepPieceList(string packagesRoot, string packId, DateTime nowUtc)
    {
        var entry = NewTrashEntry(packId + PieceListSuffix, nowUtc);
        Directory.CreateDirectory(entry);
        Json.CopyAtomic(Path.Combine(packagesRoot, packId, PackManifestFile), Path.Combine(entry, PackManifestFile), overwrite: false);
        return entry;
    }

    public string TrashPackFolder(string packagesRoot, string packId, DateTime nowUtc) =>
        TrashPackFolderAs(packagesRoot, packId, NewTrashEntry(packId, nowUtc));

    internal string TrashPackFolderAs(string packagesRoot, string packId, string entry)
    {
        MoveWhole(Path.Combine(packagesRoot, packId), entry, "pack");
        return entry;
    }

    internal string NewTrashEntry(string name, DateTime nowUtc)
    {
        var entry = Path.Combine(TrashDir, $"{Stamp(nowUtc)} {name}");
        for (int i = 2; Directory.Exists(entry); i++) entry = Path.Combine(TrashDir, $"{Stamp(nowUtc)} {name} ({i})");
        return entry;
    }

    public int EmptyTrash(DateTime nowUtc, bool everything = false)
    {
        int n = 0;
        foreach (var e in TrashEntries())
        {
            if (!everything && nowUtc - e.Stamp < TimeSpan.FromDays(TrashDays)) continue;
            if (BestEffort.DeleteFolder(e.Path)) n++;
        }
        return n;
    }

    internal static void MoveInto(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(from, to);
            return;
        }
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f, dst, true);
        }
        Directory.Delete(from, true);
    }

    public List<string> Migrate(params string[] oldSaveDirs)
    {
        var moved = new List<string>();
        foreach (var dir in oldSaveDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*" + Ext))
            {
                var name = Path.GetFileName(f);
                try
                {
                    if (name.EndsWith(".previous" + Ext, StringComparison.OrdinalIgnoreCase))
                    {
                        var scenario = name[..^(".previous" + Ext).Length];
                        var cp = Path.Combine(CheckpointDir(LegacyLivePath(scenario)), $"{CheckpointKind.StartOver}-0000_{Stamp(File.GetLastWriteTimeUtc(f))}{Ext}");
                        if (File.Exists(cp)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(cp)!);
                        File.Move(f, cp);
                        moved.Add($"{f} -> {cp}");
                    }
                    else
                    {
                        var live = LegacyLivePath(Path.GetFileNameWithoutExtension(name));
                        if (string.Equals(Path.GetFullPath(live), Path.GetFullPath(f), StringComparison.OrdinalIgnoreCase) || File.Exists(live)) continue;
                        Directory.CreateDirectory(LiveDir);
                        File.Move(f, live);
                        moved.Add($"{f} -> {live}");
                    }
                }
                catch (Exception e) { moved.Add($"{f}: not moved ({e.Message})"); }
            }
        }
        return moved;
    }
}

public static class CheckpointKind
{
    public const string Turn = "turn", Time = "time", Close = "close", StartOver = "startover", Restore = "restore";
    public const string Other = "other", Saved = "saved";
    public static readonly IReadOnlyList<string> Named = new[] { Turn, Time, Close, StartOver, Restore };
}

public static class TrashKind
{
    public const string Scenario = "scenario", Pack = "pack", Game = "game", Mirror = "mirror", PackFiles = "pack files", PieceList = "piece list", Saves = "saves", Other = "other";
}

public sealed record Checkpoint(string Path, string Kind, int Turn, DateTime Stamp)
{
    public string Describe() => Kind switch
    {
        CheckpointKind.Turn => $"end of turn {Turn}",
        CheckpointKind.Time => $"turn {Turn}, timed",
        CheckpointKind.Close => $"turn {Turn}, when the table was closed",
        CheckpointKind.StartOver => $"turn {Turn}, before a start over",
        CheckpointKind.Restore => $"turn {Turn}, before a turn was restored",
        CheckpointKind.Saved => System.IO.Path.GetFileNameWithoutExtension(Path),
        _ => System.IO.Path.GetFileName(Path),
    } + $" ({Stamp.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture)})";
}

public sealed record TrashEntry(string Path, string Kind, string Id, DateTime Stamp)
{
    public string Describe() => Kind switch
    {
        TrashKind.Scenario => $"scenario {Id}",
        TrashKind.Pack => $"pack {Id}",
        TrashKind.Game => $"saves of {Id}",
        TrashKind.Saves => Id,
        TrashKind.Mirror => $"mirror of {Id}",
        TrashKind.PackFiles => $"pictures cleared out of {Id}",
        TrashKind.PieceList => $"piece list of {Id}, as it was before a newer one came",
        _ => Id,
    };

    public int DaysLeft(DateTime nowUtc) => Math.Max(0, (int)Math.Ceiling((Stamp.AddDays(SaveStore.TrashDays) - nowUtc).TotalDays));
}

public sealed class Checkpointer
{
    readonly SaveStore _store;
    readonly string _savePath;
    readonly object _gate = new();
    readonly bool _copiesTurns;
    int _turnsSeen;
    DateTime _lastTimed;
    bool _changedSince;

    public Checkpointer(SaveStore store, string savePath, int turnsNow, DateTime nowUtc)
    {
        _store = store;
        _savePath = savePath;
        _turnsSeen = turnsNow;
        _lastTimed = nowUtc;
        _copiesTurns = store.RunOf(savePath) == null;
    }

    public Checkpoint? AfterSave(int turns, int endedTurn, int currentTurn, DateTime nowUtc)
    {
        lock (_gate)
        {
            _changedSince = true;
            if (turns < _turnsSeen) _turnsSeen = turns;
            if (turns > _turnsSeen && _copiesTurns)
            {
                var cp = Take(CheckpointKind.Turn, endedTurn, nowUtc);
                _turnsSeen = turns;
                return cp;
            }
            _turnsSeen = turns;
            if (nowUtc - _lastTimed >= SaveStore.TimedEvery) return Take(CheckpointKind.Time, currentTurn, nowUtc);
            return null;
        }
    }

    public Checkpoint? OnClose(int currentTurn, DateTime nowUtc)
    {
        lock (_gate) return _changedSince ? Take(CheckpointKind.Close, currentTurn, nowUtc) : null;
    }

    public Checkpoint? Before(string kind, int currentTurn, DateTime nowUtc)
    {
        lock (_gate) return Take(kind, currentTurn, nowUtc);
    }

    Checkpoint? Take(string kind, int turn, DateTime nowUtc)
    {
        if (!File.Exists(_savePath)) return null;
        var path = _store.Checkpoint(_savePath, kind, turn, nowUtc);
        _lastTimed = nowUtc;
        _changedSince = false;
        return new Checkpoint(path, kind, turn, nowUtc);
    }
}
