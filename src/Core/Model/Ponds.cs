namespace KTable.Core.Model;

public static class Ponds
{
    public const string Held = "reservoir", Dam = "dam";
    const double LevelGrade = 0.05, FallMostM = 0.8, StepM = 2, FoldRiseM = 1, FoldOutM = 14, HeldM = 1.2, CrestM = 0.4, BackM = 8, DamHalfM = 1.6, DamMostM = 70, BankM = 1.5;
    const int Rays = 16, RimPoints = 12, MostSteps = 60, Bisections = 10;

    public sealed record Dammed(List<Spot> Ring, List<Spot> Dam, Spot Middle, double Reach);

    public static bool Flat(BuiltSite site, Spot at) => site.Downhill(at).Length < LevelGrade;

    static double Fall(BuiltSite site, IEnumerable<Spot> rim)
    {
        var heights = rim.Select(site.HeightAt).ToList();
        return heights.Max() - heights.Min();
    }

    public static bool Level(BuiltSite site, IReadOnlyList<Spot> ring)
    {
        double span = ring.Max(p => ring.Max(q => (p - q).Length));
        return Fall(site, ring) <= Math.Max(site.M(FallMostM), span * LevelGrade);
    }

    static IEnumerable<(Spot At, Spot Fall)> Down(BuiltSite site, Spot at, double reach)
    {
        var p = at;
        double step = site.M(StepM), run = 0;
        for (int i = 0; i < MostSteps && run <= reach; i++)
        {
            var fall = site.Downhill(p);
            yield return (p, fall);
            var next = p + fall.Unit * step;
            if (fall.Length < LevelGrade / 2 || !site.Inside(next) || site.HeightAt(next) >= site.HeightAt(p)) yield break;
            (p, run) = (next, run + step);
        }
    }

    public static Spot? Hollow(BuiltSite site, Spot at, double radius, double reach)
    {
        var low = Down(site, at, reach).Last().At;
        var rim = Enumerable.Range(0, RimPoints).Select(i => low + Spot.Toward(i * 2 * Math.PI / RimPoints) * radius).ToList();
        if (!site.Buildable(low) || rim.Any(p => !site.Inside(p))) return null;
        return Fall(site, rim) <= site.M(FallMostM) && site.HeightAt(low) <= rim.Min(site.HeightAt) + site.M(FallMostM) / 2 ? low : null;
    }

    static bool Fold(BuiltSite site, Spot at, Spot fall)
    {
        if (fall.Length < LevelGrade / 2) return false;
        var across = fall.Unit.Left * site.M(FoldOutM);
        double rise = site.HeightAt(at) + site.M(FoldRiseM);
        return site.HeightAt(at + across) > rise && site.HeightAt(at - across) > rise;
    }

    public static Dammed? Behind(BuiltSite site, Spot at, double reach, double most)
    {
        foreach (var (p, fall) in Down(site, at, reach))
            if (Fold(site, p, fall) && Holding(site, p, fall.Unit, most) is { } held) return held;
        return null;
    }

    static double Shore(BuiltSite site, Spot from, Spot way, double level, double most)
    {
        double step = site.M(StepM);
        for (double s = step; s <= most; s += step)
        {
            if (site.HeightAt(from + way * s) < level) continue;
            double low = s - step, high = s;
            for (int i = 0; i < Bisections; i++)
            {
                double mid = (low + high) / 2;
                (low, high) = site.HeightAt(from + way * mid) < level ? (mid, high) : (low, mid);
            }
            return high;
        }
        return -1;
    }

    static Dammed? Holding(BuiltSite site, Spot foot, Spot fall, double most)
    {
        var across = fall.Left;
        double level = site.HeightAt(foot) + site.M(HeldM), crest = level + site.M(CrestM);
        double left = Shore(site, foot, across, crest, site.M(DamMostM)), right = Shore(site, foot, across * -1, crest, site.M(DamMostM));
        var middle = foot - fall * site.M(BackM);
        if (left < 0 || right < 0 || site.HeightAt(middle) >= level || !site.Buildable(middle)) return null;
        var dam = new List<Spot> { foot + across * (left + site.M(BankM)), foot - across * (right + site.M(BankM)) };
        double inner = (middle - foot).Dot(fall) * -1 - site.M(DamHalfM);
        var ring = new List<Spot>();
        for (int i = 0; i < Rays; i++)
        {
            var way = Spot.Toward(i * 2 * Math.PI / Rays);
            double toDam = way.Dot(fall) > 0 ? inner / way.Dot(fall) : double.MaxValue;
            double shore = Shore(site, middle, way, level, Math.Min(most, toDam));
            if (shore < 0 && toDam > most) return null;
            ring.Add(middle + way * (shore < 0 ? toDam : shore));
        }
        return ring.All(site.Inside) ? new Dammed(ring, dam, middle, ring.Max(p => (p - middle).Length)) : null;
    }

    public static IEnumerable<BuiltPart> Parts(Dammed held) => [BuiltSite.Area(Held, held.Ring), BuiltSite.Line(Dam, held.Dam)];
}
