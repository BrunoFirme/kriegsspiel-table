namespace KTable.Core.Model;

static class Gardens
{
    const double TerraceFrom = 0.06, LongM = 84, WideM = 48, StepM = 1.5, KushkLongM = 22, KushkDeepM = 12, KushkShare = 0.7, PoolLongM = 14, PoolDeepM = 9, WellsApartM = 28;
    const double TreeAside = 0.55, CrossShare = 0.85, Taken = 0.6, StewReach = 0.45, StewMostM = 45, StewGapM = 6, AboveM = 6, AcequiaLeastM = 30, AcequiaMoreM = 20;
    const int FewestSteps = 3, MostSteps = 6, MostWells = 14, MostStews = 3, LeastBands = 4;

    internal static bool Terraced(BuiltSite site, Spot at, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var fall = site.Downhill(at);
        if (fall.Length < TerraceFrom) return false;
        Spot down = fall.Unit, across = down.Left;
        double half = site.M(LongM) / 2, wide = site.M(WideM) / 2;
        var ring = Industry.Rect(at, down, site.M(LongM), site.M(WideM));
        if (ring.Any(p => !site.Free(p, 0))) return true;
        int steps = Math.Clamp((int)Math.Round((site.HeightAt(at - down * half) - site.HeightAt(at + down * half)) / site.M(StepM)), FewestSteps, MostSteps);
        double band = 2 * half / steps;
        parts.Add(BuiltSite.Area(Terraces.Garden, Industry.Rect(at + down * (band / 2), down, 2 * half - band, site.M(WideM))));
        parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
        for (int k = 0; k < steps; k++)
        {
            Spot upper = at - down * (half - band * k), lower = upper + down * band, mid = upper + down * (band / 2);
            parts.Add(BuiltSite.Area(Terraces.Terrace, [upper - across * wide, upper + across * wide, lower + across * wide, lower - across * wide]));
            parts.Add(BuiltSite.Line(Terraces.Wall, [lower + across * wide, lower - across * wide]));
            foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Sign("tree", mid + across * (side * wide * TreeAside)));
            if (k > 0 && k < steps - 1) parts.Add(BuiltSite.Sign("fountain", mid));
            if (k == steps / 2)
                foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Line(Watercourses.Channel, [mid + across * (side * wide * CrossShare), mid]));
        }
        double deep = Math.Min(site.M(KushkDeepM), band * KushkShare);
        Spot kushk = at - down * (half - band / 2), pool = at + down * (half - band / 2);
        parts.Add(BuiltSite.Block("public", kushk, across, site.M(KushkLongM), deep));
        parts.Add(BuiltSite.Line(Watercourses.Channel, [kushk + down * (deep / 2), pool - down * site.M(PoolDeepM / 2)]));
        parts.Add(BuiltSite.Area(Terraces.Pool, Industry.Rect(pool, across, site.M(PoolLongM), site.M(PoolDeepM))));
        for (int i = 1; i <= MostWells && site.Free(at - down * (half + site.M(WellsApartM) * i), 0); i++) parts.Add(BuiltSite.Sign(Hallmarks.Qanat, at - down * (half + site.M(WellsApartM) * i)));
        taken.Add((at, half * (1 + Taken)));
        return true;
    }

    internal static void Stews(BuiltSite site, Spot at, double within, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var stews = new List<Ponds.Dammed>();
        var from = at;
        for (int i = 0; i < MostStews; i++)
        {
            if (Ponds.Behind(site, from, within * StewReach, site.M(StewMostM)) is not { } held) break;
            if (held.Ring.Concat(held.Dam).Any(p => !site.Free(p, 0) || taken.Any(o => (o.At - p).Length < o.Reach) || stews.Any(s => Enclosures.Inside(s.Ring, p)))) break;
            stews.Add(held);
            parts.AddRange(Ponds.Parts(held));
            var dam = (held.Dam[0] + held.Dam[1]) * 0.5;
            from = dam + site.Downhill(dam).Unit * site.M(StewGapM);
        }
        taken.AddRange(stews.Select(s => (s.Middle, s.Reach)));
    }

    internal static void Acequia(BuiltSite site, List<BuiltPart> parts)
    {
        var fields = parts.Where(p => p.Kind == PartKinds.Area && p.Class is Terraces.Terrace or Terraces.Paddy).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();
        if (fields.Count < LeastBands) return;
        var head = fields.SelectMany(r => r).MaxBy(site.HeightAt);
        var down = site.Downhill(head).Unit;
        var start = head - down * site.M(AboveM);
        var standing = parts.Where(p => p.Kind == PartKinds.Block).Select(BuiltSite.Round).ToList();
        bool Blocked(Spot p) => standing.Any(o => (o.At - p).Length < o.Reach);
        if (Blocked(start)) return;
        double Reach(Spot way) => Math.Max(0, fields.SelectMany(r => r).Max(p => (p - start).Dot(way))) + site.M(AcequiaMoreM);
        var ends = new[] { down.Left, down.Left * -1 }.Select(way => Watercourses.Along(site, start, way, site.HeightAt(start), 0, Reach(way), Blocked)).ToList();
        var intake = site.NearestWater(ends[1][^1]) is var (_, far) && (site.NearestWater(ends[0][^1])?.Reach ?? double.MaxValue) > far ? ends[1][^1] : ends[0][^1];
        var tail = intake == ends[0][^1] ? ends[1][^1] : ends[0][^1];
        double length = ends.Sum(e => e.Zip(e.Skip(1), (a, b) => (b - a).Length).Sum());
        if (length < site.M(AcequiaLeastM)) return;
        var course = Watercourses.Along(site, intake, tail - intake, site.HeightAt(intake), Watercourses.ChannelFall, length, Blocked);
        if (course.Count > 2) parts.Add(BuiltSite.Line(Watercourses.Channel, course));
    }
}
