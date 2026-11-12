using System;
using Godot;

namespace KTable.App;

public partial class WindRose : CompassRose
{
    public double Strength
    {
        get => _strength;
        set { _strength = value; QueueRedraw(); }
    }
    double _strength = 1;

    const float Side = 132;
    const double Calm = 0.25;

    public WindRose() : base(Side, "Click or drag towards where the wind comes from (Shift: single degrees)") { From = 270; }

    protected override void DrawMark(Vector2 c, float r)
    {
        if (_strength < Calm) { DrawCircle(c, 4, Faint); return; }
        var from = Toward(From);
        float reach = r * (0.35f + 0.13f * (float)Math.Clamp(_strength, 0, 5));
        var tail = c + from * r * 0.92f;
        var head = tail - from * reach * 1.6f;
        DrawLine(tail, head, Ui.Accent, 2.5f, true);
        var side = new Vector2(-from.Y, from.X) * 6;
        DrawColoredPolygon(new[] { head - from * 2, head + from * 11 + side, head + from * 11 - side }, Ui.Accent);
        DrawCircle(tail, 3.5f, Ui.Accent);
    }
}
