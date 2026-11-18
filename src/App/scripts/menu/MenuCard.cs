using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

readonly record struct MenuTick(string Text, bool On, Action Action, string Tip);

sealed class MenuCard
{
    public readonly PopupPanel Pop = new();
    readonly VBoxContainer _box;
    readonly VBoxContainer _main = Rows();
    readonly List<VBoxContainer> _pages = new();
    VBoxContainer _into;
    Node? _host;
    Vector2 _at;
    const string Folded = "▸", Unfolded = "▾", Forward = "›", Back = "‹";
    readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    const float ScreenEdge = 8;

    public MenuCard(float width = 244)
    {
        _box = new VBoxContainer { CustomMinimumSize = Ui.Px(width, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _box.AddThemeConstantOverride("separation", Ui.Gap.None);
        _box.AddChild(_main);
        _into = _main;
        _scroll.AddChild(_box);
        Pop.AddChild(_scroll);
    }

    static VBoxContainer Rows()
    {
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", Ui.Gap.None);
        return v;
    }

    public void Page(string text, Action rows)
    {
        var open = Row(text, Forward, null);
        var page = Rows();
        page.Visible = false;
        _box.AddChild(page);
        _pages.Add(page);
        var outer = _into;
        _into = page;
        var back = Row($"{Back}  {text}", "", null);
        back.GetChild(0).GetChild<Label>(0).AddThemeFontOverride("font", Ui.SansStrong());
        Separator();
        rows();
        _into = outer;
        open.Pressed += () => Turn(page);
        back.Pressed += () => Turn(null);
    }

    void Turn(VBoxContainer? to)
    {
        _main.Visible = to == null;
        foreach (var p in _pages) p.Visible = p == to;
        _scroll.ScrollVertical = 0;
        Callable.From(Refit).CallDeferred();
    }

    static StyleBoxFlat RowStyle(Color bg) => Ui.Box(bg, 3, 8, 4);

    const int RowHeight = 26, Inset = 8, TickColumns = 2, StepSize = 22, StepValueWidth = 24;

    Button Row(string text, string accel, bool? tick)
    {
        var b = MakeRow(text, accel, tick);
        _into.AddChild(b);
        return b;
    }

    static Button MakeRow(string text, string accel, bool? tick)
    {
        var b = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(0, RowHeight), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.AddThemeStyleboxOverride("normal", RowStyle(new Color(0, 0, 0, 0)));
        b.AddThemeStyleboxOverride("hover", RowStyle(new Color(1, 1, 1, 0.08f)));
        b.AddThemeStyleboxOverride("pressed", RowStyle(new Color(1, 1, 1, 0.14f)));
        b.AddThemeStyleboxOverride("hover_pressed", RowStyle(new Color(1, 1, 1, 0.14f)));
        var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        h.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        h.OffsetLeft = Ui.Px(Inset);
        h.OffsetRight = -Ui.Px(Inset);
        h.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        if (tick != null)
        {
            var t = new Label { Text = tick.Value ? "✓" : " ", CustomMinimumSize = Ui.Px(14, 0) };
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
            a.AddThemeFontSizeOverride("font_size", Ui.Px(12));
            h.AddChild(a);
        }
        b.AddChild(h);
        return b;
    }

    public void Ticks(IReadOnlyList<MenuTick> ticks)
    {
        var grid = new GridContainer { Columns = TickColumns, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.None);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.None);
        foreach (var t in ticks)
        {
            var b = MakeRow(t.Text, "", t.On);
            b.TooltipText = t.Tip;
            var action = t.Action;
            b.Pressed += () => { Pop.Hide(); action(); };
            grid.AddChild(b);
        }
        _into.AddChild(grid);
    }

    public void Stepper(string text, int value, int least, int most, Action<int> set)
    {
        var h = new HBoxContainer { CustomMinimumSize = Ui.Px(0, RowHeight) };
        h.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        h.AddChild(new Label
        {
            Text = Fmt.OneLine(text), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MaxLinesVisible = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
        });
        var shown = new Label { CustomMinimumSize = Ui.Px(StepValueWidth, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        h.AddChild(shown);
        Button Step(string mark, string tip)
        {
            var b = new Button { Text = mark, TooltipText = tip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(StepSize, StepSize) };
            b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            Ui.MakeQuiet(b);
            h.AddChild(b);
            return b;
        }
        var down = Step("−", $"{text} −1");
        var up = Step("+", $"{text} +1");
        void Show()
        {
            shown.Text = value.ToString();
            down.Disabled = value <= least;
            up.Disabled = value >= most;
        }
        void Move(int by)
        {
            value += by;
            Show();
            set(value);
        }
        down.Pressed += () => Move(-1);
        up.Pressed += () => Move(1);
        Show();
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", Ui.Px(Inset));
        m.AddThemeConstantOverride("margin_right", Ui.Px(Inset));
        m.AddChild(h);
        _into.AddChild(m);
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

    public void Toggle(string text, bool on, Action<bool> set)
    {
        var b = Row(text, "", on);
        var tick = b.GetChild(0).GetChild<Label>(0);
        b.Pressed += () =>
        {
            on = !on;
            tick.Text = on ? "✓" : " ";
            set(on);
        };
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
        m.AddThemeConstantOverride("margin_left", Ui.Px(8));
        m.AddThemeConstantOverride("margin_right", Ui.Px(8));
        m.AddThemeConstantOverride("margin_bottom", Ui.Px(2));
        m.AddChild(Ui.Hint(text));
        _into.AddChild(m);
    }

    public void Head(string text, string aside = "", string asideTip = "")
    {
        var l =
            new Label { Text = Fmt.OneLine(text), ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MaxLinesVisible = 1,
                CustomMinimumSize = Ui.Px(0, 26), VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        l.AddThemeColorOverride("font_color", Colors.White);
        l.AddThemeFontOverride("font", Ui.SansStrong());
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        h.AddChild(l);
        if (aside.Length > 0)
        {
            var a = Ui.Hint(aside);
            a.AutowrapMode = TextServer.AutowrapMode.Off;
            a.CustomMinimumSize = Vector2.Zero;
            a.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
            a.TooltipText = asideTip;
            a.MouseFilter = Control.MouseFilterEnum.Pass;
            a.VerticalAlignment = VerticalAlignment.Center;
            h.AddChild(a);
        }
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", Ui.Px(8));
        m.AddThemeConstantOverride("margin_right", Ui.Px(8));
        m.AddChild(h);
        _into.AddChild(m);
    }

    public void Separator(string label = "")
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        _into.AddChild(sep);
        if (label.Length == 0) return;
        var l = Ui.HeadLabel(label);
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", Ui.Px(8));
        m.AddThemeConstantOverride("margin_bottom", Ui.Px(2));
        m.AddChild(l);
        _into.AddChild(m);
    }

    public void Add(Control c)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", Ui.Px(6));
        m.AddThemeConstantOverride("margin_right", Ui.Px(6));
        m.AddThemeConstantOverride("margin_top", Ui.Px(4));
        m.AddThemeConstantOverride("margin_bottom", Ui.Px(4));
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
        float edge = Ui.Px(ScreenEdge), room = view.Y - frame.Y - 2 * edge;
        float bar = content.Y > room ? _scroll.GetVScrollBar().GetCombinedMinimumSize().X : 0;
        _scroll.CustomMinimumSize = new Vector2(content.X + bar, Mathf.Min(content.Y, room));
        var size = _scroll.CustomMinimumSize + frame;
        var pos = new Vector2(
            Mathf.Clamp(_at.X, edge, Mathf.Max(edge, view.X - size.X - edge)),
            Mathf.Clamp(_at.Y, edge, Mathf.Max(edge, view.Y - size.Y - edge)));
        return new Rect2I((Vector2I)pos.Round(), (Vector2I)size.Ceil());
    }
}
