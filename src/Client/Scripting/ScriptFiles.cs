using System.Globalization;
using System.Text;
using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.Client.Scripting;

public enum Keep { Ask, File, Script }

public enum ScriptEventKind { Changed, Refused, Rewritten, Missing, Unreadable, NotTaken, Ended }

public sealed record ScriptEvent(ScriptLink Link, ScriptEventKind Kind, string Text);

public sealed record ScriptStart(ScriptLink? Link, string? FileText, string? Problem)
{
    public bool Conflict => Link == null && FileText != null;
}

public sealed class ScriptLink
{
    internal ScriptLink(string key, string path, Func<string?> current, Func<bool> alive, string known)
    {
        Key = key; Path = path; Current = current; Alive = alive; Known = known; Seen = known; Handled = known;
    }

    public string Key { get; }
    public string Path { get; }
    public string File => System.IO.Path.GetFileName(Path);
    public string Text => Known;
    internal Func<string?> Current { get; }
    internal Func<bool> Alive { get; }
    internal string Known { get; set; }
    internal string? Seen { get; set; }
    internal string? Handled { get; set; }
    internal string? Pending { get; set; }
    internal DateTime PendingUntil { get; set; }
    internal bool Missing { get; set; }
    internal bool Unreadable { get; set; }
    internal bool WriteFailed { get; set; }
}

public sealed class ScriptFiles
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const long LogBytes = 2 * 1024 * 1024;
    public const string GlobalFile = "global.lua";
    public const string StubsFile = "ktable-api.lua";
    public const string LogFile = "console.log";
    public const string OldLogFile = "console.old.log";
    public static readonly TimeSpan TakeWait = TimeSpan.FromSeconds(4);
    const int LogBacklog = 2000;
    const string WriteTag = "ktable";

    readonly string? _stubs;
    readonly List<ScriptLink> _links = new();
    readonly List<string> _unwritten = new();
    readonly string _title;
    long _consoleSeq;
    bool _begun;

    public ScriptFiles(string folder, string title, string? stubs)
    {
        Folder = folder; _title = title; _stubs = stubs;
    }

    public string Folder { get; }
    public string LogPath => System.IO.Path.Combine(Folder, LogFile);
    public IReadOnlyList<ScriptLink> Links => _links;
    public ScriptLink? Of(string key) => _links.FirstOrDefault(l => l.Key == key);

    public static string Root => System.IO.Path.Combine(DataPaths.Documents, "Scripts");
    public static string PackFolder(string root, string packId) => System.IO.Path.Combine(root, "packs", Safe(Naming.Slug(packId, "pack", 60)));
    public static string ScenarioFolder(string root, string scenario) => System.IO.Path.Combine(root, "scenarios", Safe(Naming.Slug(scenario, "game", 60)));

    public static string KindFile(string packId, string defId)
    {
        var stem = defId.StartsWith(packId + ".", StringComparison.Ordinal) ? defId[(packId.Length + 1)..] : defId;
        return Safe(Naming.Slug(stem, "piece", 80)) + ".lua";
    }

    public static string PieceFile(string pieceId, string? name) =>
        Safe(Naming.Slug(string.IsNullOrWhiteSpace(name) ? pieceId : $"{name}-{pieceId}", "piece", 80)) + ".lua";

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    static string Safe(string name) => Reserved.Contains(name) ? name + "-1" : name;

    public static string Norm(string? text)
    {
        var s = text ?? "";
        if (s.Length > 0 && s[0] == '﻿') s = s[1..];
        return s.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    public ScriptStart Start(string key, string file, Func<string?> current, Func<bool> alive, Keep keep, DateTime now)
    {
        if (Of(key) is { } already) return new ScriptStart(already, null, null);
        var path = System.IO.Path.Combine(Folder, file);
        if (_links.Any(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase)))
            path = System.IO.Path.Combine(Folder, System.IO.Path.GetFileNameWithoutExtension(file) + "-" + _links.Count + ".lua");
        var mine = Norm(current());
        try
        {
            Directory.CreateDirectory(Folder);
            if (_stubs != null) WriteIfChanged(System.IO.Path.Combine(Folder, StubsFile), _stubs);
            var there = Read(path);
            if (there.Problem != null && there.Exists) return new ScriptStart(null, null, there.Problem);
            var fileText = there.Text;
            bool differs = there.Exists && fileText.Trim().Length > 0 && fileText != mine;
            if (differs && keep == Keep.Ask) return new ScriptStart(null, fileText, null);
            var link = new ScriptLink(key, path, current, alive, mine);
            if (differs && keep == Keep.File) { link.Seen = fileText; link.Handled = null; }
            else if (!there.Exists || fileText != mine) WriteAtomic(path, mine);
            _links.Add(link);
            if (!_begun) { _begun = true; AppendLog(new[] { $"— {now.ToLocalTime():yyyy-MM-dd HH:mm} {_title} —" }); }
            return new ScriptStart(link, null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ScriptStart(null, null, e.Message);
        }
    }

    public void Stop(string key) => _links.RemoveAll(l => l.Key == key);

    public void StopAll() => _links.Clear();

    public List<ScriptEvent> Poll(DateTime now)
    {
        var said = new List<ScriptEvent>();
        foreach (var link in _links.ToList()) PollOne(link, now, said);
        if (said.Count > 0) AppendLog(said.Select(e => Line(now, e.Link.File, Words(e))));
        return said;
    }

    void PollOne(ScriptLink link, DateTime now, List<ScriptEvent> said)
    {
        if (!link.Alive())
        {
            _links.Remove(link);
            said.Add(new ScriptEvent(link, ScriptEventKind.Ended, ""));
            return;
        }
        var current = Norm(link.Current());
        if (link.Pending != null)
        {
            if (current == link.Pending) { link.Known = link.Pending; link.Pending = null; }
            else if (now < link.PendingUntil) return;
            else
            {
                said.Add(new ScriptEvent(link, ScriptEventKind.NotTaken, link.Pending));
                link.Pending = null;
                link.Known = current;
                return;
            }
        }
        if (current != link.Known || link.WriteFailed)
        {
            try
            {
                KeepUnapplied(link);
                WriteAtomic(link.Path, current);
                if (!link.WriteFailed || current != link.Known) said.Add(new ScriptEvent(link, ScriptEventKind.Rewritten, current));
                link.WriteFailed = false;
                link.Known = link.Seen = link.Handled = current;
                link.Missing = false;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (!link.WriteFailed) said.Add(new ScriptEvent(link, ScriptEventKind.Unreadable, "could not write it: " + e.Message));
                link.WriteFailed = true;
                link.Known = current;
            }
            return;
        }
        var read = Read(link.Path);
        if (!read.Exists)
        {
            if (!link.Missing) said.Add(new ScriptEvent(link, ScriptEventKind.Missing, ""));
            link.Missing = true;
            link.Seen = null;
            return;
        }
        link.Missing = false;
        if (read.Locked)
        {
            if (!link.Unreadable) said.Add(new ScriptEvent(link, ScriptEventKind.Unreadable, read.Problem ?? ""));
            link.Unreadable = true;
            return;
        }
        link.Unreadable = false;
        var text = read.Problem != null ? "\0" + read.Problem : read.Text;
        if (text != link.Seen) { link.Seen = text; return; }
        if (text == link.Handled) return;
        link.Handled = text;
        if (text == link.Known) return;
        if (read.Problem != null) { said.Add(new ScriptEvent(link, ScriptEventKind.Refused, read.Problem)); return; }
        if (text.Trim().Length > 0 && ScriptSafety.Inspect(text) is { Hostile: true } verdict)
        {
            said.Add(new ScriptEvent(link, ScriptEventKind.Refused, string.Join("; ", verdict.Reasons)));
            return;
        }
        link.Pending = text;
        link.PendingUntil = now + TakeWait;
        said.Add(new ScriptEvent(link, ScriptEventKind.Changed, text));
    }

    public static string Words(ScriptEvent e) => e.Kind switch
    {
        ScriptEventKind.Changed => e.Text.Trim().Length == 0 ? "emptied: the script is removed" : "saved: the script runs again from on_load",
        ScriptEventKind.Refused => "refused, not run: " + e.Text,
        ScriptEventKind.Rewritten => "rewritten: the script was changed in the app",
        ScriptEventKind.Missing => "gone from the folder; the script keeps running and is picked up again when the file comes back",
        ScriptEventKind.Unreadable => "not readable yet (" + e.Text + "); tried again",
        ScriptEventKind.Ended => "no longer linked: its piece is gone; the file is kept",
        ScriptEventKind.NotTaken => "not taken: the script stays as it was; save the file again to retry",
        _ => e.Kind.ToString(),
    };

    public string? FileFor(string source, string? defId)
    {
        if (Of(source) is { } own) return own.File;
        if (defId != null && Of("def:" + defId) is { } kind) return kind.File;
        return null;
    }

    public void Console(IReadOnlyList<ConsoleLine> lines, Func<string, string?> defOf, Func<ConsoleLine, string> nameOf)
    {
        var fresh = lines.Where(l => l.Seq > _consoleSeq).ToList();
        if (fresh.Count == 0) return;
        _consoleSeq = fresh[^1].Seq;
        if (_links.Count == 0) return;
        AppendLog(fresh.Select(l => Format(l, FileFor(l.Source, l.Source.Length == 0 ? null : defOf(l.Source)), nameOf(l))));
    }

    public void Note(string text, DateTime now)
    {
        if (_links.Count == 0) return;
        AppendLog(new[] { Line(now, null, text) });
    }

    static string Line(DateTime now, string? file, string text) => $"{now.ToLocalTime():HH:mm:ss} {(file == null ? "" : file + ": ")}{Indent(text)}";

    public static string Format(ConsoleLine l, string? file, string name)
    {
        var when = DateTime.TryParse(l.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "--:--:--";
        var where = file != null ? (l.Line > 0 ? $"{file}:{l.Line}" : file) : (l.Line > 0 ? $"{name} :{l.Line}" : name);
        var kind = l.Kind switch { "error" => "error: ", "todo" => "todo: ", "input" => "> ", "result" => "= ", _ => "" };
        return $"{when} {where}: {kind}{Indent(l.Text)}";
    }

    static string Indent(string text) => Norm(text).TrimEnd('\n').Replace("\n", "\n    ");

    void AppendLog(IEnumerable<string> lines)
    {
        _unwritten.AddRange(lines);
        if (_unwritten.Count > LogBacklog) _unwritten.RemoveRange(0, _unwritten.Count - LogBacklog);
        try
        {
            Directory.CreateDirectory(Folder);
            var path = LogPath;
            var info = new FileInfo(path);
            if (info.Exists && info.Length > LogBytes) File.Move(path, System.IO.Path.Combine(Folder, OldLogFile), overwrite: true);
            using (var w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)))
                foreach (var l in _unwritten) w.Write(l + "\n");
            _unwritten.Clear();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    readonly record struct FileRead(bool Exists, bool Locked, string Text, string? Problem);

    static FileRead Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new FileRead(false, false, "", null);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length > MaxBytes) return new FileRead(true, false, "", $"the file is over {MaxBytes / (1024 * 1024)} MB");
            var bytes = new byte[fs.Length];
            int got = 0;
            while (got < bytes.Length) { int n = fs.Read(bytes, got, bytes.Length - got); if (n == 0) break; got += n; }
            if (Array.IndexOf(bytes, (byte)0, 0, got) >= 0) return new FileRead(true, false, "", "the file holds NUL bytes: not a text file");
            return new FileRead(true, false, Norm(new UTF8Encoding(false, false).GetString(bytes, 0, got)), null);
        }
        catch (FileNotFoundException) { return new FileRead(false, false, "", null); }
        catch (DirectoryNotFoundException) { return new FileRead(false, false, "", null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new FileRead(true, true, "", e.Message); }
    }

    static void KeepUnapplied(ScriptLink link)
    {
        var there = Read(link.Path);
        if (!there.Exists || there.Problem != null || there.Text.Trim().Length == 0 || there.Text == link.Known) return;
        WriteAtomic(link.Path + ".bak", there.Text);
    }

    static void WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && Norm(File.ReadAllText(path)) == Norm(text)) return;
        WriteAtomic(path, Norm(text));
    }

    static void WriteAtomic(string path, string text) => Json.WriteAtomic(path, new UTF8Encoding(false).GetBytes(text), WriteTag);
}
