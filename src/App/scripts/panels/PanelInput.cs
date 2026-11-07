using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    PanelBox? _pressed, _selected;
    const string LeftPointer = "-1", RightPointer = "-2", MiddlePointer = "-3";
    const string ClickSound = "onclicksound", EnterSound = "onmouseentersound", ExitSound = "onmouseexitsound";

    static string Pointer(MouseButton b) => b switch { MouseButton.Right => RightPointer, MouseButton.Middle => MiddlePointer, _ => LeftPointer };

    static bool Interactable(PanelBox owner) => owner.Select?.Interactable ?? true;

    void Fire(PanelBox owner, string ev, string value)
    {
        if (owner.Path == null || owner.Element == null) return;
        if (string.IsNullOrEmpty(owner.Element.Get(ev))) return;
        _hooks.Send(owner.Path, ev, value);
    }

    void Sound(PanelBox owner, string key)
    {
        if (owner.Path == null || owner.Extra.GetValueOrDefault(key) is not { Length: > 0 } name || PanelValues.IsNone(name)) return;
        float volume = owner.Extra.TryGetValue("audiovolume", out var v) ? PanelValues.Float(v) : 1;
        _hooks.Sound(name, volume);
    }

    void Input(PanelBox hit, InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight } wheel)
        {
            if (wheel.Pressed) Wheel(hit, wheel);
            Accept(hit);
            return;
        }
        if (e is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            Motion(motion.GlobalPosition);
            return;
        }
        if (e is not InputEventMouseButton mb) return;
        var owner = Owner(hit);
        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed) Grab(hit, mb.GlobalPosition);
            else if (Release())
            {
                var was = _pressed;
                _pressed = null;
                if (was != null) Repaint(was);
                Accept(hit);
                return;
            }
        }
        if (owner != null) Press(hit, owner, mb.ButtonIndex, mb.Pressed);
        Accept(hit);
    }

    void Accept(PanelBox hit)
    {
        if (Nodes.GetValueOrDefault(hit) is { } c && GodotObject.IsInstanceValid(c)) c.AcceptEvent();
    }

    public void Press(PanelBox hit, PanelBox owner, MouseButton button, bool down)
    {
        var pointer = Pointer(button);
        if (owner.Tag == PanelLists.Item)
        {
            Choose(owner, down);
            return;
        }
        if (down)
        {
            if (!Interactable(owner)) return;
            _pressed = owner;
            if (button == MouseButton.Left) Select(owner);
            Repaint(owner);
            bool pressClassed = PanelClasses.Change(_local, owner, PanelClasses.Press, true);
            Fire(owner, "onMouseDown", pointer);
            if (pressClassed) Redraw();
            return;
        }
        bool clicked = _pressed == owner;
        _pressed = null;
        Repaint(owner);
        bool released = PanelClasses.Change(_local, owner, PanelClasses.Press, false);
        if (Interactable(owner)) Fire(owner, "onMouseUp", pointer);
        if (released) Redraw();
        if (!clicked) return;
        Sound(owner, ClickSound);
        if (button == MouseButton.Left && Interactable(owner)) Act(owner);
        Fire(owner, "onClick", pointer);
        if (owner.Tag != "InputField" && _selected == owner) Deselect();
    }

    void Act(PanelBox owner)
    {
        switch (owner.Tag)
        {
            case "Toggle" or "ToggleButton":
                Flip(owner);
                break;
            case "Dropdown":
                OpenList(owner);
                break;
            case PanelAccordions.Header:
                Section(owner);
                break;
        }
    }

    public void Announce(PanelMove m)
    {
        if (_scene?.At(m.Path) is not { } box) return;
        Announce(box, m.Showing ? "onshowsound" : "onhidesound");
    }

    void Announce(PanelBox b, string key)
    {
        if (b.Element != null && !b.Active) return;
        if (b.Element != null) Sound(b, key);
        foreach (var c in b.Children) Announce(c, key);
    }

    int _hoverStamp;
    const string HoveredKey = "#hovered";

    void Hover(PanelBox hit, bool entering)
    {
        int stamp = ++_hoverStamp;
        if (entering)
        {
            Track(Chain(hit));
            return;
        }
        Callable.From(() =>
        {
            if (stamp == _hoverStamp && _top != null && GodotObject.IsInstanceValid(_top)) Track(new List<PanelBox>());
        }).CallDeferred();
    }

    static List<PanelBox> Chain(PanelBox hit)
    {
        var chain = new List<PanelBox>();
        for (var at = Owner(hit); at != null; at = at.Parent) if (at.Element != null) chain.Add(at);
        return chain;
    }

    void Track(List<PanelBox> chain)
    {
        bool changed = false;
        foreach (var b in _hovered.Where(b => !chain.Contains(b)).ToList()) changed |= Left(b);
        foreach (var b in chain.Where(b => !_hovered.Contains(b))) changed |= Entered(b);
        _local[HoveredKey] = string.Join(" ", _hovered.Where(b => b.Path != null).Select(b => Key(b)));
        if (changed) Redraw();
    }

    void RestoreHover(PanelScene scene)
    {
        if (_local.GetValueOrDefault(HoveredKey) is not { Length: > 0 } keys) return;
        var wanted = keys.Split(' ').ToHashSet();
        var boxes = scene.Elements.Where(e => e.Path != null && wanted.Contains(PanelScene.Key(e.Path))).ToList();
        bool changed = false;
        foreach (var b in boxes)
        {
            _hovered.Add(b);
            if (!b.ActiveInHierarchy) changed |= Left(b);
        }
        _local[HoveredKey] = string.Join(" ", _hovered.Where(b => b.Path != null).Select(b => Key(b)));
        if (changed) Redraw();
    }

    bool Entered(PanelBox b)
    {
        _hovered.Add(b);
        Repaint(b);
        if (!Interactable(b)) return false;
        Sound(b, EnterSound);
        bool classed = PanelClasses.Change(_local, b, PanelClasses.Hover, true);
        Fire(b, "onMouseEnter", LeftPointer);
        PanelCursors.Enter(b, _hooks.CursorPicture);
        return classed;
    }

    bool Left(PanelBox b)
    {
        _hovered.Remove(b);
        Repaint(b);
        Sound(b, ExitSound);
        bool classed = PanelClasses.Change(_local, b, PanelClasses.Hover, false);
        if (Interactable(b)) Fire(b, "onMouseExit", LeftPointer);
        return classed;
    }

    void Repaint(PanelBox owner)
    {
        if (owner.Select?.TargetGraphic is not { } target) return;
        var graphic = owner.Name == target ? owner : owner.Part(target);
        if (graphic?.Image == null || !Nodes.TryGetValue(graphic, out var c) || !GodotObject.IsInstanceValid(c) || c.GetChildCount() == 0 || c.GetChild(0) is not Control old) return;
        var fresh = Paint(graphic);
        c.AddChild(fresh);
        c.MoveChild(fresh, 0);
        float fade = owner.Select.Colors.Fade;
        if (fade <= 0 || owner.Select.Transition != 1)
        {
            old.QueueFree();
            return;
        }
        fresh.Modulate = Colors.White with { A = 0 };
        var tween = c.CreateTween();
        tween.TweenProperty(fresh, "modulate:a", 1f, fade);
        tween.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(old)) old.QueueFree();
        }));
    }

    static string Key(PanelBox owner) => PanelScene.Key(owner.Path!);

    void Flip(PanelBox owner)
    {
        var key = Key(owner);
        bool on = PanelValues.Bool(_local.GetValueOrDefault(key) ?? owner.Extra.GetValueOrDefault("ison"));
        var group = Group(owner);
        if (on && group != null && !PanelValues.Bool(group.Extra.GetValueOrDefault("allowswitchoff"))) return;
        _local[key] = (!on).ToString();
        if (group != null && !on)
            foreach (var other in PanelScene.Toggles(group).Where(t => t != owner))
                if (PanelValues.Bool(_local.GetValueOrDefault(Key(other)) ?? other.Extra.GetValueOrDefault("ison")))
                {
                    _local[Key(other)] = false.ToString();
                    Fire(other, "onValueChanged", false.ToString());
                }
        Fire(owner, "onValueChanged", (!on).ToString());
        if (group != null && !on) Fire(group, "onValueChanged", PanelScene.Toggles(group).ToList().IndexOf(owner).ToString(CultureInfo.InvariantCulture));
        Redraw();
    }

    static PanelBox? Group(PanelBox b)
    {
        for (var at = b.Parent; at != null; at = at.Parent) if (at.Tag == "ToggleGroup") return at;
        return null;
    }

    public PanelBox? Hit(Vector2 pixel)
    {
        PanelBox? best = null;
        foreach (var (b, c) in Nodes)
        {
            if (!Catches(b) || !GodotObject.IsInstanceValid(c) || !c.IsVisibleInTree()) continue;
            if (c.GetGlobalRect().HasPoint(pixel)) best = b;
        }
        return best;
    }

    static bool Within(PanelBox b, PanelBox top)
    {
        for (var at = b; at != null; at = at.Parent) if (at == top) return true;
        return false;
    }

    public void Click(PanelBox hit, Vector2 at)
    {
        if (_list != null && !Within(hit, _list))
        {
            CloseList();
            return;
        }
        if (Owner(hit) is not { } owner) return;
        Grab(hit, at);
        Press(hit, owner, MouseButton.Left, true);
        Release();
        Press(hit, owner, MouseButton.Left, false);
    }
}
