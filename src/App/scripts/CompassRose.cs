using System;
using Godot;

namespace KTable.App;

public partial class CompassRose : Control
{
    public double From
    {
        get => _from;
        set { _from = ((value % 360) + 360) % 360; QueueRedraw(); }
    }
    double _from;

    public event Action<double, bool>? Turned;

    protected double Step = 5, Notch, NotchPull;
    const float DeadCentre = 4, Labels = 8, Edge = 14;
    bool _drag;

    public CompassRose(float side, string tip)
    {
        CustomMinimumSize = new Vector2(side, side);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = tip;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                _drag = b.Pressed;
                Aim(b.Position, b.ShiftPressed, final: !b.Pressed);
                AcceptEvent();
                break;
            case InputEventMouseMotion m when _drag:
                Aim(m.Position, m.ShiftPressed, final: false);
                AcceptEvent();
                break;
        }
    }

    void Aim(Vector2 at, bool fine, bool final)
    {
        var d = at - Size / 2;
        Reach(d.Length() / Radius);
        if (d.Length() >= DeadCentre)
        {
            double deg = Mathf.RadToDeg(Math.Atan2(d.X, -d.Y));
            From = fine ? Math.Round(deg) : Snapped(deg);
        }
        Turned?.Invoke(_from, final);
    }

    protected virtual void Reach(float fraction) { }

    double Snapped(double deg)
    {
        if (Notch > 0)
        {
            double nearest = Math.Round(deg / Notch) * Notch;
            if (Math.Abs(deg - nearest) <= NotchPull) return nearest;
        }
        return Math.Round(deg / Step) * Step;
    }

    protected internal float Radius => Mathf.Min(Size.X, Size.Y) / 2 - Edge;

    protected static Vector2 Toward(double deg)
    {
        float a = Mathf.DegToRad((float)deg);
        return new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
    }

    protected static readonly Color Ink = new(1, 1, 1, 0.75f), Faint = new(1, 1, 1, 0.25f);

    public override void _Draw()
    {
        var c = Size / 2;
        float r = Radius;
        DrawCircle(c, r, new Color(1, 1, 1, 0.04f));
        DrawArc(c, r, 0, Mathf.Tau, 64, Faint, 1.2f, true);
        for (int i = 0; i < 16; i++)
        {
            var dir = Toward(i * 22.5);
            float len = i % 4 == 0 ? 9 : i % 2 == 0 ? 6 : 3;
            DrawLine(c + dir * (r - len), c + dir * r, i % 4 == 0 ? Ink : Faint, 1.2f, true);
        }
        var font = Ui.Sans();
        int fs = 12;
        foreach (var (letter, deg) in new[] { ("N", 0.0), ("E", 90.0), ("S", 180.0), ("W", 270.0) })
        {
            var p = c + Toward(deg) * (r + Labels);
            var sz = font.GetStringSize(letter, HorizontalAlignment.Left, -1, fs);
            DrawString(font, p + new Vector2(-sz.X / 2, sz.Y / 3), letter, HorizontalAlignment.Left, -1, fs, letter == "N" ? Ui.Accent : Ink);
        }
        DrawMark(c, r);
    }

    protected virtual void DrawMark(Vector2 c, float r) { }
}
