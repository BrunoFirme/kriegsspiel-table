using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedSacred
{
    const float ApseAt = 0.86f, AisleFromM = 13, TranseptFromM = 26, CrossingAt = 0.6f, AisleShare = 0.55f, ApseShare = 0.85f, CrossPick = 17.3f;
    const float MinaretM = 1.7f, MinaretShare = 2.6f, TierShrink = 0.82f, TierM = 3.4f, PodiumM = 1.6f, ColumnM = 0.38f, ColumnApartM = 3;
    const float ButtressApartM = 4.2f, ButtressWideM = 0.9f, ButtressDeepM = 1.2f, ButtressShare = 0.8f;
    const int ApseSides = 7, Tiers = 5, ColumnSides = 8;

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
        else if (Manners.Kin(culture) is Manners.Tribal) return;
        else if (culture is Manners.Persian || Manners.Kin(culture) == Manners.Egyptian) Hall(r);
        else Church(r, builders, adorners, Chance);
    }

    static void Hall(RaisedRoofs.Raise r)
    {
        RaisedRoofs.Walls(r, r.F.Ring, r.Eaves, r.Wall);
        RaisedRoofs.Flat(r, true, false);
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
        float radius = Mathf.Min(prayer.Short, prayer.Long) * (ottoman ? 0.42f : 0.18f);
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
        var shaft = new[] { foot - x - z, foot + x - z, foot + x + z, foot - x + z };
        RaisedRoofs.Block(r.M, shaft, r.Bottom, r.Bottom + tall, r.Wall);
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
