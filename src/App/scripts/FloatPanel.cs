using System;
using Godot;

namespace KTable.App;

public partial class FloatPanel : PanelContainer
{
    public string Key = "";
    public Action<string, Vector2>? Moved;
    public Action? Closed;
    public VBoxContainer Body;
    public bool Resizable;
    public Func<FloatPanel, Vector2, Vector2>? Snap;
    public Action<FloatPanel>? Grabbed;

    Label _title;
    HBoxContainer _bar;
    bool _dragging;
    bool _resizing;
    Vector2 _loose;

    public FloatPanel(string title)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 5);
        AddChild(box);
        _bar = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = "Drag to move this panel" };
        _bar.AddThemeConstantOverride("separation", 4);
        box.AddChild(_bar);
        _title = new Label { Text = Spaced(title), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        Ui.HeadStyle(_title);
        _bar.AddChild(_title);
        var close = new Button { Text = "✕", Flat = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(20, 18), TooltipText = "Close" };
        close.AddThemeFontSizeOverride("font_size", 11);
        close.AddThemeColorOverride("font_color", Ui.Muted);
        close.Pressed += () => Closed?.Invoke();
        _bar.AddChild(close);
        box.AddChild(new HSeparator());
        Body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        Body.AddThemeConstantOverride("separation", 6);
        box.AddChild(Body);
        _bar.GuiInput += BarInput;
        GuiInput += PanelInput;
    }

    public void SetTitle(string t) => _title.Text = Spaced(t);

    public void AddToBar(Control c) { _bar.AddChild(c); _bar.MoveChild(c, _bar.GetChildCount() - 2); }

    static string Spaced(string t) => Ui.Spaced(t);

    public void Fit()
    {
        if (CustomMinimumSize != Vector2.Zero)
        {
            Callable.From(() => { if (IsInstanceValid(this)) { Trim(); Clamp(); } }).CallDeferred();
            return;
        }
        CustomMinimumSize = Vector2.Zero;
        ResetSize();
        Callable.From(() =>
        {
            if (IsInstanceValid(this))
            {
                ResetSize();
                Trim();
                Clamp();
            }
        }).CallDeferred();
    }

    void Trim()
    {
        var vp = GetViewportRect().Size;
        float room = vp.Y - Ceiling - 8;
        if (Size.Y <= room && Size.X <= vp.X - 8) return;
        CustomMinimumSize = new Vector2(Math.Min(Size.X, vp.X - 8), Math.Min(Size.Y, room));
        Size = CustomMinimumSize;
    }

    public float Ceiling;

    public void Clamp()
    {
        var vp = GetViewportRect().Size;
        var s = Size;
        Position = new Vector2(
            Mathf.Clamp(Position.X, 0, Math.Max(0, vp.X - Math.Min(s.X, vp.X))),
            Mathf.Clamp(Position.Y, Ceiling, Math.Max(Ceiling, vp.Y - Math.Min(s.Y, vp.Y))));
    }

    public override void _Input(InputEvent e)
    {
        if (!_dragging && !_resizing) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _dragging = _resizing = false;
            Moved?.Invoke(Key, Position);
        }
        else if (e is InputEventMouseMotion && !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _dragging = _resizing = false;
            Moved?.Invoke(Key, Position);
        }
    }

    void BarInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (mb.Pressed)
                {
                    _loose = Position;
                    Grabbed?.Invoke(this);
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                _loose += mm.Relative;
                Position = Snap?.Invoke(this, _loose) ?? _loose;
                Clamp();
                AcceptEvent();
                break;
        }
    }

    Vector2 ContentFloor()
    {
        var inner = GetChildCount() > 0 && GetChild(0) is Control c ? c.GetCombinedMinimumSize() : Vector2.Zero;
        return inner + (GetThemeStylebox("panel")?.GetMinimumSize() ?? Vector2.Zero);
    }

    void PanelInput(InputEvent e)
    {
        if (!Resizable) return;
        const float grip = 14;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                bool corner = mb.Position.X > Size.X - grip && mb.Position.Y > Size.Y - grip;
                _resizing = mb.Pressed && corner;
                if (_resizing || !mb.Pressed) AcceptEvent();
                if (!mb.Pressed) Moved?.Invoke(Key, Position);
                break;
            case InputEventMouseMotion mm when _resizing:
                var floor = ContentFloor();
                var from = CustomMinimumSize == Vector2.Zero ? Size : CustomMinimumSize;
                CustomMinimumSize = new Vector2(
                    Mathf.Clamp(from.X + mm.Relative.X, Math.Max(80, floor.X), GetViewportRect().Size.X - Position.X),
                    Mathf.Clamp(from.Y + mm.Relative.Y, Math.Max(56, floor.Y), GetViewportRect().Size.Y - Position.Y));
                Size = CustomMinimumSize;
                AcceptEvent();
                break;
        }
    }

    public override void _Draw()
    {
        if (!Resizable) return;
        var c = new Color(1, 1, 1, 0.25f);
        for (int i = 1; i <= 3; i++)
            DrawLine(new Vector2(Size.X - i * 4 - 1, Size.Y - 2), new Vector2(Size.X - 2, Size.Y - i * 4 - 1), c, 1.2f, true);
    }
}
