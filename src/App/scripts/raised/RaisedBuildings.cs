using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedBuildings
{
    const float DoorPick = 1.3f, RubbleM = 1.4f, RuinShare = 0.55f, ChimneyM = 1.6f, ChimneyHighM = 28, CoolingWaist = 0.62f, HolderShare = 0.8f;
    const float BayM = 9, BunkerM = 2.4f, StackM = 0.9f, StackUpM = 1.1f, StackDownM = 1.5f, SideM = 6, LongM = 9, DormerPick = 67.1f;
    const float ScorchPick = 81.7f, ScorchShare = 0.7f, SmokePick = 77.7f, SmokeShare = 0.14f, GateWideM = 3.4f, GateHighM = 4.6f, GateBandM = 0.8f, AwningPick = 29.3f;
    const float SpillM = 2.2f, DebrisPick = 13.7f, RafterApartM = 0.9f, RafterM = 0.16f;
    const int Debris = 7;
    static readonly Color Charred = new(0.13f, 0.11f, 0.09f);

    public readonly record struct Site(Vector3[] At, float Floor, float Bottom, string? Style, float[]? Gaps = null, float[]? Opens = null);

    public static void Add(RaisedMesh m, BuiltPainter.Shape s, Site site)
    {
        var p = site.At;
        if (p.Length < 3) return;
        m.Weather = s.Class is Settlements.Rubble or Settlements.Ruin ? 1 : RaisedLooks.Of(s, site.Style, SideM, LongM).Weather;
        m.Scorch = Scorched(s);
        m.OpenFor = null;
        m.FloorY = site.Floor;
        if (s.Class == Settlements.Rubble)
        {
            Rubble(m, s, site);
            return;
        }
        if (s.Class == Settlements.Ruin)
        {
            Ruin(m, s, site);
            return;
        }
        if (p.Length >= 8 && s.Class is Castles.RoundTower or "gasholder" or "coolingtower" or "silo" or "roundhouse")
        {
            Round(m, s, site);
            return;
        }
        if (p.Length == 4)
        {
            var closed = RaisedRows.Closed(p, site.Gaps);
            if (site.Opens is { } opens) m.OpenFor = (a, b) => RaisedRows.At(opens, closed, a, b);
            One(m, s, RaisedFrame.Of(closed), site with { At = closed }, true);
            m.OpenFor = null;
            return;
        }
        if (p.Length == 6)
        {
            var wings = RaisedFrame.Wings(p);
            One(m, s, RaisedFrame.Of(wings[0]), site, true);
            One(m, s, RaisedFrame.Of(wings[1]), site, false);
            return;
        }
        Prism(m, s, site);
    }

    static float Scorched(BuiltPainter.Shape s)
    {
        if (s.Class is Settlements.Rubble or Settlements.Ruin) return 1;
        var key = s.Uv[0] * BuiltCanvas.KeyScale;
        return s.Ravaged > 0 && BuiltCanvas.Chanced(key, ScorchPick) < s.Ravaged * ScorchShare ? 0.4f + 0.6f * BuiltCanvas.Chanced(key, ScorchPick + 1) : 0;
    }

    public static RaisedMesh.Paint WallPaint(RaisedLook look) => new(look.Wall, (int)look.Fabric + 16 * (int)look.Glazing, 0, -1, look.Storeys);

    public static RaisedMesh.Paint RoofPaint(RaisedLook look) => new(look.Roof, RaisedCodes.Roof + (int)look.Cover);

    static RaisedLook LookOf(RaisedMesh m, BuiltPainter.Shape s, RaisedFrame f, Site site) =>
        RaisedLooks.Of(s, site.Style, f.Short / m.PerMetre, f.Long / m.PerMetre);

    static void One(RaisedMesh m, BuiltPainter.Shape s, RaisedFrame f, Site site, bool main)
    {
        var look = LookOf(m, s, f, site);
        bool joined = RaisedRows.Joined(site.Gaps, site.At, f.A0, f.A3) || RaisedRows.Joined(site.Gaps, site.At, f.A1, f.A2);
        if (joined && look.Form is Form.Hip or Form.HalfHip or Form.Pyramid or Form.Mansard) look = look with { Form = Form.Gable };
        float eaves = site.Floor + look.EavesM * m.PerMetre;
        var raise = new RaisedRoofs.Raise(m, f, eaves, site.Bottom, WallPaint(look), RoofPaint(look), look.Pitch);
        if (s.Class == "church")
        {
            RaisedSacred.Of(raise, s, look, site);
            return;
        }
        if (RaisedWorks.Of(raise, s.Class, look)) return;
        bool round = look.Form == Form.Cone || look.Form == Form.Dome && s.Class is "silo" or "kiln" or "dovecote";
        float doorAt = 0.5f + (BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, DoorPick) - 0.5f) * 0.4f;
        int builders = RaisedLooks.Aged(site.Style, s.Built);
        Vector3? doorA = main && look.Door ? f.FrontA : null, doorB = main && look.Door ? f.FrontB : null;
        if (s.Class == "gatehouse" && !round)
        {
            Passage(raise, f, site.Floor);
            Roof(raise, look, s.Class, round);
            return;
        }
        if (!round && !m.Plain && RaisedShapes.Jettied(look, s.Class, builders)) raise = RaisedShapes.Jetty(raise, look, doorA, doorB, doorAt);
        else if (!round) RaisedRoofs.Walls(raise, f.Ring, eaves, raise.Wall, doorA, doorB, doorAt, main && look.Shop);
        Roof(raise, look, s.Class, round);
        if (!main || m.Plain) return;
        if (look.Shop && Manners.Named(builders).Epoch != Manners.Ancient) RaisedProps.Awning(m.Small, f.FrontA, f.FrontB, Outward(f), site.Floor, BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, AwningPick));
        Chimneys(raise, look, s, site.Style);
        RaisedShapes.Dormers(raise, look, builders, BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, DormerPick));
    }

    static void Chimneys(RaisedRoofs.Raise r, RaisedLook look, BuiltPainter.Shape s, string? style)
    {
        int builders = RaisedLooks.Aged(style, s.Built), builds = Manners.Kin(builders);
        if (BuiltShapes.Smokeless(s.Class) || Manners.Old(builds) || !RoofBook.Stacked(builders) || RoofBook.Level(look.Cover)
            || look.Form is Form.Flat or Form.Court or Form.Cone or Form.Dome or Form.Vault or Form.Atrium) return;
        if (look.Cover is Cover.Thatch or Cover.Turf && builds is Manners.Northern or Manners.Tribal) return;
        bool forge = s.Class is "smithy" or "bakery", smoking = BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, SmokePick) < SmokeShare;
        var brick = new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Brick, builders, 0.4f), (int)Fabric.Brick);
        var first = builds == Manners.Northern ? Stack(r, look, 0.5f, 1.15f, brick) : Stack(r, look, 0.3f, StackM, brick);
        if (builds != Manners.Northern && r.F.Long / r.M.PerMetre >= 13) Stack(r, look, 0.76f, StackM, brick);
        if (smoking && !forge) r.M.Smokes?.Add(first);
        if (forge) r.M.Smokes?.Add(Stack(r, look, 0.8f, 1.4f, brick));
    }

    static Vector3 Stack(RaisedRoofs.Raise r, RaisedLook look, float t, float sizeM, RaisedMesh.Paint brick)
    {
        var (at, ridge) = RaisedRoofs.Ridge(r, look.Form, t);
        float half = sizeM / 2 * r.M.PerMetre, top = ridge + StackUpM * r.M.PerMetre;
        Vector3 x = r.F.Along * half, z = r.F.Across * half;
        RaisedRoofs.Block(r.M, [at - x - z, at + x - z, at + x + z, at - x + z], ridge - StackDownM * r.M.PerMetre, top, brick);
        return at with { Y = top };
    }

    static Vector3 Outward(RaisedFrame f)
    {
        var run = (f.FrontB - f.FrontA) with { Y = 0 };
        var outward = new Vector3(-run.Z, 0, run.X);
        return outward.Dot((f.FrontA + f.FrontB) / 2 - f.Centre) < 0 ? -outward : outward;
    }

    static void Passage(RaisedRoofs.Raise r, RaisedFrame f, float floor)
    {
        float upm = r.M.PerMetre, length = ((f.FrontB - f.FrontA) with { Y = 0 }).Length() / upm;
        var through = (2 * f.Centre - (f.FrontA + f.FrontB)) with { Y = 0 };
        var wall = r.Wall with { Code = r.Wall.Code % 16, Door = -1 };
        float passage = Mathf.Min(GateWideM, length * 0.6f), band = Mathf.Max(GateBandM, (r.Eaves - floor) / upm - GateHighM);
        RaisedArcades.Gateway(r.M, f.FrontA + through / 2, f.FrontB + through / 2, r.Eaves, floor, through.Length() / 2, passage, band, wall);
    }

    public static void Roof(RaisedRoofs.Raise raise, RaisedLook look, string cls, bool round)
    {
        bool merlons = cls is "keep" or "tower" or "gatehouse" or "watchtower" or Castles.RoundTower;
        switch (look.Form)
        {
            case Form.Gable:
                RaisedRoofs.Gable(raise);
                break;
            case Form.Hip:
                RaisedRoofs.Hip(raise);
                break;
            case Form.HalfHip:
                RaisedRoofs.HalfHip(raise);
                break;
            case Form.Pyramid:
                RaisedRoofs.Pyramid(raise);
                break;
            case Form.Lean:
                RaisedRoofs.Lean(raise);
                break;
            case Form.Mansard:
                RaisedRoofs.Mansard(raise);
                break;
            case Form.Valley:
                RaisedRoofs.Valley(raise);
                break;
            case Form.Cone:
                RaisedRoofs.Cone(raise, round);
                break;
            case Form.Dome:
                RaisedRoofs.DomeOver(raise, round);
                break;
            case Form.Court or Form.Atrium:
                RaisedRoofs.Court(raise);
                break;
            case Form.Vault:
                RaisedRoofs.Vault(raise);
                break;
            default:
                RaisedRoofs.Flat(raise, merlons || RoofBook.Level(look.Cover), merlons);
                break;
        }
    }

    static void Prism(RaisedMesh m, BuiltPainter.Shape s, Site site)
    {
        var p = site.At;
        float x0 = p.Min(q => q.X), x1 = p.Max(q => q.X), z0 = p.Min(q => q.Z), z1 = p.Max(q => q.Z);
        float shortM = Mathf.Min(x1 - x0, z1 - z0) / m.PerMetre, longM = Mathf.Max(x1 - x0, z1 - z0) / m.PerMetre;
        var look = RaisedLooks.Of(s, site.Style, shortM, longM);
        float eaves = site.Floor + look.EavesM * m.PerMetre;
        var wall = WallPaint(look);
        var middle = RaisedFrame.Middle(p);
        foreach (var (a, b) in RaisedFrame.Sides(p)) m.Wall(a, b, site.Bottom, eaves, middle, wall);
        m.Face(p.Select(q => q with { Y = eaves }).ToArray(), Vector3.Up, p[1] - p[0], p[0] with { Y = eaves }, RoofPaint(look));
    }

    static void Round(RaisedMesh m, BuiltPainter.Shape s, Site site)
    {
        var p = site.At;
        var middle = RaisedFrame.Middle(p);
        float radius = p.Average(q => (q - middle).Length());
        var look = RaisedLooks.Of(s, site.Style, radius * 2 / m.PerMetre, radius * 2 / m.PerMetre);
        float eaves = site.Floor + look.EavesM * m.PerMetre;
        var wall = WallPaint(look);
        if (s.Class == "coolingtower")
        {
            RaisedRoofs.Turned(m, middle with { Y = site.Bottom }, Vector3.Right * radius, Vector3.Back * radius, radius * 2.2f,
                [new(1, 0), new(0.82f, 0.35f), new(CoolingWaist, 0.7f), new(0.68f, 1)], wall with { Code = (int)Fabric.Concrete });
            return;
        }
        foreach (var (a, b) in RaisedFrame.Sides(p)) m.Wall(a, b, site.Bottom, eaves, middle, wall);
        var lid = p.Select(q => q with { Y = eaves }).ToArray();
        m.Face(lid, Vector3.Up, p[1] - p[0], lid[0], RoofPaint(look));
        if (s.Class == Castles.RoundTower) RaisedRoofs.Parapet(m, p, eaves, RaisedRoofs.ParapetM * m.PerMetre, wall, true);
        else if (s.Class == "gasholder") RaisedRoofs.Turned(m, middle with { Y = eaves }, Vector3.Right * radius, Vector3.Back * radius, radius * 0.18f,
            [new(1, 0), new(HolderShare, 0.6f), new(0, 1)], RoofPaint(look));
        else if (s.Class is "silo" or "roundhouse") RaisedRoofs.Turned(m, middle with { Y = eaves }, Vector3.Right * radius * 1.05f, Vector3.Back * radius * 1.05f,
            Mathf.Tan(look.Pitch) * radius, [new(1, 0), new(0, 1)], RoofPaint(look));
    }

    static void Rubble(RaisedMesh m, BuiltPainter.Shape s, Site site)
    {
        var p = site.At;
        var middle = RaisedFrame.Middle(p);
        var top = middle with { Y = site.Floor + RubbleM * m.PerMetre };
        var paint = new RaisedMesh.Paint(RaisedColours.Plinth.Darkened(0.15f), (int)Fabric.Stone);
        float spill = SpillM * m.PerMetre;
        var spread = new Vector3[p.Length];
        for (int i = 0; i < p.Length; i++) spread[i] = p[i] + ((p[i] - middle) with { Y = 0 }).Normalized() * spill;
        foreach (var (a, b) in RaisedFrame.Sides(spread))
        {
            var outward = ((a + b) / 2 - middle) with { Y = 0 };
            m.Face([a with { Y = site.Bottom }, b with { Y = site.Bottom }, top], Vector3.Up + outward.Normalized(), b - a, a with { Y = site.Bottom }, paint);
        }
        Strewn(m.Small, s, spread, middle, site.Floor);
    }

    static void Strewn(RaisedMesh m, BuiltPainter.Shape s, Vector3[] around, Vector3 middle, float floor)
    {
        var key = s.Uv[0] * BuiltCanvas.KeyScale;
        float reach = around.Max(q => ((q - middle) with { Y = 0 }).Length());
        var stone = new RaisedMesh.Paint(RaisedColours.Plinth.Darkened(0.1f), (int)Fabric.Stone);
        var beam = new RaisedMesh.Paint(Charred, RaisedCodes.Timber);
        for (int i = 0; i < Debris; i++)
        {
            float a = BuiltCanvas.Chanced(key, DebrisPick + i) * Mathf.Tau, d = reach * (0.8f + 0.5f * BuiltCanvas.Chanced(key, DebrisPick + i * 2.3f));
            var at = middle + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
            float size = (0.3f + 0.5f * BuiltCanvas.Chanced(key, DebrisPick + i * 4.1f)) * m.PerMetre;
            var x = new Vector3(Mathf.Cos(a * 3), 0, Mathf.Sin(a * 3)) * size;
            var z = new Vector3(-x.Z, 0, x.X) * (i % 3 == 0 ? 0.25f : 0.8f);
            if (i % 3 == 0) x *= 4;
            RaisedRoofs.Block(m, [at - x - z, at + x - z, at + x + z, at - x + z], floor - size, floor + size * 0.6f, i % 3 == 0 ? beam : stone);
        }
    }

    static void Ruin(RaisedMesh m, BuiltPainter.Shape s, Site site)
    {
        var p = site.At;
        var middle = RaisedFrame.Middle(p);
        var key = s.Uv[0] * BuiltCanvas.KeyScale;
        var look = RaisedLooks.Of(s with { Class = "house" }, site.Style, 6, 9);
        var wall = WallPaint(look) with { Tint = look.Wall.Darkened(0.35f), Storeys = look.Storeys };
        float full = look.EavesM * RuinShare * m.PerMetre;
        for (int i = 0; i < p.Length; i++)
        {
            Vector3 a = p[i], b = p[(i + 1) % p.Length];
            float ha = site.Floor + full * (0.4f + 0.6f * BuiltCanvas.Chanced(key, i * 3.1f)), hb = site.Floor + full * (0.4f + 0.6f * BuiltCanvas.Chanced(key, (i + 1) % p.Length * 3.1f));
            m.Wall(a, b, site.Bottom, ha, middle, wall, ha, hb);
        }
        int epoch = Manners.Age(site.Style ?? "");
        if (p.Length == 4 && epoch != Manners.Ancient && !m.Plain) Rafters(m, s, RaisedFrame.Of(p), site.Floor + full * 0.4f, look.Pitch);
        if (!m.Plain) Strewn(m.Small, s, p, middle, site.Floor);
    }

    static void Rafters(RaisedMesh m, BuiltPainter.Shape s, RaisedFrame f, float eaves, float pitch)
    {
        var key = s.Uv[0] * BuiltCanvas.KeyScale;
        var beam = new RaisedMesh.Paint(Charred, RaisedCodes.Timber);
        float upm = m.PerMetre, rise = Mathf.Tan(pitch) * f.Short / 2, kept = 0.35f + 0.4f * BuiltCanvas.Chanced(key, DebrisPick + 9), half = RafterM / 2 * upm;
        float from = BuiltCanvas.Chanced(key, DebrisPick + 11) * (1 - kept), ridge = eaves + rise;
        Vector3 z = f.Across * half, x = f.Along * half;
        RaisedArcades.Prism(m.Small, f.At(from, 0.5f), f.At(from + kept, 0.5f), z, ridge - RafterM * upm, ridge, ridge - RafterM * upm, ridge, beam);
        int count = Mathf.Max(2, (int)(kept * f.Long / (RafterApartM * upm)));
        for (int i = 0; i <= count; i++)
        {
            float t = from + kept * i / count;
            foreach (float side in new[] { 0f, 1f })
                if (side == 0 || i % 2 == 0)
                    RaisedArcades.Prism(m.Small, f.At(t, side), f.At(t, 0.5f), x, eaves - RafterM * upm, eaves, ridge - RafterM * upm, ridge, beam, false);
        }
    }

    public static float Chimney(RaisedMesh m, Vector3 at, float bottom, float highM, RaisedMesh.Paint paint)
    {
        float radius = ChimneyM / 2 * m.PerMetre;
        RaisedRoofs.Turned(m, at with { Y = bottom }, Vector3.Right * radius, Vector3.Back * radius, highM * m.PerMetre, [new(1.25f, 0), new(1, 1)], paint, 8);
        return highM;
    }

    public static IEnumerable<RaisedFrame> Bays(RaisedFrame f, float perMetre)
    {
        int bays = Mathf.Max(1, (int)(f.Long / (BayM * perMetre)));
        for (int i = 0; i < bays; i++) yield return f.Part(i / (float)bays, (i + 1) / (float)bays, 0, 1);
    }

    public const float FactoryChimneyM = ChimneyHighM, LowBunkerM = BunkerM;
}
