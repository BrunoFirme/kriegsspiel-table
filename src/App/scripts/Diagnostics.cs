using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.App;

public enum LogLevel { Info, Warning, Error }

public static class Diagnostics
{
    const long MaxLogBytes = 512 * 1024;
    const int KeepProblems = 40, KeepCommands = 30, MaxProblemChars = 4000;
    const int KeepRunLines = 120, KeepEngineLines = 40;
    const double SameStartSeconds = 2;
    const string EngineLogs = "logs", CurrentEngineLog = "godot.log";
    public const string IssueUrl = "https://github.com/" + KTable.Client.Updates.Releases.Repo + "/issues/new?template=problem.yml";
    public const string Contact = "Discord b.firme, or firma.eht@outlook.com";

    static readonly object _gate = new();
    static readonly Queue<string> _problems = new();
    static readonly Queue<string> _commands = new();
    static string _logPath = "";
    static bool _installed;
    static Session? _session;
    static DiagnosticsLogger? _logger;
    static LastRun? _runs;

    public static EndedRun? ClosedUnexpectedly { get; private set; }

    public static bool GraphicsStopped { get; private set; }

    public static string LogPath => _logPath;

    public static string EngineLogPath => Path.Combine(OS.GetUserDataDir(), EngineLogs, CurrentEngineLog);

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        _logPath = Path.Combine(OS.GetUserDataDir(), "app.log");
        if (OS.HasFeature(UpdateOffer.ExportedBuild)) WatchForUnexpectedClose();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record("unhandled", e.ExceptionObject?.ToString() ?? "an unknown failure");
        TaskScheduler.UnobservedTaskException += (_, e) => { Record("task", e.Exception.ToString()); e.SetObserved(); };
        BestEffort.Noted = e => Write(LogLevel.Warning, $"[best effort] {e.GetType().Name}: {e.Message}{FirstFrame(e)}");
        try { OS.AddLogger(_logger = new DiagnosticsLogger()); }
        catch (Exception e) { Record("diagnostics", "Godot's errors cannot be listened to: " + e.Message); }
    }

    static string FirstFrame(Exception e)
    {
        var frame = e.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("KTable."));
        return frame == null ? "" : " " + frame;
    }

    static void WatchForUnexpectedClose()
    {
        try
        {
            _runs = new LastRun(OS.GetUserDataDir());
            ClosedUnexpectedly = _runs.EndedUnexpectedly(StillRunning).LastOrDefault();
            GraphicsStopped = ClosedUnexpectedly is { } run && LastRun.GraphicsLost(RunLog(run));
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            _runs.Begin(me.Id, me.StartTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Record("diagnostics", "an unexpected close cannot be noticed: " + e.Message); }
    }

    static bool StillRunning(int pid, DateTime started)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return Math.Abs((p.StartTime - started).TotalSeconds) < SameStartSeconds;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public static void Closing() => _runs?.End(OS.GetProcessId());

    public static void Watch(Session s)
    {
        _session = s;
        lock (_gate) _commands.Clear();
        s.Applied += c => Note(c);
    }

    static void Note(Command c)
    {
        var line = $"{DateTime.Now:HH:mm:ss} #{c.Seq} {c.GetType().Name} by {c.Actor}{(c.UndoOf != 0 ? $" (undo of #{c.UndoOf})" : "")}";
        lock (_gate) { _commands.Enqueue(line); while (_commands.Count > KeepCommands) _commands.Dequeue(); }
    }

    public static void Record(string kind, string text) => Write(LogLevel.Error, $"[{kind}] {text}");

    public static void Write(LogLevel level, string text)
    {
        if (text.Length == 0) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {Tag(level)} {text}";
        lock (_gate)
        {
            if (level != LogLevel.Info)
            {
                _problems.Enqueue(line.Length > MaxProblemChars ? line[..MaxProblemChars] + " …" : line);
                while (_problems.Count > KeepProblems) _problems.Dequeue();
            }
            if (_logPath.Length == 0) return;
            try
            {
                var fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > MaxLogBytes) File.Move(_logPath, Path.ChangeExtension(_logPath, ".old.log"), overwrite: true);
                File.AppendAllText(_logPath, line + "\n");
            }
            catch (Exception e)
            {
                _logPath = "";
                Problems.Tell($"The app's log could not be written ({e.Message}); Copy diagnostics in the menu still has the latest warnings and errors.");
            }
        }
    }

    static string Tag(LogLevel level) => level switch
    {
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARN ",
        _ => "INFO ",
    };

    public static string Report()
    {
        var b = new StringBuilder();
        b.AppendLine($"Kriegsspiel Table {Net.AppVersion} · protocol {ProtocolVersion.Current} · Godot {Engine.GetVersionInfo()["string"]}");
        b.AppendLine($"OS: {OS.GetName()} {OS.GetVersion()} · {OS.GetProcessorName()} ({OS.GetProcessorCount()} threads) · locale {OS.GetLocale()}");
        b.AppendLine($"GPU: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}) · {RenderingServer.GetVideoAdapterApiVersion()} · {RenderingServer.GetCurrentRenderingDriverName()}");
        if (_session is { } s)
        {
            b.AppendLine($"Table: {s.View.Name} · {(s.IsHost ? "hosting" : "joined as " + Fmt.Role(s.Me.Role))} · {s.View.Layer(KTable.Core.Model.Layers.Umpire).Count} pieces · {s.View.Sheets.Count} map sheet(s)");
            b.AppendLine("Packs: " + (s.View.Packs.Count > 0 ? string.Join(", ", s.View.Packs) : "none"));
        }
        lock (_gate)
        {
            b.AppendLine();
            b.AppendLine(_problems.Count > 0 ? "Latest warnings and errors (oldest first):" : "No warnings or errors this session.");
            foreach (var e in _problems) b.AppendLine("  " + e);
            if (_commands.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("Last commands:");
                foreach (var c in _commands) b.AppendLine("  " + c);
            }
        }
        if (ClosedUnexpectedly is { } run) LastRunLog(b, run);
        if (_logPath.Length > 0) b.AppendLine().AppendLine("Log on this PC: " + _logPath);
        return b.ToString();
    }

    static List<string> RunLog(EndedRun run) => BestEffort.Read(() => LastRun.LogSince(File.ReadLines(_logPath), run.Started, KeepRunLines), new List<string>());

    static void LastRunLog(StringBuilder b, EndedRun run)
    {
        b.AppendLine();
        b.AppendLine($"The run started {run.Started:yyyy-MM-dd HH:mm:ss} closed unexpectedly. Its log:");
        var lines = RunLog(run);
        foreach (var l in lines) b.AppendLine("  " + l);
        if (lines.Count == 0) b.AppendLine("  (nothing was written)");
        var engine = BestEffort.Read(() => new DirectoryInfo(Path.Combine(OS.GetUserDataDir(), EngineLogs)).EnumerateFiles("godot*.log")
            .Where(f => f.Name != CurrentEngineLog && f.LastWriteTime >= run.Started).OrderByDescending(f => f.LastWriteTime).FirstOrDefault(), null);
        if (engine == null) return;
        b.AppendLine();
        b.AppendLine("The engine's log of that run, last lines:");
        foreach (var l in BestEffort.Read(() => File.ReadLines(engine.FullName).TakeLast(KeepEngineLines).ToList(), new List<string>())) b.AppendLine("  " + l);
    }

    public static string Copy()
    {
        AppClipboard.SetText(Report());
        return "Diagnostics copied: paste them where you ask for help. Nothing was sent anywhere.";
    }

    public static string ReportOnGitHub()
    {
        AppClipboard.SetText(Report());
        OS.ShellOpen(IssueUrl);
        return $"Diagnostics copied: paste them into the issue form that opened in your browser, or send them to Bruno Firme ({Contact}).";
    }

}

sealed partial class DiagnosticsLogger : Logger
{
    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        var what = rationale.Length > 0 ? rationale : code;
        var where = file.Length > 0 ? $" ({Path.GetFileName(file)}:{line} {function})" : "";
        var level = errorType == (int)ErrorType.Warning ? LogLevel.Warning : LogLevel.Error;
        var kind = errorType == (int)ErrorType.Shader ? "[shader] " : "";
        Diagnostics.Write(level, kind + what + where);
    }

    public override void _LogMessage(string message, bool error) =>
        Diagnostics.Write(error ? LogLevel.Error : LogLevel.Info, message.TrimEnd());
}
