namespace KTable.Core.Model;

public static class Brushed
{
    public const string Fill = "fill", Thin = "thin";
    const double ThinShare = 0.5, Packed = 1, WorkRoomM = 5, StepM = 3, LeastRunM = 8, YardReachM = 9;

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static bool Under(IReadOnlyList<Spot> stroke, double radius, Spot p) =>
        stroke.Count == 1 ? (p - stroke[0]).Length <= radius : Enumerable.Range(0, stroke.Count - 1).Any(i => p.FromSegment(stroke[i], stroke[i + 1]) <= radius);

    public static BuiltThing? Thinned(BuiltSite site, BuiltThing place, IReadOnlyList<Spot> stroke, double radius, int seed)
    {
        if (place.Kind != BuiltKinds.Settlement || stroke.Count == 0) return null;
        var dice = new Dice(seed);
        var under = place.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "house" && !p.Kept && Under(stroke, radius, BuiltSite.Round(p).At)).ToList();
        if (under.Count == 0) return null;
        var gone = under.Where(_ => dice.Chance(ThinShare)).ToList();
        if (gone.Count == 0) gone.Add(under.MinBy(p => (BuiltSite.Round(p).At - stroke[0]).Length)!);
        var rounds = gone.Select(BuiltSite.Round).ToList();
        bool Yard(BuiltPart p) => !p.Kept && (p.Kind == PartKinds.Block && p.Class is "shed" or "privy" || p.Kind == PartKinds.Sign && p.Class is "woodpile" or "barrels")
            && rounds.Any(r => (BuiltSite.Round(p).At - r.At).Length < r.Reach + site.M(YardReachM));
        var thinned = place.Clone();
        thinned.Parts.Clear();
        foreach (var part in place.Parts)
        {
            if (Yard(part)) continue;
            if (!gone.Contains(part))
            {
                thinned.Parts.Add(part.Clone());
                continue;
            }
            var bed = BuiltSite.Area("garden", Points(part));
            bed.Kept = true;
            thinned.Parts.Add(bed);
        }
        return thinned;
    }

    static IEnumerable<(Spot A, Spot B)> Runs(IReadOnlyList<Spot> path, IReadOnlyList<Spot> stroke, double radius, double step, double least)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Spot a = path[i], ab = path[i + 1] - a;
            int steps = Math.Max(1, (int)Math.Ceiling(ab.Length / step));
            int from = -1;
            for (int s = 0; s <= steps + 1; s++)
            {
                bool under = s <= steps && Under(stroke, radius, a + ab * (s / (double)steps));
                if (under && from < 0) from = s;
                if (under || from < 0) continue;
                if ((s - 1 - from) * ab.Length / steps >= least) yield return (a + ab * (from / (double)steps), a + ab * ((s - 1) / (double)steps));
                from = -1;
            }
        }
    }

    public static BuiltThing? Filled(BuiltSite site, BuiltThing place, IReadOnlyList<Spot> stroke, double radius, int seed)
    {
        if (place.Kind != BuiltKinds.Settlement || stroke.Count == 0) return null;
        var (centre, _) = BuiltThings.Round(place);
        var dice = new Dice(seed);
        var streets = place.Parts.Where(BuiltSite.IsRoad).Select(p => new Settlements.Street(Points(p), p.Class, false)).ToList();
        streets.AddRange(site.RoadLegs.Select(leg => new Settlements.Street([leg.A, leg.B], leg.Class, false)));
        var filled = place.Clone();
        var taken = filled.Parts.Where(p => p.Kind == PartKinds.Block || p.Kind == PartKinds.Area && p.Class is not (BuiltThings.Quarter or "garden" or "orchard")).Select(BuiltSite.Round).ToList();
        int before = filled.Parts.Count;
        foreach (var street in streets.ToList())
            foreach (var (a, b) in Runs(street.Points, stroke, radius, site.M(StepM), site.M(LeastRunM)).ToList())
                Homes.Front(site, dice, centre, double.MaxValue, new Settlements.Street([a, b], street.Class, false), streets.Where(s => s != street).ToList(), Packed, filled.Parts, taken);
        var works = filled.Parts.Take(before).Where(p => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade" or "ditch" or "moat" or "rampart").ToList();
        var made = filled.Parts.Skip(before).Where(p => BuiltSite.Round(p) is var (at, r) && !works.Any(w => BuiltThings.Reach(w, at) < r + site.M(WorkRoomM))).ToList();
        filled.Parts.RemoveRange(before, filled.Parts.Count - before);
        if (!made.Any(p => p.Kind == PartKinds.Block)) return null;
        var stands = made.Where(p => p.Kind == PartKinds.Block).ToList();
        bool Covered(BuiltPart old) => old.Kind switch
        {
            PartKinds.Area => old.Class is "garden" or "orchard" && stands.Any(s => BuiltThings.Reach(old, BuiltSite.Round(s).At) <= 0),
            PartKinds.Line => !old.Kept && old.Class == "fence" && old.At.Count >= 6 && stands.Any(s => Enclosures.Inside(Points(old), BuiltSite.Round(s).At)),
            PartKinds.Sign => !old.Kept && old.Class is "tree" or "hive" or "woodpile" or "rick" && stands.Any(s => BuiltThings.Reach(s, Points(old)[0]) <= 0),
            _ => false,
        };
        filled.Parts.RemoveAll(Covered);
        foreach (var part in made) part.Kept = true;
        filled.Parts.AddRange(made);
        return filled;
    }
}
