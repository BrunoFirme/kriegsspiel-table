using Godot;
using KTable.Core.Model;

namespace KTable.App;

static partial class RaisedProps
{
    const int CraterArcs = 12;

    static readonly Color Sandstone = new(0.78f, 0.68f, 0.5f);
    static readonly Color Vermilion = new(0.66f, 0.2f, 0.14f);
    static readonly Color Lacquer = new(0.12f, 0.1f, 0.09f);
    static readonly Color Paper = new(0.84f, 0.8f, 0.68f);
    static readonly Color Turf = new(0.32f, 0.38f, 0.22f);
    static readonly Color Scorched = new(0.2f, 0.17f, 0.14f);

    static bool Mark(Spot s, string cls, RaisedMesh.Paint cloth)
    {
        var iron = P(Iron, RaisedCodes.Iron);
        switch (cls)
        {
            case Hallmarks.Sphinx:
                Sphinx(s);
                return true;
            case Hallmarks.Paifang or "torii":
                Gate(s, cls == "torii");
                return true;
            case Hallmarks.FireTower:
                foreach (var (dx, dz) in new[] { (1.2f, 1.2f), (-1.2f, 1.2f), (1.2f, -1.2f), (-1.2f, -1.2f) }) Box(s, s.Off(dx, dz), 0.3f, 0.3f, 0, 11, WoodPaint);
                Box(s, s.At, 2.8f, 2.8f, 11, 2.2f, WoodPaint);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(13.2f), s.X * s.U(2.2f), s.Z * s.U(2.2f), s.U(1.4f), [new(1, 0), new(0, 1)], P(Wood.Darkened(0.2f), RaisedCodes.Timber), 4);
                return true;
            case Hallmarks.Lantern:
                Box(s, s.At, 0.9f, 0.9f, 0, 0.25f, StonePaint);
                Drum(s, s.At, 0.18f, 0.25f, 0.9f, StonePaint, 6);
                Box(s, s.At, 0.6f, 0.6f, 1.15f, 0.45f, StonePaint);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(1.6f), s.X * s.U(0.6f), s.Z * s.U(0.6f), s.U(0.35f), [new(1, 0), new(0, 1)], StonePaint, 4);
                return true;
            case Hallmarks.Hitch:
                foreach (int end in new[] { 1, -1 }) Box(s, s.Off(1.6f * end, 0), 0.15f, 0.15f, 0, 1, WoodPaint);
                Box(s, s.At, 3.3f, 0.1f, 0.9f, 0.1f, WoodPaint);
                return true;
            case Hallmarks.AdColumn:
                Drum(s, s.At, 0.7f, 0, 2.6f, P(Paper, RaisedCodes.Cloth), 10);
                var cap = Vector3.Up * s.U(2.6f);
                RaisedRoofs.Turned(s.M, s.At + cap, Vector3.Right * s.U(0.8f), Vector3.Back * s.U(0.8f), s.U(0.7f), [new(1, 0), new(0.8f, 0.5f), new(0, 1)], P(Verdigris, RaisedCodes.Iron), 10);
                return true;
            case Hallmarks.Memorial:
                Box(s, s.At, 2.2f, 2.2f, 0, 0.4f, StonePaint);
                Box(s, s.At, 1.6f, 1.6f, 0.4f, 0.4f, StonePaint);
                Box(s, s.At, 0.8f, 0.8f, 0.8f, 3, StonePaint);
                return true;
            case Hallmarks.Shelter:
                Blob(s, s.At, 3.2f, 1.4f, P(Turf, RaisedCodes.Earth));
                Box(s, s.Off(3, 0), 0.6f, 1.2f, 0, 1.4f, P(Lacquer, RaisedCodes.Iron));
                return true;
            case Hallmarks.FuelPump:
                Box(s, s.At, 0.6f, 0.45f, 0, 1.8f, P(Cloths[0], RaisedCodes.Iron));
                return true;
            case Hallmarks.Tractor:
                Box(s, s.Off(0.2f, 0), 2.6f, 1.1f, 0.6f, 1, P(Cloths[(int)(s.Pick * 4) % 4], RaisedCodes.Iron));
                Axle(s, s.Off(-0.8f, 0), s.Z, 0.75f, 1.8f, P(Lacquer, RaisedCodes.Iron), 0.75f);
                Axle(s, s.Off(1.1f, 0), s.Z, 0.4f, 1.4f, P(Lacquer, RaisedCodes.Iron), 0.4f);
                return true;
            case Hallmarks.Qanat:
                Blob(s, s.At, 2.4f, 0.5f, P(Earth, RaisedCodes.Earth));
                Drum(s, s.At, 0.6f, 0.5f, 0.04f, P(Water.Darkened(0.6f), RaisedCodes.Water));
                return true;
            case "column":
                Box(s, s.At, 1.6f, 1.6f, 0, 0.8f, StonePaint);
                Drum(s, s.At, 0.45f, 0.8f, 7, StonePaint, 10);
                Box(s, s.At, 1.2f, 1.2f, 7.8f, 0.5f, StonePaint);
                return true;
            case "cross":
                Box(s, s.At, 0.6f, 0.6f, 0, 0.4f, StonePaint);
                Box(s, s.At, 0.25f, 0.25f, 0.4f, 2.4f, WoodPaint);
                Box(s, s.Off(0, 0), 1.2f, 0.2f, 2.1f, 0.2f, WoodPaint);
                return true;
            case "washing":
                Washing(s);
                return true;
            case "signboard":
                Box(s, s.At, 0.15f, 0.15f, 0, 2.8f, WoodPaint);
                Box(s, s.Off(0.5f, 0), 1, 0.1f, 2.6f, 0.1f, iron);
                Box(s, s.Off(0.75f, 0), 0.7f, 0.05f, 1.9f, 0.6f, cloth);
                return true;
            case "beacon":
                Drum(s, s.At, 0.12f, 0, 5, WoodPaint, 6);
                Drum(s, s.At, 0.5f, 5, 0.6f, iron, 8);
                return true;
            case Settlements.Crater or Settlements.BombCrater:
                Crater(s, cls == Settlements.BombCrater ? 7 : 3, cls == Settlements.BombCrater ? 1.1f : 0.5f);
                return true;
        }
        return false;
    }

    static readonly Color Verdigris = new(0.3f, 0.45f, 0.38f);

    static void Sphinx(Spot s)
    {
        var sand = P(Sandstone, (int)Fabric.Stone);
        Box(s, s.At, 4.6f, 1.6f, 0, 0.8f, sand);
        Box(s, s.Off(-0.5f, 0), 3, 1.2f, 0.8f, 1, sand);
        Box(s, s.Off(1.5f, 0), 1.4f, 1, 0.8f, 0.35f, sand);
        Box(s, s.Off(0.9f, 0), 0.75f, 0.8f, 1.8f, 0.85f, sand);
        Box(s, s.Off(0.82f, 0), 0.55f, 1.15f, 1.85f, 0.6f, P(Sandstone.Darkened(0.12f), (int)Fabric.Stone));
    }

    static void Gate(Spot s, bool torii)
    {
        var red = P(Vermilion, RaisedCodes.Timber);
        if (torii)
        {
            foreach (int side in new[] { 1, -1 }) Drum(s, s.Off(2 * side, 0), 0.25f, 0, 4.2f, red, 8);
            Box(s, s.At, 5, 0.3f, 3.4f, 0.3f, red);
            Box(s, s.At, 6.2f, 0.5f, 4.2f, 0.35f, P(Lacquer, RaisedCodes.Timber));
            return;
        }
        foreach (float x in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
        {
            Box(s, s.Off(x, 0), 0.9f, 0.9f, 0, 0.5f, StonePaint);
            Box(s, s.Off(x, 0), 0.5f, 0.5f, 0.5f, 5.5f, red);
        }
        Box(s, s.At, 10, 0.4f, 4.6f, 0.5f, red);
        Box(s, s.At, 10, 0.4f, 5.6f, 0.4f, red);
        Canopy(s, 11, 1.8f, 6, P(RaisedColours.Roof(Cover.Kawara, s.Pick), RaisedCodes.Roof + (int)Cover.Kawara));
    }

    static void Washing(Spot s)
    {
        foreach (int end in new[] { 1, -1 }) Box(s, s.Off(2 * end, 0), 0.1f, 0.1f, 0, 2.2f, WoodPaint);
        Box(s, s.At, 4, 0.02f, 2, 0.02f, P(Paper, RaisedCodes.Cloth));
        for (int i = 0; i < 4; i++)
        {
            var hung = P(i % 2 == 0 ? Paper : Cloths[(int)(s.Pick * 7 + i) % Cloths.Length], RaisedCodes.Cloth);
            Box(s, s.Off(-1.5f + i, 0), 0.6f, 0.02f, 1.35f + 0.1f * (i % 2), 0.65f - 0.1f * (i % 2), hung);
        }
    }

    static void Crater(Spot s, float radiusM, float rimM)
    {
        var earth = P(Scorched, RaisedCodes.Earth);
        float[] radii = [1.3f, 1, 0.72f, 0.45f];
        float[] heights = [0, rimM, 0, -rimM * 1.5f];
        for (int j = 0; j + 1 < radii.Length; j++)
            for (int i = 0; i < CraterArcs; i++)
            {
                float a0 = i * Mathf.Tau / CraterArcs, a1 = (i + 1) * Mathf.Tau / CraterArcs;
                Vector3 d0 = s.X * Mathf.Cos(a0) + s.Z * Mathf.Sin(a0), d1 = s.X * Mathf.Cos(a1) + s.Z * Mathf.Sin(a1);
                Vector3 Ring(Vector3 d, int k) => s.At + d * s.U(radiusM * radii[k]) + Vector3.Up * s.U(heights[k]);
                var facing = (d0 + d1).Normalized() * (heights[j + 1] > heights[j] ? 1 : -1) + Vector3.Up;
                s.M.Face([Ring(d0, j), Ring(d1, j), Ring(d1, j + 1), Ring(d0, j + 1)], facing, d1 - d0, Ring(d0, j), earth);
            }
    }

    public static void Lamp(RaisedMesh m, int kind, Vector3 at, float pick)
    {
        var s = new Spot(m, at, Vector3.Right, Vector3.Back, pick);
        var iron = P(Iron, RaisedCodes.Iron);
        var light = P(new Color(0.95f, 0.85f, 0.6f), RaisedCodes.Lamp);
        switch (kind)
        {
            case Lamps.Electric:
                Drum(s, s.At, 0.09f, 0, 5, iron, 6);
                Box(s, s.Off(0.5f, 0), 1, 0.08f, 4.9f, 0.08f, iron);
                Box(s, s.Off(0.95f, 0), 0.35f, 0.25f, 4.7f, 0.2f, light);
                break;
            case Lamps.Lantern:
                Drum(s, s.At, 0.07f, 0, 2.8f, iron, 6);
                Box(s, s.At, 0.32f, 0.32f, 2.8f, 0.4f, light);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(3.2f), s.X * s.U(0.28f), s.Z * s.U(0.28f), s.U(0.2f), [new(1, 0), new(0, 1)], iron, 4);
                break;
            case Lamps.Torch:
                Drum(s, s.At, 0.06f, 0, 2, WoodPaint, 5);
                Drum(s, s.At, 0.16f, 2, 0.25f, light, 6);
                break;
            case Lamps.Fire:
                Drum(s, s.At, 0.7f, 0, 0.25f, StonePaint, 8);
                Drum(s, s.At, 0.5f, 0.2f, 0.08f, light, 8);
                break;
            case Lamps.Flood:
                Drum(s, s.At, 0.15f, 0, 8, iron, 6);
                Box(s, s.At, 0.6f, 0.4f, 8, 0.4f, light);
                break;
        }
    }
}
