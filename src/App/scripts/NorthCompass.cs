using System;
using Godot;

namespace KTable.App;

public partial class NorthCompass : Control
{
    public CameraRig? Rig;
    public Func<double>? North;
    public event Action? Pressed;
    public Func<Vector2, Vector2>? Snap;
    public string NorthKey { set => TooltipText = $"North · click, or {value}, to turn the view north-up · drag to move it · right-click to put it back"; }
    public event Action<Vector2>? Dropped;
    public event Action? ResetAsked;

    const float DragFrom = 5;
    internal static bool InDemo { get; set; }
    bool _pressed, _dragging;
    Vector2 _grab, _start;

    float _shown = float.NaN;
    bool _hover;

    public NorthCompass()
    {
        CustomMinimumSize = new Vector2(96, 96);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public static float NorthOnScreen(float yaw, double northDeg) => Mathf.Wrap(yaw - Mathf.Pi + Mathf.DegToRad((float)northDeg), -Mathf.Pi, Mathf.Pi);

    public override void _Process(double delta)
    {
        if (_pressed && !Input.IsMouseButtonPressed(MouseButton.Left) && !InDemo) Release(false);
        if (Rig == null || !IsVisibleInTree()) return;
        float a = NorthOnScreen(Rig.Yaw, North?.Invoke() ?? 0);
        if (Mathf.IsEqualApprox(a, _shown)) return;
        _shown = a;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
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
