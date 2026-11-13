namespace KTable.Converter.Bundles;

public readonly record struct CutPoint(double A, double B, double C, double U, double V);

public static class TileCut
{
    const double Slack = 1e-6;
    const double Sliver = 1e-12;
    const int MostCells = 1024;

    sealed record Span(double Low, double High, Func<double, double> Local);

    public static List<CutPoint[]> Cut(CutPoint[] triangle, Wrap wrapU, Wrap wrapV)
    {
        var acrossU = Spans(triangle.Min(p => p.U), triangle.Max(p => p.U), wrapU);
        var acrossV = Spans(triangle.Min(p => p.V), triangle.Max(p => p.V), wrapV);
        if (acrossU.Count * acrossV.Count > MostCells)
        {
            acrossU = [Whole(triangle.Min(p => p.U))];
            acrossV = [Whole(triangle.Min(p => p.V))];
        }
        var pieces = new List<CutPoint[]>();
        foreach (var u in acrossU)
            foreach (var v in acrossV)
            {
                var shape = triangle.ToList();
                shape = Clip(shape, onU: true, u.Low, above: true);
                shape = Clip(shape, onU: true, u.High, above: false);
                shape = Clip(shape, onU: false, v.Low, above: true);
                shape = Clip(shape, onU: false, v.High, above: false);
                for (int k = 1; k + 1 < shape.Count; k++)
                {
                    var (a, b, c) = (shape[0], shape[k], shape[k + 1]);
                    if (Math.Abs((b.A - a.A) * (c.B - a.B) - (b.B - a.B) * (c.A - a.A)) < Sliver) continue;
                    pieces.Add([Local(a, u, v), Local(b, u, v), Local(c, u, v)]);
                }
            }
        return pieces;
    }

    static CutPoint Local(CutPoint p, Span u, Span v) => p with { U = u.Local(p.U), V = v.Local(p.V) };

    static Span Whole(double min)
    {
        double tile = Math.Floor(min + Slack);
        return new Span(double.NegativeInfinity, double.PositiveInfinity, x => Math.Clamp(x - tile, 0, 1));
    }

    static List<Span> Spans(double min, double max, Wrap wrap)
    {
        var spans = new List<Span>();
        if (wrap == Wrap.Clamp)
        {
            var cuts = new[] { 0.0, 1.0 }.Where(c => c > min + Slack && c < max - Slack).ToList();
            var edges = cuts.Prepend(double.NegativeInfinity).Append(double.PositiveInfinity).ToList();
            for (int i = 0; i + 1 < edges.Count; i++) spans.Add(new Span(edges[i], edges[i + 1], x => Math.Clamp(x, 0, 1)));
            return spans;
        }
        long first = (long)Math.Floor(min + Slack), last = Math.Max(first, (long)Math.Ceiling(max - Slack) - 1);
        if (last - first >= MostCells) last = first + MostCells;
        for (long tile = first; tile <= last; tile++)
        {
            long at = tile;
            bool flipped = wrap == Wrap.Mirror && (at % 2 + 2) % 2 == 1;
            spans.Add(new Span(tile == first ? double.NegativeInfinity : tile, tile == last ? double.PositiveInfinity : tile + 1,
                x => flipped ? 1 - Math.Clamp(x - at, 0, 1) : Math.Clamp(x - at, 0, 1)));
        }
        return spans;
    }

    static List<CutPoint> Clip(List<CutPoint> shape, bool onU, double bound, bool above)
    {
        if (double.IsInfinity(bound) || shape.Count == 0) return shape;
        double Side(CutPoint p) => ((onU ? p.U : p.V) - bound) * (above ? 1 : -1);
        var kept = new List<CutPoint>(shape.Count + 2);
        for (int i = 0; i < shape.Count; i++)
        {
            var here = shape[i];
            var next = shape[(i + 1) % shape.Count];
            double h = Side(here), n = Side(next);
            if (h >= 0) kept.Add(here);
            if (h > 0 && n < 0 || h < 0 && n > 0) kept.Add(Between(here, next, h / (h - n)));
        }
        return kept;
    }

    static CutPoint Between(CutPoint p, CutPoint q, double t) =>
        new(p.A + (q.A - p.A) * t, p.B + (q.B - p.B) * t, p.C + (q.C - p.C) * t, p.U + (q.U - p.U) * t, p.V + (q.V - p.V) * t);
}
