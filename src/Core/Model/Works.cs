namespace KTable.Core.Model;

public static class Works
{
    public const string Wall = "wall", Palisade = "palisade", Rampart = "rampart", Ditch = "ditch", Trench = "trench", Wire = "wire", StoneWall = Settlements.GardenWall, Hedge = "hedge",
        Fence = "fence";
    public const string Work = "work", Towers = "towers", Dug = "dug";
    public const int NoTowers = 0, FewTowers = 1, ManyTowers = 2;
    const string Tower = "tower";

    static readonly string[] Classes = [Wall, Palisade, Rampart, Ditch, Trench, Wire, StoneWall, Hedge, Fence];
    public static IReadOnlyList<string> All => Classes;
    public static readonly IReadOnlyList<string> Fields = [StoneWall, Hedge, Fence];

    const double TowersApartM = 55, TowerNearM = 9, GateHalfM = 5, GapHalfM = 3, GatesApartM = 20, GateLongM = 10, GateDeepM = 7, DitchOffM = 9, WetStepM = 3, WetMarginM = 2;
    const double LeastRunM = 1, GapSlackM = 4, StraightBend = 1, CurvedBend = 0.35, BayM = 7, TraverseM = 3.5, StepM = 2.6;
    const double NearPlaceReaches = 3;
    const double WallHalfM = 3, PalisadeHalfM = 2, RampartHalfM = 6, DitchHalfM = 4, TrenchHalfM = 3, FieldHalfM = 1.5;

    public static IReadOnlyList<string> Of(int epoch) => epoch == Manners.Modern ? [Wall, Trench, Wire, Ditch] : [Wall, Palisade, Rampart, Ditch];

    public static bool Is(string cls) => Array.IndexOf(Classes, cls) >= 0;

    public static bool HasTowers(string cls) => cls == Wall;

    public static bool HasDitch(string cls) => cls is Wall or Palisade or Rampart;

    public static bool HasGates(string cls) => cls is not (Trench or Ditch);

    public static double Bend(string cls) => cls is Wall or Rampart or StoneWall or Trench ? StraightBend : CurvedBend;

    public static string Named(string cls) => cls switch { StoneWall => "Stone wall", _ => char.ToUpperInvariant(cls[0]) + cls[1..] };

    public static string ClassOf(BuiltThing works) => Classes[Math.Clamp((int)works.Setting(Work, 0), 0, Classes.Length - 1)];

    public static int TowersOf(BuiltThing works) => (int)Math.Clamp(works.Setting(Towers, ManyTowers), NoTowers, ManyTowers);

    public static bool DitchOf(BuiltThing works) => works.Setting(Dug, 0) > 0;

    public static List<Spot> Course(BuiltThing works) => BuiltGeometry.Points(works.Outline).Select(Spot.Of).ToList();

    public static bool Closed(IReadOnlyList<Spot> course) => course.Count >= 4 && (course[0] - course[^1]).Length < 1e-6;

    public static List<Spot> Corners(BuiltThing works)
    {
        var course = Course(works);
        return Closed(course) ? course.Take(course.Count - 1).ToList() : course;
    }

    public static List<Spot> Straightened(IReadOnlyList<Spot> stroke, double tolerance)
    {
        if (stroke.Count < 3) return stroke.ToList();
        var keep = new bool[stroke.Count];
        keep[0] = keep[^1] = true;
        var spans = new Stack<(int A, int B)>();
        spans.Push((0, stroke.Count - 1));
        while (spans.Count > 0)
        {
            var (a, b) = spans.Pop();
            int far = -1;
            double most = tolerance;
            for (int i = a + 1; i < b; i++)
            {
                double off = stroke[i].FromSegment(stroke[a], stroke[b]);
                if (off <= most) continue;
                (far, most) = (i, off);
            }
            if (far < 0) continue;
            keep[far] = true;
            spans.Push((a, far));
            spans.Push((far, b));
        }
        return stroke.Where((_, i) => keep[i]).ToList();
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, IReadOnlyList<Spot> course, string cls, int towers, bool ditch, IReadOnlyList<Spot>? gates = null,
        IReadOnlyList<Spot>? kept = null)
    {
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Works, Outline = BuiltGeometry.Flat(course.Select(p => p.Stored)),
            Settings = { [Work] = Math.Max(0, Array.IndexOf(Classes, cls)), [Towers] = Math.Clamp(towers, NoTowers, ManyTowers), [Dug] = ditch ? 1 : 0, [Manners.Setting] = site.Manner },
        };
        if (course.Count < 2 || !Is(cls)) return thing;
        Raise(site, thing.Parts, course, cls, towers, ditch && HasDitch(cls), HasGates(cls) ? gates ?? Crossed(site, course) : [], kept ?? []);
        return thing;
    }

    static void Raise(BuiltSite site, List<BuiltPart> parts, IReadOnlyList<Spot> course, string cls, int towers, bool ditch, IReadOnlyList<Spot> gates, IReadOnlyList<Spot> kept)
    {
        bool closed = Closed(course);
        var along = Along(course);
        double length = along[^1];
        if (length <= 0) return;
        bool masonry = cls == Wall;
        double half = site.M(masonry ? GateHalfM : GapHalfM);
        var cuts = gates.Select(g => Project(course, along, g)).Select(s => (s - half, s + half)).Concat(Wet(site, course, along)).ToList();
        var runs = Runs(cuts, length, closed).Where(r => r.B - r.A >= site.M(LeastRunM)).ToList();
        foreach (var (a, b) in runs)
        {
            var points = Span(course, along, a, b, closed);
            parts.Add(BuiltSite.Line(cls, cls == Trench ? Defences.Crenellated(points, site.M(BayM), site.M(TraverseM), site.M(StepM)) : points));
        }
        if (masonry)
        {
            RaiseTowers(site, parts, course, along, runs, closed, towers, kept);
            foreach (var gate in gates) parts.Add(BuiltSite.Block("gatehouse", gate, LegAt(course, along, Project(course, along, gate)), site.M(GateLongM), site.M(GateDeepM)));
        }
        if (ditch) DigDitch(site, parts, course, gates);
    }

    static void RaiseTowers(BuiltSite site, List<BuiltPart> parts, IReadOnlyList<Spot> course, double[] along, List<(double A, double B)> runs, bool closed, int towers, IReadOnlyList<Spot> kept)
    {
        if (towers <= NoTowers) return;
        var stood = kept.ToList();
        void Stand(Spot at)
        {
            if (stood.Any(t => (t - at).Length < site.M(TowerNearM))) return;
            stood.Add(at);
            parts.Add(BuiltSite.Sign(Tower, at));
        }
        foreach (var (a, b) in runs)
        {
            Stand(PointAt(course, along, a, closed));
            for (int i = 0; i < course.Count; i++)
                if (Within(along[i], a, b, along[^1], closed)) Stand(course[i]);
            Stand(PointAt(course, along, b, closed));
        }
        if (towers < ManyTowers) return;
        double apart = site.M(TowersApartM);
        foreach (var (a, b) in runs)
        {
            int steps = (int)Math.Floor((b - a) / apart);
            for (int k = 1; k <= steps; k++) Stand(PointAt(course, along, a + (b - a) * k / (steps + 1), closed));
        }
    }

    static bool Within(double s, double a, double b, double length, bool closed) => s > a && s < b || closed && b > length && s + length > a && s + length < b;

    static void DigDitch(BuiltSite site, List<BuiltPart> parts, IReadOnlyList<Spot> course, IReadOnlyList<Spot> gates)
    {
        var off = Outward(site, course).ToList();
        var line = course.Select((p, i) => p + off[i] * site.M(DitchOffM)).ToList();
        bool Clear(Spot p) => !site.Wet(p) && site.Downhill(p).Length < BuiltSite.SteepestBuilt;
        var run = new List<Spot>();
        foreach (var p in Stepped(line, site.M(WetStepM * 4)))
        {
            if (Clear(p))
            {
                run.Add(p);
                continue;
            }
            if (run.Count > 1) parts.Add(BuiltSite.Line(Ditch, run));
            run = [];
        }
        if (run.Count > 1) parts.Add(BuiltSite.Line(Ditch, run));
        var along = Along(course);
        foreach (var gate in gates)
        {
            var outward = NormalAt(course, off, along, Project(course, along, gate));
            parts.Add(BuiltSite.Sign("bridge", gate + outward * site.M(DitchOffM), Math.Atan2(outward.Y, outward.X)));
        }
    }

    static Spot NormalAt(IReadOnlyList<Spot> course, IReadOnlyList<Spot> off, double[] along, double s)
    {
        for (int i = 0; i + 1 < course.Count; i++)
            if (s <= along[i + 1]) return (off[i] + off[i + 1]).Unit;
        return off[^1];
    }

    static IEnumerable<Spot> Outward(BuiltSite site, IReadOnlyList<Spot> course)
    {
        bool closed = Closed(course);
        var middle = closed ? Enclosures.Middle(course.Take(course.Count - 1).ToList()) : Enclosures.Middle(course);
        var place = site.Others.Where(o => o.Kind == BuiltKinds.Settlement).Select(BuiltThings.Round).Where(r => r.Reach > 0)
            .OrderBy(r => (r.At - middle).Length - r.Reach).Select(r => ((Spot At, double Reach)?)r).FirstOrDefault();
        for (int i = 0; i < course.Count; i++)
        {
            Spot into = i > 0 ? (course[i] - course[i - 1]).Unit : closed ? (course[0] - course[^2]).Unit : (course[1] - course[0]).Unit;
            Spot onward = i + 1 < course.Count ? (course[i + 1] - course[i]).Unit : closed ? (course[1] - course[0]).Unit : into;
            var normal = (into.Left + onward.Left).Unit;
            var from = closed ? middle : place is { } near && (near.At - course[i]).Length < near.Reach * NearPlaceReaches ? near.At : (Spot?)null;
            if (from is { } inner && normal.Dot(course[i] - inner) < 0) normal = normal * -1;
            yield return normal;
        }
    }

    static IEnumerable<Spot> Stepped(IReadOnlyList<Spot> line, double step)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((line[i + 1] - line[i]).Length / Math.Max(step, 1e-9)));
            for (int k = 0; k < steps; k++) yield return line[i] + (line[i + 1] - line[i]) * (k / (double)steps);
        }
        if (line.Count > 0) yield return line[^1];
    }

    static double[] Along(IReadOnlyList<Spot> course)
    {
        var along = new double[course.Count];
        for (int i = 1; i < course.Count; i++) along[i] = along[i - 1] + (course[i] - course[i - 1]).Length;
        return along;
    }

    static double Project(IReadOnlyList<Spot> course, double[] along, Spot p)
    {
        double best = 0, least = double.MaxValue;
        for (int i = 0; i + 1 < course.Count; i++)
        {
            var ab = course[i + 1] - course[i];
            double t = Math.Clamp((p - course[i]).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            double off = (course[i] + ab * t - p).Length;
            if (off >= least) continue;
            (least, best) = (off, along[i] + ab.Length * t);
        }
        return best;
    }

    static Spot PointAt(IReadOnlyList<Spot> course, double[] along, double s, bool closed = false)
    {
        double length = along[^1];
        if (closed && s > length) s -= length;
        s = Math.Clamp(s, 0, length);
        for (int i = 0; i + 1 < course.Count; i++)
        {
            if (s > along[i + 1]) continue;
            double leg = along[i + 1] - along[i];
            return leg <= 0 ? course[i] : course[i] + (course[i + 1] - course[i]) * ((s - along[i]) / leg);
        }
        return course[^1];
    }

    static Spot LegAt(IReadOnlyList<Spot> course, double[] along, double s)
    {
        for (int i = 0; i + 1 < course.Count; i++)
            if (s <= along[i + 1]) return (course[i + 1] - course[i]).Unit;
        return (course[^1] - course[^2]).Unit;
    }

    static IEnumerable<(double A, double B)> Wet(BuiltSite site, IReadOnlyList<Spot> course, double[] along)
    {
        double step = site.M(WetStepM), margin = site.M(WetMarginM);
        if (step <= 0) yield break;
        double? from = null, last = null;
        for (double s = 0; s <= along[^1]; s += step)
        {
            if (site.Wet(PointAt(course, along, s)))
            {
                from ??= s;
                last = s;
                continue;
            }
            if (from is { } a && last is { } b) yield return (a - margin, b + margin);
            from = last = null;
        }
        if (from is { } x && last is { } y) yield return (x - margin, y + margin);
    }

    static List<(double A, double B)> Runs(List<(double A, double B)> cuts, double length, bool closed)
    {
        var spans = new List<(double A, double B)>();
        foreach (var (a, b) in cuts)
        {
            spans.Add((Math.Max(0, a), Math.Min(length, b)));
            if (closed && a < 0) spans.Add((length + a, length));
            if (closed && b > length) spans.Add((0, b - length));
        }
        var runs = new List<(double A, double B)>();
        double at = 0;
        foreach (var (a, b) in spans.Where(c => c.B > c.A).OrderBy(c => c.A))
        {
            if (a > at) runs.Add((at, a));
            at = Math.Max(at, b);
        }
        if (length > at) runs.Add((at, length));
        if (closed && runs.Count > 1 && runs[0].A <= 0 && runs[^1].B >= length)
        {
            runs[^1] = (runs[^1].A, length + runs[0].B);
            runs.RemoveAt(0);
        }
        return runs;
    }

    static List<Spot> Span(IReadOnlyList<Spot> course, double[] along, double a, double b, bool closed)
    {
        double length = along[^1];
        if (closed && b > length) return Span(course, along, a, length, false).Concat(Span(course, along, 0, b - length, false).Skip(1)).ToList();
        var points = new List<Spot> { PointAt(course, along, a) };
        for (int i = 0; i < course.Count; i++)
            if (along[i] > a && along[i] < b) points.Add(course[i]);
        points.Add(PointAt(course, along, b));
        return points;
    }

    static List<Spot> Crossed(BuiltSite site, IReadOnlyList<Spot> course)
    {
        var gates = new List<Spot>();
        foreach (var (a, b) in site.RoadSegments())
            for (int i = 0; i + 1 < course.Count; i++)
                if (BuiltSite.Crossing(course[i], course[i + 1], a, b) is { } at && !gates.Any(g => (g - at).Length < site.M(GatesApartM))) gates.Add(at);
        return gates;
    }

    public static List<Spot> GatesOf(BuiltSite site, BuiltThing works)
    {
        string cls = ClassOf(works);
        if (!HasGates(cls)) return [];
        if (cls == Wall) return works.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(p => BuiltSite.Round(p).At).ToList();
        var course = Course(works);
        if (course.Count < 2) return [];
        var along = Along(course);
        var runs = works.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == cls).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).Where(r => r.Count >= 2)
            .Select(r => (A: Project(course, along, r[0]), B: Project(course, along, r[^1]))).OrderBy(r => r.A).ToList();
        var gates = new List<Spot>();
        double widest = site.M(GapHalfM * 2 + GapSlackM);
        for (int i = 0; i + 1 < runs.Count; i++)
        {
            double gap = runs[i + 1].A - runs[i].B;
            var mid = PointAt(course, along, (runs[i].B + runs[i + 1].A) / 2);
            if (gap > 0 && gap <= widest && !site.Wet(mid)) gates.Add(mid);
        }
        return gates;
    }

    public static BuiltThing Again(BuiltSite site, BuiltThing old) => Again(site, old, Course(old), ClassOf(old), TowersOf(old), DitchOf(old), GatesOf(site, old));

    static BuiltThing Again(BuiltSite site, BuiltThing old, IReadOnlyList<Spot> course, string cls, int towers, bool ditch, IReadOnlyList<Spot> gates)
    {
        var kept = old.Parts.Where(p => p.Kept && p.Kind == PartKinds.Sign && p.Class == Tower && HasTowers(cls)).Select(p => p.Clone()).ToList();
        var laid = Lay(site.In(Manners.Of(old)), old.Sheet, course, cls, towers, ditch, gates, kept.Select(p => BuiltSite.Round(p).At).ToList());
        laid.Id = old.Id;
        laid.Name = old.Name;
        foreach (var (name, value) in old.Settings) laid.Settings.TryAdd(name, value);
        if (site.UnitsPerMetre > 0) laid.Settings[BuiltThings.Scale] = site.UnitsPerMetre;
        laid.Parts.AddRange(kept);
        return laid;
    }

    public static BuiltThing Reworked(BuiltSite site, BuiltThing old, string? cls = null, int? towers = null, bool? ditch = null) =>
        Again(site, old, Course(old), cls ?? ClassOf(old), towers ?? TowersOf(old), ditch ?? DitchOf(old), HasGates(cls ?? ClassOf(old)) ? GatesOf(site, old) : []);

    static BuiltThing Recoursed(BuiltSite site, BuiltThing old, List<Spot> course) =>
        Again(site, old, course, ClassOf(old), TowersOf(old), DitchOf(old), GatesOf(site, old).Select(g => Nearest(course, g).At).ToList());

    static (Spot At, int Leg, double Reach) Nearest(IReadOnlyList<Spot> course, Spot p)
    {
        (Spot At, int Leg, double Reach) best = (course[0], 0, double.MaxValue);
        for (int i = 0; i + 1 < course.Count; i++)
        {
            var ab = course[i + 1] - course[i];
            var at = course[i] + ab * Math.Clamp((p - course[i]).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if ((at - p).Length < best.Reach) best = (at, i, (at - p).Length);
        }
        return best;
    }

    public static int CornerNear(BuiltThing works, Spot p, double reach)
    {
        var corners = Corners(works);
        int best = -1;
        for (int i = 0; i < corners.Count; i++)
            if ((corners[i] - p).Length < reach && (best < 0 || (corners[i] - p).Length < (corners[best] - p).Length)) best = i;
        return best;
    }

    public static List<Spot> Middles(IReadOnlyList<Spot> corners, bool closed) =>
        Enumerable.Range(0, closed ? corners.Count : corners.Count - 1).Select(i => (corners[i] + corners[(i + 1) % corners.Count]) * 0.5).ToList();

    public static int MiddleNear(IReadOnlyList<Spot> corners, bool closed, Spot p, double reach)
    {
        var middles = Middles(corners, closed);
        int best = -1;
        for (int i = 0; i < middles.Count; i++)
            if ((middles[i] - p).Length < reach && (best < 0 || (middles[i] - p).Length < (middles[best] - p).Length)) best = i;
        return best;
    }

    static List<Spot> Coursed(List<Spot> corners, bool closed) => closed ? corners.Append(corners[0]).ToList() : corners;

    public static BuiltThing? CornerMoved(BuiltSite site, BuiltThing works, int corner, Spot to)
    {
        var corners = Corners(works);
        if (corner < 0 || corner >= corners.Count) return null;
        corners[corner] = to;
        return Recoursed(site, works, Coursed(corners, Closed(Course(works))));
    }

    public static BuiltThing? CornerAdded(BuiltSite site, BuiltThing works, int leg, Spot at)
    {
        var corners = Corners(works);
        bool closed = Closed(Course(works));
        if (leg < 0 || leg >= (closed ? corners.Count : corners.Count - 1)) return null;
        corners.Insert(leg + 1, at);
        return Recoursed(site, works, Coursed(corners, closed));
    }

    public static BuiltThing? CornerRemoved(BuiltSite site, BuiltThing works, int corner)
    {
        var corners = Corners(works);
        bool closed = Closed(Course(works));
        if (corner < 0 || corner >= corners.Count || corners.Count <= (closed ? 3 : 2)) return null;
        corners.RemoveAt(corner);
        return Recoursed(site, works, Coursed(corners, closed));
    }

    public static BuiltThing? Gated(BuiltSite site, BuiltThing works, Spot at, double reach)
    {
        string cls = ClassOf(works);
        var course = Course(works);
        if (!HasGates(cls) || course.Count < 2) return null;
        var (on, _, off) = Nearest(course, at);
        if (off > reach) return null;
        var gates = GatesOf(site, works);
        int shut = gates.FindIndex(g => (g - on).Length < site.M(GatesApartM));
        if (shut >= 0) gates.RemoveAt(shut);
        else gates.Add(on);
        return Again(site, works, course, cls, TowersOf(works), DitchOf(works), gates);
    }

    public static BuiltThing? Towered(BuiltThing works, Spot at, double reach)
    {
        var course = Course(works);
        if (!HasTowers(ClassOf(works)) || course.Count < 2) return null;
        var towered = works.Clone();
        int near = -1;
        double least = reach;
        for (int i = 0; i < towered.Parts.Count; i++)
        {
            var part = towered.Parts[i];
            if (part.Kind != PartKinds.Sign || part.Class != Tower || (BuiltSite.Round(part).At - at).Length >= least) continue;
            (near, least) = (i, (BuiltSite.Round(part).At - at).Length);
        }
        if (near >= 0)
        {
            towered.Parts.RemoveAt(near);
            return towered;
        }
        var (on, _, off) = Nearest(course, at);
        if (off > reach) return null;
        var stood = BuiltSite.Sign(Tower, on);
        stood.Kept = true;
        towered.Parts.Add(stood);
        return towered;
    }

    public static BuiltThing? Opened(BuiltSite site, BuiltThing works, BuiltThing road)
    {
        if (works.Kind != BuiltKinds.Works || !HasGates(ClassOf(works))) return null;
        var course = Course(works);
        var gates = GatesOf(site, works);
        int had = gates.Count;
        foreach (var path in road.Parts.Where(BuiltSite.IsRoad).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()))
            for (int i = 0; i + 1 < path.Count; i++)
                for (int k = 0; k + 1 < course.Count; k++)
                    if (BuiltSite.Crossing(course[k], course[k + 1], path[i], path[i + 1]) is { } at && !gates.Any(g => (g - at).Length < site.M(GatesApartM))) gates.Add(at);
        return gates.Count == had ? null : Again(site, works, course, ClassOf(works), TowersOf(works), DitchOf(works), gates);
    }

    public static double HalfM(string cls) => cls switch
    {
        Wall => WallHalfM, Palisade => PalisadeHalfM, Rampart => RampartHalfM, Ditch => DitchHalfM, Trench or Wire => TrenchHalfM, _ => FieldHalfM,
    };

    public static BuiltThing? Cleared(BuiltThing place, BuiltThing works, double perMetre)
    {
        if (place.Kind is not (BuiltKinds.Settlement or BuiltKinds.Details) || works.Kind != BuiltKinds.Works || place.Sheet != works.Sheet) return null;
        var lines = works.Parts.Where(p => p.Kind == PartKinds.Line && Is(p.Class)).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).Where(l => l.Count >= 2).ToList();
        if (lines.Count == 0) return null;
        double half = HalfM(ClassOf(works)) * perMetre;
        bool InTheWay(BuiltPart part) => part.Kind switch
        {
            PartKinds.Block => part.Class != "gatehouse" && lines.Any(l => BuiltRoads.Reach(part, l) < half),
            PartKinds.Area => part.Class is "garden" or "orchard" or "pond" or "yard" or "churchyard" && lines.Any(l => BuiltRoads.Reach(part, l) < half),
            PartKinds.Sign => part.Class is not ("bridge" or "ford" or Tower or "cross" or "flag" or "gun") && lines.Any(l => BuiltRoads.Reach(part, l) < half),
            _ => false,
        };
        if (!place.Parts.Any(InTheWay)) return null;
        var cleared = place.Clone();
        cleared.Parts.RemoveAll(InTheWay);
        return cleared;
    }
}
