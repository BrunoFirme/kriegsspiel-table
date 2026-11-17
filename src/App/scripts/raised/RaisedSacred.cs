using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedSacred
{
    const float ApseAt = 0.86f, AisleFromM = 13, TranseptFromM = 26, CrossingAt = 0.6f, AisleShare = 0.55f, ApseShare = 0.85f, CrossPick = 17.3f;
    const float MinaretM = 1.7f, MinaretShare = 2.6f, TierShrink = 0.82f, TierM = 3.4f, PodiumM = 1.6f, ColumnM = 0.38f, ColumnApartM = 3;
    const float ButtressApartM = 4.2f, ButtressWideM = 0.9f, ButtressDeepM = 1.2f, ButtressShare = 0.8f;
    const float PylonHigh = 1.35f, PylonLean = 0.14f, CourtWall = 0.5f, HallHigh = 0.8f, ShrineHigh = 0.6f, GateHigh = 0.55f, PierM = 0.65f, PierApartM = 3.6f, PrecinctM = 0.5f;
    const float TerraceM = 2.6f, StairAt = 0.86f, SlabM = 0.7f, GalleryAt = 0.78f, GalleryM = 0.5f, GalleryOutM = 0.4f, LanternShare = 0.6f, StoneRing = 0.42f;
    const float TenshuBase = 0.25f, TenshuShrink = 0.14f, MansardDropM = 0.2f, TenshuEaveM = 0.4f;
    const int ApseSides = 7, Tiers = 5, ColumnSides = 8, Stones = 9, TenshuTiers = 3;

    static readonly Color Sandstone = new(0.8f, 0.71f, 0.55f);

    static readonly Color Gilt = new(0.78f, 0.62f, 0.3f);
    static readonly Color Verdigris = new(0.36f, 0.55f, 0.47f);

    public static void Of(RaisedRoofs.Raise r, BuiltPainter.Shape s, RaisedLook look, RaisedBuildings.Site site)
    {
        int culture = RaisedLooks.Aged(site.Style, s.Manner), builders = RaisedLooks.Aged(site.Style, s.Built), adorners = RaisedLooks.Aged(site.Style, s.Adorned);
        var key = s.Uv[0] * BuiltCanvas.KeyScale;
        float Chance(float salt) => BuiltCanvas.Chanced(key, salt);
        if (culture is Manners.Ottoman or Manners.NorthAfrican or Manners.Saracen) Mosque(r, culture == Manners.Ottoman);
        else if (culture is Manners.Chinese or Manners.Japanese or Manners.Nippon) Pagoda(r);
        else if (Manners.Kin(culture) == Manners.Roman) Temple(r);
        else if (Manners.Kin(culture) is Manners.Tribal) Grove(r, Chance(CrossPick));
        else if (culture is Manners.Persian) Terrace(r);
        else if (Manners.Kin(culture) == Manners.Egyptian) Pylons(r);
        else Church(r, builders, adorners, Chance);
    }

    static float Floor(RaisedRoofs.Raise r) => float.IsNaN(r.M.FloorY) ? r.Bottom : r.M.FloorY;

    static RaisedMesh.Paint Dressed(RaisedRoofs.Raise r) => r.Wall with { Code = (int)Fabric.Stone, Tint = Sandstone, Storeys = 0, Door = -1 };

    static void Battered(RaisedMesh m, RaisedFrame f, float bottom, float top, float lean, RaisedMesh.Paint paint)
    {
        var low = f.Ring;
        var high = f.Grown(-lean).Ring;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = low[i] with { Y = bottom }, b = low[(i + 1) % 4] with { Y = bottom };
            m.Face([a, b, high[(i + 1) % 4] with { Y = top }, high[i] with { Y = top }], ((a + b) / 2 - f.Centre) with { Y = 0 }, b - a, a, paint);
        }
        m.Face([high[0] with { Y = top }, high[1] with { Y = top }, high[2] with { Y = top }, high[3] with { Y = top }], Vector3.Up, f.Along, high[0] with { Y = top }, paint);
    }

    static void Piers(RaisedMesh m, Vector3 a, Vector3 b, float bottom, float top, RaisedMesh.Paint paint)
    {
        float upm = m.PerMetre, radius = PierM * upm;
        int count = Mathf.Max(2, (int)((b - a).Length() / (PierApartM * upm)));
        for (int i = 0; i < count; i++)
        {
            var foot = a.Lerp(b, (i + 0.5f) / count) with { Y = bottom };
            RaisedRoofs.Turned(m, foot, Vector3.Right * radius, Vector3.Back * radius, top - bottom, [new(1, 0), new(0.85f, 0.92f), new(1.15f, 1)], paint, ColumnSides);
        }
    }

    static void Precinct(RaisedMesh m, RaisedFrame f, float bottom, float top, RaisedMesh.Paint paint, float gateFrom = 2, float gateTo = 2)
    {
        var ring = f.Ring;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % 4];
            var side = new Vector3(-(b - a).Z, 0, (b - a).X).Normalized() * (PrecinctM / 2 * m.PerMetre);
            if (i != 1 || gateFrom >= gateTo)
            {
                RaisedArcades.Prism(m, a, b, side, bottom, top, bottom, top, paint);
                continue;
            }
            RaisedArcades.Prism(m, a, a.Lerp(b, gateFrom), side, bottom, top, bottom, top, paint);
            RaisedArcades.Prism(m, a.Lerp(b, gateTo), b, side, bottom, top, bottom, top, paint);
        }
    }

    static void Pylons(RaisedRoofs.Raise r)
    {
        var f = r.F;
        var stone = Dressed(r);
        float floor = Floor(r), high = r.Eaves - floor, upm = r.M.PerMetre;
        Precinct(r.M, f.Inset(0, 0.9f, 0, 1), r.Bottom, floor + high * CourtWall, stone);
        RaisedRoofs.Block(r.M, f.Inset(0.02f, 0.24f, 0.16f, 0.84f).Ring, r.Bottom, floor + high * ShrineHigh, stone);
        RaisedRoofs.Block(r.M, f.Inset(0.24f, 0.56f, 0.04f, 0.96f).Ring, r.Bottom, floor + high * HallHigh, stone);
        RaisedRoofs.Block(r.M, f.Inset(0.24f, 0.56f, 0.38f, 0.62f).Ring, floor + high * HallHigh, floor + high, stone);
        var court = f.Inset(0.58f, 0.88f, 0.04f, 0.96f);
        foreach (var (near, far) in new[] { (0.06f, 0.06f), (0.94f, 0.94f) })
        {
            Piers(r.M.Small, court.At(0, near), court.At(1, far), floor, floor + high * CourtWall, stone);
            RaisedArcades.Prism(r.M.Small, court.At(0, near), court.At(1, far), court.Across * (PierM * upm), floor + high * CourtWall, floor + high * CourtWall + SlabM * upm,
                floor + high * CourtWall, floor + high * CourtWall + SlabM * upm, stone);
        }
        foreach (var (near, far) in new[] { (0f, 0.43f), (0.57f, 1f) })
        {
            var tower = f.Inset(0.9f, 1, near, far);
            Battered(r.M, tower, r.Bottom, floor + high * PylonHigh, tower.Short * PylonLean, stone);
            RaisedRoofs.Block(r.M, tower.Grown(-tower.Short * PylonLean + 0.3f * upm).Ring, floor + high * PylonHigh, floor + high * PylonHigh + 0.5f * upm, stone);
        }
        RaisedRoofs.Block(r.M, f.Inset(0.92f, 0.98f, 0.43f, 0.57f).Ring, floor + high * GateHigh, floor + high * (GateHigh + 0.3f), stone);
    }

    static void Terrace(RaisedRoofs.Raise r)
    {
        var f = r.F;
        var stone = Dressed(r) with { Tint = Sandstone.Lightened(0.08f) };
        float floor = Floor(r), upm = r.M.PerMetre, deck = floor + TerraceM * upm, roof = Mathf.Max(r.Eaves, deck + 6 * upm), slab = SlabM * upm;
        RaisedRoofs.Block(r.M, f.Inset(0, StairAt, 0, 1).Ring, r.Bottom, deck, stone);
        var stair = f.Inset(StairAt, 1, 0.3f, 0.7f);
        r.M.Face([stair.A0 with { Y = deck }, stair.A3 with { Y = deck }, stair.A2 with { Y = floor }, stair.A1 with { Y = floor }], Vector3.Up + f.Along, f.Across, stair.A0 with { Y = deck }, stone);
        foreach (var (a, b) in new[] { (stair.A0, stair.A1), (stair.A3, stair.A2) })
            r.M.Face([a with { Y = r.Bottom }, b with { Y = r.Bottom }, b with { Y = floor }, a with { Y = deck }], (a - stair.Centre) with { Y = 0 }, b - a, a with { Y = r.Bottom }, stone);
        var hall = f.Inset(0.16f, 0.8f, 0.16f, 0.84f);
        RaisedRoofs.Block(r.M, hall.Inset(0.22f, 0.78f, 0.22f, 0.78f).Ring, deck, roof - slab, r.Wall with { Storeys = 0, Door = -1 });
        foreach (var (a, b) in RaisedFrame.Sides(hall.Inset(0.06f, 0.94f, 0.06f, 0.94f).Ring)) Piers(r.M.Small, a, b, deck, roof - slab, stone);
        RaisedRoofs.Block(r.M, hall.Ring, roof - slab, roof, stone);
    }

    static void Grove(RaisedRoofs.Raise r, float pick)
    {
        var f = r.F;
        float floor = Floor(r), radius = Mathf.Min(f.Short, f.Long) * StoneRing;
        for (int i = 0; i < Stones; i++)
        {
            float bearing = i * Mathf.Tau / Stones + pick;
            var way = f.Along * Mathf.Cos(bearing) + f.Across * Mathf.Sin(bearing);
            RaisedProps.Add(r.M, "menhir", (f.Centre + way * radius) with { Y = floor }, way, (pick + i * 0.37f) % 1);
        }
        RaisedProps.Add(r.M, "altar", f.Centre with { Y = floor }, f.Along, pick);
    }

    static void Church(RaisedRoofs.Raise r, int builders, int adorners, System.Func<float, float> chance)
    {
        var f = r.F;
        float shortM = f.Short / r.M.PerMetre, longM = f.Long / r.M.PerMetre;
        bool aisled = shortM >= AisleFromM, crossed = longM >= TranseptFromM || chance(CrossPick) < 0.3f && shortM >= 9;
        var nave = aisled ? f.Part(0, ApseAt, 0.2f, 0.8f) : f.Part(0, ApseAt, 0, 1);
        bool lancets = Lanceted(builders);
        var windows = lancets ? r.Wall with { Door = RaisedMesh.LancetDoor } : r.Wall;
        var hall = r with { F = nave, Wall = windows };
        RaisedRoofs.Walls(hall, nave.Ring, r.Eaves, windows);
        if (r.Pitch <= 0) RaisedRoofs.Flat(hall, true, false);
        else if (Manners.Kin(builders) == Manners.Northern) RaisedRoofs.Hip(hall);
        else RaisedRoofs.Gable(hall);
        if (aisled)
            foreach (var (from, to) in new[] { (0f, 0.2f), (0.8f, 1f) })
            {
                var aisle = f.Part(0, ApseAt, from, to);
                float low = r.Bottom + (r.Eaves - r.Bottom) * AisleShare;
                RaisedRoofs.Walls(r, aisle.Ring, low, windows);
                var lean = from < 0.5f ? RaisedFrame.Of([aisle.A3, aisle.A2, aisle.A1, aisle.A0]) : aisle;
                RaisedRoofs.Lean(r with { F = lean, Eaves = low, Pitch = r.Pitch * 0.5f });
            }
        Apse(r, f);
        if (lancets && !crossed) Buttresses(r, f, aisled ? AisleShare * 0.9f : ButtressShare);
        if (crossed)
        {
            var arms = f.Part(CrossingAt - 0.08f, CrossingAt + 0.08f, -0.14f, 1.14f);
            RaisedRoofs.Walls(r with { F = arms }, arms.Ring, r.Eaves, r.Wall);
            RaisedRoofs.Gable(r with { F = arms });
        }
        float ridge = r.Eaves + Mathf.Tan(r.Pitch) * nave.Short / 2;
        if (adorners is Manners.Northern or Manners.Soviet or Manners.Rus or Manners.Byzantine)
        {
            bool onion = adorners is Manners.Rus or Manners.Soviet or Manners.Northern;
            float big = f.Short * (adorners == Manners.Byzantine ? 0.36f : 0.3f), small = f.Short * 0.13f;
            var domes = r with { Roof = adorners == Manners.Byzantine ? r.Roof : new RaisedMesh.Paint(adorners == Manners.Rus ? Gilt : Verdigris, RaisedCodes.Roof + (int)Cover.Copper) };
            Drum(domes, f.At(0.5f, 0.5f), big, ridge, onion);
            foreach (var (t, s) in new[] { (0.3f, 0.22f), (0.3f, 0.78f), (0.7f, 0.22f), (0.7f, 0.78f) }) Drum(domes, f.At(t, s), small, ridge - small, onion);
        }
        else if (adorners is Manners.Continental or Manners.Western && crossed && chance(BuiltLayer.ChurchPick) > 0.7f)
            Drum(r, f.At(CrossingAt, 0.5f), f.Short * 0.3f, ridge, false);
    }

    static bool Lanceted(int builders) => builders == Manners.Gothic || Manners.Kin(builders) is Manners.Continental or Manners.Western;

    static void Buttresses(RaisedRoofs.Raise r, RaisedFrame f, float share)
    {
        float upm = r.M.PerMetre, top = r.Bottom + (r.Eaves - r.Bottom) * share, from = 0.06f * f.Long, to = (ApseAt - 0.04f) * f.Long;
        int count = Mathf.Max(2, (int)((to - from) / (ButtressApartM * upm)) + 1);
        var stone = r.Wall with { Door = -1, Storeys = 0, Code = (int)Fabric.Stone };
        foreach (float side in new[] { 0f, 1f })
        {
            var outward = side < 0.5f ? -f.Across : f.Across;
            for (int i = 0; i < count; i++)
            {
                var foot = f.At((from + (to - from) * i / (count - 1)) / f.Long, side) + outward * (ButtressDeepM / 2 * upm);
                Vector3 x = f.Along * (ButtressWideM / 2 * upm), z = outward * (ButtressDeepM / 2 * upm);
                RaisedRoofs.Block(r.M, [foot - x - z, foot + x - z, foot + x + z, foot - x + z], r.Bottom, top, stone);
            }
        }
    }

    static void Drum(RaisedRoofs.Raise r, Vector3 at, float radius, float from, bool onion)
    {
        float drum = radius * 0.9f;
        RaisedRoofs.Turned(r.M, at with { Y = from - radius * 0.3f }, Vector3.Right * radius, Vector3.Back * radius, drum, [new(1, 0), new(1, 1)], r.Wall);
        RaisedRoofs.Dome(r, at, radius, radius, Vector3.Right, Vector3.Back, from - radius * 0.3f + drum, radius, onion);
    }

    static void Apse(RaisedRoofs.Raise r, RaisedFrame f)
    {
        var centre = f.At(ApseAt, 0.5f);
        float radius = f.Short * 0.4f, bulge = Mathf.Min(radius, (1 - ApseAt) * f.Long), top = r.Bottom + (r.Eaves - r.Bottom) * ApseShare;
        var rim = new List<Vector3>();
        for (int i = 0; i <= ApseSides; i++)
        {
            float a = i * Mathf.Pi / ApseSides;
            rim.Add(centre + f.Across * (Mathf.Cos(a) * radius) + f.Along * (Mathf.Sin(a) * bulge));
        }
        float rise = Mathf.Tan(r.Pitch) * radius * 0.8f;
        var apex = centre with { Y = top + rise };
        for (int i = 0; i < ApseSides; i++)
        {
            r.M.Wall(rim[i], rim[i + 1], r.Bottom, top, centre, r.Wall);
            var outward = ((rim[i] + rim[i + 1]) / 2 - centre) with { Y = 0 };
            r.M.Face([rim[i] with { Y = top }, rim[i + 1] with { Y = top }, apex], Vector3.Up + outward.Normalized(), rim[i + 1] - rim[i], rim[i] with { Y = top }, r.Roof);
        }
    }

    static void Mosque(RaisedRoofs.Raise r, bool ottoman)
    {
        var f = r.F;
        var prayer = f.Part(0, 0.58f, 0, 1);
        var court = f.Part(0.58f, 1, 0, 1);
        var hall = r with { F = prayer };
        RaisedRoofs.Walls(hall, prayer.Ring, r.Eaves, r.Wall);
        RaisedRoofs.Flat(hall, !ottoman, false);
        float radius = Mathf.Min(prayer.Short, prayer.Long) * (ottoman ? 0.42f : 0.28f);
        var lead = r.Roof with { Code = RaisedCodes.Roof + (int)(ottoman ? Cover.Lead : Cover.Lime), Tint = ottoman ? RaisedColours.Roof(Cover.Lead, 0.5f) : r.Wall.Tint };
        RaisedRoofs.Dome(r with { Roof = lead }, prayer.Centre, radius, radius, f.Along, f.Across, r.Eaves, radius, false);
        float wallTop = r.Bottom + (r.Eaves - r.Bottom) * 0.5f;
        RaisedRoofs.Walls(r with { F = court }, court.Ring, wallTop, r.Wall);
        float tall = (r.Eaves - r.Bottom) * MinaretShare, thick = MinaretM * r.M.PerMetre;
        var foot = court.At(0.95f, 0.05f);
        if (ottoman)
        {
            RaisedRoofs.Turned(r.M, foot with { Y = r.Bottom }, Vector3.Right * thick, Vector3.Back * thick, tall, [new(1, 0), new(0.85f, 1)], r.Wall, 10);
            RaisedRoofs.Turned(r.M, foot with { Y = r.Bottom + tall }, Vector3.Right * thick * 0.9f, Vector3.Back * thick * 0.9f, thick * 3.5f, [new(1, 0), new(0, 1)], lead, 10);
            return;
        }
        Vector3 x = f.Along * thick, z = f.Across * thick;
        var shaft = new RaisedFrame(foot - x - z, foot + x - z, foot + x + z, foot - x + z, foot - x - z, foot + x - z);
        var plain = r.Wall with { Storeys = 0, Door = -1 };
        float gallery = r.Bottom + tall * GalleryAt, upm = r.M.PerMetre;
        Battered(r.M, shaft, r.Bottom, gallery, thick * 0.12f, plain);
        RaisedRoofs.Block(r.M, shaft.Grown(GalleryOutM * upm).Ring, gallery, gallery + GalleryM * upm, plain);
        var lantern = shaft.Grown(-thick * (1 - LanternShare));
        RaisedRoofs.Block(r.M, lantern.Ring, gallery + GalleryM * upm, r.Bottom + tall, plain);
        RaisedRoofs.Dome(r with { Roof = lead }, lantern.Centre, lantern.Short / 2, lantern.Short / 2, f.Along, f.Across, r.Bottom + tall, lantern.Short * 0.6f, false);
    }

    static void Pagoda(RaisedRoofs.Raise r)
    {
        var f = r.F;
        var hall = f.Part(0, 0.6f, 0, 1);
        RaisedRoofs.Walls(r with { F = hall }, hall.Ring, r.Eaves, r.Wall);
        RaisedRoofs.Hip(r with { F = hall, Pitch = r.Pitch > 0 ? r.Pitch : 0.5f });
        var tower = f.Part(0.7f, 1, 0.2f, 0.8f);
        float floor = r.Bottom, tier = TierM * r.M.PerMetre;
        for (int i = 0; i < Tiers; i++)
        {
            float shrink = Mathf.Pow(TierShrink, i);
            var level = tower.Part(0.5f - 0.5f * shrink, 0.5f + 0.5f * shrink, 0.5f - 0.5f * shrink, 0.5f + 0.5f * shrink);
            RaisedRoofs.Walls(r with { F = level, Bottom = floor }, level.Ring, floor + tier, r.Wall);
            RaisedRoofs.Hip(r with { F = level.Grown(tier * 0.25f), Eaves = floor + tier, Pitch = 0.45f }, 0.3f);
            floor += tier;
        }
    }

    public static void Tenshu(RaisedRoofs.Raise r, RaisedLook look)
    {
        var f = r.F;
        float floor = Floor(r), high = r.Eaves - floor, foot = floor + high * TenshuBase, tier = (r.Eaves - foot) / TenshuTiers;
        var stone = r.Wall with { Code = (int)Fabric.Stone, Tint = RaisedColours.Plinth.Lightened(0.1f), Storeys = 0, Door = -1 };
        var white = r.Wall with { Code = (int)Fabric.Lime + 16 * (int)Glazing.Screen, Tint = new Color(0.9f, 0.88f, 0.82f), Storeys = 1, Door = -1 };
        Battered(r.M, f, r.Bottom, foot, f.Short * 0.08f, stone);
        float inset = f.Short * 0.08f;
        var level = f.Grown(-inset);
        for (int i = 0; i < TenshuTiers; i++)
        {
            float from = foot + i * tier, eave = Mathf.Min(tier * 0.3f, inset + TenshuEaveM * r.M.PerMetre);
            RaisedRoofs.Walls(r with { F = level, Bottom = from }, level.Ring, from + tier, white);
            if (i == TenshuTiers - 1) RaisedRoofs.Hip(r with { F = level.Grown(eave), Eaves = from + tier, Pitch = 0.6f });
            else RaisedRoofs.Mansard(r with { F = level.Grown(eave), Eaves = from + tier - MansardDropM * r.M.PerMetre, Pitch = 0.5f });
            inset += level.Short * TenshuShrink;
            level = level.Grown(-level.Short * TenshuShrink);
        }
    }

    static void Temple(RaisedRoofs.Raise r)
    {
        var f = r.F;
        float podium = r.Bottom + PodiumM * r.M.PerMetre + (r.Eaves - r.Bottom) * 0.05f;
        var stone = r.Wall with { Code = (int)Fabric.Stone, Tint = new Color(0.82f, 0.79f, 0.72f), Storeys = 0 };
        RaisedRoofs.Block(r.M, f.Ring, r.Bottom, podium, stone);
        var cella = f.Part(0.07f, 0.72f, 0.2f, 0.8f);
        var roofed = f.Part(0.02f, 0.9f, 0.04f, 0.96f);
        RaisedRoofs.Walls(r with { F = cella, Bottom = podium }, cella.Ring, r.Eaves, stone);
        RaisedRoofs.Gable(r with { F = roofed, Wall = stone, Pitch = Mathf.Min(r.Pitch, 0.4f) });
        float radius = ColumnM * r.M.PerMetre;
        foreach (var (a, b) in RaisedFrame.Sides(roofed.Ring))
        {
            int count = Mathf.Max(2, (int)((b - a).Length() / (ColumnApartM * r.M.PerMetre)));
            for (int i = 0; i < count; i++)
                RaisedRoofs.Turned(r.M, a.Lerp(b, (i + 0.5f) / count) with { Y = podium }, Vector3.Right * radius, Vector3.Back * radius, r.Eaves - podium, [new(1, 0), new(0.85f, 1)],
                    stone, ColumnSides);
        }
    }
}
