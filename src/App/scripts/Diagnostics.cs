using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

public enum LogLevel { Info, Warning, Error }

public static class Diagnostics
{
    const long MaxLogBytes = 512 * 1024;
    const int KeepProblems = 40, KeepCommands = 30, MaxProblemChars = 4000;

    static readonly object _gate = new();
    static readonly Queue<string> _problems = new();
    static readonly Queue<string> _commands = new();
    static string _logPath = "";
    static bool _installed;
    static Session? _session;
    static DiagnosticsLogger? _logger;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        _logPath = Path.Combine(OS.GetUserDataDir(), "app.log");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record("unhandled", e.ExceptionObject?.ToString() ?? "an unknown failure");
        TaskScheduler.UnobservedTaskException += (_, e) => { Record("task", e.Exception.ToString()); e.SetObserved(); };
        try { OS.AddLogger(_logger = new DiagnosticsLogger()); }
        catch (Exception e) { Record("diagnostics", "Godot's errors cannot be listened to: " + e.Message); }
    }

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
        if (_logPath.Length > 0) b.AppendLine().AppendLine("Log on this PC: " + _logPath);
        return b.ToString();
    }

    public static string Copy()
    {
        AppClipboard.SetText(Report());
        return "Diagnostics copied: paste them where you ask for help. Nothing was sent anywhere.";
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
