namespace KTable.Core.Model;

public enum Landform { Plain, Floor, Floodplain, Terrace, Slope, Upland, Plateau, Knoll, Summit, Ridge, Spur, Saddle, Gorge, Rim, Foot, Fan, Cliff }

public sealed class Landforms
{
    const double NearShare = 0.25, NearLeastM = 30, FarLeastM = 90, Steps = 56, NearFloorM = 1.5, FarFloorM = 4, Strong = 1, Margin = 1.6;
    const double Flat = 0.03, Level = 0.05, Sloping = 0.08, Steep = 0.35, Sheer = 1, CurveM = 2, Rounded = 0.3, Crossed = 0.5;
    const double FloodM = 2.5, TerraceTopM = 20, Riser = 0.12, FanMost = 0.15, FanWaterM = 15, EvenM = 3;
    const int Bearings = 8, RiserSamples = 6;
    static readonly string[] Facing = ["east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east"];

    readonly BuiltSite _site;
    readonly double _x0, _y0, _step, _near, _far, _nearSd, _farSd, _curve;
    readonly int _cols, _rows;
    readonly double[] _h, _sum;
    readonly bool[] _wet;
    readonly int[] _toWet;

    public Spot Centre { get; }
    public double Within { get; }

    Landforms(BuiltSite site, Spot centre, double within)
    {
        _site = site;
        Centre = centre;
        Within = Math.Max(within, site.M(NearLeastM));
        double cell = site.Grid == null ? Within / Steps : site.Width / Math.Max(1, site.Grid.Cols - 1);
        _step = Math.Max(cell, Within * 2 / Steps);
        _near = Math.Max(site.M(NearLeastM), Within * NearShare);
        _far = Math.Max(site.M(FarLeastM), Within);
        double span = Within * Margin + _far;
        _cols = _rows = (int)Math.Ceiling(span * 2 / _step) + 1;
        (_x0, _y0) = (centre.X - span, centre.Y - span);
        _h = new double[_cols * _rows];
        _wet = new bool[_cols * _rows];
        for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _cols; x++)
            {
                var p = Node(x, y);
                _h[y * _cols + x] = site.HeightAt(Clamped(p));
                _wet[y * _cols + x] = site.Wet(Clamped(p));
            }
        foreach (var (a, b, _) in site.Waters)
            for (int k = 0, steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / (_step / 2))); k <= steps; k++)
            {
                var (x, y) = Cell(a + (b - a) * (k / (double)steps));
                _wet[y * _cols + x] = true;
            }
        _toWet = Spread(_wet, _cols, _rows);
        _sum = new double[(_cols + 1) * (_rows + 1)];
        for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _cols; x++)
                _sum[(y + 1) * (_cols + 1) + x + 1] = _h[y * _cols + x] + _sum[y * (_cols + 1) + x + 1] + _sum[(y + 1) * (_cols + 1) + x] - _sum[y * (_cols + 1) + x];
        var inside = Enumerable.Range(0, _cols * _rows).Select(i => Node(i % _cols, i / _cols)).Where(p => (p - centre).Length <= Within * Margin).ToList();
        _nearSd = Math.Max(site.M(NearFloorM), Spread(inside.Select(p => Above(p, _near))));
        _farSd = Math.Max(site.M(FarFloorM), Spread(inside.Select(p => Above(p, _far))));
        _curve = site.M(CurveM) / (_near * _near);
    }

    public static Landforms Of(BuiltSite site, Spot centre, double within) => new(site, centre, within);

    Spot Node(int x, int y) => new(_x0 + x * _step, _y0 + y * _step);

    Spot Clamped(Spot p) => new(Math.Clamp(p.X, 0, _site.Width), Math.Clamp(p.Y, 0, _site.Height));

    (int X, int Y) Cell(Spot p) => (Math.Clamp((int)Math.Round((p.X - _x0) / _step), 0, _cols - 1), Math.Clamp((int)Math.Round((p.Y - _y0) / _step), 0, _rows - 1));

    static int[] Spread(bool[] wet, int cols, int rows)
    {
        var to = new int[wet.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < wet.Length; i++)
        {
            to[i] = wet[i] ? i : -1;
            if (wet[i]) queue.Enqueue(i);
        }
        double Far(int from, int at) => Math.Pow(from % cols - at % cols, 2) + Math.Pow(from / cols - at / cols, 2);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % cols, y = i / cols;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy, j = ny * cols + nx;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || to[j] >= 0 && Far(to[j], j) <= Far(to[i], j)) continue;
                    to[j] = to[i];
                    queue.Enqueue(j);
                }
        }
        return to;
    }

    Spot? WaterNear(Spot p, double reach)
    {
        var (x, y) = Cell(p);
        int w = _toWet[y * _cols + x];
        return w >= 0 && (Node(w % _cols, w / _cols) - p).Length <= reach ? Node(w % _cols, w / _cols) : null;
    }

    static double Spread(IEnumerable<double> values)
    {
        var all = values.ToList();
        if (all.Count < 2) return 0;
        double mean = all.Average();
        return Math.Sqrt(all.Sum(v => (v - mean) * (v - mean)) / all.Count);
    }

    double Mean(Spot p, double reach)
    {
        var (x, y) = Cell(p);
        int r = Math.Max(1, (int)Math.Round(reach / _step));
        int x0 = Math.Max(0, x - r), x1 = Math.Min(_cols - 1, x + r), y0 = Math.Max(0, y - r), y1 = Math.Min(_rows - 1, y + r);
        double total = _sum[(y1 + 1) * (_cols + 1) + x1 + 1] - _sum[y0 * (_cols + 1) + x1 + 1] - _sum[(y1 + 1) * (_cols + 1) + x0] + _sum[y0 * (_cols + 1) + x0];
        return total / ((x1 - x0 + 1) * (y1 - y0 + 1));
    }

    public double Height(Spot p) => _site.HeightAt(Clamped(p));

    double Above(Spot p, double reach) => Height(p) - Mean(p, reach);

    public double NearRise(Spot p) => Above(p, _near) / _nearSd;

    public double FarRise(Spot p) => Above(p, _far) / _farSd;

    public Spot Downhill(Spot p)
    {
        double h = _step;
        return new((Height(p - new Spot(h, 0)) - Height(p + new Spot(h, 0))) / (2 * h), (Height(p - new Spot(0, h)) - Height(p + new Spot(0, h))) / (2 * h));
    }

    public double Grade(Spot p) => Downhill(p).Length;

    (double Low, double High) Bend(Spot p)
    {
        double r = Math.Max(_step, _near / 2), soft = r / 2;
        double At(double dx, double dy) => Mean(p + new Spot(dx, dy), soft);
        double mid = At(0, 0);
        double xx = (At(r, 0) - 2 * mid + At(-r, 0)) / (r * r), yy = (At(0, r) - 2 * mid + At(0, -r)) / (r * r);
        double xy = (At(r, r) - At(r, -r) - At(-r, r) + At(-r, -r)) / (4 * r * r);
        double mean = (xx + yy) / 2, gap = Math.Sqrt((xx - yy) * (xx - yy) / 4 + xy * xy);
        return (mean - gap, mean + gap);
    }

    public double? AboveWater(Spot p) => WaterNear(p, _far * 2) is { } water ? Height(p) - Height(water) : null;

    bool Around(Spot p, double reach, Func<Spot, bool> holds) =>
        Enumerable.Range(0, Bearings).Any(i => holds(p + Spot.Toward(i * 2 * Math.PI / Bearings) * reach) || holds(p + Spot.Toward(i * 2 * Math.PI / Bearings) * (reach / 2)));

    bool Risen(Spot p, Spot water)
    {
        for (int k = 1; k < RiserSamples; k++)
            if (Grade(p + (water - p) * (k / (double)RiserSamples)) > Riser) return true;
        return false;
    }

    Spot? Water(Spot p) => WaterNear(p, _far);

    public Landform At(Spot p)
    {
        if (_site.Grid == null) return Landform.Plain;
        double grade = Grade(p), near = NearRise(p), far = FarRise(p);
        if (grade >= Sheer) return Landform.Cliff;
        var (low, high) = Bend(p);
        if (grade < Sloping && low < -_curve && high > _curve * Crossed) return Landform.Saddle;
        if (Wet(p, grade) is { } wet) return wet;
        if (grade < Sloping && Edge(p) is { } edge) return edge;
        if (near <= -Strong) return far <= -Strong && Around(p, _near, q => Grade(q) > Steep) ? Landform.Gorge : Landform.Floor;
        if (near >= Strong)
        {
            if (low < -_curve && high < -_curve * Rounded) return far >= Strong ? Landform.Summit : Landform.Knoll;
            return grade >= Sloping ? Landform.Spur : Landform.Ridge;
        }
        if (far >= Strong) return grade < Level ? Landform.Plateau : Landform.Upland;
        if (far <= -Strong) return Landform.Floor;
        if (grade < Level) return Landform.Plain;
        return Fanned(p, grade, high) ? Landform.Fan : Landform.Slope;
    }

    Landform? Edge(Spot p)
    {
        double here = Height(p), even = _site.M(EvenM);
        for (int i = 0; i < Bearings; i++)
        {
            var way = Spot.Toward(i * 2 * Math.PI / Bearings);
            var (there, back) = (p + way * (_near / 2), p - way * (_near / 2));
            if (Grade(there) <= Steep && Grade(p + way * _near) <= Steep) continue;
            bool fallsAway = Height(p + way * _near) < here - even, risesAway = Height(p + way * _near) > here + even;
            if (fallsAway && Height(back) > here - even) return Landform.Rim;
            if (risesAway && Height(back) < here + even) return Landform.Foot;
        }
        return null;
    }

    Landform? Wet(Spot p, double grade)
    {
        if (grade >= Level || AboveWater(p) is not { } above || Water(p) is not { } water) return null;
        if (above < _site.M(FloodM) && grade < Flat * 2) return Landform.Floodplain;
        return above < _site.M(TerraceTopM) && Risen(p, water) ? Landform.Terrace : null;
    }

    bool Fanned(Spot p, double grade, double high) =>
        grade < FanMost && high < 0 && _site.NearestWater(p) is var (_, reach) && reach < Within && AboveWater(p) is { } above && above < _site.M(FanWaterM);

    public static string Name(Landform form) => form switch
    {
        Landform.Floor => "Valley floor",
        Landform.Floodplain => "Floodplain",
        Landform.Terrace => "River terrace",
        Landform.Upland => "Upper slope",
        Landform.Rim => "Scarp rim",
        Landform.Foot => "Scarp foot",
        Landform.Fan => "Torrent fan",
        _ => form.ToString(),
    };

    public string Named(Spot p)
    {
        double grade = Grade(p);
        var form = At(p);
        if (grade < Flat) return $"{Name(form)} · level";
        var fall = Downhill(p);
        int face = (int)Math.Round(Math.Atan2(fall.Y, fall.X) / (2 * Math.PI) * Bearings + Bearings) % Bearings;
        return $"{Name(form)} · {Math.Round(grade * 100)}% · faces {Facing[face]}";
    }

    public Spot Highest(double reach)
    {
        var best = Centre;
        for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _cols; x++)
            {
                var p = Node(x, y);
                if ((p - Centre).Length <= reach && _site.Buildable(Clamped(p)) && Height(p) > Height(best)) best = p;
            }
        return best;
    }
}
