using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Agent;

namespace KTable.App;

sealed class AgentUiTools
{
    const int RowsShown = 200;
    const double StepSeconds = 0.02, SettleSeconds = 0.15;
    static readonly string[] Actions = { "click", "right_click", "double_click", "set", "submit", "open", "close" };
    static readonly string[] FencedKeys = { "sighting" };
    const string KeyRefused = "That key opens POV capture, which stays the umpires'.";
    const string TitleRefused = "The title screen is worked through status, host_game, join_game, open_studio, import_module and the settings tools; ui_act works at the table and in the studio.";

    readonly AgentEndpoint _a;
    readonly UiDriver _ui = new();

    public AgentUiTools(AgentEndpoint a) { _a = a; }

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("ui_read",
            "Every control the person can see and use in the app's windows, as a list: its ref, kind (button, toggle, check, field, choice, list, tree, tabs, slider, menu_item…), window, text, value, tooltip, " +
            "and whether it is greyed out. ui_act uses the refs, which last until the next read. " +
            "For anything the other tools do not cover; a page at a time.",
            Args.Schema(new Arg("find", "string", "only controls whose text, value or tooltip has this"), new Arg("window", "string", "only this window (by its title)"),
                new Arg("labels", "boolean", "include plain text labels (true if left out)"), Paging.Offset),
            x => Task.FromResult(Read(x)));
        yield return new AgentTool("ui_act",
            "Uses the app as the person does. With ref (from ui_read): click, right_click, double_click, set (a field's text, a number, a choice, a tab, a list or tree item, true or false), " +
            "submit (Enter in a field), open (a menu button or a choice), close (a window). keys presses a key or a combination (ctrl+z, shift+F2, Escape); text types into what has the focus. " +
            "Returns the windows open after it; look adds a picture.",
            Args.Schema(new Arg("ref", "string", "a control's ref from ui_read"), new Arg("action", "string", string.Join(", ", Actions) + " (click if left out)"),
                new Arg("value", "string", "for set and submit, or the item to click in a list, tree, tabs or choice"),
                new Arg("keys", "string", "a key or combination"), new Arg("text", "string", "characters to type"),
                new Arg("look", "boolean", "add a picture of the window after it")),
            Act);
    }

    Window Root => _a.GetTree().Root;

    ToolResult Read(JsonElement a)
    {
        var rows = _ui.Read(Root, a.Flag("labels") != false);
        var find = a.Str("find")?.Trim() ?? "";
        var window = a.Str("window")?.Trim() ?? "";
        bool Has(string? s, string w) => s != null && s.Contains(w, StringComparison.OrdinalIgnoreCase);
        var kept = rows.Where(r => (find.Length == 0 || Has(r.Text, find) || Has(r.Value, find) || Has(r.Tip, find)) && (window.Length == 0 || Has(r.Window, window))).ToList();
        var (page, head) = Paging.Page(kept, a, RowsShown, "controls");
        var list = new JsonArray(page.Select(r => (JsonNode?)new JsonObject
        {
            ["ref"] = r.Ref, ["kind"] = r.Kind, ["window"] = r.Window, ["text"] = r.Text, ["value"] = r.Value, ["tooltip"] = r.Tip, ["greyed"] = r.Disabled ? true : null,
            ["items"] = r.Items is { Count: > 0 } items ? new JsonArray(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()) : null,
        }).ToArray());
        var title = _a.AtTitle() ? " (the title screen: read only for ui_act)" : "";
        return new ToolResult($"{head}{title}; windows open: {Windows()}\n" + list.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    string Windows() => UiDriver.OpenWindows(Root).ToList() is { Count: > 0 } open ? string.Join(", ", open) : "none but the main one";

    async Task<ToolResult> Act(JsonElement a)
    {
        if (_a.AtTitle()) return ToolResult.Fail(TitleRefused);
        var action = (a.Str("action") ?? "click").Trim().ToLowerInvariant();
        if (!Actions.Contains(action)) return ToolResult.Fail("action is " + string.Join(", ", Actions) + ".");
        string? why;
        string did;
        if (a.Str("ref") is { Length: > 0 } reference)
        {
            var (target, item, refused) = _ui.Resolve(reference);
            if (target == null) return ToolResult.Fail(refused!);
            why = UiDriver.Act(target, item, action, a.Str("value"));
            did = $"{action} on {reference}";
        }
        else if (a.Str("keys") is { Length: > 0 } keys)
        {
            why = FocusFenced() ?? await Press(keys);
            did = "pressed " + keys;
        }
        else if (a.Str("text") is { } text)
        {
            why = FocusFenced() ?? await Type(text);
            did = "typed " + text.Length + " characters";
        }
        else return ToolResult.Fail("Give ref, keys or text.");
        if (why != null) return ToolResult.Fail(why);
        await _a.Delay(SettleSeconds);
        await _a.Drawn(AgentEndpoint.SettleFrames);
        _a.Log("used the app: " + did);
        var said = $"Done: {did}. Windows open: {Windows()}.";
        if (a.Flag("look") == true && await _a.Snapshot() is { } shot) return new ToolResult(said, false, new[] { shot });
        return new ToolResult(said);
    }

    string? FocusFenced()
    {
        foreach (var w in new[] { Root }.Concat(Root.GetChildren(true).SelectMany(Windows)))
            if (AgentFence.Fenced(w.GuiGetFocusOwner()) || w != Root && w.HasFocus() && AgentFence.Fenced(w))
                return "What has the focus is out of the agent's reach.";
        return null;
    }

    static IEnumerable<Window> Windows(Node n) =>
        (n is Window { Visible: true } w ? new[] { w } : Enumerable.Empty<Window>()).Concat(n is Node3D ? Enumerable.Empty<Window>() : n.GetChildren(true).SelectMany(Windows));

    async Task<string?> Press(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "keys is a key or a combination such as ctrl+z.";
        var mods = parts[..^1].Select(m => m.ToLowerInvariant()).ToList();
        if (mods.Any(m => m is not ("ctrl" or "shift" or "alt" or "meta" or "cmd"))) return "keys takes ctrl, shift, alt or meta before the key.";
        var key = OS.FindKeycodeFromString(parts[^1]);
        if (key == Key.None) return $"No key {parts[^1]}.";
        bool ctrl = mods.Contains("ctrl"), shift = mods.Contains("shift"), alt = mods.Contains("alt"), meta = mods.Contains("meta") || mods.Contains("cmd");
        long unicode = parts[^1].Length == 1 && !ctrl && !alt && !meta ? (shift ? char.ToUpperInvariant(parts[^1][0]) : char.ToLowerInvariant(parts[^1][0])) : 0;
        InputEventKey KeyEvent(bool pressed) => new()
        {
            Keycode = key, PhysicalKeycode = key, Unicode = pressed ? unicode : 0, Pressed = pressed, CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt, MetaPressed = meta,
        };
        if (KeyFenced(KeyEvent(true))) return KeyRefused;
        foreach (var pressed in new[] { true, false })
        {
            Input.ParseInputEvent(KeyEvent(pressed));
            await _a.Delay(StepSeconds);
        }
        return null;
    }

    bool KeyFenced(InputEventKey k) => _a.TableHud() != null && FencedKeys.Any(action => _a.Cfg.Keys.Is(k, action));

    async Task<string?> Type(string text)
    {
        if (text.Any(ch => KeyFenced(new InputEventKey { Keycode = OS.FindKeycodeFromString(ch.ToString()), Unicode = ch, Pressed = true, ShiftPressed = char.IsUpper(ch) })))
            return KeyRefused;
        foreach (var ch in text)
        {
            var key = OS.FindKeycodeFromString(ch.ToString());
            foreach (var pressed in new[] { true, false })
                Input.ParseInputEvent(new InputEventKey { Keycode = key, Unicode = pressed ? ch : 0, Pressed = pressed, ShiftPressed = char.IsUpper(ch) });
            await _a.Delay(StepSeconds / 2);
        }
        return null;
    }
}
