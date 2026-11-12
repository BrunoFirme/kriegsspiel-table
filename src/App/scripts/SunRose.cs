using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class SunRose : CompassRose
{
    public double Height
    {
        get => _height;
        set { _height = value; QueueRedraw(); }
    }
    double _height = Lighting.Overhead;

    public bool Shining
    {
        get => _shining;
        set { _shining = value; QueueRedraw(); }
    }
    bool _shining = true;

    const float Side = 112, Disc = 6, RayFrom = 9, RayTo = 13;
    const double PointNotch = 45, PointPull = 8;

    public SunRose() : base(Side, "Click or drag towards where the light comes from (Shift: single degrees)")
    {
        Notch = PointNotch;
        NotchPull = PointPull;
    }

    protected override void DrawMark(Vector2 c, float r)
    {
        float up = (float)((_height - Lighting.LowestSun) / (Lighting.HighestSun - Lighting.LowestSun));
        var at = c + Toward(From) * r * (1 - Mathf.Clamp(up, 0, 1)) * 0.9f;
        var colour = _shining ? Ui.Accent : Faint;
        if (at.DistanceTo(c) > Disc) DrawLine(at, c, Faint, 1.2f, true);
        DrawCircle(at, Disc, colour);
        for (int i = 0; i < 8; i++)
        {
            var dir = Toward(i * 45);
            DrawLine(at + dir * RayFrom, at + dir * RayTo, colour, 1.5f, true);
        }
    }
}
