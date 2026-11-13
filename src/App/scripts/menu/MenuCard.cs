using System;
using Godot;

namespace KTable.App;

sealed class MenuCard
{
    public readonly PopupPanel Pop = new();
    readonly VBoxContainer _box;
    VBoxContainer _into;
    Node? _host;
    Vector2 _at;
    const string Folded = "▸", Unfolded = "▾";
    readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    const float ScreenEdge = 8;

    public MenuCard(float width = 244)
    {
        _box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _box.AddThemeConstantOverride("separation", Ui.Gap.None);
        _into = _box;
        _scroll.AddChild(_box);
        Pop.AddChild(_scroll);
    }

    static StyleBoxFlat RowStyle(Color bg) => Ui.Box(bg, 3, 8, 4);

    Button Row(string text, string accel, bool? tick)
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 26), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.AddThemeStyleboxOverride("normal", RowStyle(new Color(0, 0, 0, 0)));
        b.AddThemeStyleboxOverride("hover", RowStyle(new Color(1, 1, 1, 0.08f)));
        b.AddThemeStyleboxOverride("pressed", RowStyle(new Color(1, 1, 1, 0.14f)));
        b.AddThemeStyleboxOverride("hover_pressed", RowStyle(new Color(1, 1, 1, 0.14f)));
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        h.OffsetLeft = 8;
        h.OffsetRight = -8;
        h.AddThemeConstantOverride("separation", Ui.Gap.Row);
        if (tick != null)
        {
            var t = new Label { Text = tick.Value ? "✓" : " ", CustomMinimumSize = new Vector2(14, 0) };
            t.AddThemeColorOverride("font_color", Ui.AccentInk);
            h.AddChild(t);
        }
        h.AddChild(new Label
        {
            Text = Fmt.OneLine(text), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MaxLinesVisible = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        if (accel.Length > 0)
        {
            var a = new Label { Text = accel, Modulate = Ui.Muted };
            a.AddThemeFontSizeOverride("font_size", 12);
            h.AddChild(a);
        }
        b.AddChild(h);
        _into.AddChild(b);
        return b;
    }

    public Button Item(string text, Action action, string accel = "")
    {
        var b = Row(text, accel, null);
        b.Pressed += () => { Pop.Hide(); action(); };
        return b;
    }
    public void Check(string text, bool on, Action action, string accel = "")
    {
        var b = Row(text, accel, on);
        b.Pressed += () => { Pop.Hide(); action(); };
    }

    public void Fold(string text, string tip, bool open, Action<bool> toggled, Action rows)
    {
        var head = Row(text, open ? Unfolded : Folded, false);
        head.TooltipText = tip;
        var mark = head.GetChild(0).GetChild<Label>(-1);
        var body = new VBoxContainer { Visible = open };
        body.AddThemeConstantOverride("separation", Ui.Gap.None);
        _into.AddChild(body);
        var outer = _into;
        _into = body;
        rows();
        _into = outer;
        head.Pressed += () =>
        {
            body.Visible = !body.Visible;
            mark.Text = body.Visible ? Unfolded : Folded;
            toggled(body.Visible);
            Callable.From(Refit).CallDeferred();
        };
    }

    public void Fact(string text, string tip)
    {
        var m = new MarginContainer { TooltipText = tip };
        m.AddThemeConstantOverride("margin_left", 8);
        m.AddThemeConstantOverride("margin_right", 8);
        m.AddThemeConstantOverride("margin_bottom", 2);
        m.AddChild(Ui.Hint(text));
        _into.AddChild(m);
    }

    public void Head(string text)
    {
        var l =
            new Label { Text = Fmt.OneLine(text), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MaxLinesVisible = 1,
                CustomMinimumSize = new Vector2(0, 26), VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeColorOverride("font_color", Colors.White);
        l.AddThemeFontOverride("font", Ui.SansStrong());
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 8);
        m.AddThemeConstantOverride("margin_right", 8);
        m.AddChild(l);
        _into.AddChild(m);
    }

    public void Separator(string label = "")
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _into.AddChild(sep);
        if (label.Length == 0) return;
        var l = Ui.HeadLabel(label);
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 8);
        m.AddThemeConstantOverride("margin_bottom", 2);
        m.AddChild(l);
        _into.AddChild(m);
    }

    public void Add(Control c)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 6);
        m.AddThemeConstantOverride("margin_right", 6);
        m.AddThemeConstantOverride("margin_top", 4);
        m.AddThemeConstantOverride("margin_bottom", 4);
        m.AddChild(c);
        _into.AddChild(m);
    }

    bool PressedOutside(InputEvent e) =>
        e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle } press
        && !new Rect2(Vector2.Zero, Pop.Size).HasPoint(press.Position);

    public void Show(Node parent, Vector2 at)
    {
        Pop.PopupHide += () => Pop.QueueFree();
        Pop.WindowInput += e => { if (PressedOutside(e)) Pop.Hide(); };
        parent.AddChild(Pop);
        _host = parent;
        _at = at;
        Pop.Popup(Placed());
    }

    void Refit()
    {
        if (!GodotObject.IsInstanceValid(Pop) || !Pop.Visible) return;
        var placed = Placed();
        Pop.Position = placed.Position;
        Pop.Size = placed.Size;
    }

    Rect2I Placed()
    {
        var view = _host!.GetViewport().GetVisibleRect().Size;
        var frame = Pop.GetThemeStylebox("panel").GetMinimumSize();
        var content = _box.GetCombinedMinimumSize();
        float room = view.Y - frame.Y - 2 * ScreenEdge;
        float bar = content.Y > room ? _scroll.GetVScrollBar().GetCombinedMinimumSize().X : 0;
        _scroll.CustomMinimumSize = new Vector2(content.X + bar, Mathf.Min(content.Y, room));
        var size = _scroll.CustomMinimumSize + frame;
        var pos = new Vector2(
            Mathf.Clamp(_at.X, ScreenEdge, Mathf.Max(ScreenEdge, view.X - size.X - ScreenEdge)),
            Mathf.Clamp(_at.Y, ScreenEdge, Mathf.Max(ScreenEdge, view.Y - size.Y - ScreenEdge)));
        return new Rect2I((Vector2I)pos.Round(), (Vector2I)size.Ceil());
    }
}
