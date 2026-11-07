using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    PanelBox? _pressed;
    const string LeftPointer = "-1", RightPointer = "-2", MiddlePointer = "-3";

    static string Pointer(MouseButton b) => b switch { MouseButton.Right => RightPointer, MouseButton.Middle => MiddlePointer, _ => LeftPointer };

    static bool Interactable(PanelBox owner) => owner.Select?.Interactable ?? true;

    void Fire(PanelBox owner, string ev, string value)
    {
        if (owner.Path == null || owner.Element == null) return;
        if (string.IsNullOrEmpty(owner.Element.Get(ev))) return;
        _send(owner.Path, ev, value);
    }

    void Input(PanelBox hit, InputEvent e)
    {
        if (Owner(hit) is not { } owner) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            if (wheel.Pressed) Scroll(owner, wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
            return;
        }
        if (e is not InputEventMouseButton mb) return;
        Press(hit, owner, mb.ButtonIndex, mb.Pressed, mb.Position);
        Nodes.GetValueOrDefault(hit)?.AcceptEvent();
    }

    public void Press(PanelBox hit, PanelBox owner, MouseButton button, bool down, Vector2 inside)
    {
        var pointer = Pointer(button);
        if (!Interactable(owner)) return;
        if (down)
        {
            _pressed = owner;
            Fire(owner, "onMouseDown", pointer);
            if (owner.Tag == "Slider" && button == MouseButton.Left) Slide(owner, hit, inside);
            return;
        }
        Fire(owner, "onMouseUp", pointer);
        bool clicked = _pressed == owner;
        _pressed = null;
        if (!clicked) return;
        if (button == MouseButton.Left) Act(owner, hit, inside);
        Fire(owner, "onClick", pointer);
    }

    void Act(PanelBox owner, PanelBox hit, Vector2 inside)
    {
        switch (owner.Tag)
        {
            case "Toggle" or "ToggleButton":
                Flip(owner);
                break;
            case "Dropdown":
                Pick(owner);
                break;
            case "InputField":
                Type(owner);
                break;
        }
    }

    void Hover(PanelBox hit, bool entering)
    {
        if (Owner(hit) is not { } owner) return;
        var chain = new List<PanelBox>();
        for (var at = owner; at != null; at = at.Parent) if (at.Element != null) chain.Add(at);
        if (entering)
        {
            foreach (var b in chain.Where(b => !_hovered.Contains(b)).Reverse())
            {
                _hovered.Add(b);
                if (Interactable(b)) Fire(b, "onMouseEnter", LeftPointer);
            }
        }
        else
        {
            foreach (var b in _hovered.ToList())
            {
                _hovered.Remove(b);
                if (Interactable(b)) Fire(b, "onMouseExit", LeftPointer);
            }
        }
        Repaint(owner);
    }

    void Repaint(PanelBox owner)
    {
        if (owner.Select?.TargetGraphic is not { } target) return;
        var graphic = owner.Name == target ? owner : owner.Part(target);
        if (graphic?.Image == null || !Nodes.TryGetValue(graphic, out var c) || c.GetChildCount() == 0) return;
        var tint = Tinted(graphic, ToColor(graphic.Image.Color));
        switch (c.GetChild(0))
        {
            case Panel p when p.GetThemeStylebox("panel") is StyleBoxFlat sb:
                sb.BgColor = tint;
                break;
            case TextureRect tr:
                tr.SelfModulate = tint;
                break;
            case ColorRect cr:
                cr.Color = tint;
                break;
        }
    }

    string Key(PanelBox owner) => PanelScene.Key(owner.Path!);

    void Flip(PanelBox owner)
    {
        var key = Key(owner);
        bool on = PanelValues.Bool(_local.GetValueOrDefault(key) ?? owner.Extra.GetValueOrDefault("ison"));
        var group = Group(owner);
        if (on && group != null && !PanelValues.Bool(group.Extra.GetValueOrDefault("allowswitchoff"))) return;
        _local[key] = (!on).ToString();
        if (group != null && !on)
            foreach (var other in GroupToggles(group).Where(t => t != owner))
                if (PanelValues.Bool(_local.GetValueOrDefault(Key(other)) ?? other.Extra.GetValueOrDefault("ison")))
                {
                    _local[Key(other)] = false.ToString();
                    Fire(other, "onValueChanged", false.ToString());
                }
        Fire(owner, "onValueChanged", (!on).ToString());
        if (group != null && !on) Fire(group, "onValueChanged", GroupToggles(group).IndexOf(owner).ToString(CultureInfo.InvariantCulture));
        _redraw();
    }

    static PanelBox? Group(PanelBox b)
    {
        for (var at = b.Parent; at != null; at = at.Parent) if (at.Tag == "ToggleGroup") return at;
        return null;
    }

    static List<PanelBox> GroupToggles(PanelBox group)
    {
        var list = new List<PanelBox>();
        void Walk(PanelBox b)
        {
            foreach (var c in b.Children)
            {
                if (c.Element != null && c.Tag is "Toggle" or "ToggleButton") list.Add(c);
                else if (c.Tag != "ToggleGroup") Walk(c);
            }
        }
        Walk(group);
        return list;
    }

    void Slide(PanelBox owner, PanelBox hit, Vector2 inside)
    {
        if (!Nodes.TryGetValue(owner, out var c)) return;
        var local = c.GetLocalMousePosition();
        float t = Mathf.Clamp(local.X / Mathf.Max(1, c.Size.X), 0, 1);
        float min = PanelValues.Float(owner.Extra.GetValueOrDefault("minvalue")), max = PanelValues.Float(owner.Extra.GetValueOrDefault("maxvalue") ?? "1");
        float v = min + (max - min) * t;
        if (PanelValues.Bool(owner.Extra.GetValueOrDefault("wholenumbers"))) v = Mathf.Round(v);
        var text = v.ToString(CultureInfo.InvariantCulture);
        if (_local.GetValueOrDefault(Key(owner)) == text) return;
        _local[Key(owner)] = text;
        Fire(owner, "onValueChanged", text);
        _redraw();
    }

    void Pick(PanelBox owner)
    {
        var options = owner.Element?.Options ?? new();
        if (options.Count == 0 || !Nodes.TryGetValue(owner, out var c)) return;
        var menu = new PopupMenu();
        for (int i = 0; i < options.Count; i++) menu.AddItem(options[i].Value ?? "", i);
        menu.IdPressed += id =>
        {
            var was = PanelScene.DropdownIndex(owner, owner.Element!, _local.GetValueOrDefault(Key(owner)));
            if (was != (int)id)
            {
                _local[Key(owner)] = id.ToString(CultureInfo.InvariantCulture);
                Fire(owner, "onValueChanged", id.ToString(CultureInfo.InvariantCulture));
                _redraw();
            }
            menu.QueueFree();
        };
        menu.PopupHide += () => { if (GodotObject.IsInstanceValid(menu)) menu.QueueFree(); };
        c.GetTree().Root.AddChild(menu);
        menu.Popup(new Rect2I((Vector2I)c.GetViewport().GetMousePosition(), Vector2I.Zero));
    }

    void Type(PanelBox owner)
    {
        if (!Nodes.TryGetValue(owner, out var c) || PanelValues.Bool(owner.Extra.GetValueOrDefault("readonly"))) return;
        var key = Key(owner);
        var start = _local.GetValueOrDefault(key) ?? owner.Extra.GetValueOrDefault("text") ?? "";
        bool lines = (owner.Extra.GetValueOrDefault("linetype") ?? "SingleLine") != "SingleLine";
        int limit = PanelValues.Int(owner.Extra.GetValueOrDefault("characterlimit"));
        var mode = owner.Extra.GetValueOrDefault("charactervalidation") ?? "None";
        var dlg = new ConfirmationDialog { Title = "Text", OkButtonText = "OK" };
        Control field = lines ? new TextEdit { Text = start, CustomMinimumSize = new Vector2(420, 160) } : new LineEdit { Text = start, CustomMinimumSize = new Vector2(320, 0), MaxLength = limit };
        void Changed(string typed)
        {
            var ok = PanelTyping.Valid(typed, mode);
            if (limit > 0 && ok.Length > limit) ok = ok[..limit];
            if (_local.GetValueOrDefault(key) == ok) return;
            _local[key] = ok;
            Fire(owner, "onValueChanged", ok);
        }
        if (field is LineEdit le)
        {
            le.TextChanged += Changed;
            le.TextSubmitted += _ => { Fire(owner, "onSubmit", le.Text); dlg.EmitSignal(AcceptDialog.SignalName.Confirmed); };
        }
        else ((TextEdit)field).TextChanged += () => Changed(((TextEdit)field).Text);
        dlg.AddChild(field);
        dlg.Confirmed += () => { Fire(owner, "onEndEdit", _local.GetValueOrDefault(key) ?? start); _redraw(); };
        dlg.Canceled += () => { Fire(owner, "onEndEdit", _local.GetValueOrDefault(key) ?? start); _redraw(); };
        dlg.VisibilityChanged += () => { if (!dlg.Visible) dlg.QueueFree(); };
        c.GetTree().Root.AddChild(dlg);
        dlg.PopupCentered();
        field.GrabFocus();
    }

    void Scroll(PanelBox owner, int notches)
    {
        for (var at = owner; at != null; at = at.Parent)
        {
            if (at.Tag is not ("VerticalScrollView" or "HorizontalScrollView") || at.Part("Content") is not { } content || !Nodes.TryGetValue(content, out var c)) continue;
            bool vertical = at.Tag == "VerticalScrollView";
            const float Notch = 40;
            var view = Nodes.GetValueOrDefault(at.Part("Viewport")!);
            float room = vertical ? Math.Max(0, c.Size.Y - (view?.Size.Y ?? 0)) : Math.Max(0, c.Size.X - (view?.Size.X ?? 0));
            if (vertical) c.Position = c.Position with { Y = Math.Clamp(c.Position.Y + notches * Notch, -room, 0) };
            else c.Position = c.Position with { X = Math.Clamp(c.Position.X + notches * Notch, -room, 0) };
            return;
        }
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

    public void Click(PanelBox hit, Vector2 inside)
    {
        if (Owner(hit) is not { } owner) return;
        Press(hit, owner, MouseButton.Left, true, inside);
        Press(hit, owner, MouseButton.Left, false, inside);
    }
}
