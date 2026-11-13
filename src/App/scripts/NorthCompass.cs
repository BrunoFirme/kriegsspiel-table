using System;
using System.Globalization;
using Godot;

namespace KTable.App;

public partial class NorthCompass : Control
{
    public CameraRig? Rig;
    public Func<double>? North;
    public event Action? Pressed;
    public Func<Vector2, Vector2>? Snap;
    public string NorthKey
    {
        set => TooltipText = $"North · click, or {value}, to turn the view north-up · the wheel over it zooms · drag to move it · right-click to put it back";
    }
    public event Action<Vector2>? Dropped;
    public event Action? ResetAsked;

    const float DragFrom = 5;
    internal static bool InDemo { get; set; }
    bool _pressed, _dragging;
    Vector2 _grab, _start;

    float _shown = float.NaN;
    bool _hover;

    readonly LineEdit _zoom;
    const float ZoomWidth = 64, ZoomGap = 2, ZoomFade = 8;
    const int ZoomLettering = 11, ZoomMargin = 4;
    const string ZoomShown = "0.#", TableUnit = " u";

    public NorthCompass()
    {
        CustomMinimumSize = new Vector2(96, 96);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        _zoom = new LineEdit
        {
            Alignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(ZoomWidth, 0), SelectAllOnFocus = true, ContextMenuEnabled = false,
            FocusMode = FocusModeEnum.Click, Visible = false, Modulate = Colors.White with { A = 0 },
            TooltipText = "Zoom: how far the eye is from the point it looks at, in table units · click to type it",
        };
        var plate = Ui.Panel();
        plate.ContentMarginLeft = plate.ContentMarginRight = ZoomMargin;
        plate.ContentMarginTop = plate.ContentMarginBottom = 0;
        _zoom.AddThemeStyleboxOverride("normal", plate);
        _zoom.AddThemeStyleboxOverride("focus", plate);
        _zoom.AddThemeFontSizeOverride("font_size", ZoomLettering);
        _zoom.AddThemeConstantOverride("minimum_character_width", 0);
        _zoom.TextSubmitted += TakeZoom;
        _zoom.FocusExited += SayZoom;
        AddChild(_zoom);
    }

    public static float NorthOnScreen(float yaw, double northDeg) => Mathf.Wrap(yaw - Mathf.Pi + Mathf.DegToRad((float)northDeg), -Mathf.Pi, Mathf.Pi);

    public override void _Process(double delta)
    {
        if (_pressed && !Input.IsMouseButtonPressed(MouseButton.Left) && !InDemo) Release(false);
        if (Rig == null || !IsVisibleInTree()) return;
        ShowZoom((float)delta);
        float a = NorthOnScreen(Rig.Yaw, North?.Invoke() ?? 0);
        if (Mathf.IsEqualApprox(a, _shown)) return;
        _shown = a;
        QueueRedraw();
    }

    void ShowZoom(float delta)
    {
        var at = GetGlobalTransformWithCanvas().AffineInverse() * Pointer.In(GetViewport());
        bool wanted = _zoom.HasFocus() || InDisc(at) || _zoom.Visible && new Rect2(_zoom.Position, _zoom.Size).Grow(ZoomGap * 2).HasPoint(at);
        float alpha = Mathf.MoveToward(_zoom.Modulate.A, wanted ? 1 : 0, ZoomFade * delta);
        if (!Mathf.IsEqualApprox(alpha, _zoom.Modulate.A)) _zoom.Modulate = Colors.White with { A = alpha };
        _zoom.Visible = alpha > 0;
        if (!_zoom.Visible) return;
        PlaceZoom();
        if (!_zoom.HasFocus()) SayZoom();
    }

    void PlaceZoom()
    {
        var size = _zoom.GetCombinedMinimumSize();
        float below = GetViewportRect().Size.Y - GetGlobalRect().End.Y;
        _zoom.Position = new Vector2((Size.X - size.X) / 2, below >= size.Y + ZoomGap ? Size.Y + ZoomGap : -size.Y - ZoomGap);
        _zoom.Size = size;
    }

    void TakeZoom(string text)
    {
        var typed = text.Replace(',', '.').Trim().TrimEnd('u').Trim();
        if (double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) && distance > 0) Rig?.ZoomTo((float)distance);
        _zoom.ReleaseFocus();
        SayZoom();
    }

    void SayZoom()
    {
        if (Rig == null) return;
        var said = Rig.ZoomGoal.ToString(ZoomShown, CultureInfo.InvariantCulture) + TableUnit;
        if (_zoom.Text != said) _zoom.Text = said;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel when InDisc(wheel.Position):
                AcceptEvent();
                Rig?.Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? CameraRig.WheelNotch : 1 / CameraRig.WheelNotch);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rc when InDisc(rc.Position):
                AcceptEvent();
                ResetAsked?.Invoke();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down when InDisc(down.Position):
                AcceptEvent();
                _pressed = true;
                _dragging = false;
                _grab = down.GlobalPosition;
                _start = Position;
                break;
            case InputEventMouseMotion mm when _pressed:
                AcceptEvent();
                if (!_dragging && (mm.GlobalPosition - _grab).Length() < DragFrom) break;
                if (!_dragging)
                {
                    _dragging = true;
                    SetAnchorsPreset(LayoutPreset.TopLeft, true);
                    MouseDefaultCursorShape = CursorShape.Move;
                }
                var loose = _start + (mm.GlobalPosition - _grab);
                Position = Snap?.Invoke(loose) ?? loose;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up when _pressed:
                AcceptEvent();
                Release(InDisc(up.Position));
                break;
        }
    }

    void Release(bool overDial)
    {
        if (!_pressed) return;
        _pressed = false;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        if (_dragging) { _dragging = false; Dropped?.Invoke(Position); }
        else if (overDial) Pressed?.Invoke();
    }

    public override void _Input(InputEvent e)
    {
        if (_pressed && e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up)
            Release(InDisc(GetGlobalTransformWithCanvas().AffineInverse() * up.GlobalPosition));
    }

    public override bool _HasPoint(Vector2 point) => InDisc(point);

    Vector2 Centre => Size / 2;
    bool InDisc(Vector2 p) => (p - Centre).Length() <= Radius + 2;
    float Radius => Mathf.Min(Size.X, Size.Y) / 2 - 2;

    public override void _Draw() => CompassFace.Draw(this, GetThemeDefaultFont() ?? ThemeDB.FallbackFont, Centre, Radius, float.IsNaN(_shown) ? 0 : _shown, _hover);
}
