namespace KTable.Core.Model;

sealed class WayNet
{
    const double BucketM = 16, NearSteps = 0.5;

    readonly double _step, _perMetre, _bucket;
    readonly Func<Spot, double> _bareM;
    readonly Func<Spot, bool> _wet;
    readonly Dictionary<(int, int), List<(Spot At, double Level, double Reach)>> _laid = new();

    public WayNet(double step, double perMetre, Func<Spot, double> bareM, Func<Spot, bool> wet) =>
        (_step, _perMetre, _bareM, _wet, _bucket) = (step, perMetre, bareM, wet, Math.Max(step * 2, BucketM * perMetre));

    public double Step => _step;

    (int, int) Bucket(Spot p) => ((int)Math.Floor(p.X / _bucket), (int)Math.Floor(p.Y / _bucket));

    (double Level, double Off)? Met(Spot p, double halfM)
    {
        (double Level, double Off)? best = null;
        var (bx, by) = Bucket(p);
        for (int y = by - 1; y <= by + 1; y++)
            for (int x = bx - 1; x <= bx + 1; x++)
            {
                if (!_laid.TryGetValue((x, y), out var near)) continue;
                foreach (var (at, level, reach) in near)
                {
                    double off = (at - p).Length;
                    if (off > Math.Max(reach, halfM * _perMetre) + _step * NearSteps || best is { } known && off >= known.Off) continue;
                    best = (level, off);
                }
            }
        return best;
    }

    public Profile Lay(IReadOnlyList<Spot> path, double grade, double halfM, Func<Spot, Spot, Band> band) => Lay(path, [grade], halfM, band);

    public Profile Lay(IReadOnlyList<Spot> path, IReadOnlyList<double> grades, double halfM, Func<Spot, Spot, Band> band, double aimM = 0)
    {
        var at = Profiles.Stations(path, _step);
        int n = at.Count;
        double[] along = new double[n], bare = new double[n], pinned = new double[n], level = new double[n];
        var bands = new Band[n];
        var holds = new Hold[n];
        for (int i = 0; i < n; i++)
        {
            along[i] = i == 0 ? 0 : along[i - 1] + (at[i] - at[i - 1]).Length / _perMetre;
            bare[i] = _bareM(at[i]);
            bands[i] = _wet(at[i]) ? new Band(Hold.Free, 0, 0) : band(at[i], at[Math.Min(n - 1, i + 1)] - at[Math.Max(0, i - 1)]);
            holds[i] = bands[i].Hold;
            if (holds[i] != Hold.Banded || Met(at[i], halfM) is not { } met || met.Level < bare[i] - bands[i].CutM || met.Level > bare[i] + bands[i].FillM) continue;
            holds[i] = Hold.Fixed;
            pinned[i] = met.Level;
        }
        for (int g = 0; g + 1 < grades.Count; g++)
            if (Profiles.Fit(along, bare, grades[g], bands, (Hold[])holds.Clone(), pinned, level, false, aimM)) return Kept(at, along, bare, level, holds, halfM, grades[g]);
        Profiles.Fit(along, bare, grades[^1], bands, holds, pinned, level, true, aimM);
        return Kept(at, along, bare, level, holds, halfM, grades[^1]);
    }

    Profile Kept(List<Spot> at, double[] along, double[] bare, double[] level, Hold[] holds, double halfM, double grade)
    {
        int n = at.Count;
        for (int i = 0; i < n; i++)
        {
            if (holds[i] == Hold.Free) continue;
            var key = Bucket(at[i]);
            if (!_laid.TryGetValue(key, out var list)) _laid[key] = list = new();
            list.Add((at[i], level[i], halfM * _perMetre));
        }
        return new Profile(at, along, bare, level, holds, grade);
    }
}

sealed class WayMarks
{
    const double PinnedM = 1.5, SpannedM = 2, CellM = 24, EndM = 0.05, Aligned = 0.8;

    readonly record struct Mark(Spot A, Spot B, double Reach, bool Pins, double Before, double After);

    readonly double _cell;
    readonly Dictionary<(int, int), List<Mark>> _lines = new();
    readonly List<(List<Spot> Ring, Spot Low, Spot High, bool Cut)> _banks = new();

    public WayMarks(IEnumerable<BuiltPart> parts, double perMetre)
    {
        _cell = CellM * perMetre;
        foreach (var part in parts)
        {
            if (part.Kind == PartKinds.Area && Grades.Banked(part.Class))
            {
                var ring = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
                if (ring.Count >= 3)
                    _banks.Add((ring, new Spot(ring.Min(p => p.X), ring.Min(p => p.Y)), new Spot(ring.Max(p => p.X), ring.Max(p => p.Y)), part.Class == Grades.Cutting));
                continue;
            }
            if (part.Kind != PartKinds.Line || !Grades.Pinned(part.Class) && !Grades.Spanned(part.Class)) continue;
            bool pins = Grades.Pinned(part.Class);
            var points = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            double reach = (pins ? PinnedM : SpannedM) * perMetre, end = pins ? EndM * perMetre : reach;
            for (int i = 0; i + 1 < points.Count; i++) File(new Mark(points[i], points[i + 1], reach, pins, i == 0 ? end : reach, i + 2 == points.Count ? end : reach));
        }
    }

    void File(Mark mark)
    {
        Spot a = mark.A, b = mark.B;
        double reach = mark.Reach;
        int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - reach) / _cell), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + reach) / _cell);
        int y0 = (int)Math.Floor((Math.Min(a.Y, b.Y) - reach) / _cell), y1 = (int)Math.Floor((Math.Max(a.Y, b.Y) + reach) / _cell);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!_lines.TryGetValue((x, y), out var list)) _lines[(x, y)] = list = new();
                list.Add(mark);
            }
    }

    static bool On(Mark mark, Spot p, Spot way)
    {
        var run = mark.B - mark.A;
        double length = run.Length, along = (p - mark.A).Dot(run.Unit);
        if (along < -mark.Before || along > length + mark.After || p.FromSegment(mark.A, mark.B) > mark.Reach) return false;
        return !mark.Pins || Math.Abs(way.Unit.Dot(run.Unit)) >= Aligned;
    }

    public Band At(Spot p, Spot way, bool engineered)
    {
        if (_lines.TryGetValue(((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell)), out var near))
        {
            bool spanned = false, pinned = false;
            foreach (var mark in near)
                if (On(mark, p, way)) (spanned, pinned) = (spanned || !mark.Pins, pinned || mark.Pins);
            if (spanned) return new Band(Hold.Free, 0, 0);
            if (pinned) return new Band(Hold.Natural, 0, 0);
        }
        var held = new Band(Hold.Banded, Grades.HeldM, Grades.HeldM);
        if (!engineered) return held;
        foreach (var (ring, low, high, cut) in _banks)
            if (p.X >= low.X && p.X <= high.X && p.Y >= low.Y && p.Y <= high.Y && Enclosures.Inside(ring, p))
                return cut ? held with { CutM = Grades.CuttingMostM } : held with { FillM = Grades.EmbankingMostM };
        return held;
    }

    public static IEnumerable<(BuiltPart Part, Profile Profile)> Graded(IReadOnlyList<BuiltPart> parts, int manner, WayNet net, double perMetre)
    {
        var marks = new WayMarks(parts, perMetre);
        foreach (var part in parts.Where(Grades.Graded).OrderBy(p => Grades.Rank(p.Class)))
        {
            var path = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (path.Count < 2) continue;
            bool engineered = Grades.Engineered(part.Class);
            yield return (part, net.Lay(path, Grades.Ruling(manner, part.Class), Grades.HalfM(part.Class), (p, way) => marks.At(p, way, engineered), engineered ? Grades.AimM : 0));
        }
    }
}
