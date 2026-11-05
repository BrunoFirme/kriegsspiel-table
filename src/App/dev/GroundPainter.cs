using System.Collections.Generic;
using KTable.Core.Model;

namespace KTable.App;

sealed class GroundPainter
{
    const int CountryRoad = 50, LoneFarms = 25;
    readonly ReliefGrid _g;
    readonly double _cellMetres, _metresPerUnit, _du, _dv;
    byte _salt;

    public GroundPainter(ReliefGrid g, double cellMetres, double metresPerUnit, byte salt = 0, double offsetU = 0, double offsetV = 0)
    {
        _g = g;
        _cellMetres = cellMetres;
        _metresPerUnit = metresPerUnit;
        _salt = salt;
        _du = offsetU;
        _dv = offsetV;
    }

    public ReliefGrid Grid => _g;

    (double, double)[] Local(IReadOnlyList<(double, double)> pts)
    {
        var o = new (double, double)[pts.Count];
        for (int i = 0; i < o.Length; i++) o[i] = (pts[i].Item1 - _du, pts[i].Item2 - _dv);
        return o;
    }

    static byte K(string name) => GroundKinds.Of(name)!.Value;

    Brush Painting(string kind, int first, int second, double density = 1) =>
        new(BrushMode.Paint, 0, 1, K(kind), Density: density, Salt: ++_salt, Detail: GroundKinds.Levels(first, second));

    public void Paint(string kind, double r, double density, params (double, double)[] pts) =>
        ReliefBrush.Stroke(_g, Local(pts), r, Painting(kind, CountryRoad, LoneFarms, density));

    public void PaintLevels(string kind, int first, int second, double r, params (double, double)[] pts) =>
        ReliefBrush.Stroke(_g, Local(pts), r, Painting(kind, first, second));

    public void PaintAt(string kind, int first, int second, double u0, double v0, double u1, double v1) =>
        ReliefBrush.Shape(_g, Local([(u0, v0), (u1, v0), (u1, v1), (u0, v1)]), Painting(kind, first, second));

    public void PaintShape(string kind, int first, int second, params (double, double)[] pts) =>
        ReliefBrush.Shape(_g, Local(pts), Painting(kind, first, second));

    public void PaintRoad(int road, double r, params (double, double)[] pts) =>
        ReliefBrush.Stroke(_g, Local(pts), r, Painting("road", road, 0));

    public void Raise(double r, double strength, params (double, double)[] pts) => ReliefBrush.Stroke(_g, Local(pts), r, new Brush(BrushMode.Raise, 0, strength));

    public void Lower(double r, double strength, params (double, double)[] pts) => ReliefBrush.Stroke(_g, Local(pts), r, new Brush(BrushMode.Lower, 0, strength));

    public void Hollow(double r, params (double, double)[] pts) => ReliefBrush.Stroke(_g, Local(pts), r, new Brush(BrushMode.Flatten, 0, 1, Level: 0));

    public void Valley(double r, double depth, params (double, double)[] pts)
    {
        foreach (var (reach, share) in new[] { (1.0, 0.25), (0.75, 0.3), (0.5, 0.4), (0.33, 0.6), (0.2, 1.0), (0.12, 1.6), (0.06, 3.0), (0.02, 6.0) })
            Lower(r * reach, depth * share, pts);
    }

    public void Route(int road, double r, params (double, double)[] pts) =>
        GroundRoute.Lay(_g, Painting("road", road, 0), Local(pts), r, _cellMetres, _metresPerUnit);

    public void River(double r, double u, double v) =>
        GroundRoute.Lay(_g, Painting("water", 0, 0), Local([(u, v)]), r, _cellMetres, _metresPerUnit);

    public void Grass(double rows = 0.03)
    {
        for (double v = 0.015; v < 1; v += rows) ReliefBrush.Stroke(_g, [(0.0, v), (1.0, v)], 9, Painting("grass", CountryRoad, LoneFarms));
    }

    public static IReadOnlyList<(double, double)> Blob(double u, double v, double ru, double rv, int corners, double wobble, double seed)
    {
        var pts = new List<(double, double)>();
        for (int i = 0; i < corners; i++)
        {
            double a = i * 2 * System.Math.PI / corners;
            double w = 1 + wobble * System.Math.Sin(a * 3 + seed) * System.Math.Cos(a * 2 + seed * 1.7);
            pts.Add((u + ru * w * System.Math.Cos(a), v + rv * w * System.Math.Sin(a)));
        }
        return pts;
    }
}
