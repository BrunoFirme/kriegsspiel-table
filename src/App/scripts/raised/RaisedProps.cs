using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedProps
{
    const int Round = 8;
    const float Thin = 0.08f;

    static readonly Color Wood = new(0.42f, 0.32f, 0.22f);
    static readonly Color Stone = new(0.62f, 0.6f, 0.55f);
    static readonly Color Straw = new(0.72f, 0.6f, 0.36f);
    static readonly Color Iron = new(0.2f, 0.2f, 0.21f);
    static readonly Color Water = new(0.22f, 0.32f, 0.36f);
    static readonly Color Earth = new(0.3f, 0.24f, 0.17f);
    static readonly Color Clay = new(0.66f, 0.4f, 0.28f);
    static readonly Color[] Cloths = [new(0.62f, 0.2f, 0.16f), new(0.78f, 0.62f, 0.32f), new(0.36f, 0.42f, 0.5f), new(0.34f, 0.44f, 0.28f), new(0.86f, 0.82f, 0.72f)];

    readonly record struct Spot(RaisedMesh M, Vector3 At, Vector3 X, Vector3 Z, float Pick)
    {
        public float U(float metres) => metres * M.PerMetre;
        public Vector3 Off(float xM, float zM) => At + X * U(xM) + Z * U(zM);
    }

    static RaisedMesh.Paint P(Color tint, int code) => new(tint, code);

    static RaisedMesh.Paint WoodPaint => P(Wood, RaisedCodes.Timber);

    static RaisedMesh.Paint StonePaint => P(Stone, (int)Fabric.Stone);

    public static bool Add(RaisedMesh m, string cls, Vector3 at, Vector3 across, float pick)
    {
        var x = across with { Y = 0 };
        if (x.LengthSquared() < 1e-12f) x = Vector3.Right;
        x = x.Normalized();
        var s = new Spot(m, at, x, new Vector3(-x.Z, 0, x.X), pick);
        var cloth = P(Cloths[Mathf.Clamp((int)(pick * Cloths.Length), 0, Cloths.Length - 1)], RaisedCodes.Cloth);
        switch (cls)
        {
            case "cart" or Markets.Barrow:
                Cart(s, cls == Markets.Barrow);
                return true;
            case "barrels" or "vats":
                foreach (var (dx, dz) in new[] { (0.5f, 0f), (-0.45f, 0.3f), (0f, -0.55f) }) Drum(s, s.Off(dx, dz), cls == "vats" ? 0.6f : 0.45f, 0, 1, WoodPaint);
                return true;
            case "crates":
                Box(s, s.Off(-0.5f, 0), 0.95f, 0.95f, 0, 0.95f, P(Wood.Lightened(0.15f), RaisedCodes.Timber));
                Box(s, s.Off(0.55f, 0.2f), 0.8f, 0.8f, 0, 0.8f, P(Wood.Lightened(0.25f), RaisedCodes.Timber));
                return true;
            case "trough":
                Box(s, s.At, 2.2f, 0.75f, 0, 0.6f, StonePaint);
                Box(s, s.At, 1.8f, 0.4f, 0.55f, 0.06f, P(Water, RaisedCodes.Water));
                return true;
            case "fountain":
                Drum(s, s.At, 1.8f, 0, 0.55f, StonePaint);
                Drum(s, s.At, 1.4f, 0.5f, 0.06f, P(Water, RaisedCodes.Water));
                Drum(s, s.At, 0.35f, 0, 1.6f, StonePaint);
                Drum(s, s.At, 0.75f, 1.3f, 0.2f, StonePaint);
                return true;
            case "woodpile" or "logs":
                Box(s, s.At, cls == "logs" ? 3 : 2.2f, 0.8f, 0, cls == "logs" ? 0.7f : 1.2f, P(Wood.Lightened(0.1f), RaisedCodes.Bark));
                return true;
            case "hive" or "sacks" or "stooks" or "dungheap" or "oven":
                Heap(s, cls);
                return true;
            case "boat" or "ferryboat":
                Hull(s, cls == "ferryboat" ? 4 : 2.2f, cls == "ferryboat" ? 1.6f : 0.75f);
                return true;
            case "gallows":
                Box(s, s.At, 0.25f, 0.25f, 0, 3.2f, WoodPaint);
                Box(s, s.Off(1.2f, 0), 2.6f, 0.2f, 3, 0.2f, WoodPaint);
                return true;
            case "rock":
                Blob(s, s.At, 0.9f + 0.7f * s.Pick, 0.9f, StonePaint);
                return true;
            case "gun":
                Box(s, s.Off(-0.5f, 0), 1.5f, 0.7f, 0.3f, 0.5f, WoodPaint);
                Axle(s, s.Off(0.4f, 0), s.X, 0.16f, 2.2f, P(Iron, RaisedCodes.Iron), 0.9f);
                foreach (int side in new[] { 1, -1 }) Axle(s, s.Off(-0.2f, 0.6f * side), s.Z, 0.6f, 0.12f, WoodPaint, 0.6f);
                return true;
            case "well" or "pump":
                Well(s, cls == "pump");
                return true;
            case "rick":
                RaisedRoofs.Turned(s.M, s.At, Vector3.Right * s.U(2.4f), Vector3.Back * s.U(2.4f), s.U(3.2f), [new(1, 0), new(1, 0.45f), new(0.75f, 0.75f), new(0, 1)], P(Straw, RaisedCodes.Straw));
                return true;
            case "tower":
                Drum(s, s.At, 3.3f, 0, 9, StonePaint);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(9), Vector3.Right * s.U(3.6f), Vector3.Back * s.U(3.6f), s.U(3.5f), [new(1, 0), new(0, 1)], P(RaisedColours.Roof(Cover.Slate, s.Pick), RaisedCodes.Roof + (int)Cover.Slate));
                return true;
            case "bridge":
                Bridge(s);
                return true;
            case "shrine" or "sentrybox":
                Box(s, s.At, 1.1f, 0.9f, 0, cls == "shrine" ? 1.5f : 2.3f, cls == "shrine" ? StonePaint : WoodPaint);
                return true;
            case "flag":
                Drum(s, s.At, 0.07f, 0, 7, WoodPaint, 6);
                Box(s, s.Off(0.8f, 0), 1.5f, Thin, 5.8f, 1, cloth);
                return true;
            case "tent":
                Tent(s, cloth with { Tint = Cloths[^1] });
                return true;
            case "marketcross" or "statue" or "obelisk" or "menhir" or "altar":
                Monument(s, cls);
                return true;
            case "jars" or Markets.Jars:
                foreach (var (dx, dz) in new[] { (0.4f, 0f), (-0.35f, 0.3f), (0f, -0.4f) })
                    RaisedRoofs.Turned(s.M, s.Off(dx, dz), Vector3.Right * s.U(0.35f), Vector3.Back * s.U(0.35f), s.U(0.9f), [new(0.5f, 0), new(1, 0.45f), new(0.4f, 1)], P(Clay, RaisedCodes.Earth), Round);
                return true;
            case "bench":
                Box(s, s.At, 1.8f, 0.4f, 0, 0.45f, WoodPaint);
                return true;
        }
        return Ware(s, cls, cloth);
    }

    static bool Ware(Spot s, string cls, RaisedMesh.Paint cloth)
    {
        switch (cls)
        {
            case Markets.Stall:
                Box(s, s.At, 3.6f, 2.4f, 0.8f, 0.1f, WoodPaint);
                foreach (var (dx, dz) in new[] { (1.7f, 1.1f), (-1.7f, 1.1f), (1.7f, -1.1f), (-1.7f, -1.1f) }) Box(s, s.Off(dx, dz), 0.1f, 0.1f, 0, 2.2f, WoodPaint);
                Canopy(s, 3.9f, 2.7f, 2.2f, cloth);
                return true;
            case Markets.Booth or Markets.Kiosk:
                Box(s, s.At, cls == Markets.Kiosk ? 2.4f : 3.6f, 2.2f, 0, 2.3f, WoodPaint);
                Canopy(s, cls == Markets.Kiosk ? 2.8f : 4, 2.6f, 2.3f, cloth);
                return true;
            case Markets.Trestle:
                Box(s, s.At, 2.6f, 1, 0.75f, 0.08f, WoodPaint);
                foreach (int side in new[] { 1, -1 }) Box(s, s.Off(1.1f * side, 0), 0.1f, 0.9f, 0, 0.75f, WoodPaint);
                return true;
            case Markets.Rugs:
                Box(s, s.At, 2.3f, 1.5f, 0, 0.04f, cloth);
                return true;
            case Markets.Baskets:
                foreach (var (dx, dz) in new[] { (0.4f, 0f), (-0.35f, 0.35f), (-0.1f, -0.4f) }) Drum(s, s.Off(dx, dz), 0.3f, 0, 0.4f, P(Straw, RaisedCodes.Straw));
                return true;
            case Markets.Parasol:
                Drum(s, s.At, 0.05f, 0, 2.3f, WoodPaint, 6);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(1.9f), Vector3.Right * s.U(1.6f), Vector3.Back * s.U(1.6f), s.U(0.5f), [new(1, 0), new(0, 1)], cloth, Round);
                return true;
            case Markets.Hurdles:
                foreach (var (dx, dz, along) in new[] { (0f, 1.5f, true), (0f, -1.5f, true), (1.5f, 0f, false), (-1.5f, 0f, false) })
                    Box(s, s.Off(dx, dz), along ? 3 : Thin, along ? Thin : 3, 0, 0.9f, WoodPaint);
                return true;
        }
        return false;
    }

    static void Box(Spot s, Vector3 centre, float longM, float wideM, float bottomM, float highM, RaisedMesh.Paint paint)
    {
        Vector3 x = s.X * s.U(longM / 2), z = s.Z * s.U(wideM / 2);
        RaisedRoofs.Block(s.M, [centre - x - z, centre + x - z, centre + x + z, centre - x + z], centre.Y + s.U(bottomM), centre.Y + s.U(bottomM + highM), paint);
    }

    static void Drum(Spot s, Vector3 centre, float radiusM, float bottomM, float highM, RaisedMesh.Paint paint, int sides = Round) =>
        RaisedRoofs.Turned(s.M, centre + Vector3.Up * s.U(bottomM), Vector3.Right * s.U(radiusM), Vector3.Back * s.U(radiusM), s.U(highM), [new(1, 0), new(1, 1), new(0, 1)], paint, sides);

    static void Blob(Spot s, Vector3 centre, float radiusM, float highM, RaisedMesh.Paint paint) =>
        RaisedRoofs.Turned(s.M, centre, Vector3.Right * s.U(radiusM), Vector3.Back * s.U(radiusM * 0.85f), s.U(highM), [new(1, 0), new(0.9f, 0.5f), new(0.5f, 0.9f), new(0, 1)], paint, 7);

    static void Axle(Spot s, Vector3 centre, Vector3 axis, float radiusM, float lengthM, RaisedMesh.Paint paint, float liftM)
    {
        var a = axis.Normalized();
        var side = a.Cross(Vector3.Up).Normalized();
        var middle = centre + Vector3.Up * s.U(liftM);
        var ring = new List<Vector3>();
        for (int i = 0; i < Round; i++)
        {
            float t = i * Mathf.Tau / Round;
            ring.Add((side * Mathf.Cos(t) + Vector3.Up * Mathf.Sin(t)) * s.U(radiusM));
        }
        var half = a * s.U(lengthM / 2);
        for (int i = 0; i < Round; i++)
        {
            Vector3 p = ring[i], q = ring[(i + 1) % Round];
            s.M.Face([middle - half + p, middle + half + p, middle + half + q, middle - half + q], p + q, a, middle - half + p, paint);
        }
        var front = new Vector3[Round];
        var back = new Vector3[Round];
        for (int i = 0; i < Round; i++)
        {
            front[i] = middle + half + ring[i];
            back[i] = middle - half + ring[i];
        }
        s.M.Face(front, a, side, front[0], paint);
        s.M.Face(back, -a, side, back[0], paint);
    }

    static void Cart(Spot s, bool barrow)
    {
        float longM = barrow ? 1.4f : 3.2f, wideM = barrow ? 0.6f : 1.5f;
        Box(s, s.At, longM, wideM, barrow ? 0.4f : 0.6f, barrow ? 0.4f : 0.6f, P(Wood.Lightened(0.1f), RaisedCodes.Timber));
        if (barrow)
        {
            Axle(s, s.Off(longM / 2, 0), s.Z, 0.3f, 0.1f, WoodPaint, 0.3f);
            return;
        }
        foreach (int end in new[] { 1, -1 }) Axle(s, s.Off(end * longM * 0.3f, 0), s.Z, 0.55f, wideM * 1.25f, WoodPaint, 0.55f);
        Box(s, s.Off(longM / 2 + 0.9f, 0), 1.8f, 0.1f, 0.6f, 0.1f, WoodPaint);
    }

    static void Heap(Spot s, string cls)
    {
        var (radius, high, paint) = cls switch
        {
            "hive" => (0.35f, 0.6f, P(Straw, RaisedCodes.Straw)),
            "stooks" => (0.5f, 1.2f, P(Straw, RaisedCodes.Straw)),
            "dungheap" => (1.2f, 0.8f, P(Earth, RaisedCodes.Earth)),
            "oven" => (1.4f, 1.6f, P(Clay.Darkened(0.1f), RaisedCodes.Earth)),
            _ => (0.35f, 0.6f, P(Cloths[^1].Darkened(0.2f), RaisedCodes.Cloth)),
        };
        if (cls == "sacks")
            foreach (var (dx, dz) in new[] { (0.35f, 0f), (-0.3f, 0.3f), (0f, -0.35f) }) Blob(s, s.Off(dx, dz), radius, high, paint);
        else if (cls == "stooks") RaisedRoofs.Turned(s.M, s.At, Vector3.Right * s.U(radius), Vector3.Back * s.U(radius), s.U(high), [new(1, 0), new(0, 1)], paint, 6);
        else Blob(s, s.At, radius, high, paint);
    }

    static void Hull(Spot s, float halfM, float beamM)
    {
        Vector3 bow = s.X * s.U(halfM), beam = s.Z * s.U(beamM);
        var at = s.At;
        Vector3[] hull = [at + bow, at + bow * 0.62f + beam * 0.78f, at + bow * 0.1f + beam, at - bow * 0.7f + beam * 0.86f, at - bow + beam * 0.5f,
            at - bow - beam * 0.5f, at - bow * 0.7f - beam * 0.86f, at + bow * 0.1f - beam, at + bow * 0.62f - beam * 0.78f];
        RaisedRoofs.Block(s.M, hull, at.Y - s.U(0.2f), at.Y + s.U(0.45f), P(Wood.Lightened(0.12f), RaisedCodes.Timber));
    }

    static void Well(Spot s, bool pump)
    {
        if (pump)
        {
            Box(s, s.At, 0.4f, 0.4f, 0, 1.4f, WoodPaint);
            Box(s, s.Off(0.35f, 0), 0.4f, 0.08f, 1, 0.08f, P(Iron, RaisedCodes.Iron));
            return;
        }
        Drum(s, s.At, 1.2f, 0, 0.9f, StonePaint);
        Drum(s, s.At, 0.9f, 0.85f, 0.06f, P(Water.Darkened(0.5f), RaisedCodes.Water));
        foreach (int side in new[] { 1, -1 }) Box(s, s.Off(0, 1.1f * side), 0.15f, 0.15f, 0, 2.3f, WoodPaint);
        var frame = RaisedFrame.Of([s.Off(-0.9f, -1.4f), s.Off(0.9f, -1.4f), s.Off(0.9f, 1.4f), s.Off(-0.9f, 1.4f)]);
        RaisedRoofs.Gable(new RaisedRoofs.Raise(s.M, frame, s.At.Y + s.U(2.3f), s.At.Y + s.U(2.3f), WoodPaint, P(RaisedColours.Roof(Cover.Shingle, s.Pick), RaisedCodes.Roof + (int)Cover.Shingle), 0.7f));
    }

    static void Bridge(Spot s)
    {
        float deck = 1.6f;
        Box(s, s.At, 14, 7.2f, deck - 0.5f, 0.5f, StonePaint);
        foreach (int side in new[] { 1, -1 }) Box(s, s.Off(0, 3.4f * side), 14, 0.4f, deck, 0.9f, StonePaint);
        foreach (int end in new[] { 1, -1 }) Box(s, s.Off(4.5f * end, 0), 1.2f, 6.8f, -1.5f, deck + 1, StonePaint);
    }

    static void Tent(Spot s, RaisedMesh.Paint cloth)
    {
        var frame = RaisedFrame.Of([s.Off(-1.6f, -1.2f), s.Off(1.6f, -1.2f), s.Off(1.6f, 1.2f), s.Off(-1.6f, 1.2f)]);
        RaisedRoofs.Gable(new RaisedRoofs.Raise(s.M, frame, s.At.Y + s.U(0.25f), s.At.Y, cloth, cloth, 1.1f));
    }

    static void Canopy(Spot s, float longM, float wideM, float atM, RaisedMesh.Paint cloth)
    {
        var frame = RaisedFrame.Of([s.Off(-longM / 2, -wideM / 2), s.Off(longM / 2, -wideM / 2), s.Off(longM / 2, wideM / 2), s.Off(-longM / 2, wideM / 2)]);
        RaisedRoofs.Gable(new RaisedRoofs.Raise(s.M, frame, s.At.Y + s.U(atM), s.At.Y + s.U(atM), cloth, cloth, 0.35f));
    }

    static void Monument(Spot s, string cls)
    {
        switch (cls)
        {
            case "obelisk":
                Box(s, s.At, 1.6f, 1.6f, 0, 0.5f, StonePaint);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(0.5f), Vector3.Right * s.U(0.6f), Vector3.Back * s.U(0.6f), s.U(9), [new(1, 0), new(0.62f, 0.93f), new(0, 1)], StonePaint, 4);
                break;
            case "menhir":
                RaisedRoofs.Turned(s.M, s.At, s.X * s.U(0.5f), s.Z * s.U(0.8f), s.U(3), [new(1, 0), new(0.85f, 0.7f), new(0.4f, 1)], P(Stone.Darkened(0.12f), (int)Fabric.Stone), 6);
                break;
            case "altar":
                Box(s, s.At, 1.4f, 1, 0, 1, StonePaint);
                break;
            default:
                Box(s, s.At, 2.4f, 2.4f, 0, 0.5f, StonePaint);
                Box(s, s.At, 1.4f, 1.4f, 0.5f, 0.4f, StonePaint);
                Drum(s, s.At, cls == "statue" ? 0.4f : 0.22f, 0.9f, cls == "statue" ? 1.9f : 3.5f, cls == "statue" ? P(new Color(0.36f, 0.42f, 0.38f), RaisedCodes.Iron) : StonePaint, 8);
                break;
        }
    }
}
