using System;
using Godot;
using KTable.Client.Prefs;

namespace KTable.App;

public partial class NorthDial : Control
{
    public const double TurnStep = 15, FineTurn = 1, DragStep = 5;
    const float Side = 150, Band = 16, DeadCentre = 4;

    public double North
    {
        get => _north;
        set { _north = KTable.Core.Model.Calibration.Turned(value); QueueRedraw(); }
    }
    double _north;

    public event Action<double, bool>? Turned;

    readonly Keymap _keys;
    bool _drag, _hover;

    public NorthDial(Keymap keys)
    {
        _keys = keys;
        CustomMinimumSize = new Vector2(Side, Side);
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"Drag the rose until its N points where north is on the map · {keys.Label("turnLeft")} {keys.Label("turnRight")} turn it, Shift by 1°";
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when b.Pressed || _drag:
                if (b.Pressed) GrabFocus();
                _drag = b.Pressed;
                Aim(b.Position, b.ShiftPressed, final: !b.Pressed);
                AcceptEvent();
                break;
            case InputEventMouseMotion m when _drag:
                Aim(m.Position, m.ShiftPressed, final: false);
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true } k when _keys.Is(k, "turnLeft"):
                TurnBy(k.ShiftPressed ? -FineTurn : -TurnStep);
                AcceptEvent();
                break;
            case InputEventKey { Pressed: true } k when _keys.Is(k, "turnRight"):
                TurnBy(k.ShiftPressed ? FineTurn : TurnStep);
                AcceptEvent();
                break;
        }
    }

    public void TurnBy(double deg)
    {
        North = Math.Round(_north + deg);
        Turned?.Invoke(_north, true);
    }

    void Aim(Vector2 at, bool fine, bool final)
    {
        var d = at - Size / 2;
        if (d.Length() >= DeadCentre)
        {
            double deg = Mathf.RadToDeg(Math.Atan2(d.X, -d.Y));
            North = fine ? Math.Round(deg) : Math.Round(deg / DragStep) * DragStep;
        }
        Turned?.Invoke(_north, final);
    }

    public override void _Draw()
    {
        var c = Size / 2;
        float outer = Mathf.Min(Size.X, Size.Y) / 2 - 1;
        var ink = CompassFace.Ivory;
        var ring = HasFocus() ? new Color(Ui.Accent, 0.22f) : new Color(ink, _hover ? 0.12f : 0.08f);
        DrawArc(c, outer - Band / 2, 0, Mathf.Tau, 96, ring, Band, true);
        for (int i = 1; i < 12; i++)
        {
            float a = Mathf.DegToRad(i * 30);
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            float len = i % 3 == 0 ? 6 : 3.5f;
            DrawLine(c + dir * (outer - Band / 2 - len / 2), c + dir * (outer - Band / 2 + len / 2), new Color(ink, i % 3 == 0 ? 0.6f : 0.35f), 1.2f, true);
        }
        var font = GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
        const int topSize = 9;
        const string top = "TOP";
        var sz = font.GetStringSize(top, HorizontalAlignment.Left, -1, topSize);
        DrawString(font, c + new Vector2(-sz.X / 2, -(outer - Band / 2) + font.GetAscent(topSize) / 2 - 1), top, HorizontalAlignment.Left, -1, topSize, new Color(ink, 0.85f));
        CompassFace.Draw(this, font, c, outer - Band - 2, Mathf.DegToRad((float)_north), _hover || _drag);
    }
}
