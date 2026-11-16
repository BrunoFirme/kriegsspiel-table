using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedAreas
{
    const int TheatreTiers = 4, ArenaTiers = 3;
    const float TheatreTierShare = 0.16f, TheatreTierM = 2.2f, StageShare = 0.16f, ArenaTierShare = 0.17f, SinkM = 0.4f;
    const float BedShare = 0.26f, BedHedgeM = 0.4f, BedHedgeHighM = 0.55f, TopiaryM = 0.6f, TopiaryHighM = 2.2f;
    const float OrchardApartM = 6, OrchardCrownM = 2.3f, VineRowM = 2.3f, VineM = 0.5f, VineHighM = 1.3f, PieceM = 6, MostTrees = 400, MostVinePieces = 900;
    static readonly float[] ArenaTops = [12, 8.5f, 5];
    static readonly Color Vine = new(0.3f, 0.4f, 0.18f);

    public static void Add(RaisedChunk chunk, RaisedMesh m, BuiltPainter.Shape s, Vector3[] world, Func<Vector3, float> height, string? style)
    {
        if (world.Length < 3) return;
        int builders = RaisedLooks.Aged(style, s.Built), culture = RaisedLooks.Aged(style, s.Manner);
        float pick = BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, 41.3f);
        var stone = new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone);
        switch (s.Class)
        {
            case Hallmarks.Theatre:
                Theatre(m, world, height, stone);
                break;
            case "arena":
                Arena(m, world, height, stone);
                break;
            case Hallmarks.Parterre when world.Length == 4:
                Parterre(m.Small, world, height);
                break;
            case "orchard":
                Orchard(chunk, world, height, culture, m.PerMetre);
                break;
            case "vineyard" when world.Length == 4:
                Vineyard(m.Small, world, height);
                break;
            case "field" when Hedged(culture):
                var hedge = s with { Class = "hedge" };
                RaisedLines.Add(m.Small, hedge, [.. world, world[0]], height, style);
                break;
        }
    }

    static bool Hedged(int culture) => Manners.Kin(culture) is Manners.Continental or Manners.Western or Manners.Northern || culture == Manners.Gothic;

    static Vector3 Scaled(Vector3 centre, Vector3 p, float share) => centre + (p - centre) * share;

    static void Band(RaisedMesh m, Vector3 centre, Vector3 p, Vector3 q, float outer, float inner, float top, float low, RaisedMesh.Paint paint)
    {
        Vector3 o0 = Scaled(centre, p, outer), o1 = Scaled(centre, q, outer), i0 = Scaled(centre, p, inner), i1 = Scaled(centre, q, inner);
        m.Face([i0 with { Y = top }, o0 with { Y = top }, o1 with { Y = top }, i1 with { Y = top }], Vector3.Up, o1 - o0, o0 with { Y = top }, paint);
        var toward = (centre - (i0 + i1) / 2) with { Y = 0 };
        m.Face([i0 with { Y = low }, i1 with { Y = low }, i1 with { Y = top }, i0 with { Y = top }], toward, i1 - i0, i0 with { Y = low }, paint);
    }

    static void Theatre(RaisedMesh m, Vector3[] p, Func<Vector3, float> height, RaisedMesh.Paint stone)
    {
        var centre = (p[0] + p[^1]) / 2;
        float upm = m.PerMetre, ground = height(centre), sink = ground - SinkM * upm;
        var arc = p.Select(q => q with { Y = 0 }).ToArray();
        for (int t = 0; t < TheatreTiers; t++)
        {
            float outer = 1 - t * TheatreTierShare, inner = 1 - (t + 1) * TheatreTierShare;
            float top = ground + (TheatreTiers - t) * TheatreTierM * upm, low = t + 1 < TheatreTiers ? ground + (TheatreTiers - t - 1) * TheatreTierM * upm : sink;
            for (int i = 0; i + 1 < arc.Length; i++) Band(m, centre with { Y = 0 }, arc[i], arc[i + 1], outer, inner, top, low, stone);
            foreach (var end in new[] { arc[0], arc[^1] })
            {
                Vector3 o = Scaled(centre with { Y = 0 }, end, outer), n = Scaled(centre with { Y = 0 }, end, inner);
                var stageward = (centre - (p[p.Length / 2] with { Y = centre.Y })) with { Y = 0 };
                m.Face([o with { Y = sink }, n with { Y = sink }, n with { Y = top }, o with { Y = top }], stageward, n - o, o with { Y = sink }, stone);
            }
        }
        float outerTop = ground + TheatreTiers * TheatreTierM * upm;
        var arcade = stone with { Door = RaisedMesh.ArcadeDoor, Storeys = 2 };
        for (int i = 0; i + 1 < arc.Length; i++) m.Wall(arc[i], arc[i + 1], sink, outerTop, centre, arcade);
        Vector3 across = (arc[^1] - arc[0]) / 2, back = ((arc[arc.Length / 2] - (centre with { Y = 0 })) with { Y = 0 }).Normalized(), mid = centre with { Y = 0 };
        var stage = -back * (across.Length() * StageShare);
        Vector3[] scaena = [mid - across * 1.05f, mid + across * 1.05f, mid + across * 1.05f + stage, mid - across * 1.05f + stage];
        RaisedRoofs.Block(m, scaena, sink, ground + (TheatreTiers - 1) * TheatreTierM * upm, arcade);
    }

    static void Arena(RaisedMesh m, Vector3[] p, Func<Vector3, float> height, RaisedMesh.Paint stone)
    {
        var flat = p.Select(q => q with { Y = 0 }).ToArray();
        var centre = RaisedFrame.Middle(flat);
        float upm = m.PerMetre, ground = height(centre), sink = ground - SinkM * upm;
        for (int t = 0; t < ArenaTiers; t++)
        {
            float outer = 1 - t * ArenaTierShare, inner = 1 - (t + 1) * ArenaTierShare;
            float top = ground + ArenaTops[t] * upm, low = t + 1 < ArenaTiers ? ground + ArenaTops[t + 1] * upm : sink;
            for (int i = 0; i < flat.Length; i++) Band(m, centre, flat[i], flat[(i + 1) % flat.Length], outer, inner, top, low, stone);
        }
        var arcade = stone with { Door = RaisedMesh.ArcadeDoor, Storeys = 3 };
        for (int i = 0; i < flat.Length; i++) m.Wall(flat[i], flat[(i + 1) % flat.Length], sink, ground + ArenaTops[0] * upm, centre, arcade);
    }

    static void Parterre(RaisedMesh m, Vector3[] p, Func<Vector3, float> height)
    {
        var hedge = new RaisedMesh.Paint(RaisedColours.Hedge, RaisedCodes.Hedge);
        Vector3 along = (p[1] - p[0]) with { Y = 0 }, across = (p[3] - p[0]) with { Y = 0 }, origin = p[0] with { Y = 0 };
        float upm = m.PerMetre;
        for (int c = 0; c < 3; c += 2)
            for (int r = 0; r < 2; r++)
            {
                var mid = origin + along * ((c + 0.5f) / 3) + across * ((r + 0.5f) / 2);
                float ground = height(mid);
                Vector3 a = along * (BedShare / 2), b = across * (BedShare * 1.4f / 2);
                foreach (float share in new[] { 1f, 0.62f })
                {
                    Vector3[] bed = [mid - (a + b) * share, mid + (a - b) * share, mid + (a + b) * share, mid - (a - b) * share];
                    for (int i = 0; i < 4; i++) Strip(m, bed[i], bed[(i + 1) % 4], ground, BedHedgeM * upm, BedHedgeHighM * upm, hedge);
                }
                RaisedRoofs.Turned(m, mid with { Y = ground }, Vector3.Right * TopiaryM * upm, Vector3.Back * TopiaryM * upm, TopiaryHighM * upm, [new(1, 0), new(0.7f, 0.5f), new(0, 1)], hedge, 8);
            }
        var basin = origin + along / 2 + across / 2;
        float radius = along.Length() * 0.08f, at = height(basin);
        RaisedRoofs.Turned(m, basin with { Y = at }, Vector3.Right * radius, Vector3.Back * radius, 0.5f * upm, [new(1, 0), new(1, 1), new(0, 1)],
            new RaisedMesh.Paint(RaisedColours.Plinth.Lightened(0.3f), (int)Fabric.Stone));
        RaisedRoofs.Turned(m, basin with { Y = at + 0.45f * upm }, Vector3.Right * radius * 0.82f, Vector3.Back * radius * 0.82f, 0.06f * upm, [new(1, 0), new(1, 1), new(0, 1)],
            new RaisedMesh.Paint(new Color(0.22f, 0.32f, 0.36f), RaisedCodes.Water));
    }

    static void Strip(RaisedMesh m, Vector3 a, Vector3 b, float ground, float wide, float high, RaisedMesh.Paint paint)
    {
        var side = new Vector3(-(b - a).Z, 0, (b - a).X).Normalized() * (wide / 2);
        RaisedRoofs.Block(m, [a - side, b - side, b + side, a + side], ground - wide, ground + high, paint);
    }

    static bool Inside(Vector3[] ring, Vector3 at)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            if (ring[i].Z > at.Z != ring[j].Z > at.Z && at.X < (ring[j].X - ring[i].X) * (at.Z - ring[i].Z) / (ring[j].Z - ring[i].Z) + ring[i].X) inside = !inside;
        return inside;
    }

    static void Orchard(RaisedChunk chunk, Vector3[] p, Func<Vector3, float> height, int culture, float upm)
    {
        var kind = Manners.Named(culture).Seat is Manners.NearEast or Manners.South || Manners.Kin(culture) == Manners.Roman ? TreeKind.Olive : TreeKind.Fruit;
        Vector3 along = ((p[1] - p[0]) with { Y = 0 }).Normalized(), across = new(-along.Z, 0, along.X);
        float apart = OrchardApartM * upm;
        var centre = RaisedFrame.Middle(p.Select(q => q with { Y = 0 }).ToArray());
        float reach = p.Max(q => (q with { Y = 0 }).DistanceTo(centre));
        int steps = (int)(reach / apart), planted = 0;
        for (int i = -steps; i <= steps && planted < MostTrees; i++)
            for (int j = -steps; j <= steps && planted < MostTrees; j++)
            {
                var at = centre + along * (i * apart) + across * (j * apart);
                if (!Inside(p, at)) continue;
                float pick = BuiltCanvas.Chanced(new Vector2(at.X, at.Z) * 0.37f, 3.3f);
                chunk.Tree(at with { Y = height(at) }, kind, OrchardCrownM * (0.85f + 0.3f * pick) * upm, pick);
                planted++;
            }
    }

    static void Vineyard(RaisedMesh m, Vector3[] p, Func<Vector3, float> height)
    {
        var vine = new RaisedMesh.Paint(Vine, RaisedCodes.Hedge);
        Vector3 along = (p[1] - p[0]) with { Y = 0 }, across = (p[3] - p[0]) with { Y = 0 }, origin = p[0] with { Y = 0 };
        float upm = m.PerMetre;
        int rows = (int)(across.Length() / (VineRowM * upm)), pieces = Math.Max(1, (int)(along.Length() / (PieceM * upm)));
        if (rows * pieces > MostVinePieces) return;
        for (int r = 0; r < rows; r++)
        {
            var start = origin + across * ((r + 0.5f) / rows);
            for (int k = 0; k < pieces; k++)
            {
                Vector3 a = start + along * ((k + 0.05f) / pieces), b = start + along * ((k + 0.95f) / pieces);
                Strip(m, a, b, Mathf.Min(height(a), height(b)), VineM * upm, VineHighM * upm, vine);
            }
        }
    }
}
