using System.Globalization;
using KTable.Core.Model;
using KTable.Core.Panels;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed partial class PanelQueue
{
    static readonly string[] MouseEvents = { "onClick", "onMouseEnter", "onMouseExit", "onMouseUp", "onMouseDown" };

    public static string UiEventName(string ev) => ev switch { "click" => "onClick", "end" => "onEndEdit", "change" => "onValueChanged", _ => ev };

    public void React(UiEvent e, DynValue who)
    {
        var layout = e.Piece == null ? State.UiLayout : State.Find(e.Layer ?? Layers.Umpire, e.Piece)?.UiLayout;
        if (layout == null) return;
        var element = e.Path is { Count: > 0 } path ? layout.At(path) : layout.ById(e.Id);
        if (element == null) return;
        var ev = UiEventName(e.Event);
        var handler = element.Get(ev);
        if (string.IsNullOrEmpty(handler)) return;
        var parts = handler.Trim(')', ';').Split(',', '(');
        var method = parts[0];
        var custom = parts.Length > 1 ? parts[1] : null;
        var value = UiEventValue(element, ev, custom, e.Value);
        var (ps, fn) = UiHandlerOwner(e.Piece, method);
        if (ps == null) return;
        var id = element.Get("id");
        host.Call(ps, fn, who, value == null ? DynValue.Nil : DynValue.NewString(value), id == null ? DynValue.Nil : DynValue.NewString(id));
    }

    static string? UiEventValue(UiElement e, string ev, string? custom, string raw)
    {
        if (MouseEvents.Contains(ev, StringComparer.OrdinalIgnoreCase)) return string.IsNullOrEmpty(custom) || custom is "value" or "pointerId" ? raw : custom;
        bool changed = ev.Equals("onValueChanged", StringComparison.OrdinalIgnoreCase);
        bool edited = ev.Equals("onEndEdit", StringComparison.OrdinalIgnoreCase) || ev.Equals("onSubmit", StringComparison.OrdinalIgnoreCase);
        var asked = string.IsNullOrEmpty(custom) ? "value" : custom;
        var lower = asked.ToLowerInvariant();
        return e.Tag switch
        {
            PanelTags.Toggle or PanelTags.ToggleButton or "Slider" when changed => lower is "selectedvalue" or "value" ? raw : asked,
            "InputField" when changed || edited => lower == "value" ? raw : asked,
            PanelTags.Dropdown when changed => lower switch { "selectedtext" or "selectedvalue" or "value" => OptionText(e, raw), "selectedindex" => raw, _ => asked },
            "ToggleGroup" when changed => lower switch { "selectedvalue" or "value" => raw, "selectedtext" => GroupText(e, raw), _ => asked },
            _ => custom,
        };
    }

    static string OptionText(UiElement e, string raw)
    {
        var options = e.Options ?? new();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < options.Count ? options[i].Value ?? "" : raw;
    }

    static string GroupText(UiElement e, string raw)
    {
        var toggles = Toggles(e).ToList();
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || i < 0 || i >= toggles.Count) return raw;
        var t = toggles[i];
        return t.Get("text") ?? t.Children.FirstOrDefault(c => c.Tag == PanelTags.Text)?.Inner ?? "";
    }

    static IEnumerable<UiElement> Toggles(UiElement e)
    {
        foreach (var c in e.Children)
        {
            if (c.Tag is PanelTags.Toggle or PanelTags.ToggleButton) yield return c;
            else foreach (var d in Toggles(c)) yield return d;
        }
    }

    (PieceScript? ps, string fn) UiHandlerOwner(string? piece, string method)
    {
        if (!method.Contains('/'))
        {
            if (piece == null) return (host.GlobalScript, method);
            return (host.ScriptOf(piece), method);
        }
        var parts = method.Split('/');
        if (parts.Length != 2) return (null, method);
        if (parts[0].Equals("global", StringComparison.OrdinalIgnoreCase)) return (host.GlobalScript, parts[1]);
        var p = State.Layer(Layers.Umpire).FirstOrDefault(x => x.Guid == parts[0] || x.Id == parts[0]);
        if (p == null)
        {
            host.SayRaw("Xml UI couldn't find Object with guid: " + parts[0], ErrorColor);
            return (null, parts[1]);
        }
        return (host.ScriptOf(p.Id), parts[1]);
    }
}
