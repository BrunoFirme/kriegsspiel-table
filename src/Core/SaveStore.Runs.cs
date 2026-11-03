using System.Globalization;
using KTable.Core.Model;

namespace KTable.Core;

public sealed record RunFolder(string Id, string Folder, RunMark Mark);

public sealed record TrashedSave(TrashEntry Entry, string Name, string Item, bool Folder);

public sealed partial class SaveStore
{
    public const string RunMarkFile = ".run", SafetyFolder = ".safety", CurrentFile = "current", MigratedRun = "first";
    const string SavesSuffix = " (saves)", TrashedLive = "live", TrashedSaves = "saves", TrashedItem = "item", TrashedSafety = "safety", TrashedFrom = "from.json";
    const string MirrorsFolder = "mirrors";

    public string SavesRoot(string scenarioDir) => Path.Combine(HistoryDir, scenarioDir);

    string RunsLiveDir(string scenarioDir) => Path.Combine(LiveDir, scenarioDir);

    public string LegacyLivePath(string scenarioDir) => Path.Combine(LiveDir, scenarioDir + Ext);

    public string LivePath(string scenarioDir, string run) => Path.Combine(LiveDir, scenarioDir, run + Ext);

    public string LivePath(string scenarioDir)
    {
        if (CurrentRun(scenarioDir) is { } run) return LivePath(scenarioDir, run);
        return HasLegacyGame(scenarioDir) ? LegacyLivePath(scenarioDir) : LivePath(scenarioDir, MigratedRun);
    }

    public bool HasGame(string scenarioDir)
    {
        var live = LivePath(scenarioDir);
        return File.Exists(live) || Checkpoints(live).Count > 0;
    }

    public (string ScenarioDir, string Run)? RunOf(string savePath)
    {
        string full;
        try { full = Path.GetFullPath(savePath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var live = Path.GetFullPath(LiveDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(live, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(Ext, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = full[live.Length..].Split(Path.DirectorySeparatorChar);
        if (parts.Length != 2 || parts[0].Equals(MirrorsFolder, StringComparison.OrdinalIgnoreCase)) return null;
        var run = parts[1][..^Ext.Length];
        return IsRunId(run) ? (parts[0], run) : null;
    }

    static bool IsRunId(string s) => s.Length > 0 && !s.Contains('.') && s.All(c => char.IsLetterOrDigit(c) || c == '-');

    public string? CurrentRun(string scenarioDir)
    {
        var marked = MarkedCurrent(scenarioDir);
        if (marked != null && RunHasGame(scenarioDir, marked)) return marked;
        var newest = RunLives(scenarioDir).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (newest != null) return Path.GetFileNameWithoutExtension(newest);
        var safety = Path.Combine(SavesRoot(scenarioDir), SafetyFolder);
        if (Directory.Exists(safety))
        {
            var kept = Directory.GetDirectories(safety).Where(d => IsRunId(Path.GetFileName(d)) && Directory.EnumerateFiles(d, "*" + Ext).Any())
                .OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
            if (kept != null) return Path.GetFileName(kept);
        }
        var withSaves = Runs(scenarioDir).Where(r => Directory.EnumerateFiles(r.Folder, "*" + Ext).Any()).OrderByDescending(r => r.Mark.Created, StringComparer.Ordinal).FirstOrDefault();
        return withSaves?.Id ?? marked;
    }

    string? MarkedCurrent(string scenarioDir)
    {
        try
        {
            var f = Path.Combine(RunsLiveDir(scenarioDir), CurrentFile);
            if (!File.Exists(f)) return null;
            var id = File.ReadAllText(f).Trim();
            return IsRunId(id) ? id : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public void SetCurrent(string scenarioDir, string run)
    {
        if (!IsRunId(run)) throw new ArgumentException("not a run", nameof(run));
        Json.WriteAtomic(Path.Combine(RunsLiveDir(scenarioDir), CurrentFile), run);
    }

    public bool RunHasGame(string scenarioDir, string run)
    {
        var live = LivePath(scenarioDir, run);
        return File.Exists(live) || Checkpoints(live).Count > 0;
    }

    IEnumerable<string> RunLives(string scenarioDir)
    {
        var dir = RunsLiveDir(scenarioDir);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*" + Ext).Where(f => IsRunId(Path.GetFileNameWithoutExtension(f)));
    }

    public IReadOnlyList<RunFolder> Runs(string scenarioDir)
    {
        var root = SavesRoot(scenarioDir);
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

    public static bool IsHidden(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.') && p != ".");
    }

    public string? RunFolderOf(string scenarioDir, string run) => Runs(scenarioDir).FirstOrDefault(r => r.Id == run)?.Folder;

    public string NewRunId(string scenarioDir, DateTime nowUtc)
    {
        var stamp = Stamp(nowUtc);
        var taken = Runs(scenarioDir).Select(r => r.Id).ToHashSet();
        for (int n = 1; ; n++)
        {
            var id = n == 1 ? stamp : $"{stamp}-{n.ToString(CultureInfo.InvariantCulture)}";
            if (!taken.Contains(id) && !File.Exists(LivePath(scenarioDir, id)) && !Directory.Exists(Path.Combine(SavesRoot(scenarioDir), SafetyFolder, id))) return id;
        }
    }

    public string NextRunName(string scenarioDir) => SaveNames.Run(Runs(scenarioDir).Count + 1);

    public string MakeRun(string scenarioDir, string run, string name, string? from, DateTime nowUtc, bool backfill = false, string? placeIn = null)
    {
        if (RunFolderOf(scenarioDir, run) is { } existing) return existing;
        var root = SavesRoot(scenarioDir);
        var parent = placeIn != null && Directory.Exists(placeIn) && (SamePath(placeIn, root) || IsUnder(placeIn, root)) && !IsHidden(root, placeIn) ? placeIn : root;
        Directory.CreateDirectory(parent);
        var folder = FreeFolder(parent, SaveNames.Clean(name, SaveNames.Run(1)));
        Directory.CreateDirectory(folder);
        WriteMark(folder, new RunMark { Id = run, Created = nowUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), From = from, Backfill = backfill });
        return folder;
    }

    public string EnsureRunFolder(string scenarioDir, string run, DateTime nowUtc) =>
        RunFolderOf(scenarioDir, run) ?? MakeRun(scenarioDir, run, NextRunName(scenarioDir), null, nowUtc);

    static string FreeFolder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(parent, $"{name} ({n.ToString(CultureInfo.InvariantCulture)})");
        return path;
    }

    static string FreeSaveFile(string folder, string name)
    {
        var path = Path.Combine(folder, name + Ext);
        for (int n = 2; File.Exists(path) || Directory.Exists(path); n++) path = Path.Combine(folder, $"{name} ({n.ToString(CultureInfo.InvariantCulture)}){Ext}");
        return path;
    }

    public static string WriteNewSave(string folder, string name, byte[] packed)
    {
        Directory.CreateDirectory(folder);
        for (int attempt = 0; ; attempt++)
        {
            var path = FreeSaveFile(folder, SaveNames.Clean(name));
            var tmp = Json.WriteTemp(path, packed, Guid.NewGuid().ToString("N"));
            try { File.Move(tmp, path, overwrite: false); return path; }
            catch (IOException) when (File.Exists(path) && attempt < 100) { Json.Discard(tmp); }
            catch { Json.Discard(tmp); throw; }
        }
    }

    public string WriteTurnSave(string scenarioDir, string run, SaveFile save, DateTime nowUtc)
    {
        var folder = EnsureRunFolder(scenarioDir, run, nowUtc);
        return WriteNewSave(folder, SaveNames.Turn(save.Snapshot.Clock.Turn, save.Snapshot.Clock.GameTime), Json.PackedBytes(save));
    }

    public string SaveInto(string scenarioDir, string folder, string name, byte[] packed, bool replace, DateTime nowUtc)
    {
        CheckInside(scenarioDir, folder, allowRoot: true);
        var target = Path.Combine(folder, SaveNames.Clean(name) + Ext);
        if (File.Exists(target))
        {
            if (!replace) throw new IOException($"a save named {Path.GetFileNameWithoutExtension(target)} is there already");
            var written = WriteNewSave(folder, SaveNames.Clean(name) + " (new)", packed);
            TrashSaves(scenarioDir, target, nowUtc);
            File.Move(written, target);
            return target;
        }
        return WriteNewSave(folder, name, packed);
    }

    public string MakeFolder(string scenarioDir, string parent, string name)
    {
        CheckInside(scenarioDir, parent, allowRoot: true);
        var folder = FreeFolder(parent, SaveNames.Clean(name, "New folder"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string Rename(string scenarioDir, string path, string name)
    {
        CheckInside(scenarioDir, path, allowRoot: false);
        var parent = Path.GetDirectoryName(path)!;
        bool folder = Directory.Exists(path);
        var clean = SaveNames.Clean(name, folder ? "Folder" : "Save");
        var wanted = Path.Combine(parent, folder ? clean : clean + Ext);
        if (string.Equals(wanted, path, StringComparison.Ordinal)) return path;
        if (string.Equals(wanted, path, StringComparison.OrdinalIgnoreCase))
        {
            var step = path + "." + Guid.NewGuid().ToString("N");
            if (folder) { Directory.Move(path, step); Directory.Move(step, wanted); }
            else { File.Move(path, step); File.Move(step, wanted); }
            return wanted;
        }
        if (folder) { var to = FreeFolder(parent, clean); Directory.Move(path, to); return to; }
        var file = FreeSaveFile(parent, clean);
        File.Move(path, file);
        return file;
    }

    public string MoveSave(string scenarioDir, string path, string intoFolder)
    {
        CheckInside(scenarioDir, path, allowRoot: false);
        CheckInside(scenarioDir, intoFolder, allowRoot: true);
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(intoFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return path;
        if (Directory.Exists(path))
        {
            if (IsUnder(intoFolder, path)) throw new IOException("a folder cannot go inside itself");
            var to = FreeFolder(intoFolder, Path.GetFileName(path));
            Directory.Move(path, to);
            return to;
        }
        var file = FreeSaveFile(intoFolder, Path.GetFileNameWithoutExtension(path));
        File.Move(path, file);
        return file;
    }

    public IReadOnlyList<RunFolder> RunsIn(string scenarioDir, string path) =>
        Directory.Exists(path) ? Runs(scenarioDir).Where(r => IsUnder(r.Folder, path)).ToList() : [];

    public string TrashSaves(string scenarioDir, string path, DateTime nowUtc, string? runInPlay = null)
    {
        CheckInside(scenarioDir, path, allowRoot: false);
        var runs = RunsIn(scenarioDir, path);
        if (runInPlay != null && runs.Any(r => r.Id == runInPlay)) throw new IOException("the game on the table now is saved in it");
        var root = SavesRoot(scenarioDir);
        var name = Path.GetFileName(path);
        var entry = NewTrashEntry($"{scenarioDir} · {Path.GetFileNameWithoutExtension(name)}{SavesSuffix}", nowUtc);
        Directory.CreateDirectory(entry);
        Json.WriteAtomic(Path.Combine(entry, TrashedFrom), Json.Serialize(new SavesInTrash
        {
            Scenario = scenarioDir, Parent = Path.GetRelativePath(root, Path.GetDirectoryName(path)!), Name = name,
        }));
        var item = Path.Combine(entry, TrashedItem, name);
        Directory.CreateDirectory(Path.GetDirectoryName(item)!);
        if (Directory.Exists(path)) MoveInto(path, item);
        else MoveFile(path, item);
        foreach (var r in runs)
        {
            var live = LivePath(scenarioDir, r.Id);
            foreach (var f in new[] { live, SaveLog.PathFor(live), SaveLog.OldPathFor(live) })
                if (File.Exists(f)) MoveFile(f, Path.Combine(entry, TrashedLive, Path.GetFileName(f)));
            MoveInto(Path.Combine(root, SafetyFolder, r.Id), Path.Combine(entry, TrashedSafety, r.Id));
        }
        return entry;
    }

    public IReadOnlyList<TrashedSave> SavesTrash(string scenarioDir)
    {
        var list = new List<TrashedSave>();
        foreach (var e in TrashEntries().Where(e => e.Kind == TrashKind.Saves))
        {
            SavesInTrash from;
            try { from = Json.Load<SavesInTrash>(Path.Combine(e.Path, TrashedFrom)); }
            catch (Exception ex) when (IsDamage(ex) || ex is IOException or UnauthorizedAccessException) { continue; }
            if (!string.Equals(from.Scenario, scenarioDir, StringComparison.OrdinalIgnoreCase)) continue;
            var item = Path.Combine(e.Path, TrashedItem, from.Name);
            bool folder = Directory.Exists(item);
            if (!folder && !File.Exists(item)) continue;
            list.Add(new TrashedSave(e, folder ? from.Name : Path.GetFileNameWithoutExtension(from.Name), item, folder));
        }
        return list;
    }

    string RestoreSaves(TrashEntry e, DateTime nowUtc)
    {
        var from = Json.Load<SavesInTrash>(Path.Combine(e.Path, TrashedFrom));
        if (from.Scenario.Length == 0 || from.Scenario.Contains(Path.DirectorySeparatorChar) || from.Scenario.Contains('/')) throw new IOException("this entry does not say where it came from");
        var root = SavesRoot(from.Scenario);
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
                var to = Path.Combine(RunsLiveDir(from.Scenario), Path.GetFileName(f));
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
        return $"{Path.GetFileNameWithoutExtension(back ?? from.Name)} is back among the saves of {from.Scenario}.";
    }

    void MergeSaves(string scenarioDir, string savesFrom, string liveFrom, bool takeCurrent, DateTime nowUtc)
    {
        if (!Directory.Exists(savesFrom) && !Directory.Exists(liveFrom)) return;
        var ids = new HashSet<string>();
        if (Directory.Exists(liveFrom))
            foreach (var f in Directory.GetFiles(liveFrom, "*" + Ext)) if (IsRunId(Path.GetFileNameWithoutExtension(f))) ids.Add(Path.GetFileNameWithoutExtension(f));
        var marks = Directory.Exists(savesFrom)
            ? Directory.EnumerateFiles(savesFrom, RunMarkFile, SearchOption.AllDirectories).Where(m => !IsHidden(savesFrom, Path.GetDirectoryName(m)!)).ToList()
            : new List<string>();
        foreach (var m in marks) if (ReadMark(m) is { } mark) ids.Add(mark.Id);
        var map = new Dictionary<string, string>();
        bool Taken(string id) => map.ContainsValue(id) || File.Exists(LivePath(scenarioDir, id)) || RunFolderOf(scenarioDir, id) != null
            || Directory.Exists(Path.Combine(SavesRoot(scenarioDir), SafetyFolder, id));
        foreach (var id in ids)
        {
            var to = id;
            for (int n = 0; Taken(to); n++) to = NewRunId(scenarioDir, nowUtc.AddSeconds(n));
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
                if (!map.TryGetValue(id, out var to)) continue;
                var live = LivePath(scenarioDir, to);
                MoveFile(f, live);
                foreach (var (log, dst) in new[] { (SaveLog.PathFor(f), SaveLog.PathFor(live)), (SaveLog.OldPathFor(f), SaveLog.OldPathFor(live)) })
                    if (File.Exists(log)) MoveFile(log, dst);
            }
        }
        var root = SavesRoot(scenarioDir);
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
        foreach (var (_, to) in map) if (File.Exists(LivePath(scenarioDir, to)) && RunFolderOf(scenarioDir, to) == null) MakeRun(scenarioDir, to, NextRunName(scenarioDir), null, nowUtc, backfill: true);
        if (incomingCurrent != null && (takeCurrent || CurrentRun(scenarioDir) == null)) SetCurrent(scenarioDir, incomingCurrent);
    }

    public string AdoptLegacyGame(string scenarioDir, string saveFile, string? checkpoints, string name, DateTime nowUtc)
    {
        var run = NewRunId(scenarioDir, nowUtc);
        var live = LivePath(scenarioDir, run);
        if (File.Exists(saveFile))
        {
            MoveFile(saveFile, live);
            foreach (var (log, dst) in new[] { (SaveLog.PathFor(saveFile), SaveLog.PathFor(live)), (SaveLog.OldPathFor(saveFile), SaveLog.OldPathFor(live)) })
                if (File.Exists(log)) MoveFile(log, dst);
        }
        if (checkpoints != null) MoveInto(checkpoints, CheckpointDir(live));
        MakeRun(scenarioDir, run, name, null, nowUtc, backfill: true);
        SetCurrent(scenarioDir, run);
        return run;
    }

    public string AddGame(string scenarioDir, string saveFile, string name, DateTime nowUtc)
    {
        var run = NewRunId(scenarioDir, nowUtc);
        var live = LivePath(scenarioDir, run);
        Directory.CreateDirectory(Path.GetDirectoryName(live)!);
        Json.CopyAtomic(saveFile, live, overwrite: false);
        MakeRun(scenarioDir, run, name, null, nowUtc, backfill: true);
        SetCurrent(scenarioDir, run);
        return run;
    }

    public void AddSaves(string scenarioDir, string savesFrom, string liveFrom, DateTime nowUtc) =>
        MergeSaves(scenarioDir, savesFrom, liveFrom, takeCurrent: true, nowUtc);

    bool HasLegacyGame(string scenarioDir)
    {
        if (File.Exists(LegacyLivePath(scenarioDir))) return true;
        var root = SavesRoot(scenarioDir);
        return Directory.Exists(root) && Directory.EnumerateFiles(root, "*" + Ext).Any(f => CheckpointName.IsMatch(Path.GetFileName(f)));
    }

    public List<string> MigrateToRuns(DateTime nowUtc)
    {
        var moved = new List<string>();
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(LiveDir))
            foreach (var f in Directory.GetFiles(LiveDir, "*" + Ext))
                if (!Path.GetFileNameWithoutExtension(f).Contains('.')) dirs.Add(Path.GetFileNameWithoutExtension(f));
        if (Directory.Exists(HistoryDir))
            foreach (var d in Directory.GetDirectories(HistoryDir))
                if (!Path.GetFileName(d).Equals(MirrorsFolder, StringComparison.OrdinalIgnoreCase)) dirs.Add(Path.GetFileName(d));
        foreach (var dir in dirs)
        {
            try { if (MigrateToRun(dir, nowUtc)) moved.Add($"{dir}: its game is {SaveNames.Run(1)} of its saves"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { moved.Add($"{dir}: not moved yet ({e.Message})"); }
        }
        return moved;
    }

    bool MigrateToRun(string scenarioDir, DateTime nowUtc)
    {
        if (!HasLegacyGame(scenarioDir)) return false;
        var legacy = LegacyLivePath(scenarioDir);
        var live = LivePath(scenarioDir, MigratedRun);
        var when = File.Exists(legacy) ? File.GetLastWriteTimeUtc(legacy) : nowUtc;
        if (File.Exists(legacy) && !File.Exists(live))
        {
            MoveFile(legacy, live);
            foreach (var (log, dst) in new[] { (SaveLog.PathFor(legacy), SaveLog.PathFor(live)), (SaveLog.OldPathFor(legacy), SaveLog.OldPathFor(live)) })
                if (File.Exists(log) && !File.Exists(dst)) MoveFile(log, dst);
        }
        var root = SavesRoot(scenarioDir);
        if (Directory.Exists(root))
        {
            var safety = CheckpointDir(live);
            foreach (var f in Directory.GetFiles(root, "*" + Ext).Where(f => CheckpointName.IsMatch(Path.GetFileName(f))))
            {
                var to = Path.Combine(safety, Path.GetFileName(f));
                if (!File.Exists(to)) MoveFile(f, to);
            }
        }
        MakeRun(scenarioDir, MigratedRun, SaveNames.Run(1), null, when, backfill: true);
        if (MarkedCurrent(scenarioDir) == null) SetCurrent(scenarioDir, MigratedRun);
        return true;
    }

    public IReadOnlyList<(string ScenarioDir, RunFolder Run)> RunsToFill()
    {
        var list = new List<(string, RunFolder)>();
        if (!Directory.Exists(HistoryDir)) return list;
        foreach (var d in Directory.GetDirectories(HistoryDir))
        {
            var dir = Path.GetFileName(d);
            if (dir.Equals(MirrorsFolder, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var r in Runs(dir)) if (r.Mark.Backfill) list.Add((dir, r));
        }
        return list;
    }

    public int FillRun(string scenarioDir, RunFolder run, CancellationToken stop = default)
    {
        var live = LivePath(scenarioDir, run.Id);
        var source = File.Exists(live) ? live : Checkpoints(live).Where(c => c.Kind != CheckpointKind.Saved).Select(c => c.Path).FirstOrDefault();
        int written = 0;
        if (source != null)
        {
            var save = Json.Load<SaveFile>(source);
            var turns = save.Turns ?? new();
            foreach (var t in turns) t.Normalize();
            for (int i = 0; i < turns.Count; i++)
            {
                stop.ThrowIfCancellationRequested();
                var name = SaveNames.Turn(turns[i].Turn, turns[i].GameTime);
                if (File.Exists(Path.Combine(run.Folder, SaveNames.Clean(name) + Ext))) continue;
                WriteNewSave(run.Folder, name, Json.PackedBytes(SaveFile.OfTurn(save.ScenarioId, save.Created, save.Access ?? new GameAccess(), turns, i, null)));
                written++;
            }
        }
        run.Mark.Backfill = false;
        WriteMark(run.Folder, run.Mark);
        return written;
    }

    void CheckInside(string scenarioDir, string path, bool allowRoot)
    {
        var root = Path.GetFullPath(SavesRoot(scenarioDir)).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        bool isRoot = string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        if (isRoot ? !allowRoot : !IsUnder(full, root) || IsHidden(root, full)) throw new IOException($"{path} is not among the saves of {scenarioDir}");
    }

    static bool IsUnder(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    static void MoveFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        try { File.Move(from, to, overwrite: false); }
        catch (IOException e) when (e.HResult == ErrorNotSameDevice) { Json.CopyAtomic(from, to, overwrite: false); File.Delete(from); }
    }
}
