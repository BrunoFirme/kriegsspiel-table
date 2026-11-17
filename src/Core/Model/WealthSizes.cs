namespace KTable.Core.Model;

public static class WealthSizes
{
    const string Fence = "fence";
    const int Halvings = 10, SlackUnits = 2, LeastCorners = 4, FenceInts = 4, CroftInts = 8, CornerInts = 2, EndAt = 6;
    static readonly string[] Crofts = ["orchard", Terraces.Garden];

    public static double Typical(WealthLevel level) => level switch
    {
        WealthLevel.Poor => Wealth.PoorBelow / 2,
        WealthLevel.Rich => (Wealth.RichFrom + 1) / 2,
        _ => (Wealth.RichFrom + Wealth.PoorBelow) / 2,
    };

    public static double Factor(WealthLevel laid, WealthLevel to) => Callings.Scale(Typical(to)) / Callings.Scale(Typical(laid));

    public static bool Sized(BuiltPart part) => Looks.Takes(part) && WealthLooks.Dresses(part.Class);

    public static List<int> LaidOf(BuiltPart part) => part.LaidAt is { } laid && Holds(part.At, laid) ? laid : part.At;

    public static Spot Standing(BuiltPart part)
    {
        var ring = Ring(LaidOf(part));
        return new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
    }

    static List<Spot> Ring(List<int> at) => BuiltGeometry.Points(at).Select(Spot.Of).ToList();

    public static List<int> Scaled(List<int> laid, double by)
    {
        var ring = Ring(laid);
        var front = (ring[0] + ring[1]) * 0.5;
        return BuiltGeometry.Flat(ring.Select(p => (front + (p - front) * by).Stored));
    }

    static bool Holds(List<int> at, List<int> laid)
    {
        if (at.Count != laid.Count || laid.Count < LeastCorners * CornerInts) return false;
        List<Spot> now = Ring(at), was = Ring(laid);
        double from = (was[1] - was[0]).Length;
        if (from <= 0) return false;
        var sized = Scaled(laid, (now[1] - now[0]).Length / from);
        return sized.Zip(at).All(p => Math.Abs(p.First - p.Second) <= SlackUnits);
    }

    internal static void Fit(BuiltThing set, int index, BuiltSite? site)
    {
        var part = set.Parts[index];
        if (!Sized(part)) return;
        var laid = new List<int>(LaidOf(part));
        double factor = part.Wealth == null ? 1 : Factor(PlaceZones.Laid(part), PlaceZones.WealthOf(part));
        var sized = factor == 1 ? laid : factor < 1 ? Scaled(laid, factor) : Largest(set, index, site, laid, factor);
        bool same = sized.SequenceEqual(laid);
        part.At = same ? laid : sized;
        part.LaidAt = same ? null : laid;
        Walled(set, index, part.Wealth != null && PlaceZones.WealthOf(part) == WealthLevel.Rich);
    }

    static List<int> Largest(BuiltThing set, int index, BuiltSite? site, List<int> laid, double factor)
    {
        double perMetre = site is { UnitsPerMetre: > 0 } ? site.UnitsPerMetre : set.Setting(BuiltThings.Scale, 0);
        if (perMetre <= 0) return laid;
        var most = Scaled(laid, factor);
        var room = new WealthRoom(set, index, site, perMetre, Ring(laid), Ring(most));
        if (room.Fits(Ring(most))) return most;
        double low = 1, high = factor;
        for (int k = 0; k < Halvings; k++)
        {
            double mid = (low + high) / 2;
            if (room.Fits(Ring(Scaled(laid, mid)))) low = mid;
            else high = mid;
        }
        return low == 1 ? laid : Scaled(laid, low);
    }

    static void Walled(BuiltThing set, int index, bool rich)
    {
        if (set.Parts[index].Group == 0) return;
        var own = Groups.Of(set, index).Select(i => set.Parts[i]).ToList();
        foreach (var line in own.Where(p => p.Kind == PartKinds.Line))
        {
            if (rich && line.Class == Fence && line.LaidAt == null && Croft(own, line.At) is { } ring)
            {
                line.LaidAt = line.At;
                line.Class = Settlements.GardenWall;
                line.At = BuiltGeometry.Flat([ring[0], ring[3], ring[2], ring[1]]);
            }
            else if (!rich && line.Class == Settlements.GardenWall && line.LaidAt is { Count: FenceInts } fence && line.At.Take(FenceInts).SequenceEqual(fence))
            {
                line.Class = Fence;
                line.At = fence;
                line.LaidAt = null;
            }
        }
    }

    static List<SheetPoint>? Croft(List<BuiltPart> own, List<int> fence) =>
        fence.Count == FenceInts && own.FirstOrDefault(p => p.Kind == PartKinds.Area && Crofts.Contains(p.Class) && p.At.Count == CroftInts
            && p.At.Take(CornerInts).SequenceEqual(fence.Take(CornerInts)) && p.At.Skip(EndAt).SequenceEqual(fence.Skip(CornerInts))) is { } croft
            ? BuiltGeometry.Points(croft.At) : null;
}
