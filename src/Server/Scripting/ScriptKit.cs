using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KTable.Core.Model;

namespace KTable.Server.Scripting;

public sealed record KitKind(double W, double H, IReadOnlyList<string> Counters, IReadOnlyList<string> States, IReadOnlyList<string> TextLines)
{
    public static KitKind Of(PieceDef d) =>
        new(d.Size.W, d.Size.H, d.Counters.Select(c => c.Name).ToList(), d.States.Select(s => s.Name).ToList(), d.TextLines.Select(t => t.Name).ToList());
}

public sealed record KitHook(string Event, IReadOnlyList<string> Params, string Body);

public sealed record KitPart(string Top, IReadOnlyList<KitHook> Hooks, string Functions, bool Kept, bool Redraw = false);

public sealed record KitExample(string Group, string Name, string Needs, IReadOnlyList<(string Key, string Text)> Sections)
{
    public string? Missing(KitKind k) => Needs switch
    {
        "counter" when k.Counters.Count == 0 => "Needs a counter on the kind",
        "states" when k.States.Count < 2 => "Needs two states on the kind",
        _ => null,
    };
}

public static class ScriptKit
{
    const string KeptTop = "local kept = {}";
    const string KeptLoad = "local ok, held = pcall(json.decode, saved or \"\")";
    const string KeptLoadEnd = "if ok and type(held) == \"table\" then kept = held end";
    const string KeptSave = "function on_save()\n  return json.encode(kept)\nend";
    const string Resource = "kit-examples.txt";
    const int KeptLook = 4;
    public const string DrawHook = "draw";
    public const string DrawFn = "kit_draw";
    const string Redraws = "self.clear_widgets()\nself.menu_clear()\n" + DrawFn + "()";

    static readonly Regex Placeholder = new(@"\{(\$?)([a-z][a-z0-9]*)\}", RegexOptions.CultureInvariant);
    static readonly Regex Identifier = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant);
    static readonly Regex Return = new(@"\breturn\b", RegexOptions.CultureInvariant);

    static IReadOnlyList<KitExample>? _examples;

    public static IReadOnlyList<KitExample> Examples => _examples ??= Parse(ReadResource());

    static string ReadResource()
    {
        using var stream = typeof(ScriptKit).Assembly.GetManifestResourceStream(Resource);
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<KitExample> Parse(string text)
    {
        var list = new List<KitExample>();
        string[]? head = null;
        var sections = new List<(string Key, string Text)>();
        string? key = null;
        var body = new StringBuilder();
        void EndSection()
        {
            if (key != null) sections.Add((key, body.ToString().Trim('\n')));
            key = null;
            body.Clear();
        }
        void EndExample()
        {
            EndSection();
            if (head != null) list.Add(new KitExample(head[0], head[1], head.Length > 2 ? head[2] : "", sections.ToList()));
            sections.Clear();
        }
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                EndExample();
                head = line[3..].Split('|').Select(s => s.Trim()).ToArray();
            }
            else if (line.StartsWith("@ ", StringComparison.Ordinal))
            {
                EndSection();
                key = line[2..].Trim();
            }
            else if (key != null) body.Append(line).Append('\n');
        }
        EndExample();
        return list;
    }

    public static KitPart Fit(KitExample e, KitKind k, string script)
    {
        var names = new Names(script);
        var top = "";
        var functions = "";
        var hooks = new List<KitHook>();
        foreach (var (key, raw) in e.Sections)
        {
            var text = Fill(raw, k, names);
            if (key == "top") top = text;
            else if (key == "functions") functions = text;
            else hooks.Add(HookOf(key, text));
        }
        return new KitPart(top, hooks, functions, e.Needs == "kept", ManyStates(k));
    }

    public static bool ManyStates(KitKind k) => k.States.Count >= 2;

    static KitHook HookOf(string key, string body)
    {
        int open = key.IndexOf('(');
        var ev = open < 0 ? key : key[..open];
        var ps = open < 0 ? [] : key[(open + 1)..].TrimEnd(')').Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return new KitHook(ev, ps, body);
    }

    static string Fill(string text, KitKind k, Names names) => Placeholder.Replace(text, m =>
        m.Groups[1].Value == "$" ? names.For(m.Groups[2].Value) : Value(m.Groups[2].Value, k));

    static string Value(string name, KitKind k)
    {
        double w = Math.Max(k.W, 0.1), h = Math.Max(k.H, 0.1), m = Math.Min(w, h);
        double ahead = -h * 0.5 - m * 0.6;
        return name switch
        {
            "left" => Num(-w * 0.32), "right" => Num(w * 0.32), "top" => Num(-h * 0.28), "bottom" => Num(h * 0.28),
            "btn" => Num(m * 0.22), "wide" => Num(w * 0.4), "font" => Num(m * 0.12), "big" => Num(m * 0.2),
            "boxw" => Num(w * 0.8), "boxh" => Num(m * 0.2), "tall" => Num(h * 0.7), "pad" => Num(m * 0.05),
            "ring" => Num(Math.Max(w, h) * 2), "edge" => Num(-h * 0.5), "ahead" => Num(ahead), "head" => Num(ahead + m * 0.2),
            "left2" => Num(-m * 0.15), "right2" => Num(m * 0.15),
            "counter" => Esc(k.Counters.FirstOrDefault() ?? ""),
            _ => throw new InvalidOperationException($"no value for {{{name}}}"),
        };
    }

    public static string Num(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    public static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    public static string Str(string s) => "\"" + Esc(s) + "\"";

    public sealed class Names
    {
        readonly HashSet<string> _taken;
        readonly Dictionary<string, string> _given = new();

        public Names(string script) => _taken = Identifier.Matches(script).Select(m => m.Value).ToHashSet();

        public string For(string wanted)
        {
            if (_given.TryGetValue(wanted, out var had)) return had;
            var name = wanted;
            for (int n = 2; _taken.Contains(name); n++) name = wanted + n;
            _taken.Add(name);
            _given[wanted] = name;
            return name;
        }
    }

    public static string Alone(KitPart part) => Add("", part).Text;

    public static (string Text, string? Refused) Add(string script, KitPart part)
    {
        var lines = script.Replace("\r", "").Split('\n').ToList();
        if (lines.Count == 1 && lines[0].Length == 0) lines.Clear();
        bool hasKept = lines.Any(l => l.Trim() == KeptTop);
        if (part.Kept && !hasKept && HeaderAt(lines, "on_save") >= 0) return (script, "The script has its own on_save");
        foreach (var hook in part.Hooks)
            if (Return.IsMatch(hook.Body) && HeaderAt(lines, hook.Event) >= 0) return (script, $"The script has its own {hook.Event}");
        var hooks = part.Hooks.Select(h => h.Event == DrawHook ? h with { Event = DrawFn, Params = [] } : h).ToList();
        if (hooks.Any(h => h.Event == DrawFn) && HeaderAt(lines, DrawFn) < 0)
        {
            if (part.Redraw) hooks.Insert(0, new KitHook("on_state", ["old", "new"], Redraws));
            hooks.Insert(0, new KitHook("on_load", ["saved"], DrawFn + "()"));
        }
        var tail = new List<string>();
        var tops = new List<string>();
        if (part.Kept && !hasKept)
        {
            tops.Add(KeptTop);
            hooks.Insert(0, new KitHook("on_load", ["saved"], KeptLoad + "\n" + KeptLoadEnd));
            tail.Add(KeptSave);
        }
        if (part.Top.Length > 0) tops.Add(part.Top);
        if (tops.Count > 0)
        {
            int at = lines.FindIndex(l => l.Trim() == KeptTop) + 1;
            lines.InsertRange(at, (at == 0 && lines.Count > 0 ? tops.Append("") : tops).SelectMany(t => t.Split('\n')));
        }
        foreach (var hook in hooks)
            if (!MergeInto(lines, hook)) Append(lines, $"function {hook.Event}({string.Join(", ", hook.Params)})\n{Indent(hook.Body)}\nend");
        if (part.Functions.Length > 0) tail.Add(part.Functions);
        foreach (var t in tail) Append(lines, t);
        return (string.Join("\n", lines) + "\n", null);
    }

    static void Append(List<string> lines, string text)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 0) lines.Add("");
        lines.AddRange(text.Split('\n'));
    }

    static Regex HeaderOf(string ev) => new($@"^function\s+{Regex.Escape(ev)}\s*\(([^)]*)\)(.*)$", RegexOptions.CultureInvariant);

    static int HeaderAt(List<string> lines, string ev)
    {
        var re = HeaderOf(ev);
        return lines.FindIndex(l => re.IsMatch(l));
    }

    static bool MergeInto(List<string> lines, KitHook hook)
    {
        int i = HeaderAt(lines, hook.Event);
        if (i < 0) return false;
        var m = HeaderOf(hook.Event).Match(lines[i]);
        var had = m.Groups[1].Value.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var alias = new List<string>();
        var all = had.ToList();
        for (int j = 0; j < hook.Params.Count; j++)
        {
            if (j >= all.Count) all.Add(hook.Params[j]);
            else if (all[j] != hook.Params[j] && Uses(hook.Body, hook.Params[j])) alias.Add($"local {hook.Params[j]} = {all[j]}");
        }
        var rest = m.Groups[2].Value.Trim();
        lines[i] = $"function {hook.Event}({string.Join(", ", all)})";
        int at = i + 1;
        if (rest.Length > 0 && !rest.StartsWith("--", StringComparison.Ordinal)) lines.Insert(at, "  " + rest);
        else if (rest.Length > 0) lines[i] += " " + rest;
        for (int k = at; k < Math.Min(lines.Count, at + KeptLook); k++)
            if (lines[k].Trim() == KeptLoad) { at = k + 2; break; }
        lines.InsertRange(at, alias.Select(a => "  " + a).Concat(Indent(hook.Body).Split('\n')));
        return true;
    }

    static bool Uses(string body, string name) => Regex.IsMatch(body, $@"(?<![\w.]){Regex.Escape(name)}\b", RegexOptions.CultureInvariant);

    static string Indent(string body) => string.Join("\n", body.Split('\n').Select(l => l.Length == 0 ? l : "  " + l));
}
