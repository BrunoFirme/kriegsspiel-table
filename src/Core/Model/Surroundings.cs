namespace KTable.Core.Model;

public sealed class Surroundings
{
    public const int Bearings = 48;
    const double From = 0.2, To = 1.7, StepM = 5, RiverWidestM = 170, AcrossMostM = 320;

    public sealed record Bank(int Bearing, Spot Land, Spot Way, double Reach, double WideM, bool Sea);

    public sealed record Edge(int Bearing, Spot At, Spot Way, double Reach);

    public IReadOnlyList<Bank> Banks { get; private init; } = [];
    public IReadOnlyList<Edge> Woods { get; private init; } = [];
    public IReadOnlyList<Edge> Marshes { get; private init; } = [];
    public IReadOnlyList<Edge> Rocks { get; private init; } = [];
    public IReadOnlyList<Edge> Sands { get; private init; } = [];
    public IReadOnlyList<Edge> Fields { get; private init; } = [];
    public double Wooded { get; private init; }
    public double Tilled { get; private init; }

    public bool Sea => Banks.Count > 0 && Banks.Count(b => b.Sea) * 2 > Banks.Count;

    public bool River => Banks.Count > 0 && !Sea && Banks.Count(b => b.WideM <= RiverWidestM) * 2 >= Banks.Count;

    public bool Lake => Banks.Count > 0 && !Sea && !River;

    public static Spot Tangent(IReadOnlyList<Bank> banks, int i)
    {
        Bank before = banks[Math.Max(0, i - 1)], after = banks[Math.Min(banks.Count - 1, i + 1)];
        return before.Bearing == after.Bearing || Math.Abs(after.Bearing - before.Bearing) > 2 ? banks[i].Way.Left : (after.Land - before.Land).Unit;
    }

    public static Surroundings Of(BuiltSite site, Spot centre, double within)
    {
        var banks = new List<Bank>();
        List<Edge> woods = [], marshes = [], rocks = [], sands = [], fields = [];
        int seen = 0, wooded = 0, tilled = 0;
        double step = site.M(StepM);
        if (step <= 0 || site.Grid == null) return new Surroundings();
        for (int i = 0; i < Bearings; i++)
        {
            var way = Spot.Toward(i * 2 * Math.PI / Bearings);
            bool wood = false, marsh = false, rock = false, sand = false, field = false;
            for (double far = within * From; far <= within * To; far += step)
            {
                var p = centre + way * far;
                if (!site.Inside(p)) break;
                byte kind = site.KindAt(p);
                if (kind == GroundKinds.Water)
                {
                    double wide = 0;
                    while (wide < site.M(AcrossMostM) && site.Inside(p + way * wide) && site.KindAt(p + way * wide) == GroundKinds.Water) wide += step;
                    banks.Add(new Bank(i, p - way * step, way, far - step, wide / site.UnitsPerMetre, site.Sea(p + way * Math.Min(wide, step * 2))));
                    break;
                }
                seen++;
                if (kind == GroundKinds.Wood) wooded++;
                if (kind == GroundKinds.Field) tilled++;
                if (kind == GroundKinds.Wood && !wood) woods.Add(new Edge(i, p, way, far));
                if (kind == GroundKinds.Marsh && !marsh) marshes.Add(new Edge(i, p, way, far));
                if (kind == GroundKinds.Rock && !rock) rocks.Add(new Edge(i, p, way, far));
                if (kind == GroundKinds.Sand && !sand) sands.Add(new Edge(i, p, way, far));
                if (kind == GroundKinds.Field && !field) fields.Add(new Edge(i, p, way, far));
                field |= kind == GroundKinds.Field;
                wood |= kind == GroundKinds.Wood;
                marsh |= kind == GroundKinds.Marsh;
                rock |= kind == GroundKinds.Rock;
                sand |= kind == GroundKinds.Sand;
            }
        }
        return new Surroundings
        {
            Banks = banks, Woods = woods, Marshes = marshes, Rocks = rocks, Sands = sands, Fields = fields, Wooded = seen == 0 ? 0 : wooded / (double)seen, Tilled = seen == 0 ? 0
                : tilled / (double)seen,
        };
    }
}
