using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    [Flags]
    enum Way { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }

    static Way Navigation(PanelBox b)
    {
        var s = b.Extra.GetValueOrDefault("navigation");
        if (s != null && int.TryParse(s, out var n) && n is >= 0 and <= 4) return (Way)n;
        return PanelValues.Enum<Way>(s) ?? Way.Automatic;
    }

    void Select(PanelBox owner)
    {
        if (owner.Select == null || owner.Path == null || !Interactable(owner) || Navigation(owner) == Way.None || _selected == owner) return;
        if (_selected != null) Deselect();
        PanelFocus.Take(this, _local, Redraw);
        _selected = owner;
        _local[PanelFocus.Selected] = Key(owner);
        Repaint(owner);
        if (PanelClasses.Change(_local, owner, PanelClasses.Select, true)) Redraw();
        if (owner.Tag == "InputField") Edit(owner);
    }

    void Deselect()
    {
        if (_selected is not { } was) return;
        EndEdit(false);
        _selected = null;
        _local.Remove(PanelFocus.Selected);
        _local.Remove(PanelFocus.Editing);
        Repaint(was);
        if (PanelClasses.Change(_local, was, PanelClasses.Select, false)) Redraw();
    }

    public void Deselected()
    {
        if (_selected is not { } was) return;
        _selected = null;
        Repaint(was);
        if (PanelClasses.Change(_local, was, PanelClasses.Select, false)) Redraw();
    }

    public bool Holds(Vector2 screen)
    {
        if (_list != null) return true;
        if (_hooks.Port != null) return _selected != null && _hooks.PlatePixel(screen) is { } pixel && Hit(pixel) is { } hit && Owner(hit) == _selected;
        return _selected != null && Nodes.TryGetValue(_selected, out var c) && GodotObject.IsInstanceValid(c) && c.GetGlobalRect().HasPoint(screen);
    }

    bool Alive(PanelBox b) => Nodes.TryGetValue(b, out var c) && GodotObject.IsInstanceValid(c) && c.IsVisibleInTree();

    public bool Key(InputEventKey e)
    {
        if (_selected == null || !Alive(_selected) || !e.Pressed) return false;
        if (e.Keycode == Godot.Key.Tab)
        {
            SelectNext(Find(_selected, e.ShiftPressed ? new Vector2(0, -1) : new Vector2(0, 1), e.ShiftPressed ? "selectonup" : "selectondown"));
            return true;
        }
        if (_editor != null) return Typing(e);
        if (e.Keycode is Godot.Key.Enter or Godot.Key.KpEnter or Godot.Key.Space && !e.Echo)
        {
            Submit(_selected, e.Keycode == Godot.Key.Space);
            return false;
        }
        if (Move(e.Keycode) is { } dir) Step(_selected, dir);
        return false;
    }

    static Vector2? Move(Key k) => k switch
    {
        Godot.Key.Left or Godot.Key.A => new Vector2(-1, 0),
        Godot.Key.Right or Godot.Key.D => new Vector2(1, 0),
        Godot.Key.Up or Godot.Key.W => new Vector2(0, -1),
        Godot.Key.Down or Godot.Key.S => new Vector2(0, 1),
        _ => null,
    };

    void Submit(PanelBox b, bool space)
    {
        switch (b.Tag)
        {
            case "Toggle" or "ToggleButton":
                Flip(b);
                break;
            case "Dropdown" or PanelLists.Textless:
                OpenList(b);
                break;
            case "InputField":
                Edit(b);
                break;
        }
        bool submits = !string.IsNullOrEmpty(b.Element?.Get("onSubmit"));
        if (submits)
        {
            if (!(b.Tag == "InputField" && space)) Fire(b, "onSubmit", Value(b));
            return;
        }
        Sound(b, ClickSound);
        Fire(b, "onClick", LeftPointer);
        if (b.Tag != "InputField" && _selected == b) Deselect();
    }

    string Value(PanelBox b) => _local.GetValueOrDefault(Key(b)) ?? b.Extra.GetValueOrDefault("text") ?? "";

    void Step(PanelBox b, Vector2 dir)
    {
        var key = dir.X < 0 ? "selectonleft" : dir.X > 0 ? "selectonright" : dir.Y < 0 ? "selectonup" : "selectondown";
        var next = Find(b, dir, key);
        if (b.Tag == "Slider" && next == null && Nudge(b, dir)) return;
        SelectNext(next);
    }

    const float GameUnitsPerOurs = 10;

    Vector3 Unity(Vector2 pixel)
    {
        if (_hooks.PlateWorld(pixel) is { } w) return new Vector3(w.X, w.Y, -w.Z) * GameUnitsPerOurs;
        float height = _top != null && GodotObject.IsInstanceValid(_top) && _top.IsInsideTree() ? _top.GetViewport().GetVisibleRect().Size.Y : 0;
        return new Vector3(pixel.X, height - pixel.Y, 0);
    }

    bool Selectable(PanelBox b, Control c) =>
        b.Select != null && b.Path != null && Interactable(b) && Navigation(b) != Way.None && GodotObject.IsInstanceValid(c) && c.IsVisibleInTree();

    (PanelView View, PanelBox Box)? Find(PanelBox from, Vector2 dir, string explicitKey)
    {
        var way = Navigation(from);
        if (way == Way.Explicit) return from.Extra.GetValueOrDefault(explicitKey) is { } id && _scene?.ById(id) is { Select: not null } b && Alive(b) ? (this, b) : null;
        bool along = dir.X != 0 ? (way & Way.Horizontal) != 0 : (way & Way.Vertical) != 0;
        if (!along || !Nodes.TryGetValue(from, out var fc)) return null;
        var rect = fc.GetGlobalRect();
        var center = Unity(rect.GetCenter());
        var edge = Unity(rect.GetCenter() + rect.Size * dir * 0.5f);
        var toward = (edge - center).Normalized();
        (PanelView, PanelBox)? best = null;
        float score = float.NegativeInfinity;
        foreach (var view in PanelFocus.Views())
            foreach (var (b, c) in view.Nodes)
            {
                if (b == from || !view.Selectable(b, c)) continue;
                var to = view.Unity(c.GetGlobalRect().GetCenter()) - edge;
                float dot = toward.Dot(to);
                if (dot <= 0) continue;
                float s = dot / Mathf.Max(1e-6f, to.LengthSquared());
                if (s <= score) continue;
                score = s;
                best = (view, b);
            }
        return best;
    }

    void SelectNext((PanelView View, PanelBox Box)? next)
    {
        if (next is not { } n) return;
        if (n.View != this) Deselect();
        n.View.Select(n.Box);
    }

    bool Nudge(PanelBox b, Vector2 dir)
    {
        int way = PanelValues.Enum<SliderWay>(b.Extra.GetValueOrDefault("direction")) is { } d ? (int)d : 0;
        bool horizontal = way < 2;
        if (horizontal != (dir.X != 0)) return false;
        float min = PanelValues.Float(b.Extra.GetValueOrDefault("minvalue")), max = PanelValues.Float(b.Extra.GetValueOrDefault("maxvalue") ?? "1");
        bool whole = PanelValues.Bool(b.Extra.GetValueOrDefault("wholenumbers"));
        float step = whole ? 1 : (max - min) * Tenth;
        bool reverse = way is 1 or 3;
        float sign = (horizontal ? dir.X : -dir.Y) * (reverse ? -1 : 1);
        float now = PanelValues.Float(_local.GetValueOrDefault(Key(b)) ?? b.Extra.GetValueOrDefault("value"));
        SetSlider(b, now + sign * step);
        return true;
    }

    const float Tenth = 0.1f;
}
