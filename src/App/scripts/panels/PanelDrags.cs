using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    enum Pull { Element, Slider, Bar, Scroll }

    sealed class DragState(Pull kind, PanelBox box, Control node, Vector2 from)
    {
        public Pull Kind { get; } = kind;
        public PanelBox Box { get; } = box;
        public Control Node { get; } = node;
        public Vector2 From { get; } = from;
        public Vector2 Home { get; init; }
        public V2 Grip { get; init; }
        public bool Moved { get; set; }
    }

    DragState? _drag;
    const float DragThreshold = 5;
    readonly Dictionary<PanelBox, PanelScrollMotion> _motions = new();
    ulong _lastStep;
    bool _stepping;
    const float Micro = 1e-6f, PerSecond = 1e-6f;

    static bool Draggable(PanelBox owner) => PanelValues.Bool(owner.Extra.GetValueOrDefault("allowdragging"));

    static bool IsView(PanelBox b) => b.Tag is "VerticalScrollView" or "HorizontalScrollView" && b.Part("Viewport")?.Part("Content") != null;

    static bool IsBar(PanelBox b) => b.Name.StartsWith("Scrollbar", StringComparison.Ordinal) && b.Part("Sliding Area") != null;

    void Grab(PanelBox hit, Vector2 at)
    {
        PanelCursors.Pressed(true);
        _drag = null;
        for (var b = hit; b != null; b = b.Parent)
        {
            if (!Nodes.TryGetValue(b, out var node)) continue;
            if (b.Element != null && b.Tag == "Slider" && Interactable(b))
            {
                _drag = new DragState(Pull.Slider, b, node, at) { Grip = SliderGrip(b, hit, at) };
                Slide(b, at);
                return;
            }
            if (IsBar(b) && b.Parent != null && IsView(b.Parent))
            {
                _drag = new DragState(Pull.Bar, b, node, at) { Grip = BarGrip(b, hit, at) };
                if (hit.Name != "Handle") DragBar(b, at, V2.Zero);
                return;
            }
            if (b.Element != null && Draggable(b))
            {
                _drag = new DragState(Pull.Element, b, node, at) { Home = node.Position };
                return;
            }
            if (b.Tag == "InputField" && b.Element != null) return;
            if (IsView(b))
            {
                _drag = new DragState(Pull.Scroll, b, node, at);
                return;
            }
        }
    }

    void Motion(Vector2 at)
    {
        if (_drag is not { } d || !GodotObject.IsInstanceValid(d.Node)) return;
        if (!d.Moved && (at - d.From).Length() < DragThreshold) return;
        bool first = !d.Moved;
        d.Moved = true;
        switch (d.Kind)
        {
            case Pull.Slider:
                Slide(d.Box, at);
                break;
            case Pull.Bar:
                DragBar(d.Box, at, d.Grip);
                break;
            case Pull.Element:
                DragElement(d, at, first);
                break;
            case Pull.Scroll:
                DragView(d, at, first);
                break;
        }
    }

    bool Release()
    {
        PanelCursors.Pressed(false);
        if (_slid)
        {
            _slid = false;
            Redraw();
        }
        if (_drag is not { } d) return false;
        _drag = null;
        if (!d.Moved) return false;
        if (d.Kind == Pull.Element) EndDrag(d);
        if (d.Kind == Pull.Scroll && _motions.TryGetValue(d.Box, out var m))
        {
            m.EndDrag();
            Run();
        }
        return true;
    }

    void DragElement(DragState d, Vector2 at, bool first)
    {
        if (first) Fire(d.Box, "onBeginDrag", "");
        var scale = d.Node.GetGlobalTransform().Scale;
        var to = d.Home + (at - d.From) / new Vector2(Mathf.Max(Micro, Mathf.Abs(scale.X)), Mathf.Max(Micro, Mathf.Abs(scale.Y)));
        bool inside = !d.Box.Extra.TryGetValue("restrictdraggingtoparentbounds", out var r) || PanelValues.Bool(r);
        if (inside && d.Node.GetParentControl() is { } parent)
            to = new Vector2(Mathf.Clamp(to.X, 0, Mathf.Max(0, parent.Size.X - d.Node.Size.X)), Mathf.Clamp(to.Y, 0, Mathf.Max(0, parent.Size.Y - d.Node.Size.Y)));
        d.Node.Position = to;
        Fire(d.Box, "onDrag", "");
    }

    void EndDrag(DragState d)
    {
        bool home = !d.Box.Extra.TryGetValue("returntooriginalpositionwhenreleased", out var r) || PanelValues.Bool(r);
        if (home && GodotObject.IsInstanceValid(d.Node)) d.Node.Position = d.Home;
        Fire(d.Box, "onEndDrag", "");
    }

    V2 Local(PanelBox b, Vector2 at)
    {
        if (!Nodes.TryGetValue(b, out var c) || !GodotObject.IsInstanceValid(c)) return V2.Zero;
        var p = c.GetGlobalTransform().AffineInverse() * at;
        return new V2(p.X + b.RectMin.X, b.RectMin.Y + b.RectSize.Y - p.Y);
    }

    V2 SliderGrip(PanelBox slider, PanelBox hit, Vector2 at)
    {
        if (hit.Name != "Handle" || slider.Part("Handle") is not { } handle || handle.Parent is not { } area) return V2.Zero;
        var center = handle.PivotAt + handle.RectMin + handle.RectSize * 0.5f;
        return Local(area, at) - center;
    }

    void Slide(PanelBox owner, Vector2 at)
    {
        var area = owner.Part("Handle Slide Area") ?? owner;
        var local = Local(area, at) - (_drag?.Kind == Pull.Slider ? _drag.Grip : V2.Zero);
        int dir = PanelValues.Enum<SliderWay>(owner.Extra.GetValueOrDefault("direction")) is { } d ? (int)d : 0;
        int axis = dir < 2 ? 0 : 1;
        float t = Mathf.Clamp((local[axis] - area.RectMin[axis]) / Mathf.Max(1, area.RectSize[axis]), 0, 1);
        if (dir is 1 or 3) t = 1 - t;
        float min = PanelValues.Float(owner.Extra.GetValueOrDefault("minvalue")), max = PanelValues.Float(owner.Extra.GetValueOrDefault("maxvalue") ?? "1");
        SetSlider(owner, min + (max - min) * t);
    }

    void SetSlider(PanelBox owner, float v)
    {
        float min = PanelValues.Float(owner.Extra.GetValueOrDefault("minvalue")), max = PanelValues.Float(owner.Extra.GetValueOrDefault("maxvalue") ?? "1");
        v = Mathf.Clamp(v, Mathf.Min(min, max), Mathf.Max(min, max));
        if (PanelValues.Bool(owner.Extra.GetValueOrDefault("wholenumbers"))) v = Mathf.Round(v);
        var text = PanelValues.FloatText(v);
        if ((_local.GetValueOrDefault(Key(owner)) ?? owner.Extra.GetValueOrDefault("value")) == text) return;
        _local[Key(owner)] = text;
        Fire(owner, "onValueChanged", text);
        if (_drag?.Kind != Pull.Slider)
        {
            Redraw();
            return;
        }
        PanelScene.SliderAt(owner, text);
        Refit(owner);
        _slid = true;
    }

    bool _slid;

    enum SliderWay { LeftToRight, RightToLeft, BottomToTop, TopToBottom }

    V2 BarGrip(PanelBox bar, PanelBox hit, Vector2 at)
    {
        if (hit.Name != "Handle" || bar.Part("Handle") is not { } handle || handle.Parent is not { } area) return V2.Zero;
        var center = handle.PivotAt + handle.RectMin + handle.RectSize * 0.5f;
        return Local(area, at) - center;
    }

    void DragBar(PanelBox bar, Vector2 at, V2 grip)
    {
        if (bar.Parent is not { } view || bar.Part("Sliding Area") is not { } area) return;
        int axis = bar.Name.EndsWith("Horizontal", StringComparison.Ordinal) ? 0 : 1;
        float value = PanelScrollMotion.BarValue(bar, axis, Local(area, at), grip);
        if (value < 0) return;
        Scroll(view, PanelScrollMotion.FromBar(view, axis, value, view.Scrolled));
    }

    void DragView(DragState d, Vector2 at, bool first)
    {
        var port = d.Box.Part("Viewport")!;
        var m = Motion(d.Box);
        if (first) m.BeginDrag(Local(port, d.From));
        m.Drag(Local(port, at));
        Scroll(d.Box, m.Offset);
        Run();
    }

    PanelScrollMotion Motion(PanelBox view)
    {
        if (!_motions.TryGetValue(view, out var m))
        {
            m = new PanelScrollMotion(view, view.Scrolled);
            _motions[view] = m;
        }
        return m;
    }

    void Wheel(PanelBox hit, InputEventMouseButton e)
    {
        float k = e.Factor > 0 ? e.Factor : 1;
        var delta = e.ButtonIndex switch { MouseButton.WheelUp => new V2(0, k), MouseButton.WheelDown => new V2(0, -k), MouseButton.WheelLeft => new V2(-k, 0), _ => new V2(k, 0) };
        for (var b = hit; b != null; b = b.Parent)
        {
            if (!IsView(b)) continue;
            var m = Motion(b);
            m.Wheel(delta);
            Scroll(b, m.Offset);
            Run();
            return;
        }
    }

    void Scroll(PanelBox view, V2 to)
    {
        var now = PanelScrolls.Move(view, to);
        if (_motions.TryGetValue(view, out var m)) m.Offset = now;
        if (view.Path != null) _local[PanelScrolls.Key(view)] = PanelScrolls.Text(now);
        Refit(view);
        if (_hooks.Port is { } port && GodotObject.IsInstanceValid(port)) port.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    void Run()
    {
        if (_stepping || _top == null || !GodotObject.IsInstanceValid(_top) || !_top.IsInsideTree()) return;
        _stepping = true;
        _lastStep = Time.GetTicksUsec();
        _top.GetTree().ProcessFrame += Frame;
    }

    void Frame()
    {
        bool alive = _top != null && GodotObject.IsInstanceValid(_top) && _top.IsInsideTree();
        var now = Time.GetTicksUsec();
        float dt = (now - _lastStep) * PerSecond;
        _lastStep = now;
        bool moving = false;
        if (alive)
            foreach (var (view, m) in _motions)
            {
                if (!m.Moving) continue;
                var was = m.Offset;
                m.Step(dt);
                if (m.Offset != was) Scroll(view, m.Offset);
                moving |= m.Moving;
            }
        if (moving) return;
        _stepping = false;
        if (Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame -= Frame;
    }
}
