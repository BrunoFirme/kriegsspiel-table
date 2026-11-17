namespace KTable.Core.Model;

sealed class WealthRoom
{
    const double StepM = 0.5, ThinHalfM = 0.3, InsetM = 0.05, NearM = 20, CloserM = 0.01;

    readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public static Box Of(IReadOnlyList<Spot> points, double grow = 0) =>
            new(points.Min(p => p.X) - grow, points.Min(p => p.Y) - grow, points.Max(p => p.X) + grow, points.Max(p => p.Y) + grow);

        public bool Meets(Box o) => X0 <= o.X1 && o.X0 <= X1 && Y0 <= o.Y1 && o.Y0 <= Y1;
    }

    sealed record Walls(List<Spot> Ring, bool MetLaid, bool Holds);

    sealed record Run(List<Spot> Line, double Half, double LaidGap, bool MetLaid);

    readonly BuiltSite? _site;
    readonly double _step, _inset, _closer;
    readonly List<Spot> _laid;
    readonly List<Walls> _walls = [];
    readonly List<Run> _runs = [];
    readonly List<Spot> _marks = [];

    static List<Spot> Ring(List<int> at) => BuiltGeometry.Points(at).Select(Spot.Of).ToList();

    public WealthRoom(BuiltThing set, int index, BuiltSite? site, double perMetre, List<Spot> laid, List<Spot> most)
    {
        _site = site;
        _laid = laid;
        _step = StepM * perMetre;
        _inset = InsetM * perMetre;
        _closer = CloserM * perMetre;
        var near = Box.Of(most, NearM * perMetre);
        var laidInset = Footprints.Shrunk(laid, _inset);
        var others = (site?.Others ?? []).Where(o => o.Id != set.Id).SelectMany(o => o.Parts);
        foreach (var part in set.Parts.Where((_, i) => i != index).Concat(others))
            foreach (var at in part.LaidAt is { } was ? new[] { part.At, was } : [part.At])
            {
                var points = Ring(at);
                if (points.Count == 0 || !near.Meets(Box.Of(points))) continue;
                if (part.Kind == PartKinds.Sign) _marks.Add(points[0]);
                else if (part.Kind == PartKinds.Line && points.Count >= 2) _runs.Add(new Run(points, HalfOf(part) * perMetre, Gap(laid, points), Crossed(laidInset, points)));
                else if (points.Count >= 3 && (Stood.Stands(part) || part.Kind == PartKinds.Area && (PlotRoom.Grown.Contains(part.Class) || PlotRoom.Held.Contains(part.Class))))
                    _walls.Add(new Walls(points, Footprints.Overlap(laidInset, points), laid.All(p => Enclosures.Inside(points, p))));
            }
    }

    static double HalfOf(BuiltPart part) =>
        TravelMap.WaterHalfM(part.Class) is > 0 and var water ? water : Looks.Paved(part) || part.Class == "railway" ? Grades.HalfM(part.Class) : ThinHalfM;

    public bool Fits(List<Spot> ring)
    {
        if (_site != null && ring.Any(c => !Enclosures.Inside(_laid, c) && !_site.Buildable(c))) return false;
        var grown = Samples(ring).Where(p => !Enclosures.Inside(_laid, p)).ToList();
        if (_site != null && grown.Any(_site.Wet)) return false;
        var inset = Footprints.Shrunk(ring, _inset);
        if (_walls.Any(w => w.Holds ? grown.Any(p => !Enclosures.Inside(w.Ring, p)) : grown.Any(p => Enclosures.Inside(w.Ring, p)) || !w.MetLaid && Footprints.Overlap(inset, w.Ring)))
            return false;
        if (_runs.Any(r => !r.MetLaid && Crossed(inset, r.Line) || grown.Any(p => From(r.Line, p) < Math.Min(r.Half, r.LaidGap - _closer)))) return false;
        return !_marks.Any(m => Enclosures.Inside(ring, m) && !Enclosures.Inside(_laid, m));
    }

    IEnumerable<Spot> Edges(List<Spot> ring)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / _step));
            for (int k = 0; k < n; k++) yield return a + (b - a) * (k / (double)n);
        }
    }

    IEnumerable<Spot> Samples(List<Spot> ring)
    {
        foreach (var p in Edges(ring)) yield return p;
        var box = Box.Of(ring);
        for (double x = box.X0 + _step / 2; x < box.X1; x += _step)
            for (double y = box.Y0 + _step / 2; y < box.Y1; y += _step)
                if (Enclosures.Inside(ring, new Spot(x, y))) yield return new Spot(x, y);
    }

    static double From(List<Spot> line, Spot p)
    {
        double least = double.MaxValue;
        for (int i = 0; i + 1 < line.Count; i++) least = Math.Min(least, p.FromSegment(line[i], line[i + 1]));
        return least;
    }

    double Gap(List<Spot> ring, List<Spot> line) => Crossed(ring, line) ? 0 : Edges(ring).Min(p => From(line, p));

    static bool Crossed(List<Spot> ring, List<Spot> line)
    {
        if (line.Any(p => Enclosures.Inside(ring, p))) return true;
        for (int i = 0; i + 1 < line.Count; i++)
            for (int k = 0; k < ring.Count; k++)
                if (Cross(line[i], line[i + 1], ring[k], ring[(k + 1) % ring.Count])) return true;
        return false;
    }

    static bool Cross(Spot a, Spot b, Spot c, Spot d)
    {
        static double Turn(Spot o, Spot p, Spot q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        return Turn(a, b, c) * Turn(a, b, d) < 0 && Turn(c, d, a) * Turn(c, d, b) < 0;
    }
}
