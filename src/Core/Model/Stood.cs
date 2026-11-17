using System.Runtime.CompilerServices;

namespace KTable.Core.Model;

sealed class Stood
{
    const double CellM = 24, SlackM = 0.75;
    static readonly ConditionalWeakTable<List<BuiltPart>, Stood> Known = new();
    static readonly string[] Courts = ["villa", "insula", "domus", "cloister"];
    public const double CourtShare = 0.32, CourtMostM = 8;

    public sealed class Stand(BuiltPart part, List<Spot> ring, List<Spot> core)
    {
        public BuiltPart Part => part;
        public List<Spot> Ring => ring;
        public List<Spot> Core => core;
        public readonly double X0 = ring.Min(p => p.X), Y0 = ring.Min(p => p.Y), X1 = ring.Max(p => p.X), Y1 = ring.Max(p => p.Y);
        internal int Seen;
    }

    readonly Dictionary<(int, int), List<Stand>> _cells = new();
    readonly double _cell, _slack;
    int _seen, _stamp;
    BuiltPart? _last;

    public Stood(BuiltSite site)
    {
        _cell = Math.Max(site.M(CellM), 1e-9);
        _slack = site.M(SlackM);
    }

    public static bool Stands(BuiltPart part) => part.Kind == PartKinds.Block && part.At.Count >= 6 && part.Class != BuiltThings.Quarter;

    public static Stood Of(BuiltSite site, List<BuiltPart> parts)
    {
        var stood = Known.GetValue(parts, _ => new Stood(site));
        stood.Read(parts);
        return stood;
    }

    void Read(List<BuiltPart> parts)
    {
        if (_seen > parts.Count || _seen > 0 && !ReferenceEquals(parts[_seen - 1], _last))
        {
            _cells.Clear();
            _seen = 0;
        }
        for (; _seen < parts.Count; _seen++)
            if (Stands(parts[_seen])) Add(parts[_seen]);
        _last = _seen > 0 ? parts[_seen - 1] : null;
    }

    static List<Spot> Ring(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    Stand Made(BuiltPart part)
    {
        var ring = Ring(part);
        return new Stand(part, ring, Footprints.Shrunk(ring, _slack));
    }

    int Cell(double v) => (int)Math.Floor(v / _cell);

    public void Add(BuiltPart part)
    {
        var stand = Made(part);
        for (int x = Cell(stand.X0); x <= Cell(stand.X1); x++)
            for (int y = Cell(stand.Y0); y <= Cell(stand.Y1); y++)
                (_cells.TryGetValue((x, y), out var list) ? list : _cells[(x, y)] = new()).Add(stand);
    }

    Stand? First(double x0, double y0, double x1, double y1, Func<Stand, bool> wanted)
    {
        int stamp = ++_stamp;
        for (int x = Cell(x0); x <= Cell(x1); x++)
            for (int y = Cell(y0); y <= Cell(y1); y++)
            {
                if (!_cells.TryGetValue((x, y), out var list)) continue;
                foreach (var stand in list)
                {
                    if (stand.Seen == stamp || stand.X0 > x1 || x0 > stand.X1 || stand.Y0 > y1 || y0 > stand.Y1) continue;
                    stand.Seen = stamp;
                    if (wanted(stand)) return stand;
                }
            }
        return null;
    }

    public bool Crowds(BuiltPart block)
    {
        var mine = Made(block);
        return First(mine.X0, mine.Y0, mine.X1, mine.Y1, other => !ReferenceEquals(other.Part, block) && Footprints.Overlap(mine.Core, other.Core)) != null;
    }

    public Stand? Holding(Spot p, double deeper) => First(p.X, p.Y, p.X, p.Y, stand => Enclosures.Inside(stand.Ring, p) && Edge(stand.Ring, p) > deeper);

    public bool Fronted(Spot p, double reach) =>
        First(p.X - reach, p.Y - reach, p.X + reach, p.Y + reach, stand => Enclosures.Inside(stand.Ring, p) || Edge(stand.Ring, p) <= reach) != null;

    public static bool InCourt(BuiltSite site, Stand stand, Spot p)
    {
        if (!Courts.Contains(stand.Part.Class) || stand.Ring.Count != 4) return false;
        double sideA = (stand.Ring[1] - stand.Ring[0]).Length, sideB = (stand.Ring[2] - stand.Ring[1]).Length;
        return Edge(stand.Ring, p) > Math.Min(CourtShare * Math.Min(sideA, sideB), site.M(CourtMostM));
    }

    static double Edge(List<Spot> ring, Spot p)
    {
        double least = double.MaxValue;
        for (int i = 0; i < ring.Count; i++) least = Math.Min(least, p.FromSegment(ring[i], ring[(i + 1) % ring.Count]));
        return least;
    }
}
