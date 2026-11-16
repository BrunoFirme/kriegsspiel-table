using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class RaisedArcades
{
    const int ArcSteps = 6;
    const float SinkM = 0.6f, KeyM = 0.4f, LeastOpenM = 1.2f, AqueductWideM = 2.6f, AqueductBayM = 6, AqueductPierM = 1.6f, ChannelM = 1.4f, AqueductLiftM = 3;
    const float BridgeWideM = 7.2f, BridgeBayM = 9, BridgePierM = 1.8f, BridgeDeckM = 1.2f, BridgeLiftM = 1.6f, RiseShare = 0.1f, MostRiseM = 8, WaterDeepM = 2, BankM = 0.3f;
    const float ParapetM = 0.9f, ParapetWideM = 0.4f, AbutmentM = 3;
    const float PlankLiftM = 0.8f, PlankM = 0.3f, PlankWideM = 4.5f, PostM = 0.35f, PostHighM = 4.5f;

    public static void Prism(RaisedMesh m, Vector3 a, Vector3 b, Vector3 side, float lowA, float topA, float lowB, float topB, RaisedMesh.Paint paint, bool ends = true)
    {
        var along = (b - a) with { Y = 0 };
        Vector3 la = a - side, ra = a + side, lb = b - side, rb = b + side;
        m.Face([la with { Y = topA }, lb with { Y = topB }, rb with { Y = topB }, ra with { Y = topA }], Vector3.Up, along, la with { Y = topA }, paint);
        m.Face([la with { Y = lowA }, lb with { Y = lowB }, lb with { Y = topB }, la with { Y = topA }], -side, along, la with { Y = lowA }, paint);
        m.Face([ra with { Y = lowA }, rb with { Y = lowB }, rb with { Y = topB }, ra with { Y = topA }], side, along, ra with { Y = lowA }, paint);
        if (!ends) return;
        m.Face([la with { Y = lowA }, ra with { Y = lowA }, ra with { Y = topA }, la with { Y = topA }], -along, side, la with { Y = lowA }, paint);
        m.Face([lb with { Y = lowB }, rb with { Y = lowB }, rb with { Y = topB }, lb with { Y = topB }], along, side, lb with { Y = lowB }, paint);
    }

    static void Run(RaisedMesh m, Vector3 a, Vector3 b, float deckA, float deckB, Func<Vector3, float> ground, float halfWide, float bayM, float pierM, float bandM,
        RaisedMesh.Paint paint, bool pierAtEnds, float deepM = 0)
    {
        float upm = m.PerMetre, length = ((b - a) with { Y = 0 }).Length();
        if (length < 1e-6f) return;
        var along = ((b - a) with { Y = 0 }) / length;
        var side = new Vector3(-along.Z, 0, along.X) * halfWide;
        int bays = Math.Max(1, (int)Math.Round(length / (bayM * upm)));
        float bay = length / bays, pier = pierM * upm, band = bandM * upm, sink = SinkM * upm;
        float Deck(float x) => Mathf.Lerp(deckA, deckB, x / length);
        Vector3 At(float x) => a + along * x;
        Prism(m, a, b, side, deckA - band, deckA, deckB - band, deckB, paint);
        for (int k = 0; k <= bays; k++)
        {
            if (!pierAtEnds && (k == 0 || k == bays)) continue;
            float x = k * bay, x0 = Mathf.Max(0, x - pier / 2), x1 = Mathf.Min(length, x + pier / 2);
            float foot = Mathf.Min(ground(At(x0)), ground(At(x1))) - sink;
            if (Deck(x) - band > foot) Prism(m, At(x0), At(x1), side, foot, Deck(x0) - band, foot, Deck(x1) - band, paint);
        }
        for (int k = 0; k < bays; k++)
        {
            float x0 = k * bay + pier / 2, x1 = (k + 1) * bay - pier / 2;
            if (x1 <= x0) continue;
            float r = (x1 - x0) / 2, mid = (x0 + x1) / 2, top = Deck(mid) - band, floor = ground(At(mid)) - deepM * upm, spring = top - r - KeyM * upm;
            if (spring < floor + LeastOpenM * upm)
            {
                if (top > floor - sink) Prism(m, At(x0), At(x1), side, floor - sink, Deck(x0) - band, floor - sink, Deck(x1) - band, paint, false);
                continue;
            }
            Arch(m, At(mid), along, side, r, spring, top, paint);
        }
    }

    static void Arch(RaisedMesh m, Vector3 centre, Vector3 along, Vector3 side, float r, float spring, float top, RaisedMesh.Paint paint)
    {
        var arc = new List<Vector3>();
        for (int i = 0; i <= ArcSteps; i++)
        {
            float t = Mathf.Pi * (1 - i / (float)ArcSteps);
            arc.Add(centre + along * (Mathf.Cos(t) * r) + Vector3.Up * (spring - centre.Y + Mathf.Sin(t) * r));
        }
        for (int i = 0; i < ArcSteps; i++)
        {
            Vector3 p = arc[i], q = arc[i + 1];
            foreach (int face in new[] { 1, -1 })
            {
                var o = side * face;
                m.Face([p + o, q + o, q with { Y = top } + o, p with { Y = top } + o], o, along, p + o, paint);
            }
            var inward = (centre with { Y = spring } - (p + q) / 2).Normalized();
            m.Face([p - side, q - side, q + side, p + side], inward, along, p - side, paint);
        }
    }

    public static void Gateway(RaisedMesh m, Vector3 a, Vector3 b, float top, float floor, float halfWide, float passageM, float bandM, RaisedMesh.Paint paint)
    {
        float length = ((b - a) with { Y = 0 }).Length() / m.PerMetre;
        Run(m, a, b, top, top, _ => floor, halfWide, length, length - passageM, bandM, paint, true);
    }

    public static void Aqueduct(RaisedMesh m, Vector3[] world, Func<Vector3, float> ground, RaisedMesh.Paint stone)
    {
        float upm = m.PerMetre, total = 0;
        var at = new float[world.Length];
        for (int i = 1; i < world.Length; i++) at[i] = total += ((world[i] - world[i - 1]) with { Y = 0 }).Length();
        if (total <= 0) return;
        float from = ground(world[0]), to = ground(world[^1]);
        var deck = new float[world.Length];
        for (int i = 0; i < world.Length; i++)
            deck[i] = Mathf.Max(Mathf.Lerp(from, to, at[i] / total) + AqueductLiftM * upm, ground(world[i]) + (ChannelM + LeastOpenM) * upm);
        for (int i = 0; i + 1 < world.Length; i++)
            Run(m, world[i], world[i + 1], deck[i], deck[i + 1], ground, AqueductWideM / 2 * upm, AqueductBayM, AqueductPierM, ChannelM, stone, true);
    }

    public static void Bridge(RaisedMesh m, Vector3 centre, Vector3 axis, float spanM, Func<Vector3, float> ground, RaisedMesh.Paint stone)
    {
        float upm = m.PerMetre, half = (spanM / 2 + AbutmentM) * upm;
        var flat = (axis with { Y = 0 }).Normalized();
        Vector3 a = centre - flat * half, b = centre + flat * half;
        float lowA = ground(a) + BankM * upm, lowB = ground(b) + BankM * upm;
        float crown = Mathf.Max(ground(a), ground(b)) + Mathf.Min(BridgeLiftM + spanM * RiseShare, MostRiseM) * upm;
        var side = new Vector3(-flat.Z, 0, flat.X) * (BridgeWideM / 2 * upm);
        Run(m, a, centre, lowA, crown, ground, BridgeWideM / 2 * upm, BridgeBayM, BridgePierM, BridgeDeckM, stone, true, WaterDeepM);
        Run(m, centre, b, crown, lowB, ground, BridgeWideM / 2 * upm, BridgeBayM, BridgePierM, BridgeDeckM, stone, true, WaterDeepM);
        foreach (int face in new[] { 1, -1 })
        {
            var rail = side * face - side.Normalized() * (face * ParapetWideM / 2 * upm);
            var thick = side.Normalized() * (ParapetWideM / 2 * upm);
            Prism(m, a + rail, centre + rail, thick, lowA, lowA + ParapetM * upm, crown, crown + ParapetM * upm, stone);
            Prism(m, centre + rail, b + rail, thick, crown, crown + ParapetM * upm, lowB, lowB + ParapetM * upm, stone);
        }
    }

    public static void Drawbridge(RaisedMesh m, Vector3 centre, Vector3 axis, float spanM, Func<Vector3, float> ground, RaisedMesh.Paint wood, RaisedMesh.Paint iron)
    {
        float upm = m.PerMetre, half = spanM / 2 * upm;
        var flat = (axis with { Y = 0 }).Normalized();
        Vector3 a = centre - flat * half, b = centre + flat * half, side = new Vector3(-flat.Z, 0, flat.X) * (PlankWideM / 2 * upm);
        float deckA = ground(a) + PlankLiftM * upm, deckB = ground(b) + PlankLiftM * upm;
        Prism(m, a, b, side, deckA - PlankM * upm, deckA, deckB - PlankM * upm, deckB, wood);
        foreach (int face in new[] { 1, -1 })
        {
            var post = b + side * face * 1.1f;
            var x = flat * (PostM / 2 * upm);
            var z = side.Normalized() * (PostM / 2 * upm);
            RaisedRoofs.Block(m, [post - x - z, post + x - z, post + x + z, post - x + z], ground(post) - SinkM * upm, ground(post) + PostHighM * upm, wood);
            var chain = side * face * 1.02f;
            Prism(m, a + chain, b + chain + Vector3.Up * ((PostHighM - PlankLiftM) * upm), z * 0.2f, deckA, deckA + 0.06f * upm, deckB + (PostHighM - PlankLiftM) * upm,
                deckB + (PostHighM - PlankLiftM + 0.06f) * upm, iron);
        }
    }
}
