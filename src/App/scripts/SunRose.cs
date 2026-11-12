using System;
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

    public bool Moon
    {
        get => _moon;
        set { _moon = value; QueueRedraw(); }
    }
    bool _moon;

    internal const float Track = 0.9f;
    const float Side = 112, Disc = 6, RayFrom = 9, RayTo = 13, MoonRadius = 8, MoonBite = 0.9f;
    const int MoonSteps = 14;
    static readonly Vector2 BiteToward = new Vector2(0.6f, -0.8f);
    const double PointNotch = 45, PointPull = 8;

    public SunRose() : base(Side, "Drag the sun to where the light comes from, nearer the centre to stand higher (Shift: single degrees)")
    {
        Notch = PointNotch;
        NotchPull = PointPull;
    }

    protected override void Reach(float fraction)
    {
        double up = Math.Clamp(1 - fraction / Track, 0, 1);
        Height = Math.Round(Lighting.LowestSun + up * (Lighting.HighestSun - Lighting.LowestSun));
    }

    protected override void DrawMark(Vector2 c, float r)
    {
        float up = (float)((_height - Lighting.LowestSun) / (Lighting.HighestSun - Lighting.LowestSun));
        var at = c + Toward(From) * r * (1 - Mathf.Clamp(up, 0, 1)) * Track;
        var colour = _shining ? Ui.Accent : Faint;
        if (at.DistanceTo(c) > Disc) DrawLine(at, c, Faint, 1.2f, true);
        if (_moon) { DrawColoredPolygon(Crescent(at), colour); return; }
        DrawCircle(at, Disc, colour);
        for (int i = 0; i < 8; i++)
        {
            var dir = Toward(i * 45);
            DrawLine(at + dir * RayFrom, at + dir * RayTo, colour, 1.5f, true);
        }
    }

    static Vector2[] Crescent(Vector2 at)
    {
        float bite = MoonRadius * MoonBite, horn = Mathf.Acos(bite / (2 * MoonRadius));
        var u = BiteToward;
        var v = new Vector2(-u.Y, u.X);
        Vector2 On(Vector2 centre, float a) => centre + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * MoonRadius;
        var points = new Vector2[MoonSteps * 2 + 2];
        for (int i = 0; i <= MoonSteps; i++)
        {
            points[i] = On(at, Mathf.Lerp(horn, Mathf.Tau - horn, i / (float)MoonSteps));
            points[MoonSteps + 1 + i] = On(at + u * bite, Mathf.Lerp(Mathf.Pi + horn, Mathf.Pi - horn, i / (float)MoonSteps));
        }
        return points;
    }
}
