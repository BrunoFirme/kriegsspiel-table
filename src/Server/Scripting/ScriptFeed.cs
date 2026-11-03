using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.Server.Scripting;

public sealed class ScriptFeed
{
    public const int Keep = 500;
    public const int TextKeep = 200;

    public List<string> Text { get; } = new();
    public IReadOnlyList<ConsoleLine> Lines => _lines;
    readonly List<ConsoleLine> _lines = new(), _unsent = new();
    long _seq;
    readonly Dictionary<string, (string message, int line)> _lastErrors = new();

    public void Add(string text, string kind, string source, string? name, int line = 0)
    {
        Text.Add(text);
        if (Text.Count > TextKeep) Text.RemoveAt(0);
        var entry = new ConsoleLine { Seq = ++_seq, At = Repeatable.UtcNow.ToString("o"), Kind = kind, Source = source, Name = name, Line = line, Text = text };
        _lines.Add(entry);
        if (_lines.Count > Keep) _lines.RemoveAt(0);
        _unsent.Add(entry);
        if (_unsent.Count > Keep) _unsent.RemoveAt(0);
    }

    public List<ConsoleLine> TakeUnsent()
    {
        if (_unsent.Count == 0) return new();
        var l = _unsent.ToList(); _unsent.Clear(); return l;
    }

    public int ErrorRaised(string source, string message)
    {
        int at = LineOf(message);
        _lastErrors[source] = (message, at);
        return at;
    }

    public bool LastError(string source, out string message, out int line)
    {
        bool has = _lastErrors.TryGetValue(source, out var e);
        (message, line) = e;
        return has;
    }

    public void Forget(string source) => _lastErrors.Remove(source);

    public void ForgetAll() => _lastErrors.Clear();

    public static int LineOf(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, @"\((\d+),\d+(?:-\d+)?\)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }
}
