using System;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

public partial class ToolIcon
{
    public const string TerrainKinds = "terrain:";
    const float Bold = 1.7f, Fine = 1.2f, Left = 1, Right = 19, Foot = 18.5f, Body = 0.34f, RoadHalf = 2.3f, RiverHalf = 2.6f;
    const int ProfileSteps = 36;
    static readonly Color Blue = new(0.5f, 0.74f, 0.95f);

    Color Soft => new(_c, _c.A * Body);

    Vector2[] Pts(params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2];
        for (int i = 0; i < xy.Length; i += 2) pts[i / 2] = P(xy[i], xy[i + 1]);
        return pts;
    }

    void Trace(Color ink, float width, params float[] xy) => DrawPolyline(Pts(xy), ink, width * _k, antialiased: true);

    void Solid(params float[] xy)
    {
        var pts = Pts(xy);
        DrawColoredPolygon(pts, _c);
        DrawPolyline([.. pts, pts[0]], _c, 1, antialiased: true);
    }

    void Ground(Func<float, float> top)
    {
        var line = new Vector2[ProfileSteps + 1];
        for (int i = 0; i <= ProfileSteps; i++)
        {
            float x = Left + (Right - Left) * i / ProfileSteps;
            line[i] = P(x, top(x));
        }
        DrawColoredPolygon([.. line, P(Right, Foot), P(Left, Foot)], Soft);
        DrawPolyline(line, _c, Bold * _k, antialiased: true);
    }

    void Ground(params float[] top)
    {
        var line = Pts(top);
        DrawColoredPolygon([.. line, P(Right, Foot), P(Left, Foot)], Soft);
        DrawPolyline(line, _c, Bold * _k, antialiased: true);
    }

    static float Bell(float x, float mid, float half)
    {
        float d = Mathf.Abs(x - mid) / half;
        return d >= 1 ? 0 : 0.5f * (1 + Mathf.Cos(Mathf.Pi * d));
    }

    void ArrowUp(float x, float tip, float foot)
    {
        Solid(x, tip, x + 2.7f, tip + 3.4f, x - 2.7f, tip + 3.4f);
        Line(x, tip + 3.2f, x, foot, Bold);
    }

    void ArrowDown(float x, float top, float tip)
    {
        Solid(x, tip, x + 2.7f, tip - 3.4f, x - 2.7f, tip - 3.4f);
        Line(x, top, x, tip - 3.2f, Bold);
    }

    void Dashes(Color ink, float y)
    {
        for (float x = Left; x < Right; x += 3.2f) DrawLine(P(x, y), P(Mathf.Min(Right, x + 1.8f), y), ink, Fine * _k, antialiased: true);
    }

    void House(float left, float foot, float wide, float wall, float roof) =>
        Solid(left, foot, left, foot - wall, left + wide / 2, foot - wall - roof, left + wide, foot - wall, left + wide, foot);

    Vector2[] Winding() => Bend(2, 16, 8, 1, 12, 19, 18, 4);

    static Vector2[] Beside(Vector2[] mid, float by)
    {
        var side = new Vector2[mid.Length];
        for (int i = 0; i < mid.Length; i++)
        {
            var along = (mid[Math.Min(mid.Length - 1, i + 1)] - mid[Math.Max(0, i - 1)]).Normalized();
            side[i] = mid[i] + new Vector2(-along.Y, along.X) * by;
        }
        return side;
    }

    void Band(Color ink, float half)
    {
        var mid = Winding();
        DrawPolyline(mid, new Color(ink, ink.A * Body), 2 * half * _k, antialiased: true);
        DrawPolyline(Beside(mid, half * _k), ink, Fine * _k, antialiased: true);
        DrawPolyline(Beside(mid, -half * _k), ink, Fine * _k, antialiased: true);
    }

    void Ring(float cx, float cy, float r, float turn)
    {
        const int Steps = 40;
        var pts = new Vector2[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float a = Mathf.Tau * i / Steps, far = r * (1 + 0.13f * Mathf.Sin(2 * a + turn) + 0.07f * Mathf.Cos(3 * a - turn));
            pts[i] = P(cx + Mathf.Cos(a) * far * 1.12f, cy + Mathf.Sin(a) * far * 0.9f);
        }
        DrawPolyline(pts, _c, Fine * _k, antialiased: true);
    }

    void StarFort()
    {
        float[] corner = [1.6f, -4.8f, 1.6f, -6.4f, 8, -8, 6.4f, -1.6f, 4.8f, -1.6f];
        var pts = new Vector2[corner.Length * 2 + 1];
        for (int turn = 0; turn < 4; turn++)
            for (int i = 0; i < corner.Length; i += 2)
            {
                float x = corner[i], y = corner[i + 1];
                for (int t = 0; t < turn; t++) (x, y) = (-y, x);
                pts[turn * corner.Length / 2 + i / 2] = P(10 + x, 10 + y);
            }
        pts[^1] = pts[0];
        DrawColoredPolygon(pts[..^1], Soft);
        DrawPolyline(pts, _c, Bold * _k, antialiased: true);
    }

    void Earthwork()
    {
        DrawColoredPolygon(Pts(3.5f, 3.5f, 16.5f, 3.5f, 16.5f, 16.5f, 3.5f, 16.5f), Soft);
        Trace(_c, Bold * 1.3f, 8, 16.5f, 3.5f, 16.5f, 3.5f, 3.5f, 16.5f, 3.5f, 16.5f, 16.5f, 12, 16.5f);
    }

    void DrawTerrain()
    {
        if (!Kind.StartsWith(TerrainKinds)) return;
        switch (Kind[TerrainKinds.Length..])
        {
            case "raise":
                Ground(x => 15.5f - 5 * Bell(x, 10, 7));
                ArrowUp(10, 1.2f, 8);
                break;
            case "lower":
                Ground(x => 9.5f + 6 * Bell(x, 10, 7));
                ArrowDown(10, 1.2f, 12.6f);
                break;
            case "smooth":
                Trace(Soft, Fine, 1, 11, 3, 6.5f, 5, 12, 7, 5, 9, 10.5f, 11, 6.5f, 13, 13, 15, 8.5f, 17, 13.5f, 19, 10.5f);
                Ground(x => 10.2f - 1.6f * Mathf.Sin((x - 1) * 0.42f) + 0.16f * (x - 1));
                break;
            case "flatten":
                Trace(Soft, Fine, 1, 12, 2.5f, 9.5f, 4.5f, 6.5f, 6.5f, 9.5f, 8, 12);
                Trace(Soft, Fine, 10.5f, 12, 12.5f, 8, 14.5f, 5.5f, 16.5f, 8.5f, 18, 12);
                Ground(x => 12);
                ArrowDown(9.3f, 1.2f, 10);
                break;
            case ReliefTool.LevelMode:
                Dashes(Soft, 7.5f);
                Ground(1, 16, 4, 16, 6.5f, 7.5f, 13.5f, 7.5f, 16, 16, 19, 16);
                break;
            case ReliefTool.HillMode:
                Ground(x => 16.5f - 12 * Bell(x, 10, 9));
                break;
            case ReliefTool.RidgeMode:
                DrawColoredPolygon(Pts(6, 6.5f, 15.5f, 3.5f, 19, 12.5f, 10.5f, 17.5f), Soft);
                Solid(1.5f, 17.5f, 6, 6.5f, 10.5f, 17.5f);
                Trace(_c, Bold, 6, 6.5f, 15.5f, 3.5f, 19, 12.5f);
                break;
            case ReliefTool.ValleyMode:
                Ground(1, 6, 4.5f, 6, 10, 16, 15.5f, 6, 19, 6);
                break;
            case ReliefTool.SlopeMode:
                Ground(1, 16, 3.5f, 16, 16.5f, 5, 19, 5);
                break;
            case ReliefTool.ContoursMode:
                Ring(10, 10, 8, 0.4f);
                Ring(9.5f, 10.2f, 5.2f, 1.1f);
                Ring(9, 10.4f, 2.4f, 1.9f);
                break;
            case ReliefTool.RoughenMode:
                Ground(1, 11, 2.8f, 8.5f, 4.4f, 12, 6.2f, 7, 8, 11.5f, 9.6f, 9, 11.4f, 13, 13.2f, 7.5f, 15, 11.5f, 16.8f, 9, 19, 12);
                break;
            case ReliefTool.ErodeMode:
                Ground(1, 7, 3.5f, 7, 5, 11.5f, 6.5f, 7, 8.5f, 7, 10.5f, 15, 12.5f, 7, 14.5f, 7, 15.8f, 10.5f, 17, 7, 19, 7);
                Trace(Blue, Fine, 10.5f, 1, 10.5f, 5);
                Trace(Blue, Fine, 5, 2.5f, 5, 5);
                Trace(Blue, Fine, 15.8f, 2.5f, 15.8f, 5);
                break;
            case ReliefTool.LiftMode:
                Ground(x => 12.5f);
                ArrowUp(5.5f, 1.5f, 10.5f);
                ArrowUp(14.5f, 1.5f, 10.5f);
                break;
            case BuiltKinds.Settlement:
                House(1.5f, 17, 6, 4, 3.4f);
                House(8.2f, 17, 5.4f, 7, 3.6f);
                House(14.3f, 17, 4.4f, 3.4f, 2.8f);
                Line(10.9f, 6.4f, 10.9f, 2.6f, Fine);
                Line(9.5f, 3.9f, 12.3f, 3.9f, Fine);
                break;
            case BuiltKinds.Fort:
                StarFort();
                break;
            case BuildPrefs.Redoubt:
                Earthwork();
                break;
            case BuiltKinds.Building:
                House(3.5f, 17, 13, 6.5f, 6.5f);
                break;
            case "track":
                var dashes = Winding();
                for (int i = 0; i + 2 < dashes.Length; i += 5) DrawPolyline(dashes[i..Math.Min(dashes.Length, i + 4)], _c, Bold * _k, antialiased: true);
                break;
            case BuiltKinds.Road:
                DrawPolyline(Winding(), _c, Bold * 1.25f * _k, antialiased: true);
                break;
            case "chaussee":
                Band(_c, RoadHalf);
                break;
            case "stream":
                DrawPolyline(Winding(), Blue, Bold * _k, antialiased: true);
                break;
            case "river":
                Band(Blue, RiverHalf);
                break;
        }
    }
}
