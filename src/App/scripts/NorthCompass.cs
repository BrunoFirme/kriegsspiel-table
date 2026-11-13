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
        set => TooltipText = $"North · click, or {value}, to turn the view north-up · the wheel over it zooms · point at the zoom under it to type it or slide it · "
            + "drag to move it · right-click to put it back";
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
    readonly PanelContainer _dialPlate;
    readonly HSlider _dial;
    bool _dialHeld;
    const float ZoomWidth = 64, ZoomGap = 2, ZoomFade = 8, DialHeight = 14, DialStep = 0.001f;
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
        _dial = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = DialStep, CustomMinimumSize = new Vector2(CustomMinimumSize.X - 2 * ZoomMargin, DialHeight), FocusMode = FocusModeEnum.None,
            TooltipText = "Zoom: how far the eye is from the point it looks at · to the right is closer",
        };
        _dial.ValueChanged += v => Rig?.ZoomTo(DistanceAt((float)v));
        _dial.DragStarted += () => _dialHeld = true;
        _dial.DragEnded += _ => _dialHeld = false;
        _dialPlate = new PanelContainer { Visible = false, Modulate = Colors.White with { A = 0 } };
        _dialPlate.AddThemeStyleboxOverride("panel", plate);
        _dialPlate.AddChild(_dial);
        AddChild(_dialPlate);
    }

    float DistanceAt(float dialled) => Rig == null ? 0 : Rig.MaxDistance * Mathf.Pow(Rig.MinDistance / Rig.MaxDistance, dialled);

    float DialAt(float distance) => Rig == null ? 0 : Mathf.Clamp(Mathf.Log(distance / Rig.MaxDistance) / Mathf.Log(Rig.MinDistance / Rig.MaxDistance), 0, 1);

    static Rect2 Around(Control c) => new Rect2(c.Position, c.Size).Grow(ZoomGap * 2);

    static void Fade(Control c, bool wanted, float delta)
    {
        float alpha = Mathf.MoveToward(c.Modulate.A, wanted ? 1 : 0, ZoomFade * delta);
        if (!Mathf.IsEqualApprox(alpha, c.Modulate.A)) c.Modulate = Colors.White with { A = alpha };
        c.Visible = alpha > 0;
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
        bool onReadout = _zoom.Visible && Around(_zoom).HasPoint(at);
        bool dialWanted = _dialHeld || onReadout || _dialPlate.Visible && Around(_dialPlate).HasPoint(at);
        Fade(_zoom, _zoom.HasFocus() || InDisc(at) || dialWanted, delta);
        Fade(_dialPlate, dialWanted && _zoom.Visible, delta);
        if (!_zoom.Visible) return;
        PlaceZoom();
        if (!_zoom.HasFocus()) SayZoom();
        if (_dialPlate.Visible && !_dialHeld) _dial.SetValueNoSignal(DialAt(Rig!.ZoomGoal));
    }

    void PlaceZoom()
    {
        Vector2 size = _zoom.GetCombinedMinimumSize(), dial = _dialPlate.GetCombinedMinimumSize();
        float below = GetViewportRect().Size.Y - GetGlobalRect().End.Y;
        bool under = below >= size.Y + dial.Y + 2 * ZoomGap;
        float y = under ? Size.Y + ZoomGap : -size.Y - ZoomGap;
        _zoom.Position = new Vector2((Size.X - size.X) / 2, y);
        _zoom.Size = size;
        _dialPlate.Position = new Vector2((Size.X - dial.X) / 2, under ? y + size.Y + ZoomGap : y - dial.Y - ZoomGap);
        _dialPlate.Size = dial;
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
