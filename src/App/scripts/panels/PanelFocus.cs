using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

static class PanelFocus
{
    public const string Selected = "#selected", Editing = "#editing", Caret = "#caret";
    static PanelView? _view;
    static Dictionary<string, string>? _local;
    static Action? _redraw;

    public static PanelView? View => _view;
    static readonly List<WeakReference<PanelView>> Surfaces = new();

    public static void Showing(PanelView view)
    {
        Surfaces.RemoveAll(w => !w.TryGetTarget(out var v) || !v.Live);
        Surfaces.Add(new WeakReference<PanelView>(view));
    }

    public static IEnumerable<PanelView> Views()
    {
        foreach (var w in Surfaces.ToArray())
            if (w.TryGetTarget(out var v) && v.Live) yield return v;
    }

    public static void Take(PanelView view, Dictionary<string, string> local, Action redraw)
    {
        if (_local != null && !ReferenceEquals(_local, local) && _local.Remove(Selected))
        {
            _view?.EndEdit(false);
            _local.Remove(Editing);
            _redraw?.Invoke();
        }
        _view = view;
        _local = local;
        _redraw = redraw;
    }

    public static void Shown(PanelView view, Dictionary<string, string> local)
    {
        if (ReferenceEquals(local, _local)) _view = view;
    }

    public static void Drop()
    {
        if (_local == null || !_local.ContainsKey(Selected)) return;
        _view?.EndEdit(false);
        _local.Remove(Selected);
        _local.Remove(Editing);
        _view?.Deselected();
        _view = null;
    }

    public static void Forget(Dictionary<string, string> local)
    {
        if (!ReferenceEquals(local, _local)) return;
        _view = null;
        _local = null;
        _redraw = null;
    }

    public static bool Key(InputEventKey e) => _view != null && _view.Key(e);

    public static void Pressed(Vector2 screen)
    {
        if (_view == null || _view.Holds(screen)) return;
        Drop();
    }

    public static bool Holds(PanelBox b, Dictionary<string, string> local) => b.Path != null && local.GetValueOrDefault(Selected) == PanelScene.Key(b.Path);
}

static class PanelCursors
{
    const int Largest = 256;
    static Texture2D? _default, _click;
    static bool _down, _custom;

    public static void Enter(PanelBox b, Func<string, Texture2D?> picture)
    {
        if (Cursor(b.Extra.GetValueOrDefault("cursor"), picture) is { } c)
        {
            _default = c.Texture;
            DefaultSpot = c.Spot;
        }
        if (Cursor(b.Extra.GetValueOrDefault("cursorclick"), picture) is { } k)
        {
            _click = k.Texture;
            ClickSpot = k.Spot;
        }
        Apply();
    }

    static Vector2 DefaultSpot, ClickSpot;

    static (Texture2D Texture, Vector2 Spot)? Cursor(string? value, Func<string, Texture2D?> picture)
    {
        if (string.IsNullOrEmpty(value) || PanelValues.IsNone(value)) return null;
        var parts = value.Split('|');
        if (picture(parts[0]) is not { } tex || tex.GetWidth() > Largest || tex.GetHeight() > Largest) return null;
        var spot = parts.Length > 1 ? PanelValues.Vector2(parts[1]) : V2.Zero;
        return (tex, new Vector2(spot.X, spot.Y));
    }

    public static void Pressed(bool down)
    {
        if (_down == down) return;
        _down = down;
        Apply();
    }

    static void Apply()
    {
        var tex = _down ? _click ?? _default : _default;
        if (tex == null) return;
        Input.SetCustomMouseCursor(tex, Input.CursorShape.Arrow, _down && _click != null ? ClickSpot : DefaultSpot);
        _custom = true;
    }

    public static void Reset()
    {
        _default = null;
        _click = null;
        _down = false;
        if (!_custom) return;
        _custom = false;
        Input.SetCustomMouseCursor(null);
    }
}
