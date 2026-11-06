using System.Diagnostics;

namespace KTable.Core;

public sealed class InstallJournal
{
    public const string Folder = "_installing";
    const string Suffix = ".journal.json";
    const string FinishedSuffix = ".finished" + Suffix, AbandonedSuffix = ".abandoned" + Suffix, FinishedMarker = ".finished";

    public sealed class Step
    {
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public string? Entry { get; set; }
        public bool Moved { get; set; }
    }

    public sealed class Record
    {
        public int Format { get; set; } = 1;
        public string What { get; set; } = "";
        public int Pid { get; set; }
        public long PidStarted { get; set; }
        public DateTime Started { get; set; }
        public bool Finished { get; set; }
        public bool Abandoned { get; set; }
        public List<Step> Steps { get; set; } = new();
    }

    readonly Record _r;
    readonly SaveStore _saves;
    readonly string _root;

    public string FilePath { get; }

    InstallJournal(string path, string root, Record r, SaveStore saves)
    {
        FilePath = path;
        _root = root;
        _r = r;
        _saves = saves;
    }

    public static InstallJournal Begin(string packagesRoot, SaveStore saves, string what, DateTime nowUtc)
    {
        var (pid, started) = Me();
        var r = new Record { What = what, Pid = pid, PidStarted = started, Started = nowUtc };
        var name = $"{nowUtc.ToUniversalTime():yyyyMMdd-HHmmss}-{Repeatable.NewGuid().ToString("N")[..12]}{Suffix}";
        var j = new InstallJournal(Path.Combine(packagesRoot, Folder, name), Path.GetFullPath(packagesRoot), r, saves);
        j.Write();
        return j;
    }

    public string Target(string kind, string id) => kind == "pack" ? Path.Combine(_root, id) : Path.Combine(_root, "scenarios", id);

    public string? MakeWay(string kind, string id, DateTime nowUtc)
    {
        if (kind is not ("pack" or "scenario")) throw new ArgumentException("a pack or a scenario folder", nameof(kind));
        if (_r.Steps.Any(s => s.Kind == kind && string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"the {kind} {id} has been made way for once already in this work");
        var step = new Step { Kind = kind, Id = id };
        if (Directory.Exists(Target(kind, id))) step.Entry = _saves.NewTrashEntry(id, nowUtc);
        _r.Steps.Add(step);
        Write();
        if (step.Entry == null) return null;
        try
        {
            if (kind == "pack") _saves.TrashPackFolderAs(_root, id, step.Entry);
            else _saves.TrashScenarioFolderAs(_root, id, step.Entry);
        }
        catch
        {
            _r.Steps.Remove(step);
            BestEffort.Try(() => Write());
            throw;
        }
        step.Moved = true;
        Write();
        return step.Entry;
    }

    public string? Finish()
    {
        _r.Finished = true;
        Exception? writing = null;
        try { Write(); } catch (Exception e) { writing = e; }
        if (TryDelete(FilePath) || writing == null) return null;
        if (TryMove(FilePath, Renamed(FilePath, FinishedSuffix))) return null;
        try { File.WriteAllBytes(FilePath + FinishedMarker, Array.Empty<byte>()); return null; }
        catch (Exception e)
        {
            return $"{_r.What} is finished, but its record ({Path.GetFileName(FilePath)} in {Path.GetDirectoryName(FilePath)}) could not be marked so or removed "
                + $"({writing.Message}; {e.Message}): delete that file before the app starts again, or the next start undoes it";
        }
    }

    public Undone RollBack(DateTime nowUtc) => Undo(FilePath, _root, _r, _saves, nowUtc);

    public static List<Undone> RecoverAll(string packagesRoot, SaveStore saves, DateTime nowUtc, Func<int, long, bool>? alive = null)
    {
        var done = new List<Undone>();
        var dir = Path.Combine(packagesRoot, Folder);
        if (!Directory.Exists(dir)) return done;
        var found = new List<(string file, Record r)>();
        foreach (var f in Directory.GetFiles(dir, "*" + Suffix))
        {
            try
            {
                var r = Json.Load<Record>(f);
                if (f.EndsWith(FinishedSuffix, StringComparison.OrdinalIgnoreCase) || File.Exists(f + FinishedMarker)) r.Finished = true;
                if (f.EndsWith(AbandonedSuffix, StringComparison.OrdinalIgnoreCase)) r.Abandoned = true;
                found.Add((f, r));
            }
            catch (Exception e)
            {
                string? stays = null;
                try { File.Move(f, f + ".unreadable", true); }
                catch (Exception m) when (m is IOException or UnauthorizedAccessException) { stays = m.Message; }
                var u = new Undone { What = "an import or install", File = f };
                u.Problems.Add($"its record ({Path.GetFileName(f)}) could not be read ({e.Message}), so whatever it moved is still in the trash"
                    + (stays != null ? $"; the record could not be set aside either ({stays}), so this is said again at the next start" : ""));
                done.Add(u);
            }
        }
        foreach (var m in Directory.GetFiles(dir, "*" + Suffix + FinishedMarker))
            if (!File.Exists(m[..^FinishedMarker.Length])) TryDelete(m);
        var root = Path.GetFullPath(packagesRoot);
        foreach (var (f, r) in found.OrderByDescending(x => x.r.Started))
        {
            if ((alive ?? Alive)(r.Pid, r.PidStarted)) continue;
            done.Add(Undo(f, root, r, saves, nowUtc));
        }
        return done;
    }

    static Undone Undo(string file, string root, Record r, SaveStore saves, DateTime nowUtc)
    {
        var u = new Undone { What = r.What, File = file, WasFinished = r.Finished };
        if (r.Finished) { Remove(file, u); return u; }
        bool careful = r.Abandoned;
        for (int i = r.Steps.Count - 1; i >= 0; i--)
        {
            var s = r.Steps[i];
            try { UndoStep(root, saves, s, nowUtc, u, careful); }
            catch (Exception e) { u.Problems.Add($"{Describe(s)} could not be put back ({e.Message}){(s.Entry != null ? " and is still in the trash" : "")}"); continue; }
            r.Steps.RemoveAt(i);
            BestEffort.Try(() => WriteRecord(file, r));
        }
        if (u.Problems.Count == 0 || r.Abandoned) Remove(file, u);
        else
        {
            r.Abandoned = true;
            MarkAbandoned(file, r, u);
        }
        return u;
    }

    static void MarkAbandoned(string file, Record r, Undone u)
    {
        Exception? writing = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { WriteRecord(file, r); return; }
            catch (Exception e) { writing = e; }
        }
        u.Notes.Add($"its record could not be rewritten ({writing!.Message})");
        if (!file.EndsWith(AbandonedSuffix, StringComparison.OrdinalIgnoreCase) && TryMove(file, Renamed(file, AbandonedSuffix)))
        {
            u.Notes.Add("so it was renamed to say the next start is to try again with care");
            return;
        }
        if (TryDelete(file)) { u.Notes.Add("nor renamed, so it was removed: what it could not put back stays in the trash"); return; }
        u.Problems.Add($"its record ({Path.GetFileName(file)}) could not be updated, renamed or removed ({writing.Message}), so the next start may undo it again in full");
    }

    static void Remove(string file, Undone u)
    {
        if (!TryDelete(file)) u.Notes.Add($"its record ({Path.GetFileName(file)}) could not be removed");
        TryDelete(file + FinishedMarker);
    }

    static string Renamed(string file, string suffix) =>
        (file.EndsWith(FinishedSuffix, StringComparison.OrdinalIgnoreCase) ? file[..^FinishedSuffix.Length]
            : file.EndsWith(AbandonedSuffix, StringComparison.OrdinalIgnoreCase) ? file[..^AbandonedSuffix.Length]
            : file[..^Suffix.Length]) + suffix;

    static bool TryMove(string from, string to)
    {
        try { File.Move(from, to, overwrite: false); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static void UndoStep(string root, SaveStore saves, Step s, DateTime nowUtc, Undone u, bool careful)
    {
        if (s.Kind == "game") { UndoGame(saves, s, nowUtc, u); return; }
        var target = s.Kind == "pack" ? Path.Combine(root, s.Id) : Path.Combine(root, "scenarios", s.Id);
        bool there = Directory.Exists(target);
        if (s.Entry == null)
        {
            if (there && !careful && saves.TrashHalfMade(root, s.Kind, s.Id, nowUtc) != null) u.Trashed.Add(Describe(s));
            return;
        }
        if (!Directory.Exists(s.Entry))
        {
            if (there) return;
            throw new IOException("it is no longer in the trash");
        }
        if (there)
        {
            if (!s.Moved) return;
            if (careful) throw new IOException("something else stands in its place now");
            if (saves.TrashHalfMade(root, s.Kind, s.Id, nowUtc) != null) u.Trashed.Add(Describe(s));
        }
        saves.Restore(new TrashEntry(s.Entry, s.Kind, s.Id, nowUtc), root, nowUtc);
        u.Back.Add(Describe(s));
    }

    static void UndoGame(SaveStore saves, Step s, DateTime nowUtc, Undone u)
    {
        if (s.Entry == null || !Directory.Exists(s.Entry)) return;
        saves.PutGameBack(s.Entry, s.Id, nowUtc);
        u.Back.Add(Describe(s));
    }

    static string Describe(Step s) => s.Kind switch
    {
        "pack" => $"the pack {s.Id}",
        "scenario" => $"the scenario {s.Id}",
        _ => $"the game in play of {s.Id}",
    };

    void Write() => WriteRecord(FilePath, _r);

    static void WriteRecord(string file, Record r) => Json.WriteAtomic(file, Json.Serialize(r, indented: true));

    static bool TryDelete(string file)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { File.Delete(file); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (attempt < 2) Thread.Sleep(40 * (attempt + 1)); }
        }
        return !File.Exists(file);
    }

    static (int pid, long started) Me()
    {
        try { using var p = Process.GetCurrentProcess(); return (p.Id, p.StartTime.ToUniversalTime().Ticks); }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception) { return (Environment.ProcessId, 0); }
    }

    static bool Alive(int pid, long startedTicks)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;
            if (startedTicks == 0) return true;
            return Math.Abs(p.StartTime.ToUniversalTime().Ticks - startedTicks) < TimeSpan.TicksPerSecond;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception) { return false; }
    }
}

public sealed class Undone
{
    public string What { get; init; } = "";
    public string File { get; init; } = "";
    public bool WasFinished { get; init; }
    public List<string> Back { get; } = new();
    public List<string> Trashed { get; } = new();
    public List<string> Problems { get; } = new();
    public List<string> Notes { get; } = new();

    public bool Any => Back.Count + Trashed.Count + Problems.Count > 0;

    public string Describe()
    {
        var what = What.Length > 0 ? char.ToUpperInvariant(What[0]) + What[1..] : "An import";
        var said = $"{what} was cut off half way and has been undone";
        if (Back.Count > 0) said += ": what you had is back as it was";
        if (Trashed.Count > 0) said += Back.Count > 0 ? ", and what it had begun is in the trash" : ": what it had begun is in the trash";
        if (Problems.Count > 0) said += $"{(Back.Count + Trashed.Count > 0 ? "; but " : ": ")}{string.Join("; ", Problems)} (restore from the trash at the foot of Piece studio or Scenarios)";
        return said + ".";
    }

    public string Details() =>
        $"{What}: back {(Back.Count > 0 ? string.Join(", ", Back) : "nothing")}; to the trash {(Trashed.Count > 0 ? string.Join(", ", Trashed) : "nothing")}"
        + (Problems.Count > 0 ? "; not put back: " + string.Join("; ", Problems) : "") + (Notes.Count > 0 ? "; its record: " + string.Join(", ", Notes) : "")
        + (File.Length > 0 ? $" ({System.IO.Path.GetFileName(File)})" : "");
}
