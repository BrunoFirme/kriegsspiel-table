using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using KTable.Core.Panels;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class PlayerTools
{
    readonly TableTools _t;

    public PlayerTools(TableTools t) => _t = t;

    const int PanelRowsListed = 400;
    const double PictureShareSeconds = 30;
    static readonly string[] PanelEvents = { "onClick", "onValueChanged", "onEndEdit", "onSubmit" };
    static readonly string[] UiEventWords = { "click", "change", "end" };

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("press_button",
            "Presses a button a piece's script drew on it, as a player clicking it does (so it tests what people see). By its label or its number from list_buttons.",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("label", "string", "the button's label"), new Arg("number", "number", "its number from list_buttons (from 1)"), new Arg("alt", "boolean", "a right-click")),
            PressButton);
        yield return new AgentTool("list_buttons",
            "The buttons, input boxes and menu rows a piece's script gave it, and the table's own script menu rows and hotkeys.",
            Args.Schema(new Arg("id", "string", "a piece's id; leave out for the table's")), ListButtons);
        yield return new AgentTool("pick_menu_row",
            "Picks a row a script added to a piece's right-click menu, or (no id) to the table's menu, by its label.",
            Args.Schema(new Arg("id", "string", "the piece's id; leave out for the table's menu"), new Arg("label", "string", "the row's label", true), new Arg("x", "number", "for the table's menu: where it was opened"), new Arg("z", "number", "map units")),
            PickRow);
        yield return new AgentTool("press_hotkey",
            "Presses a hotkey a script registered, by its label, with these pieces selected.",
            Args.Schema(new Arg("label", "string", "the hotkey's label", true), new Arg("selected", "array", "piece ids to have selected"), new Arg("id", "string", "the piece under the pointer")),
            PressHotkey);
        yield return new AgentTool("type_in_box",
            "Types into an input box a piece's script drew (list_buttons shows them, with their numbers), as the person typing there and leaving the box does; the script's on_change runs and may change the text. Returns what the box holds then.",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("text", "string", "the whole text the box is to hold", true),
                new Arg("number", "number", "its number from list_buttons (from 1)"), new Arg("label", "string", "its label or placeholder, when it has one")),
            TypeInBox);
        yield return new AgentTool("press_number",
            "Presses a number key over pieces, as the person does with the pointer on them: their state (or a die's side) becomes that number, or, when the table has a global script, " +
            "its on_number_typed hears it first and may keep the state as it is.",
            Args.Schema(new Arg("ids", "array", "the pieces' ids", true), new Arg("number", "number", $"1 to {NumberKey.Most} (the 0 key is 10)", true)),
            PressNumber);
        yield return new AgentTool("read_panels",
            "The panels a script laid over the screen (no id: the table's own, set with ui.set) or on a piece (its self.ui): every element with its path, tag, id, text, value, " +
            "whether it is shown, and the events it answers. use_panel clicks or fills one.",
            Args.Schema(new Arg("id", "string", "a piece's id; leave out for the table's panels"), Paging.Offset),
            ReadPanels);
        yield return new AgentTool("use_panel",
            "Uses an element of a script's panel as the person does (read_panels lists them): click a button, change a toggle (True or False), a slider (a number), " +
            "a dropdown (an option's text or number from 0) or an input field's text, or end an input field's editing. The script's handler runs; returns what it printed.",
            Args.Schema(new Arg("element", "string", "the element's id, or its path from read_panels (such as 0/2/1)", true), new Arg("id", "string", "the piece whose panel it is; leave out for the table's"),
                new Arg("event", "string", "click (default), change or end"), new Arg("value", "string", "for change and end: the new value")),
            UsePanel);
        yield return new AgentTool("answer_dialog",
            "The windows a script opened for this seat with game.dialog and that wait for an answer: with nothing given, lists them (number, kind, title, text, options). " +
            "Given an answer, answers one as the person would: a memo or input's text, yes or no for a confirm, one of the options, a colour's name, or for a picture a file or web link. " +
            "cancel closes it unanswered. The script's function runs; returns what it printed.",
            Args.Schema(new Arg("number", "number", "which window, from 1 (the first if left out)"), new Arg("answer", "string", "the answer"), new Arg("cancel", "boolean", "close it unanswered")),
            AnswerDialog);
    }

    async Task<ToolResult> TypeInBox(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (TableTools.Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        if (a.Str("text") is not { } text) return ToolResult.Fail("text is needed.");
        bool Box(Widget w) => w.Kind == "input";
        int index = a.Num("number") is { } n ? (int)n - 1
            : a.Str("label") is { Length: > 0 } label ? p.Widgets.FindIndex(w => Box(w) && w.Label.Contains(label, StringComparison.OrdinalIgnoreCase))
            : p.Widgets.FindIndex(Box);
        if (index < 0 || index >= p.Widgets.Count || !Box(p.Widgets[index])) return ToolResult.Fail($"{TableTools.NameOf(s, p)} has no such input box: list_buttons shows them.");
        long seq = TableTools.LastSeq(s);
        s.Do(new EditWidget { Layer = Layers.Umpire, Id = id, Index = index + 1, Value = text });
        var done = await Pressed(s, seq, $"typed into box {index + 1} on {TableTools.NameOf(s, p)}");
        var now = TableTools.Find(s, id)?.Widgets.ElementAtOrDefault(index)?.Value;
        return new ToolResult(done.Text + $"\nThe box holds: {now}", done.IsError);
    }

    async Task<ToolResult> PressNumber(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        if (a.Num("number") is not { } number || number < 1 || number > NumberKey.Most) return ToolResult.Fail($"number is 1 to {NumberKey.Most}.");
        int n = (int)number;
        var ids = TableTools.List(a, "ids").Distinct().ToList();
        var found = ids.Select(id => s.View.Find(Layers.Umpire, id)).OfType<PieceInstance>().ToList();
        if (found.Count == 0) return ToolResult.Fail("ids names no piece on this table.");
        var cmds = NumberKey.Commands(s, found.Select(p => (Layers.Umpire, p)), n);
        if (cmds.Count == 0) return ToolResult.Fail($"No piece there takes {n}: none has a state {n} or a side {n} it does not already show, or they lie in another umpire's zone.");
        long seq = TableTools.LastSeq(s);
        s.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
        var done = await Pressed(s, seq, $"pressed {n} over {found.Count} piece{(found.Count == 1 ? "" : "s")}");
        var rows = new JsonArray(found.Select(p => s.View.Find(Layers.Umpire, p.Id)).OfType<PieceInstance>()
            .Select(p => (JsonNode?)new JsonObject { ["id"] = p.Id, ["state"] = p.State + 1, ["side"] = NumberKey.IsDie(s.Def(p.Def), p) ? p.ShownFace + 1 : null }).ToArray());
        return new ToolResult(done.Text + "\n" + rows.ToJsonString(TableTools.Json), done.IsError);
    }

    static UiLayout? LayoutOf(ClientSession s, string id, out string whose, out ToolResult? why)
    {
        why = null;
        if (id.Length == 0)
        {
            whose = "the table";
            return s.View.UiLayout;
        }
        if (TableTools.Find(s, id) is not { } p)
        {
            whose = id;
            why = ToolResult.Fail($"No piece {id} on this table.");
            return null;
        }
        whose = TableTools.NameOf(s, p);
        return p.UiLayout;
    }

    Task<ToolResult> ReadPanels(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return Task.FromResult(why!);
        var id = a.Str("id") ?? "";
        var layout = LayoutOf(s, id, out var whose, out why);
        if (why != null) return Task.FromResult(why);
        if (layout == null || layout.Elements.Count == 0) return Task.FromResult(new ToolResult($"No panels on {whose}."));
        var all = new List<(string Path, UiElement Element)>();
        void Walk(List<UiElement> list, string prefix)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var path = prefix.Length == 0 ? i.ToString(CultureInfo.InvariantCulture) : prefix + "/" + i.ToString(CultureInfo.InvariantCulture);
                all.Add((path, list[i]));
                Walk(list[i].Children, path);
            }
        }
        Walk(layout.Elements, "");
        var (page, head) = Paging.Page(all, a, PanelRowsListed, $"elements on {whose}");
        var rows = new JsonArray(page.Select(x =>
        {
            var e = x.Element;
            var events = PanelEvents.Where(ev => e.Get(ev) is { Length: > 0 }).ToList();
            return (JsonNode?)new JsonObject
            {
                ["path"] = x.Path, ["tag"] = e.Tag, ["id"] = e.Get("id"), ["text"] = e.Inner ?? e.Get("text"), ["value"] = e.Get("value") ?? e.Get("isOn"),
                ["options"] = e.Options is { Count: > 0 } o ? new JsonArray(o.Select(v => (JsonNode?)JsonValue.Create(v.Value)).ToArray()) : null,
                ["shown"] = e.Shown is { } shown ? shown : null, ["active"] = e.Active ?? e.Get("active"),
                ["answers"] = events.Count > 0 ? string.Join(", ", events) : null,
            };
        }).ToArray());
        return Task.FromResult(new ToolResult(head + "\n" + rows.ToJsonString(TableTools.Json)));
    }

    async Task<ToolResult> UsePanel(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        var layout = LayoutOf(s, id, out var whose, out why);
        if (why != null) return why;
        if (layout == null) return ToolResult.Fail($"No panels on {whose}: read_panels shows what there is.");
        var key = a.Str("element")?.Trim() ?? "";
        List<int>? path = null;
        UiElement? element;
        if (key.Split('/') is var parts && parts.All(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            path = parts.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList();
            element = layout.At(path);
        }
        else element = layout.ById(key);
        if (element == null) return ToolResult.Fail($"{whose} has no element {key}: read_panels lists them.");
        var ev = (a.Str("event") ?? "click").Trim().ToLowerInvariant();
        if (!UiEventWords.Contains(ev)) return ToolResult.Fail("event is click, change or end.");
        var value = a.Str("value") ?? "";
        if (ev == "change" && element.Tag == PanelTags.Dropdown && element.Options is { Count: > 0 } options && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            int at = options.FindIndex(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
            if (at < 0) return ToolResult.Fail($"The dropdown has no option {value}: {string.Join(", ", options.Select(o => o.Value))}.");
            value = at.ToString(CultureInfo.InvariantCulture);
        }
        long seq = TableTools.LastSeq(s);
        var piece = id.Length > 0 ? id : null;
        s.Do(new UiEvent { Layer = piece == null ? null : Layers.Umpire, Piece = piece, Id = path == null ? key : "", Path = path, Event = ev, Value = value });
        return await Pressed(s, seq, $"used {element.Get("id") ?? key} on {whose}'s panel ({ev})");
    }

    static JsonObject DialogRow(ShowDialog d, int number) => new()
    {
        ["number"] = number, ["kind"] = d.Kind, ["title"] = d.Title, ["text"] = d.Text.Length > 0 ? d.Text : null,
        ["options"] = d.Kind == "color" ? new JsonArray(ScriptDialogs.Colours.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())
            : d.Options.Count > 0 ? new JsonArray(d.Options.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray()) : null,
    };

    async Task<ToolResult> AnswerDialog(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        var open = s.OpenDialogs.ToList();
        if (a.Str("answer") == null && a.Flag("cancel") != true)
            return new ToolResult(open.Count == 0 ? "No script window waits for an answer." : new JsonArray(open.Select((d, i) => (JsonNode?)DialogRow(d, i + 1)).ToArray()).ToJsonString(TableTools.Json));
        if (open.Count == 0) return ToolResult.Fail("No script window waits for an answer.");
        int number = (int)(a.Num("number") ?? 1);
        if (number < 1 || number > open.Count) return ToolResult.Fail($"number is 1 to {open.Count}.");
        var d = open[number - 1];
        string? value = null;
        var files = new List<string>();
        if (a.Flag("cancel") != true)
        {
            var answer = a.Str("answer")!;
            switch (d.Kind)
            {
                case "confirm":
                    if (answer.Trim().ToLowerInvariant() is not ("yes" or "no")) return ToolResult.Fail("A confirm is answered yes or no.");
                    value = answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ? ScriptDialogs.Yes : null;
                    break;
                case "options" or "color":
                    var choices = d.Kind == "color" ? ScriptDialogs.Colours.ToList() : d.Options;
                    value = choices.FirstOrDefault(o => string.Equals(o, answer.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (value == null) return ToolResult.Fail("The answer is one of: " + string.Join(", ", choices) + ".");
                    break;
                case "picture":
                    if (_t.Hooks.KeepPicture is not { } keep) return ToolResult.Fail("A picture is answered from the app's own window.");
                    if (await keep(answer.Trim(), false) is not { } kept) return ToolResult.Fail($"{answer} could not be used as a picture.");
                    value = kept;
                    files.Add(kept);
                    break;
                default:
                    value = answer;
                    break;
            }
        }
        long seq = TableTools.LastSeq(s);
        var result = new DialogResult { Token = d.Token, Value = value };
        if (files.Count > 0 && _t.Hooks.SendWithFiles is { } send) send(files, result);
        else s.Do(result);
        await _t.Until(() => s.OpenDialogs.All(x => x.Token != d.Token), files.Count > 0 ? PictureShareSeconds : Waits.LandingSeconds);
        var title = d.Title.Length > 0 ? d.Title : d.Kind;
        return await Pressed(s, seq, value == null ? $"closed the window {title} unanswered" : $"answered the window {title}");
    }

    Task<ToolResult> ListButtons(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return Task.FromResult(why!);
        var id = a.Str("id") ?? "";
        var o = new JsonObject();
        if (id.Length > 0)
        {
            if (TableTools.Find(s, id) is not { } p) return Task.FromResult(ToolResult.Fail($"No piece {id} on this table."));
            o["widgets"] =
                new JsonArray(p.Widgets.Select((w, i) => (JsonNode?)new JsonObject { ["number"] = i + 1, ["kind"] = w.Kind, ["label"] = w.Label, ["face"] = w.Face, ["value"] = w.Kind == "input" ? w.Value : null, ["presses"] = w.OnClick != null
                    || w.OnChange != null }).ToArray());
            o["menuRows"] = new JsonArray(p.MenuItems.Select(m => (JsonNode?)JsonValue.Create(m.Label)).ToArray());
        }
        else
        {
            o["menuRows"] = new JsonArray(s.View.MenuItems.Select(m => (JsonNode?)JsonValue.Create(m.Label)).ToArray());
            o["hotkeys"] = new JsonArray(s.View.Hotkeys.Select(h => (JsonNode?)JsonValue.Create(h.Label)).ToArray());
        }
        return Task.FromResult(new ToolResult(o.ToJsonString(TableTools.Json)));
    }

    async Task<ToolResult> Pressed(ClientSession s, long seq, string what)
    {
        await _t.Landed(s);
        _t.Log(what);
        var fresh = s.ConsoleLines.Where(l => l.Seq > seq).ToList();
        return new ToolResult(char.ToUpperInvariant(what[0]) + what[1..] + "." + TableTools.Since(s, seq, 60), fresh.Any(l => l.Kind == "error"));
    }

    async Task<ToolResult> PressButton(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (TableTools.Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        int index = a.Num("number") is { } n ? (int)n - 1 : a.Str("label") is { Length: > 0 } label
? p.Widgets.FindIndex(w => w.OnClick != null && w.Label.Equals(label, StringComparison.OrdinalIgnoreCase)) is var exact and >= 0 ? exact
                : p.Widgets.FindIndex(w => w.OnClick != null && w.Label.Contains(label, StringComparison.OrdinalIgnoreCase))
            : -1;
        if (index < 0 || index >= p.Widgets.Count) return ToolResult.Fail($"{TableTools.NameOf(s, p)} has no such button: list_buttons shows them.");
        long seq = TableTools.LastSeq(s);
        s.Do(new ClickWidget { Layer = Layers.Umpire, Id = id, Index = index + 1, Alt = a.Flag("alt") ?? false });
        return await Pressed(s, seq, $"pressed {p.Widgets[index].Label} on {TableTools.NameOf(s, p)}");
    }

    async Task<ToolResult> PickRow(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var label = a.Str("label") ?? "";
        long seq = TableTools.LastSeq(s);
        if (a.Str("id") is { Length: > 0 } id)
        {
            if (TableTools.Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
            int i = p.MenuItems.FindIndex(m => m.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return ToolResult.Fail($"{TableTools.NameOf(s, p)}'s menu has no row {label}: list_buttons shows them.");
            s.Do(new PickMenuItem { Layer = Layers.Umpire, Id = id, Index = i + 1 });
            return await Pressed(s, seq, $"picked {label} on {TableTools.NameOf(s, p)}");
        }
        int g = s.View.MenuItems.FindIndex(m => m.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (g < 0) return ToolResult.Fail($"The table's menu has no row {label}: list_buttons shows them.");
        s.Do(new PickGlobalMenuItem { Index = g + 1, Pos = a.Num("x") is { } x && a.Num("z") is { } z ? new Vec2(x, z) : null });
        return await Pressed(s, seq, $"picked {label} on the table's menu");
    }

    async Task<ToolResult> PressHotkey(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var label = a.Str("label") ?? "";
        int i = s.View.Hotkeys.FindIndex(h => h.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return ToolResult.Fail($"No hotkey {label}: list_buttons shows them.");
        long seq = TableTools.LastSeq(s);
        var under = a.Str("id") is { Length: > 0 } id ? TableTools.Find(s, id) : null;
        s.Do(new Hotkey { Index = i + 1, Layer = under == null ? null : Layers.Umpire, Piece = under?.Id, Pos = under?.Pos, Selected = TableTools.List(a, "selected") });
        return await Pressed(s, seq, $"pressed the hotkey {label}");
    }
}
