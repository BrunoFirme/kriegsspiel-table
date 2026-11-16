namespace KTable.Core.Model;

public static class Footings
{
    public const string Mark = "ground", Revetment = "revetment";
    public const double StoreyM = 2.8, IntoSlope = 0.45, StoopM = 1.2, CellarMostM = 2 * StoreyM;
    const double LeastFallM = 0.5, WallFromM = 1, ApronM = 1.5, LeastCells = 2, BatterM = 2.5;

    public readonly record struct Pad(IReadOnlyList<Spot> Ring, double Level, bool Carved, double FallM, Footing Kind);

    public static bool Marked(BuiltThing thing) => thing.Setting(Mark, 0) >= 1;

    public static BuiltThing Marking(BuiltThing thing)
    {
        thing.Settings[Mark] = 1;
        return thing;
    }

    public static Footing Kind(BuiltThing thing) => Slopes.Of((int)thing.Setting(Manners.Setting, Manners.Continental)).House;

    public static double Floor(Footing kind, double low, double high, double front, double storey) => kind switch
    {
        Footing.RoofYard => low,
        Footing.Stilts => high,
        _ => Math.Clamp(front, Math.Max(low, high - storey), high),
    };

    public static bool Sits(Footing kind, double lowM, double highM, double frontM)
    {
        double floor = Floor(kind, lowM, highM, frontM, StoreyM);
        return floor - lowM <= CellarMostM && (kind == Footing.Stilts || floor - frontM <= StoopM);
    }

    public static double BatterOf(double unitsPerMetre) => BatterM * unitsPerMetre;

    public static bool Fits(Pad pad, double cell) => pad.Carved && Narrowest(pad.Ring) >= LeastCells * cell;

    internal static double Narrowest(IReadOnlyList<Spot> ring)
    {
        double least = double.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            var along = (ring[(i + 1) % ring.Count] - ring[i]).Unit;
            var across = along.Left;
            double low = ring.Min(p => p.Dot(across)), high = ring.Max(p => p.Dot(across));
            least = Math.Min(least, high - low);
        }
        return least;
    }

    static List<Spot> Grown(IReadOnlyList<Spot> ring, double by)
    {
        var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
        return ring.Select(p => p + (p - middle).Unit * by).ToList();
    }

    public static IEnumerable<Pad> Of(BuiltThing thing, Func<Spot, double> height, double metresPerHeight, double unitsPerMetre)
    {
        if (!Marked(thing) || metresPerHeight <= 0) yield break;
        var kind = Kind(thing);
        if (kind == Footing.Stilts) yield break;
        var floors = thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is Forts.Ground or Forts.Platform).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();
        foreach (var part in thing.Parts)
        {
            if (part.Kind != PartKinds.Block || part.At.Count < 6 || part.Class == Fieldworks.Cave) continue;
            var outline = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (floors.Count > 0 && floors.Any(floor => Enclosures.Inside(floor, BuiltSite.Round(part).At))) continue;
            var heights = outline.Select(height).ToList();
            double low = heights.Min(), high = heights.Max(), fallM = (high - low) * metresPerHeight, front = (heights[0] + heights[1]) / 2;
            if (fallM < LeastFallM) continue;
            var ring = Grown(outline, ApronM * unitsPerMetre);
            yield return kind switch
            {
                Footing.StoneBase => new(ring, Floor(kind, low, high, front, StoreyM / metresPerHeight), false, fallM, kind),
                Footing.Bank when fallM >= StoreyM => new(ring, Floor(kind, low, high, front, StoreyM / metresPerHeight), false, fallM, kind),
                Footing.RoofYard => new(ring, low, true, fallM, kind),
                _ => new(ring, low + (high - low) * IntoSlope, true, fallM, kind),
            };
        }
    }

    public static IEnumerable<BuiltPart> Walls(BuiltThing thing, Func<Spot, double> height, double metresPerHeight, double unitsPerMetre)
    {
        if (!Slopes.Of((int)thing.Setting(Manners.Setting, Manners.Continental)).Revets) yield break;
        foreach (var pad in Of(thing, height, metresPerHeight, unitsPerMetre))
        {
            var ring = pad.Ring;
            var run = new List<Spot>();
            int start = Enumerable.Range(0, ring.Count).FirstOrDefault(i => !Stands(pad, ring[i], ring[(i + 1) % ring.Count], height, metresPerHeight), -1);
            if (start < 0)
            {
                yield return Oriented(ring.Append(ring[0]).ToList(), pad, height);
                continue;
            }
            for (int k = 1; k <= ring.Count; k++)
            {
                int i = (start + k) % ring.Count;
                Spot a = ring[i], b = ring[(i + 1) % ring.Count];
                if (Stands(pad, a, b, height, metresPerHeight))
                {
                    if (run.Count == 0) run.Add(a);
                    run.Add(b);
                    continue;
                }
                if (run.Count > 1) yield return Oriented(run, pad, height);
                run = new();
            }
            if (run.Count > 1) yield return Oriented(run, pad, height);
        }
    }

    static bool Stands(Pad pad, Spot a, Spot b, Func<Spot, double> height, double metresPerHeight)
    {
        double drop = (pad.Level - height((a + b) * 0.5)) * metresPerHeight;
        return pad.Kind is Footing.StoneBase or Footing.Bank ? drop >= WallFromM : Math.Abs(drop) >= WallFromM;
    }

    static BuiltPart Oriented(List<Spot> run, Pad pad, Func<Spot, double> height)
    {
        var mid = run[run.Count / 2];
        var along = run[Math.Min(run.Count - 1, run.Count / 2 + 1)] - run[Math.Max(0, run.Count / 2 - 1)];
        var left = mid + along.Unit.Left * (along.Length + 1e-9);
        bool lowLeft = height(left) < height(mid - along.Unit.Left * (along.Length + 1e-9));
        if (!lowLeft) run.Reverse();
        return BuiltSite.Line(Revetment, run);
    }
}
