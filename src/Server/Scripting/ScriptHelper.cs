using System.Text.RegularExpressions;

namespace KTable.Server.Scripting;

public enum HelperPlace { Top, Middle, Bottom }

public enum HelperField { Label, Place, Counter, Amount, State, TextLine, Text, Choices, Colour, Dice }

public sealed record HelperTrigger(string Name, string? Event, string[] Params, string? Face = null, bool Row = false)
{
    public bool Button => Face != null;
}

public sealed record HelperAction(string Name, HelperField[] Fields, Func<HelperChoice, KitKind, string> Code, string? Loops = null, int States = 0);

public sealed record HelperChoice
{
    public int Trigger { get; init; }
    public int Action { get; init; }
    public string Label { get; init; } = "";
    public HelperPlace Place { get; init; } = HelperPlace.Bottom;
    public int Counter { get; init; }
    public int Amount { get; init; } = 1;
    public int State { get; init; }
    public int TextLine { get; init; }
    public string Text { get; init; } = "";
    public string Choices { get; init; } = "";
    public string Colour { get; init; } = "#E0B040";
    public int Dice { get; init; } = 1;
    public int Sides { get; init; } = 6;
}

public static class ScriptHelper
{
    const string Who = "(self.name or self.kind_name or \"Piece\")";
    const string NameToken = "{name}";

    public static readonly HelperTrigger[] Triggers =
    [
        new("A button on the front", null, ["who"], Face: "front"),
        new("A button on the back", null, ["who"], Face: "back"),
        new("A right-click row", null, ["who"], Row: true),
        new("It is dropped", "on_drop", ["who"]),
        new("It is picked up", "on_pick_up", ["who"]),
        new("Its state changes", "on_state", ["old", "new"]),
        new("It is turned over", "on_flip", ["face_down"]),
        new("A turn ends", "on_turn", ["turn"]),
        new("The script starts", "on_load", ["saved"]),
    ];

    public static readonly HelperAction[] Actions =
    [
        new("Say a line", [HelperField.Text], (c, _) => $"game.say({Spoken(c.Text)})"),
        new("Add to a counter", [HelperField.Counter, HelperField.Amount], (c, k) =>
            $"self.set_counter({Counter(c, k)}, self.counters[{Counter(c, k)}] + {c.Amount})"),
        new("Set a counter", [HelperField.Counter, HelperField.Amount], (c, k) => $"self.set_counter({Counter(c, k)}, {c.Amount})"),
        new("Roll dice and take them from a counter", [HelperField.Counter, HelperField.Dice], (c, k) =>
            $"local total = 0\nfor _, v in ipairs(game.roll({c.Sides}, {c.Dice}, {Who} .. \": {c.Dice}d{c.Sides}\")) do total = total + v end\n"
            + $"self.set_counter({Counter(c, k)}, self.counters[{Counter(c, k)}] - total)"),
        new("Roll dice", [HelperField.Dice], (c, _) => $"game.roll({c.Sides}, {c.Dice}, {Who} .. \": {c.Dice}d{c.Sides}\")"),
        new("Go to a state", [HelperField.State], (c, k) => $"if self.state ~= {StateNo(c, k)} then self.set_state({StateNo(c, k)}) end", States: 1),
        new("Go to the next state", [], (_, _) => "self.set_state(self.state % #self.states + 1)", Loops: "on_state", States: 2),
        new("Write a text line", [HelperField.TextLine, HelperField.Text], (c, k) => $"self.set_text({TextLine(c, k)}, {Spoken(c.Text)})"),
        new("Ask, and write the answer in a text line", [HelperField.TextLine, HelperField.Text, HelperField.Choices], (c, k) =>
            $"game.dialog(who, {{ kind = \"options\", title = {ScriptKit.Str(c.Text)}, options = {{ {Options(c.Choices)} }} }}, function(answer)\n"
            + $"  if answer then self.set_text({TextLine(c, k)}, answer) end\nend)"),
        new("Rename it", [HelperField.Text], (c, _) => $"self.set_name({Spoken(c.Text)})"),
        new("Tint it", [HelperField.Colour], (c, _) => $"self.set_tint({ScriptKit.Str(c.Colour)})"),
        new("Ring it, or take the ring off", [HelperField.Colour], (c, _) => $"self.set_highlight(self.highlight == nil and {ScriptKit.Str(c.Colour)} or nil)"),
        new("Turn it over", [], (_, _) => "self.flip()", Loops: "on_flip"),
        new("Lock or unlock it", [], (_, _) => "self.set_locked(not self.locked)"),
    ];

    public static string? Missing(HelperAction a, HelperTrigger t, KitKind k)
    {
        if (a.Loops != null && a.Loops == t.Event) return "It would run itself again without end";
        if (a.Fields.Contains(HelperField.Counter) && k.Counters.Count == 0) return "The kind has no counter";
        if (a.Fields.Contains(HelperField.TextLine) && k.TextLines.Count == 0) return "The kind has no text line";
        if (k.States.Count < a.States) return a.States == 1 ? "The kind has no state" : "The kind has fewer than two states";
        return null;
    }

    public static KitPart Make(HelperChoice c, KitKind k, string script)
    {
        var t = Triggers[Math.Clamp(c.Trigger, 0, Triggers.Length - 1)];
        var a = Actions[Math.Clamp(c.Action, 0, Actions.Length - 1)];
        var body = a.Code(c, k);
        if (t.Event != null) return new KitPart("", [new KitHook(t.Event, t.Params, body)], "", false);
        var names = new ScriptKit.Names(script);
        var fn = names.For(Slug(c.Label));
        var label = ScriptKit.Str(c.Label.Length > 0 ? c.Label : a.Name);
        var functions = $"function {fn}(who)\n{Indent(body)}\nend";
        string draw;
        if (t.Row) draw = $"self.menu_add({label}, \"{fn}\")";
        else
        {
            double h = Math.Max(k.H, 0.1), m = Math.Min(Math.Max(k.W, 0.1), h);
            double z = c.Place switch { HelperPlace.Top => -h * 0.28, HelperPlace.Middle => 0, _ => h * 0.28 };
            draw = $"self.button{{ face = \"{t.Face}\", x = 0, z = {ScriptKit.Num(z)}, w = {ScriptKit.Num(Math.Max(k.W, 0.1) * 0.6)}, h = {ScriptKit.Num(m * 0.22)}, "
                + $"label = {label}, font = {ScriptKit.Num(m * 0.12)}, on_click = \"{fn}\" }}";
        }
        return new KitPart("", [new KitHook(ScriptKit.DrawHook, [], draw)], functions, false, ScriptKit.ManyStates(k));
    }

    static string Slug(string label)
    {
        var s = Regex.Replace(label.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (s.Length == 0 || char.IsDigit(s[0])) s = "clicked";
        return s.Length > 24 ? s[..24].TrimEnd('_') : s;
    }

    static string Indent(string body) => string.Join("\n", body.Split('\n').Select(l => l.Length == 0 ? l : "  " + l));

    static string Counter(HelperChoice c, KitKind k) => ScriptKit.Str(k.Counters.ElementAtOrDefault(c.Counter) ?? k.Counters.FirstOrDefault() ?? "");

    static string TextLine(HelperChoice c, KitKind k) => ScriptKit.Str(k.TextLines.ElementAtOrDefault(c.TextLine) ?? k.TextLines.FirstOrDefault() ?? "");

    static int StateNo(HelperChoice c, KitKind k) => Math.Clamp(c.State, 0, Math.Max(0, k.States.Count - 1)) + 1;

    static string Options(string choices)
    {
        var list = choices.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (list.Count == 0) list = ["Yes", "No"];
        return string.Join(", ", list.Select(ScriptKit.Str));
    }

    static string Spoken(string text)
    {
        if (!text.Contains(NameToken, StringComparison.Ordinal)) return ScriptKit.Str(text);
        var parts = text.Split(NameToken).Select(p => p.Length == 0 ? null : ScriptKit.Str(p));
        return string.Join(" .. " + Who + " .. ", parts.Select(p => p ?? "\"\""));
    }
}
