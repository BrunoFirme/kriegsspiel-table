namespace KTable.Core.Model;

public static class Forts
{
    const double LeastPulled = 0.6;
    const int TracePoints = 5, TipAt = 2;
    public const string Bastions = "bastions", Outworks = "outworks", Form = "form";
    public const string Ground = "terreplein", Ravelin = "ravelin", Ditch = "moat", Slope = "glacis", Platform = "platform";
    public const int FewestBastions = 4, MostBastions = 8;
    const double CurtainAt = 0.7, ShoulderAt = 0.84, FlankShare = 0.36, ShoulderShare = 0.3;
    public const double Reach = 1.42, CurtainClear = 0.65, LinkReach = 3, ShiftReach = 3;
    public const double GlacisFrom = 0.1, RavelinsFrom = 0.35;
    const double ParadeAt = 0.3, BarracksAt = 0.52, BarrackDepthM = 10, BarrackMostM = 60, BarrackShare = 0.45, BarrackPlay = 0.7;
    const double FurnishedFromM = 90, CornerAt = 0.5, MagazineLongM = 12, MagazineDeepM = 8, QuartersLongM = 16, QuartersDeepM = 10, WellAt = 0.18;
    const double RavelinWide = 0.5, RavelinLong = 0.62, RavelinOff = 0.8, RavelinLeastM = 16, GateHalfM = 4.5, GatedReachM = 30, RingStepM = 3, RingKeepM = 0.25;
    const double FullAtM = 100, LeastScale = 0.5, MostScale = 1.2, BlendM = 6, InnerShare = 0.45;
    const int MostFurnishedSides = 6, RingLeast = 96, RingMost = 420, Halvings = 22;

    public readonly record struct Profile(double Crest, double Talus, double Scarp, double Wide, double Way, double Glacis, double High, double Deep, double Lip, double Outwork)
    {
        public double Foot => Crest / 2 + Scarp;
        public double Brink => Wide + Scarp;
        public double Rim => Brink + Way;
        public double Out => Rim + Math.Max(Glacis, Scarp * 3);
        public double Inner => Crest / 2 + Talus;

        public Profile In(double unitsPerMetre) =>
            new(Crest * unitsPerMetre, Talus * unitsPerMetre, Scarp * unitsPerMetre, Wide * unitsPerMetre, Way * unitsPerMetre, Glacis * unitsPerMetre, High, Deep, Lip, Outwork);
    }

    public static Profile Shape(double radiusM, bool field, bool glacis)
    {
        double k = Math.Clamp(radiusM / FullAtM, LeastScale, MostScale), tall = Math.Sqrt(k);
        return field ? new(3.2 * k, 4.5 * k, 1.5, 5.5 * k, 0, glacis ? 13 * k : 0, 2.6, 2.2, glacis ? 0.9 : 0, 0)
            : new(7 * k, 8.5 * k, 2, 17 * k, glacis ? 7 * k : 0, glacis ? 36 * k : 0, 6.5 * tall, 4.5 * tall, glacis ? 2.3 * tall : 0, 4.6 * tall);
    }

    public static Profile Raised(double radiusM, bool inner)
    {
        double k = Math.Clamp(radiusM / FullAtM, LeastScale, MostScale);
        return inner ? new(0, 0, 5 * k, 0, 0, 0, 8, 0, 0, 0) : new(0, 0, 4 * k, 22 * k, 0, 0, 4, 3.5, 0, 0);
    }

    static double Signed(IReadOnlyList<Spot> ring, Spot p)
    {
        double least = double.MaxValue;
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            Spot a = ring[j], b = ring[i];
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            least = Math.Min(least, p.FromSegment(a, b));
        }
        return inside ? -least : least;
    }

    public sealed class Body
    {
        public required IReadOnlyList<Spot> Trace { get; init; }
        public required IReadOnlyList<IReadOnlyList<Spot>> Ravelins { get; init; }
        public required IReadOnlyList<(Spot From, Spot To)> Gates { get; init; }
        public required Profile P { get; init; }
        public required bool Level { get; init; }
        public required double Blend { get; init; }
        public required double GateHalf { get; init; }
        public bool Whole { get; init; }
        public bool Crag { get; init; }

        public Spot Middle => new(Trace.Average(p => p.X), Trace.Average(p => p.Y));

        public double Girth => Trace.Max(p => (p - Middle).Length) + P.Foot + P.Out;

        double Outwork(Spot p)
        {
            double least = double.MaxValue;
            foreach (var ravelin in Ravelins) least = Math.Min(least, Signed(ravelin, p));
            return least;
        }

        public double Beyond(Spot p) => Math.Min(Signed(Trace, p) - P.Foot, Outwork(p));

        public (double Up, double Natural) At(Spot p)
        {
            double e = Signed(Trace, p);
            if (Crag) return Cragged(p, e);
            bool gated = Gates.Any(g => p.FromSegment(g.From, g.To) < GateHalf);
            if (Whole && e <= P.Foot) return (e <= 0 ? P.High : P.High + (-P.Deep - P.High) * (e / P.Scarp), 0);
            if (e <= P.Foot)
            {
                if (e < -P.Inner) return (0, Level ? 0 : Math.Clamp((-e - P.Inner) / Blend, 0, 1));
                if (gated) return (0, 0);
                return (e > P.Crest / 2 ? P.High + (-P.Deep - P.High) * ((e - P.Crest / 2) / P.Scarp) : e >= -P.Crest / 2 ? P.High : P.High * (1 - (-P.Crest / 2 - e) / P.Talus), 0);
            }
            double work = Outwork(p), g = Math.Min(e - P.Foot, work);
            if (work <= 0) return (-work < P.Crest ? P.Outwork : P.Outwork * 0.4, 0);
            if (g > P.Out) return (0, 1);
            if (gated && g <= P.Rim) return (0, 0);
            if (g <= P.Wide) return (-P.Deep, 0);
            if (g <= P.Brink) return (-P.Deep * (1 - (g - P.Wide) / P.Scarp), 0);
            if (g <= P.Rim) return (0, 0);
            double t = (g - P.Rim) / (P.Out - P.Rim);
            return (P.Lip * (1 - t), t);
        }

        (double Up, double Natural) Cragged(Spot p, double e)
        {
            if (Whole) return (0, Math.Clamp(e / Blend, 0, 1));
            if (e < -P.Inner) return (0, 0);
            if (Gates.Any(g => p.FromSegment(g.From, g.To) < GateHalf) && e <= P.Foot) return (0, 0);
            if (e <= P.Crest / 2) return (e >= -P.Crest / 2 ? P.High : P.High * (1 - (-P.Crest / 2 - e) / P.Talus), 0);
            if (e <= P.Foot) return (P.High * (1 - (e - P.Crest / 2) / P.Scarp), 0);
            double work = Outwork(p);
            if (work <= 0) return (-work < P.Crest ? P.Outwork : P.Outwork * 0.4, 0);
            return (0, Math.Clamp((e - P.Foot) / Blend, 0, 1));
        }

        public List<Spot> Ring(double beyond, double step)
        {
            var middle = Middle;
            double far = Girth + beyond;
            int count = Math.Clamp((int)(2 * Math.PI * far / step), RingLeast, RingMost);
            var bearings = Enumerable.Range(0, count).Select(i => i * 2 * Math.PI / count).Concat(Trace.Concat(Ravelins.SelectMany(r => r)).Select(p => Math.Atan2(p.Y - middle.Y, p.X - middle.X)))
                .Select(a => (a % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI)).Distinct().OrderBy(a => a);
            var ring = new List<Spot>();
            foreach (double bearing in bearings)
            {
                var way = Spot.Toward(bearing);
                double near = 0, out_ = far;
                for (int i = 0; i < Halvings; i++)
                {
                    double mid = (near + out_) / 2;
                    if (Beyond(middle + way * mid) > beyond) out_ = mid;
                    else near = mid;
                }
                ring.Add(middle + way * near);
            }
            var kept = new List<Spot> { ring[0] };
            for (int i = 1; i + 1 < ring.Count; i++)
                if (ring[i].FromSegment(kept[^1], ring[i + 1]) > step * RingKeepM) kept.Add(ring[i]);
            kept.Add(ring[^1]);
            return kept;
        }
    }

    static List<Spot> RingOf(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    public static bool Shaped(BuiltThing thing) => thing.Parts.Any(p => p.Kind == PartKinds.Area && p.Class is Ground or Platform);

    public static IEnumerable<Body> Bodies(BuiltThing thing, double unitsPerMetre)
    {
        if (unitsPerMetre <= 0) yield break;
        var ravelins = thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == Ravelin).Select(RingOf).ToList();
        bool glacis = thing.Parts.Any(p => p.Kind == PartKinds.Area && p.Class == Slope), marked = Footings.Marked(thing);
        var moats = thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is Ditch or Castles.Cut).Select(RingOf).Where(r => r.Count >= 3).ToList();
        int platforms = 0;
        foreach (var ground in thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is Ground or Platform))
        {
            var trace = RingOf(ground);
            if (trace.Count < 3) continue;
            bool whole = ground.Class == Platform;
            var middle = new Spot(trace.Average(p => p.X), trace.Average(p => p.Y));
            double reach = trace.Max(p => (p - middle).Length) / unitsPerMetre;
            var shape = (whole ? Raised(platforms++ == 0 ? reach : reach / InnerShare, platforms > 1) : Shape(reach, trace.Count == 4, glacis)).In(unitsPerMetre);
            bool crag = marked && !moats.Any(moat => Enclosures.Inside(moat, middle));
            var body = new Body
            {
                Trace = trace, Ravelins = whole ? []
                    : ravelins, Gates = [], P = shape, Level = crag || thing.Kind == BuiltKinds.Fort, Blend = BlendM * unitsPerMetre, GateHalf = GateHalfM * unitsPerMetre, Whole = whole,
                Crag = crag,
            };
            var gates = new List<(Spot, Spot)>();
            foreach (var bridge in thing.Parts.Where(p => p.Kind == PartKinds.Sign && p.Class == "bridge" && p.At.Count >= 2))
            {
                var at = Spot.Of(BuiltGeometry.Points(bridge.At)[0]);
                if (body.Beyond(at) is not (> 0 and var off) || off > shape.Brink) continue;
                var way = Spot.Toward(bridge.Turn * Math.PI / 1800);
                gates.Add((at - way * (shape.Brink + shape.Foot + shape.Inner), at + way * shape.Rim));
            }
            yield return new Body { Trace = trace, Ravelins = body.Ravelins, Gates = gates, P = shape, Level = body.Level, Blend = body.Blend, GateHalf = body.GateHalf, Whole = whole, Crag = crag };
        }
    }

    static double Gap(double from, double to)
    {
        double gap = ((to - from) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        return gap < 1e-9 ? 2 * Math.PI : gap;
    }

    static List<(double Bearing, double Radius)> Even(double radius, int bastions, double turn) =>
        Enumerable.Range(0, bastions).Select(i => (turn + i * 2 * Math.PI / bastions, radius)).ToList();

    public static List<Spot> Trace(Spot centre, double radius, int bastions, double turn) => Trace(centre, Even(radius, bastions, turn));

    public static List<Spot> Trace(Spot centre, IReadOnlyList<(double Bearing, double Radius)> tips)
    {
        var trace = new List<Spot>();
        for (int i = 0; i < tips.Count; i++)
        {
            var (a, radius) = tips[i];
            double before = Gap(tips[(i + tips.Count - 1) % tips.Count].Bearing, a) / 2, after = Gap(a, tips[(i + 1) % tips.Count].Bearing) / 2;
            trace.Add(centre + Spot.Toward(a - before * FlankShare) * (radius * CurtainAt));
            trace.Add(centre + Spot.Toward(a - before * ShoulderShare) * (radius * ShoulderAt));
            trace.Add(centre + Spot.Toward(a) * radius);
            trace.Add(centre + Spot.Toward(a + after * ShoulderShare) * (radius * ShoulderAt));
            trace.Add(centre + Spot.Toward(a + after * FlankShare) * (radius * CurtainAt));
        }
        return trace;
    }

    public sealed record Star(Spot Centre, List<(double Bearing, double Radius)> Tips, double Outworks);

    static Spot? Centre(Spot left, Spot tip, Spot right)
    {
        Spot mid = (left + right) * 0.5, chord = (right - left).Unit, inward = chord.Left.Dot(mid - tip) > 0 ? chord.Left : chord.Left * -1;
        double h = (right - left).Length / 2, d = (mid - tip).Dot(inward), s = (tip - mid).Dot(chord), q = ShoulderAt * ShoulderAt;
        double a = 1 - q, b = -2 * q * d, c = h * h - q * (d * d + s * s), disc = b * b - 4 * a * c;
        return disc < 0 ? null : mid + inward * ((-b + Math.Sqrt(disc)) / (2 * a));
    }

    public static Star? Of(BuiltThing place)
    {
        var trace = place.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == Ground).Select(RingOf).Concat(place.Parts.Where(Ringed).Select(p => RingOf(p).SkipLast(1).ToList()))
            .FirstOrDefault(ring => ring.Count >= TracePoints * FewestBastions && ring.Count % TracePoints == 0);
        if (trace == null || Centre(trace[TipAt - 1], trace[TipAt], trace[TipAt + 1]) is not { } centre) return null;
        var tips = Enumerable.Range(0, trace.Count / TracePoints).Select(i => trace[i * TracePoints + TipAt] - centre).Select(tip => (Math.Atan2(tip.Y, tip.X), tip.Length)).ToList();
        bool ravelins = place.Parts.Any(p => p.Kind == PartKinds.Area && p.Class == Ravelin || p.Kind == PartKinds.Line && p.Class == "rampart" && p.At.Count == 6);
        bool glacis = place.Parts.Any(p => p.Class == Slope && p.Kind is PartKinds.Area or PartKinds.Line);
        return new Star(centre, tips, ravelins ? 1 : glacis ? GlacisFrom : 0);
    }

    static bool Ringed(BuiltPart p) => p.Kind == PartKinds.Line && p.Class == "rampart" && p.At.Count > 2 * TracePoints * FewestBastions && p.At[0] == p.At[^2] && p.At[1] == p.At[^1];

    static bool Earthwork(BuiltPart p) =>
        p.Kind == PartKinds.Area && p.Class is Ground or Ravelin or Ditch or Slope or Platform || p.Kind == PartKinds.Line && p.Class is "rampart" or "glacis" or Ditch or Castles.Cut;

    public static BuiltThing? TipMoved(BuiltSite site, BuiltThing place, int tip, Spot to)
    {
        if (Of(place) is not { } star || tip < 0 || tip >= star.Tips.Count) return null;
        var tips = star.Tips.ToList();
        tips[tip] = (tips[tip].Bearing, Math.Max((to - star.Centre).Length, star.Tips.Min(t => t.Radius) * LeastPulled));
        var moved = place.Clone();
        var gates = moved.Parts.Where(p => p.Kind == PartKinds.Sign && p.Class == "bridge").Select(p => Spot.Of(BuiltGeometry.Points(p.At)[0])).ToList();
        bool crag = Footings.Marked(place) && !place.Parts.Any(p => p.Kind == PartKinds.Area && p.Class == Ditch);
        double own = star.Tips.Max(t => t.Radius) * FortGround.DetachedFrom;
        moved.Parts.RemoveAll(p => Earthwork(p) && (BuiltSite.Round(p).At - star.Centre).Length < own);
        double perMetre = site.UnitsPerMetre > 0 ? site.UnitsPerMetre : 1, reach = Shape(tips.Max(t => t.Radius) / perMetre, false, star.Outworks >= GlacisFrom).In(perMetre).Out;
        moved.Parts.InsertRange(0, Works(site, star.Centre, Trace(star.Centre, tips), tips, star.Outworks, gates, crag ? FortGround.Open(site, star.Centre, reach) : null));
        return moved;
    }

    public static bool Plain(BuiltThing thing) => thing.Kind == BuiltKinds.Fort && thing.Settings.GetValueOrDefault(Bastions) < FewestBastions && thing.Setting(Form, 0) < 1;

    internal static List<Spot> Redoubt(Spot centre, double radius, double turn) =>
        Enumerable.Range(0, 4).Select(i => centre + Spot.Toward(turn + Math.PI / 4 + i * Math.PI / 2) * radius).ToList();

    static List<Spot> Closed(IEnumerable<Spot> ring)
    {
        var closed = ring.ToList();
        closed.Add(closed[0]);
        return closed;
    }

    public static List<BuiltPart> Works(BuiltSite site, Spot centre, double radius, int bastions, double turn, double outworks, IReadOnlyList<Spot>? gates = null) =>
        bastions < FewestBastions ? Works(site, centre, Redoubt(centre, radius, turn), [], outworks, gates ?? []) : Around(site, centre, Even(radius, bastions, turn), outworks, gates);

    public static List<BuiltPart> Around(BuiltSite site, Spot centre, IReadOnlyList<(double Bearing, double Radius)> tips, double outworks, IReadOnlyList<Spot>? gates = null) =>
        Works(site, centre, Trace(centre, tips), tips, outworks, gates ?? []);

    internal static List<BuiltPart> Works(BuiltSite site, Spot centre, List<Spot> trace, IReadOnlyList<(double Bearing, double Radius)> tips, double outworks, IReadOnlyList<Spot> gates,
        Func<Spot, bool>? open = null)
    {
        double radius = trace.Max(p => (p - centre).Length), perMetre = site.UnitsPerMetre > 0 ? site.UnitsPerMetre : 1;
        bool glacis = outworks >= GlacisFrom;
        var shape = Shape(radius / perMetre, tips.Count == 0, glacis).In(perMetre);
        var ravelins = new List<List<Spot>>();
        for (int i = 0; outworks >= RavelinsFrom && i < tips.Count; i++)
        {
            Spot a = trace[i * TracePoints + TracePoints - 1], b = trace[(i + 1) % tips.Count * TracePoints], mid = (a + b) * 0.5, along = (b - a).Unit;
            var outward = along.Left.Dot(mid - centre) > 0 ? along.Left : along.Left * -1;
            double wide = (b - a).Length * RavelinWide, from = shape.Foot + shape.Wide * RavelinOff;
            if (wide < site.M(RavelinLeastM) || gates.Any(g => (g - mid).Length < (b - a).Length / 2 + site.M(GatedReachM)) || open?.Invoke(mid) == false) continue;
            ravelins.Add([mid - along * (wide / 2) + outward * from, mid + outward * (from + wide * RavelinLong), mid + along * (wide / 2) + outward * from]);
        }
        var body = new Body { Trace = trace, Ravelins = ravelins, Gates = [], P = shape, Level = true, Blend = 0, GateHalf = 0 };
        double step = site.M(RingStepM);
        var parts = new List<BuiltPart>();
        if (open != null) return Hillside(site, body, trace, ravelins, glacis, open);
        if (glacis) parts.Add(BuiltSite.Area(Slope, body.Ring(shape.Out, step)));
        parts.Add(BuiltSite.Area(Ditch, body.Ring(shape.Wide + shape.Scarp / 2, step)));
        parts.Add(BuiltSite.Area(Ground, trace));
        parts.AddRange(ravelins.Select(ravelin => BuiltSite.Area(Ravelin, ravelin)));
        if (glacis) parts.Add(BuiltSite.Line("glacis", Closed(body.Ring(shape.Rim, step))));
        parts.Add(BuiltSite.Line("rampart", Closed(trace)));
        parts.AddRange(ravelins.Select(ravelin => BuiltSite.Line("rampart", ravelin)));
        return parts;
    }

    static List<BuiltPart> Hillside(BuiltSite site, Body body, List<Spot> trace, List<List<Spot>> ravelins, bool glacis, Func<Spot, bool> open)
    {
        double step = site.M(RingStepM);
        var parts = new List<BuiltPart> { BuiltSite.Area(Ground, trace) };
        parts.AddRange(ravelins.Select(ravelin => BuiltSite.Area(Ravelin, ravelin)));
        parts.AddRange(Enclosures.Runs(body.Ring(body.P.Wide / 2, step), open).Select(run => BuiltSite.Line(Castles.Cut, run)));
        if (glacis) parts.AddRange(Enclosures.Runs(body.Ring(body.P.Rim, step), open).Select(run => BuiltSite.Line("glacis", run)));
        parts.Add(BuiltSite.Line("rampart", Closed(trace)));
        parts.AddRange(ravelins.Select(ravelin => BuiltSite.Line("rampart", ravelin)));
        return parts;
    }

    public static Spot Gate(BuiltSite site, IReadOnlyList<Spot> trace, Spot centre, double bearing, bool glacis, bool platform = false)
    {
        var way = Spot.Toward(bearing);
        double radius = trace.Max(p => (p - centre).Length), perMetre = site.UnitsPerMetre > 0 ? site.UnitsPerMetre : 1;
        var shape = (platform ? Raised(radius / perMetre, false) : Shape(radius / perMetre, trace.Count == 4, glacis)).In(perMetre);
        var hit = Enclosures.Hits(trace, centre, centre + way * (radius * 2)).OrderBy(p => (p - centre).Length).Select(p => (Spot?)p).FirstOrDefault() ?? centre + way * radius;
        return hit + way * (shape.Foot + shape.Wide / 2);
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot asked, double radius, int seed, int bastions, double outworks, double turn)
    {
        bool field = bastions < FewestBastions;
        double perMetre = site.UnitsPerMetre > 0 ? site.UnitsPerMetre : 1;
        var shape = Shape(radius / perMetre, field, outworks >= GlacisFrom).In(perMetre);
        double girth = radius + shape.Foot + shape.Brink;
        var knoll = field && site.Epoch == Manners.Later ? FortGround.Knoll(site, asked, radius) : null;
        asked = knoll ?? asked;
        bool crag = FortGround.Steep(site, asked, girth) && Strongholds.Holds(site, asked, radius);
        var centre = crag ? asked : site.DryNear(asked, girth, radius * ShiftReach) ?? asked;
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Fort, Seed = seed, Settings = { [Bastions] = bastions, [Outworks] = outworks, ["turn"] = turn, [Manners.Setting] = site.Manner, [Footings.Mark] = 1 },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + Spot.Toward(turn) * radius).Stored]),
        };
        if (!crag && !site.Dry(centre, girth)) return thing;
        int sides = Math.Max(4, bastions);
        double inner = radius * (field ? CurtainAt : CurtainAt * Math.Cos(Math.PI / sides)) - shape.Inner;
        var dice = new Dice(seed);
        int entry = dice.Below(sides);
        var road = site.NearestRoad(centre) is var (to, off) && off > radius && off < radius * LinkReach ? to : (Spot?)null;
        double Side(int i) => turn + (2 * i + 1) * Math.PI / sides;
        if (road is { } toward) entry = Enumerable.Range(0, sides).MinBy(i => Math.Abs(Math.IEEERemainder(Side(i) - Math.Atan2(toward.Y - centre.Y, toward.X - centre.X), 2 * Math.PI)));
        double gate = Side(entry);
        List<(double Bearing, double Radius)> tips = field ? [] : crag ? FortGround.Tips(site, centre, radius, bastions, turn) : Even(radius, bastions, turn);
        var trace = field ? Redoubt(centre, radius, turn) : Trace(centre, tips);
        var bridge = Gate(site, trace, centre, gate, outworks >= GlacisFrom);
        thing.Parts.AddRange(Works(site, centre, trace, tips, outworks, [bridge], crag ? FortGround.Open(site, centre, shape.Out) : null));
        thing.Parts.Add(BuiltSite.Area("square", Enumerable.Range(0, sides).Select(i => centre + Spot.Toward(turn + (2 * i + 1) * Math.PI / sides) * (inner * ParadeAt))));
        double length = Math.Min(site.M(BarrackMostM), inner * BarrackShare);
        var beyond = bridge + Spot.Toward(gate) * (shape.Out - shape.Wide / 2);
        thing.Parts.Add(BuiltSite.Line("road", road is { } joined ? [centre + Spot.Toward(gate) * (inner * ParadeAt), beyond, joined] : [centre + Spot.Toward(gate) * (inner * ParadeAt), beyond]));
        for (int i = 0; i < sides; i++)
        {
            double a = Side(i);
            var at = centre + Spot.Toward(a) * (inner * BarracksAt);
            if (site.Buildable(at) && i != entry && !field && (!crag || Enclosures.Inside(trace, at))) thing.Parts.Add(BuiltSite.Block("barracks", at, Spot.Toward(a).Left, length * dice.Between(BarrackPlay, 1), site.M(BarrackDepthM)));
        }
        thing.Parts.Add(BuiltSite.Sign("bridge", bridge, gate));
        thing.Parts.Add(BuiltSite.Sign("flag", centre));
        if (!field) thing.Parts.AddRange(Props.Guns(centre, tips));
        else thing.Parts.AddRange(Enumerable.Range(0, 4).Where(i => i != entry).Select(i => BuiltSite.Sign("gun", centre + Spot.Toward(Side(i)) * (radius * CurtainAt * 0.82), Side(i))));
        if (!field && sides <= MostFurnishedSides && inner >= site.M(FurnishedFromM)) Furnish(site, dice, centre, turn, sides, inner, thing.Parts);
        if (knoll != null) FortGround.Fleches(site, centre, radius, turn, thing.Parts);
        if (!field && site.Epoch == Manners.Later) FortGround.Detached(site, centre, girth, turn, thing.Parts);
        return thing;
    }

    static void Furnish(BuiltSite site, Dice dice, Spot centre, double turn, int sides, double inner, List<BuiltPart> parts)
    {
        int first = dice.Below(sides);
        double Bearing(int i) => turn + (first + i) * 2 * Math.PI / sides;
        var magazine = centre + Spot.Toward(Bearing(0)) * (inner * CornerAt);
        if (site.Buildable(magazine)) parts.Add(BuiltSite.Block("magazine", magazine, Spot.Toward(Bearing(0)).Left, site.M(MagazineLongM), site.M(MagazineDeepM)));
        var quarters = centre + Spot.Toward(Bearing(sides / 2)) * (inner * CornerAt);
        if (site.Buildable(quarters)) parts.Add(BuiltSite.Block("public", quarters, Spot.Toward(Bearing(sides / 2)).Left, site.M(QuartersLongM), site.M(QuartersDeepM)));
        parts.Add(BuiltSite.Sign("well", centre + Spot.Toward(Bearing(1)) * (inner * WellAt)));
    }
}
