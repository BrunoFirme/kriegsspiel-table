using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static partial class RaisedProps
{
    const float HerdTurn = 2.4f, HerdApart = 1.15f, FowlApartM = 0.7f;

    static readonly Color Hide = new(0.45f, 0.36f, 0.26f);
    static readonly Color Fleece = new(0.84f, 0.8f, 0.7f);
    static readonly Color Swine = new(0.72f, 0.56f, 0.5f);
    static readonly Color Dun = new(0.52f, 0.5f, 0.46f);
    static readonly Color Sand = new(0.74f, 0.62f, 0.42f);
    static readonly Color Bay = new(0.36f, 0.22f, 0.14f);
    static readonly Color Net = new(0.5f, 0.46f, 0.36f);

    static readonly Dictionary<string, bool> Known = new();

    public static bool Modelled(string cls)
    {
        lock (Known)
        {
            if (!Known.TryGetValue(cls, out bool modelled))
                Known[cls] = modelled = cls is "tree" or "tower" || cls is not ("bridge" or BuiltShapes.Drawbridge) && Add(new RaisedMesh(), cls, Vector3.Zero, Vector3.Right, 0.5f);
            return modelled;
        }
    }

    readonly record struct Build(float LongM, float HighM, float WideM, float LegM, float NeckM, Color Tint, float HumpM = 0);

    static bool Life(Spot s, string cls)
    {
        switch (cls)
        {
            case "dog":
                Herd(s, 1, new Build(0.75f, 0.5f, 0.26f, 0.28f, 0.1f, Hide));
                return true;
            case "pigs":
                Herd(s, 3, new Build(1.1f, 0.62f, 0.45f, 0.26f, -0.08f, Swine));
                return true;
            case "sheep":
                Herd(s, 4, new Build(1, 0.7f, 0.5f, 0.32f, 0.05f, Fleece));
                return true;
            case "cattle":
                Herd(s, 3, new Build(2.1f, 1.3f, 0.7f, 0.65f, 0.1f, Hide.Darkened(0.15f)));
                return true;
            case "horses":
                Herd(s, 2, new Build(2.1f, 1.5f, 0.55f, 0.85f, 0.55f, Bay));
                return true;
            case Streetlife.Donkeys:
                Herd(s, 2, new Build(1.5f, 1.1f, 0.45f, 0.6f, 0.3f, Dun));
                return true;
            case Streetlife.Camels:
                Herd(s, 2, new Build(2.5f, 1.85f, 0.65f, 1.15f, 0.7f, Sand, 0.45f));
                return true;
            case "hens":
                Fowl(s, 4, 0.2f, Hide.Lightened(0.25f));
                return true;
            case "geese":
                Fowl(s, 3, 0.32f, Fleece.Lightened(0.3f));
                return true;
            case "racks":
                foreach (int end in new[] { 1, -1 }) Box(s, s.Off(1.4f * end, 0), 0.1f, 0.1f, 0, 1.7f, WoodPaint);
                Box(s, s.At, 2.9f, 0.08f, 1.6f, 0.08f, WoodPaint);
                Box(s, s.At, 2.5f, Thin, 0.7f, 0.9f, P(Hide.Lightened(0.2f), RaisedCodes.Cloth));
                return true;
            case "nets":
                foreach (int end in new[] { 1, -1 }) Box(s, s.Off(1.8f * end, 0), 0.1f, 0.1f, 0, 1.9f, WoodPaint);
                Box(s, s.At, 3.6f, Thin, 0.3f, 1.5f, P(Net, RaisedCodes.Cloth));
                return true;
            case "anvil":
                Box(s, s.At, 0.5f, 0.5f, 0, 0.55f, WoodPaint);
                Box(s, s.At, 0.7f, 0.25f, 0.55f, 0.25f, P(Iron, RaisedCodes.Iron));
                return true;
            case "wheels":
                foreach (var (dx, lean) in new[] { (-0.5f, 0.55f), (0.5f, 0.6f) }) Axle(s, s.Off(dx, 0), s.Z, lean, 0.1f, WoodPaint, lean);
                return true;
            case "wheel":
                Axle(s, s.At, s.Z, 2.2f, 0.9f, WoodPaint, 1.6f);
                return true;
            case "sweep":
                Drum(s, s.At, 0.9f, 0, 0.7f, StonePaint);
                Box(s, s.Off(1.8f, 0), 0.22f, 0.22f, 0, 3.6f, WoodPaint);
                Box(s, s.Off(0.6f, 0), 5.2f, 0.12f, 3.5f, 0.12f, WoodPaint);
                return true;
            case "cistern":
                Drum(s, s.At, 2.4f, 0, 0.9f, StonePaint, 10);
                Drum(s, s.At, 2.1f, 0.85f, 0.06f, P(Water, RaisedCodes.Water), 10);
                return true;
            case "watertower":
                foreach (var (dx, dz) in new[] { (1.5f, 1.5f), (-1.5f, 1.5f), (1.5f, -1.5f), (-1.5f, -1.5f) }) Box(s, s.Off(dx, dz), 0.3f, 0.3f, 0, 9, P(Iron, RaisedCodes.Iron));
                Drum(s, s.At, 2.6f, 9, 3.4f, P(Iron.Lightened(0.25f), RaisedCodes.Iron), 10);
                RaisedRoofs.Turned(s.M, s.At + Vector3.Up * s.U(12.4f), Vector3.Right * s.U(2.8f), Vector3.Back * s.U(2.8f), s.U(1.1f), [new(1, 0), new(0, 1)], P(Iron, RaisedCodes.Iron), 10);
                return true;
            case "clamp":
                Blob(s, s.At, 2.6f, 1.9f, P(Earth.Darkened(0.2f), RaisedCodes.Earth));
                return true;
            case "mound":
                Blob(s, s.At, 5, 2.2f, P(Turf, RaisedCodes.Earth));
                return true;
            case "ovoo":
                Blob(s, s.At, 1.8f, 1.5f, StonePaint);
                Drum(s, s.At, 0.06f, 1.3f, 2, WoodPaint, 6);
                return true;
            case "stocks":
                foreach (int end in new[] { 1, -1 }) Box(s, s.Off(0.9f * end, 0), 0.15f, 0.15f, 0, 1.3f, WoodPaint);
                Box(s, s.At, 2, 0.12f, 0.8f, 0.45f, WoodPaint);
                return true;
            case "roadblock":
                foreach (int end in new[] { 1, -1 }) Box(s, s.Off(1.6f * end, 0), 0.5f, 0.9f, 0, 0.9f, WoodPaint);
                Box(s, s.At, 3.6f, 0.14f, 0.85f, 0.14f, P(Cloths[0], RaisedCodes.Timber));
                return true;
            case "lookout":
                foreach (var (dx, dz) in new[] { (1, 1f), (-1, 1f), (1, -1f), (-1, -1f) }) Box(s, s.Off(dx, dz), 0.2f, 0.2f, 0, 6, WoodPaint);
                Box(s, s.At, 2.6f, 2.6f, 6, 0.15f, WoodPaint);
                Canopy(s with { At = s.At + Vector3.Up * s.U(6) }, 3, 3, 2.1f, P(Straw, RaisedCodes.Straw));
                return true;
            case "plough":
                Box(s, s.At, 1.8f, 0.12f, 0.35f, 0.12f, WoodPaint);
                Box(s, s.Off(-0.7f, 0), 0.5f, 0.1f, 0, 0.45f, P(Iron, RaisedCodes.Iron));
                return true;
        }
        return false;
    }

    static void Herd(Spot s, int most, Build build)
    {
        int count = most <= 1 ? 1 : 1 + (int)(s.Pick * 7.3f % 1 * most);
        for (int i = 0; i < Mathf.Min(count, most); i++)
        {
            float bearing = s.Pick * Mathf.Tau + i * HerdTurn, facing = bearing * 1.7f + i;
            var at = i == 0 ? s.At : s.At + (s.X * Mathf.Cos(bearing) + s.Z * Mathf.Sin(bearing)) * s.U(build.LongM * HerdApart);
            var ahead = s.X * Mathf.Cos(facing) + s.Z * Mathf.Sin(facing);
            Beast(s with { At = at, X = ahead, Z = new Vector3(-ahead.Z, 0, ahead.X) }, build);
        }
    }

    static void Beast(Spot b, Build k)
    {
        var hide = P(k.Tint, RaisedCodes.Cloth);
        float leg = Mathf.Max(0.07f, k.WideM * 0.22f), body = k.HighM - k.LegM, head = k.LongM * 0.28f;
        var dark = P(k.Tint.Darkened(0.2f), RaisedCodes.Cloth);
        foreach (var (dx, dz) in new[] { (0.36f, 0.3f), (0.36f, -0.3f), (-0.36f, 0.3f), (-0.36f, -0.3f) }) Box(b, b.Off(dx * k.LongM, dz * k.WideM), leg, leg, 0, k.LegM, dark);
        Box(b, b.At, k.LongM, k.WideM, k.LegM, body, hide);
        if (k.HumpM > 0) Box(b, b.Off(-0.05f * k.LongM, 0), k.LongM * 0.4f, k.WideM * 0.8f, k.HighM, k.HumpM, hide);
        if (k.NeckM > 0.2f) Box(b, b.Off(k.LongM * 0.46f, 0), leg * 1.6f, k.WideM * 0.5f, k.LegM + body * 0.5f, body * 0.5f + k.NeckM, hide);
        Box(b, b.Off(k.LongM * 0.5f + head * 0.4f, 0), head, k.WideM * 0.55f, k.LegM + body * 0.45f + k.NeckM, body * 0.55f, hide);
    }

    static void Fowl(Spot s, int most, float sizeM, Color tint)
    {
        int count = 2 + (int)(s.Pick * 5.1f % 1 * (most - 1));
        for (int i = 0; i < count; i++)
        {
            float bearing = s.Pick * Mathf.Tau + i * HerdTurn;
            var at = s.At + (s.X * Mathf.Cos(bearing) + s.Z * Mathf.Sin(bearing)) * s.U(FowlApartM * (0.4f + 0.3f * i));
            var ahead = s.X * Mathf.Cos(bearing * 2.3f) + s.Z * Mathf.Sin(bearing * 2.3f);
            var b = s with { At = at, X = ahead, Z = new Vector3(-ahead.Z, 0, ahead.X) };
            Box(b, b.At, sizeM * 1.4f, sizeM * 0.8f, sizeM * 0.5f, sizeM, P(tint, RaisedCodes.Cloth));
            Box(b, b.Off(sizeM * 0.7f, 0), sizeM * 0.45f, sizeM * 0.4f, sizeM * 1.2f, sizeM * 0.9f, P(tint.Darkened(0.1f), RaisedCodes.Cloth));
        }
    }
}
