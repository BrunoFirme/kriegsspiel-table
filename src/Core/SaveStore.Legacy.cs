using System.Globalization;
using KTable.Core.Model;

namespace KTable.Core;

public sealed record RunFolder(string Id, string Folder, RunMark Mark);

public sealed partial class SaveStore
{
    public const string RunMarkFile = ".run", MigratedRun = "first";
    const string HandoverRunMark = "ktable-run.json";

    string LegacyRoot(string scenarioDir) => Path.Combine(HistoryDir, scenarioDir);

    string LegacySafety(string scenarioDir, string run) => Path.Combine(LegacyRoot(scenarioDir), SafetyFolder, run);

    public string LegacyLivePath(string scenarioDir) => Path.Combine(LiveDir, scenarioDir + Ext);

    string LegacyLive(string scenarioDir, string run) => Path.Combine(LiveDir, scenarioDir, run + Ext);

    static bool IsRunId(string s) => s.Length > 0 && !s.Contains('.') && s.All(c => char.IsLetterOrDigit(c) || c == '-');

    static readonly System.Text.RegularExpressions.Regex OldLiveName = new(@"^(\d{8}-\d{6}(-\d+)?|first)$");

    static bool IsOldLive(string path) => OldLiveName.IsMatch(Path.GetFileNameWithoutExtension(path)) && ReadIdOrNull(path) == null;

    IReadOnlyList<RunFolder> LegacyRuns(string scenarioDir) => RunsUnder(LegacyRoot(scenarioDir));

    static IReadOnlyList<RunFolder> RunsUnder(string root)
    {
        var list = new List<RunFolder>();
        if (!Directory.Exists(root)) return list;
        foreach (var markFile in Directory.EnumerateFiles(root, RunMarkFile, SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(markFile)!;
            if (IsHidden(root, folder)) continue;
            if (ReadMark(markFile) is { } mark) list.Add(new RunFolder(mark.Id, folder, mark));
        }
        return list;
    }

    static RunMark? ReadMark(string markFile)
    {
        try { var m = Json.Load<RunMark>(markFile); return IsRunId(m.Id) ? m : null; }
        catch (Exception e) when (IsDamage(e) || e is IOException or UnauthorizedAccessException) { return null; }
    }

    static void WriteMark(string folder, RunMark mark) => Json.WriteAtomic(Path.Combine(folder, RunMarkFile), Json.Serialize(mark, indented: true));

    string? LegacyCurrent(string scenarioDir)
    {
        try
        {
            var f = Path.Combine(SavesRoot(scenarioDir), CurrentFile);
            if (!File.Exists(f)) return null;
            var id = File.ReadAllText(f).Trim();
            return IsRunId(id) ? id : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    string LegacyRunId(string scenarioDir, DateTime nowUtc)
    {
        var taken = LegacyRuns(scenarioDir).Select(r => r.Id).ToHashSet();
        for (int n = 1; ; n++)
        {
            var id = Stamp(nowUtc) + (n == 1 ? "" : "-" + n.ToString(CultureInfo.InvariantCulture));
            if (!taken.Contains(id) && !File.Exists(LegacyLive(scenarioDir, id)) && !Directory.Exists(LegacySafety(scenarioDir, id))) return id;
        }
    }

    void MakeLegacyRun(string scenarioDir, string run, string name, DateTime nowUtc)
    {
        if (LegacyRuns(scenarioDir).Any(r => r.Id == run)) return;
        var root = LegacyRoot(scenarioDir);
        Directory.CreateDirectory(root);
        var folder = FreeFolder(root, SaveNames.Clean(name, "Game 1"));
        Directory.CreateDirectory(folder);
        WriteMark(folder, new RunMark { Id = run, Created = nowUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) });
    }

    void SetLegacyCurrent(string scenarioDir, string run)
    {
        Directory.CreateDirectory(SavesRoot(scenarioDir));
        Json.WriteAtomic(Path.Combine(SavesRoot(scenarioDir), CurrentFile), run);
    }

    bool HasLegacyGame(string scenarioDir)
    {
        if (File.Exists(LegacyLivePath(scenarioDir))) return true;
        var root = LegacyRoot(scenarioDir);
        return Directory.Exists(root) && Directory.EnumerateFiles(root, "*" + Ext).Any(f => CheckpointName.IsMatch(Path.GetFileName(f)));
    }

    void MigrateToRun(string scenarioDir, DateTime nowUtc)
    {
        if (!HasLegacyGame(scenarioDir)) return;
        var legacy = LegacyLivePath(scenarioDir);
        var live = LegacyLive(scenarioDir, MigratedRun);
        var when = File.Exists(legacy) ? File.GetLastWriteTimeUtc(legacy) : nowUtc;
        if (File.Exists(legacy) && !File.Exists(live)) MoveSaveWithLogs(legacy, live);
        var root = LegacyRoot(scenarioDir);
        if (Directory.Exists(root))
            foreach (var f in Directory.GetFiles(root, "*" + Ext).Where(f => CheckpointName.IsMatch(Path.GetFileName(f))))
            {
                var to = Path.Combine(LegacySafety(scenarioDir, MigratedRun), Path.GetFileName(f));
                if (!File.Exists(to)) MoveFile(f, to);
            }
        MakeLegacyRun(scenarioDir, MigratedRun, "Game 1", when);
        if (LegacyCurrent(scenarioDir) == null) SetLegacyCurrent(scenarioDir, MigratedRun);
    }

    bool IsOldLayout(string scenarioDir) =>
        File.Exists(LegacyLivePath(scenarioDir)) || Directory.Exists(LegacyRoot(scenarioDir)) || LegacyCurrent(scenarioDir) != null
        || (Directory.Exists(SavesRoot(scenarioDir)) && !File.Exists(Path.Combine(SavesRoot(scenarioDir), CurrentFile))
            && Directory.EnumerateFiles(SavesRoot(scenarioDir), "*" + Ext).Any(IsOldLive));

    static string? ReadIdOrNull(string path)
    {
        try { return Json.ReadSaveId(path); }
        catch (Exception e) when (IsFileTrouble(e)) { return null; }
    }

    public List<string> ConvertToSaves(DateTime nowUtc)
    {
        var lines = new List<string>();
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(LiveDir))
        {
            foreach (var f in Directory.GetFiles(LiveDir, "*" + Ext))
                if (!Path.GetFileNameWithoutExtension(f).Contains('.')) dirs.Add(Path.GetFileNameWithoutExtension(f));
            foreach (var d in Directory.GetDirectories(LiveDir))
                if (!Path.GetFileName(d).Equals(MirrorsFolder, StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(d).StartsWith('.')) dirs.Add(Path.GetFileName(d));
        }
        if (Directory.Exists(HistoryDir))
            foreach (var d in Directory.GetDirectories(HistoryDir))
                if (!Path.GetFileName(d).Equals(MirrorsFolder, StringComparison.OrdinalIgnoreCase)) dirs.Add(Path.GetFileName(d));
        foreach (var dir in dirs)
        {
            try { lines.AddRange(ConvertScenario(dir, nowUtc)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { lines.Add($"{dir}: not converted yet ({e.Message}); tried again at the next start"); }
        }
        lines.AddRange(MoveTree(Path.Combine(HistoryDir, MirrorsFolder), Path.Combine(MirrorDir, SafetyFolder)));
        RemoveIfEmpty(HistoryDir);
        return lines;
    }

    public List<string> ConvertScenario(string scenarioDir, DateTime nowUtc)
    {
        var lines = new List<string>();
        if (!IsOldLayout(scenarioDir)) return lines;
        MigrateToRun(scenarioDir, nowUtc);
        var root = SavesRoot(scenarioDir);
        var legacyRoot = LegacyRoot(scenarioDir);
        Directory.CreateDirectory(root);
        var oldCurrent = LegacyCurrent(scenarioDir);
        string? newCurrent = null;
        var runs = LegacyRuns(scenarioDir);
        var orphans = Directory.GetFiles(root, "*" + Ext)
            .Where(f => IsOldLive(f) && runs.All(r => r.Id != Path.GetFileNameWithoutExtension(f))).ToList();
        foreach (var run in runs)
        {
            var parent = Path.Combine(root, Path.GetRelativePath(legacyRoot, Path.GetDirectoryName(run.Folder)!));
            var name = Path.GetFileName(run.Folder);
            var target = ConvertRun(scenarioDir, run, parent, name, nowUtc, lines);
            if (target != null && run.Id == oldCurrent) newCurrent = target;
        }
        foreach (var orphan in orphans.Where(File.Exists))
        {
            var id = Path.GetFileNameWithoutExtension(orphan);
            var target = FreeSaveFile(root, NextGameName(scenarioDir));
            MoveSaveWithLogs(orphan, target);
            AdoptSafety(scenarioDir, id, target);
            lines.Add($"{orphan} -> {target}");
            if (id == oldCurrent) newCurrent = target;
        }
        lines.AddRange(MoveTree(legacyRoot, root));
        var current = Path.Combine(root, CurrentFile);
        if (newCurrent != null) SetCurrent(scenarioDir, newCurrent);
        else if (oldCurrent != null) BestEffort.DeleteFile(current);
        RemoveIfEmpty(legacyRoot);
        return lines;
    }

    string? ConvertRun(string scenarioDir, RunFolder run, string parent, string name, DateTime nowUtc, List<string> lines)
    {
        var live = LegacyLive(scenarioDir, run.Id);
        var safety = LegacySafety(scenarioDir, run.Id);
        var turnFiles = Directory.GetFiles(run.Folder, "*" + Ext).ToList();
        string? source = File.Exists(live) ? live
            : (Directory.Exists(safety) ? Directory.GetFiles(safety, "*" + Ext).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null)
            ?? turnFiles.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        string? target = null;
        if (source != null)
        {
            target = FreeSaveFile(parent, name);
            if (source == live) MoveSaveWithLogs(live, target);
            else Json.CopyAtomic(source, target, overwrite: false);
            lines.Add($"{source} -> {target}");
            AdoptSafety(scenarioDir, run.Id, target);
        }
        var inSave = target != null ? EndingsIn(target) : new HashSet<string>();
        var kept = turnFiles.Where(f => !IsTurnOf(f, inSave)).ToList();
        var trashed = turnFiles.Except(kept).ToList();
        if (trashed.Count > 0)
        {
            var entry = TrashLooseSaves(scenarioDir, Path.GetRelativePath(SavesRoot(scenarioDir), parent), $"{name} · turns", trashed, nowUtc);
            lines.Add($"{trashed.Count} turn save(s) of {name} already inside {Path.GetFileName(target)}: in the trash at {entry}");
        }
        BestEffort.DeleteFile(Path.Combine(run.Folder, RunMarkFile));
        if (Directory.EnumerateFileSystemEntries(run.Folder).Any())
        {
            var leftovers = FreeFolder(parent, name);
            lines.AddRange(MoveTree(run.Folder, leftovers));
        }
        RemoveIfEmpty(run.Folder);
        return target;
    }

    static HashSet<string> EndingsIn(string savePath)
    {
        try
        {
            var save = Json.Load<SaveFile>(savePath);
            return (save.Turns ?? new()).Select(t => $"{t.Turn}|{t.EndedAt}").ToHashSet();
        }
        catch (Exception e) when (IsFileTrouble(e)) { return new(); }
    }

    static bool IsTurnOf(string file, HashSet<string> endings)
    {
        try { return Json.ReadSaveHeadline(file) is { } h && endings.Contains($"{h.Preview.Turn}|{h.Saved}"); }
        catch (Exception e) when (IsFileTrouble(e)) { return false; }
    }

    void AdoptSafety(string scenarioDir, string run, string target)
    {
        var from = LegacySafety(scenarioDir, run);
        var to = Path.Combine(SavesRoot(scenarioDir), SafetyFolder, run);
        if (Directory.Exists(from)) MoveTree(from, to);
        if (Directory.Exists(to)) WriteSafetyOwner(scenarioDir, to, target);
    }

    string TrashLooseSaves(string scenarioDir, string parent, string name, IReadOnlyList<string> files, DateTime nowUtc)
    {
        var entry = NewTrashEntry($"{scenarioDir} · {name}{SavesSuffix}", nowUtc);
        Directory.CreateDirectory(entry);
        Json.WriteAtomic(Path.Combine(entry, TrashedFrom), Json.Serialize(new SavesInTrash { Scenario = scenarioDir, Parent = parent, Name = name }));
        var folder = Path.Combine(entry, TrashedItem, name);
        Directory.CreateDirectory(folder);
        foreach (var f in files) MoveFile(f, Path.Combine(folder, Path.GetFileName(f)));
        return entry;
    }

    static bool IsLegacyEntry(string entry) =>
        Directory.Exists(Path.Combine(entry, TrashedLive)) || Directory.Exists(Path.Combine(entry, TrashedSaves))
        || (Directory.Exists(entry) && Directory.EnumerateFiles(entry, RunMarkFile, SearchOption.AllDirectories).Any());

    void RestoreLegacyGame(TrashEntry e, string scenarioDir, DateTime nowUtc)
    {
        var save = Path.Combine(e.Path, scenarioDir + Ext);
        var cps = Path.Combine(e.Path, "checkpoints");
        if (File.Exists(save) || Directory.Exists(cps)) AdoptLegacyGame(scenarioDir, save, cps, "Restored", nowUtc);
        MergeLegacySaves(scenarioDir, Path.Combine(e.Path, TrashedSaves), Path.Combine(e.Path, TrashedLive), takeCurrent: true, nowUtc);
        ConvertScenario(scenarioDir, nowUtc);
    }

    void AdoptLegacyGame(string scenarioDir, string saveFile, string? checkpoints, string name, DateTime nowUtc)
    {
        var run = LegacyRunId(scenarioDir, nowUtc);
        var live = LegacyLive(scenarioDir, run);
        if (File.Exists(saveFile)) MoveSaveWithLogs(saveFile, live);
        if (checkpoints != null) MoveInto(checkpoints, LegacySafety(scenarioDir, run));
        MakeLegacyRun(scenarioDir, run, name, nowUtc);
        SetLegacyCurrent(scenarioDir, run);
    }

    string RestoreLegacySaves(TrashEntry e, SavesInTrash from, DateTime nowUtc)
    {
        var root = LegacyRoot(from.Scenario);
        var parent = Path.GetFullPath(Path.Combine(root, from.Parent));
        if (!IsUnder(parent, root)) parent = root;
        Directory.CreateDirectory(parent);
        var itemDir = Path.Combine(e.Path, TrashedItem);
        string? back = null;
        if (Directory.Exists(itemDir))
            foreach (var item in Directory.EnumerateFileSystemEntries(itemDir))
            {
                if (Directory.Exists(item)) { back = FreeFolder(parent, Path.GetFileName(item)); MoveInto(item, back); }
                else { back = FreeSaveFile(parent, Path.GetFileNameWithoutExtension(item)); MoveFile(item, back); }
            }
        var lives = Path.Combine(e.Path, TrashedLive);
        if (Directory.Exists(lives))
            foreach (var f in Directory.GetFiles(lives))
            {
                var to = Path.Combine(LiveDir, from.Scenario, Path.GetFileName(f));
                if (!File.Exists(to)) MoveFile(f, to);
            }
        var safety = Path.Combine(e.Path, TrashedSafety);
        if (Directory.Exists(safety))
            foreach (var d in Directory.GetDirectories(safety))
            {
                var to = Path.Combine(root, SafetyFolder, Path.GetFileName(d));
                if (!Directory.Exists(to)) MoveInto(d, to);
            }
        Directory.Delete(e.Path, true);
        ConvertScenario(from.Scenario, nowUtc);
        return $"{Path.GetFileNameWithoutExtension(back ?? from.Name)} is back among the saves of {from.Scenario}.";
    }

    void MergeLegacySaves(string scenarioDir, string savesFrom, string liveFrom, bool takeCurrent, DateTime nowUtc)
    {
        if (!Directory.Exists(savesFrom) && !Directory.Exists(liveFrom)) return;
        var ids = new HashSet<string>();
        if (Directory.Exists(liveFrom))
            foreach (var f in Directory.GetFiles(liveFrom, "*" + Ext)) if (IsRunId(Path.GetFileNameWithoutExtension(f))) ids.Add(Path.GetFileNameWithoutExtension(f));
        if (Directory.Exists(savesFrom))
            foreach (var m in Directory.EnumerateFiles(savesFrom, HandoverRunMark, SearchOption.AllDirectories).ToList())
                File.Move(m, Path.Combine(Path.GetDirectoryName(m)!, RunMarkFile), true);
        var marks = Directory.Exists(savesFrom)
            ? Directory.EnumerateFiles(savesFrom, RunMarkFile, SearchOption.AllDirectories).Where(m => !IsHidden(savesFrom, Path.GetDirectoryName(m)!)).ToList()
            : new List<string>();
        foreach (var m in marks) if (ReadMark(m) is { } mark) ids.Add(mark.Id);
        var map = new Dictionary<string, string>();
        bool Taken(string id) => map.ContainsValue(id) || File.Exists(LegacyLive(scenarioDir, id)) || LegacyRuns(scenarioDir).Any(r => r.Id == id)
            || Directory.Exists(LegacySafety(scenarioDir, id)) || Directory.Exists(Path.Combine(SavesRoot(scenarioDir), SafetyFolder, id));
        foreach (var id in ids)
        {
            var to = id;
            for (int n = 0; Taken(to); n++) to = LegacyRunId(scenarioDir, nowUtc.AddSeconds(n + 1));
            map[id] = to;
        }
        foreach (var m in marks)
            if (ReadMark(m) is { } mark && map.TryGetValue(mark.Id, out var to) && to != mark.Id) { mark.Id = to; WriteMark(Path.GetDirectoryName(m)!, mark); }
        string? incomingCurrent = null;
        if (Directory.Exists(liveFrom))
        {
            var cur = Path.Combine(liveFrom, CurrentFile);
            if (File.Exists(cur)) { var c = File.ReadAllText(cur).Trim(); if (map.TryGetValue(c, out var mc)) incomingCurrent = mc; }
            var lives = Directory.GetFiles(liveFrom, "*" + Ext).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            if (incomingCurrent == null && lives.Select(Path.GetFileNameWithoutExtension).FirstOrDefault(id => id != null && map.ContainsKey(id)) is { } newest) incomingCurrent = map[newest];
            foreach (var f in lives)
            {
                var id = Path.GetFileNameWithoutExtension(f);
                if (map.TryGetValue(id, out var to)) MoveSaveWithLogs(f, LegacyLive(scenarioDir, to));
            }
        }
        var root = LegacyRoot(scenarioDir);
        if (Directory.Exists(savesFrom))
        {
            var safety = Path.Combine(savesFrom, SafetyFolder);
            if (Directory.Exists(safety))
                foreach (var d in Directory.GetDirectories(safety))
                {
                    var id = Path.GetFileName(d);
                    var to = Path.Combine(root, SafetyFolder, map.GetValueOrDefault(id, id));
                    if (!Directory.Exists(to)) MoveInto(d, to);
                }
            foreach (var item in Directory.EnumerateFileSystemEntries(savesFrom).ToList())
            {
                var name = Path.GetFileName(item);
                if (name.StartsWith('.')) continue;
                Directory.CreateDirectory(root);
                if (Directory.Exists(item)) MoveInto(item, FreeFolder(root, name));
                else if (name.EndsWith(Ext, StringComparison.OrdinalIgnoreCase)) MoveFile(item, FreeSaveFile(root, Path.GetFileNameWithoutExtension(name)));
            }
        }
        foreach (var (_, to) in map)
            if (File.Exists(LegacyLive(scenarioDir, to)) && LegacyRuns(scenarioDir).All(r => r.Id != to)) MakeLegacyRun(scenarioDir, to, "Game 1", nowUtc);
        if (incomingCurrent != null && (takeCurrent || CurrentSave(scenarioDir) == null)) SetLegacyCurrent(scenarioDir, incomingCurrent);
    }

    public void AddLegacySaves(string scenarioDir, string savesFrom, string liveFrom, DateTime nowUtc)
    {
        MergeLegacySaves(scenarioDir, savesFrom, liveFrom, takeCurrent: true, nowUtc);
        ConvertScenario(scenarioDir, nowUtc);
    }

    static void MoveSaveWithLogs(string from, string to)
    {
        MoveFile(from, to);
        SaveLock.ClearStale(from);
        foreach (var (log, dst) in new[] { (SaveLog.PathFor(from), SaveLog.PathFor(to)), (SaveLog.OldPathFor(from), SaveLog.OldPathFor(to)) })
            if (File.Exists(log) && !File.Exists(dst)) MoveFile(log, dst);
    }
}
