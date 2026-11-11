using System;
using Godot;

namespace KTable.App;

public partial class NorthCompass : Control
{
    public CameraRig? Rig;
    public Func<double>? North;
    public event Action? Pressed;
    public Func<Vector2, Vector2>? Snap;
    public event Action<Vector2>? Dropped;
    public event Action? ResetAsked;

    const float DragFrom = 5;
    internal static bool InDemo { get; set; }
    bool _pressed, _dragging;
    Vector2 _grab, _start;

    float _shown = float.NaN;
    bool _hover;

    static readonly Color Disc = new(0.10f, 0.10f, 0.11f, 0.86f);
    static readonly Color Ivory = new(0.93f, 0.91f, 0.85f);
    static readonly Color Shade = new(0.55f, 0.53f, 0.49f);
    static readonly Color Red = new(0.78f, 0.20f, 0.16f);
    static readonly Color RedShade = new(0.50f, 0.11f, 0.09f);

    public NorthCompass()
    {
        CustomMinimumSize = new Vector2(96, 96);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "North · click to turn the view north-up · drag to move it · right-click to put it back";
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

    public override void _Draw()
    {
        var c = Centre;
        float r = Radius;
        float turn = float.IsNaN(_shown) ? 0 : _shown;
        Vector2 Dir(float a) => new(Mathf.Sin(a + turn), -Mathf.Cos(a + turn));

        DrawCircle(c, r, Disc, true, -1, true);
        DrawArc(c, r - 0.5f, 0, Mathf.Tau, 96, new Color(Ivory, _hover ? 0.95f : 0.7f), 1.2f, true);
        DrawArc(c, r - 13, 0, Mathf.Tau, 96, new Color(Ui.Accent, 0.75f), 1f, true);
        for (int i = 0; i < 36; i++)
        {
            float a = Mathf.DegToRad(i * 10);
            bool major = i % 3 == 0, cardinal = i % 9 == 0;
            if (cardinal) continue;
            float len = major ? 5.5f : 3f;
            DrawLine(c + Dir(a) * (r - 3), c + Dir(a) * (r - 3 - len), new Color(Ivory, major ? 0.75f : 0.4f), major ? 1.2f : 1f, true);
        }

        var font = GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
        int fs = Mathf.RoundToInt(Mathf.Clamp(r * 0.24f, 9, 14));
        foreach (var (letter, deg) in new[] { ("N", 0f), ("E", 90f), ("S", 180f), ("W", 270f) })
        {
            var at = c + Dir(Mathf.DegToRad(deg)) * (r - 7);
            var sz = font.GetStringSize(letter, HorizontalAlignment.Left, -1, fs);
            var col = letter == "N" ? new Color(0.95f, 0.42f, 0.36f) : new Color(Ivory, 0.85f);
            DrawString(font, at + new Vector2(-sz.X / 2, font.GetAscent(fs) / 2 - 1), letter, HorizontalAlignment.Left, -1, fs, col);
        }

        float inner = r - 13;
        void Point(float a, float len, float half, Color lit, Color dark)
        {
            var tip = c + Dir(a) * len;
            var left = c + Dir(a - Mathf.Pi / 2) * half;
            var right = c + Dir(a + Mathf.Pi / 2) * half;
            DrawColoredPolygon(new[] { c, left, tip }, lit);
            DrawColoredPolygon(new[] { c, tip, right }, dark);
            DrawPolyline(new[] { left, tip }, lit, 1f, true);
            DrawPolyline(new[] { tip, right }, dark, 1f, true);
        }
        for (int i = 0; i < 4; i++)
            Point(Mathf.DegToRad(45 + i * 90), inner * 0.64f, inner * 0.16f, new Color(Shade, 0.95f), new Color(0.32f, 0.31f, 0.29f, 0.95f));
        for (int i = 1; i < 4; i++)
            Point(Mathf.DegToRad(i * 90), inner * 0.96f, inner * 0.22f, Ivory, Shade);
        Point(0, inner * 0.96f, inner * 0.22f, Red, RedShade);
        DrawCircle(c, 3.4f, Ui.Accent, true, -1, true);
        DrawCircle(c, 1.6f, Disc, true, -1, true);
    }
}
