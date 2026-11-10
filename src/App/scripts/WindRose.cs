using System;
using Godot;

namespace KTable.App;

public partial class WindRose : Control
{
    public double From
    {
        get => _from;
        set { _from = ((value % 360) + 360) % 360; QueueRedraw(); }
    }
    public double Strength
    {
        get => _strength;
        set { _strength = value; QueueRedraw(); }
    }
    double _from = 270, _strength = 1;

    public event Action<double, bool>? Turned;

    bool _drag;

    public WindRose()
    {
        CustomMinimumSize = new Vector2(132, 132);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Click or drag towards where the wind comes from (Shift: single degrees)";
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
        if (d.Length() < 4) { if (final) Turned?.Invoke(_from, true); return; }
        double deg = Mathf.RadToDeg(Math.Atan2(d.X, -d.Y));
        deg = fine ? Math.Round(deg) : Math.Round(deg / 5) * 5;
        From = deg;
        Turned?.Invoke(_from, final);
    }

    public override void _Draw()
    {
        var c = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) / 2 - 14;
        var ink = new Color(1, 1, 1, 0.75f);
        var faint = new Color(1, 1, 1, 0.25f);
        DrawCircle(c, r, new Color(1, 1, 1, 0.04f));
        DrawArc(c, r, 0, Mathf.Tau, 64, faint, 1.2f, true);
        for (int i = 0; i < 16; i++)
        {
            float a = Mathf.DegToRad(i * 22.5f);
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            float len = i % 4 == 0 ? 9 : i % 2 == 0 ? 6 : 3;
            DrawLine(c + dir * (r - len), c + dir * r, i % 4 == 0 ? ink : faint, 1.2f, true);
        }
        var font = Ui.Sans();
        int fs = 12;
        foreach (var (letter, deg) in new[] { ("N", 0f), ("E", 90f), ("S", 180f), ("W", 270f) })
        {
            float a = Mathf.DegToRad(deg);
            var p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (r + 8);
            var sz = font.GetStringSize(letter, HorizontalAlignment.Left, -1, fs);
            DrawString(font, p + new Vector2(-sz.X / 2, sz.Y / 3), letter, HorizontalAlignment.Left, -1, fs, letter == "N" ? Ui.Accent : ink);
        }
        if (_strength < 0.25) { DrawCircle(c, 4, faint); return; }
        float fa = Mathf.DegToRad((float)_from);
        var from = new Vector2(Mathf.Sin(fa), -Mathf.Cos(fa));
        float reach = r * (0.35f + 0.13f * (float)Math.Clamp(_strength, 0, 5));
        var tail = c + from * r * 0.92f;
        var head = tail - from * reach * 1.6f;
        DrawLine(tail, head, Ui.Accent, 2.5f, true);
        var side = new Vector2(-from.Y, from.X) * 6;
        DrawColoredPolygon(new[] { head - from * 2, head + from * 11 + side, head + from * 11 - side }, Ui.Accent);
        DrawCircle(tail, 3.5f, Ui.Accent);
    }
}
