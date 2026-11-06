using System;
using Godot;

namespace KTable.App;

sealed class MenuCard
{
    public readonly PopupPanel Pop = new();
    readonly VBoxContainer _box;
    readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    const float ScreenEdge = 8;

    public MenuCard(float width = 244)
    {
        _box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _box.AddThemeConstantOverride("separation", Ui.Gap.None);
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
        _box.AddChild(b);
        return b;
    }

    public void Item(string text, Action action, string accel = "")
    {
        var b = Row(text, accel, null);
        b.Pressed += () => { Pop.Hide(); action(); };
    }
    public void Check(string text, bool on, Action action, string accel = "")
    {
        var b = Row(text, accel, on);
        b.Pressed += () => { Pop.Hide(); action(); };
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
        _box.AddChild(m);
    }

    public void Separator(string label = "")
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", Ui.Gap.Row);
        _box.AddChild(sep);
        if (label.Length == 0) return;
        var l = Ui.HeadLabel(label);
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 8);
        m.AddThemeConstantOverride("margin_bottom", 2);
        m.AddChild(l);
        _box.AddChild(m);
    }

    public void Add(Control c)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 6);
        m.AddThemeConstantOverride("margin_right", 6);
        m.AddThemeConstantOverride("margin_top", 4);
        m.AddThemeConstantOverride("margin_bottom", 4);
        m.AddChild(c);
        _box.AddChild(m);
    }

    public void Show(Node parent, Vector2 at)
    {
        Pop.PopupHide += () => Pop.QueueFree();
        parent.AddChild(Pop);
        var view = parent.GetViewport().GetVisibleRect().Size;
        var frame = Pop.GetThemeStylebox("panel").GetMinimumSize();
        var content = _box.GetCombinedMinimumSize();
        float room = view.Y - frame.Y - 2 * ScreenEdge;
        float bar = content.Y > room ? _scroll.GetVScrollBar().GetCombinedMinimumSize().X : 0;
        _scroll.CustomMinimumSize = new Vector2(content.X + bar, Mathf.Min(content.Y, room));
        var size = _scroll.CustomMinimumSize + frame;
        var pos = new Vector2(
            Mathf.Clamp(at.X, ScreenEdge, Mathf.Max(ScreenEdge, view.X - size.X - ScreenEdge)),
            Mathf.Clamp(at.Y, ScreenEdge, Mathf.Max(ScreenEdge, view.Y - size.Y - ScreenEdge)));
        Pop.Popup(new Rect2I((Vector2I)pos.Round(), (Vector2I)size.Ceil()));
    }
}
