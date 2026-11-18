using KTable.Core.Model;

namespace KTable.Client;

public sealed class SightEye
{
    public const int MostEyes = 64;
    public const double SharedShare = 0.02;

    readonly byte[] _seen = new byte[Viewshed.Bearings * Viewshed.Rings];

    public SightEye(Vec2 center, IReadOnlyList<string> ids)
    {
        Center = center;
        Ids = ids;
    }

    public Vec2 Center { get; }
    public IReadOnlyList<string> Ids { get; }
    public Viewshed? Land { get; set; }
    public double Reach { get; private set; }
    public byte[] Seen => _seen;

    public void Lay(double reach, Func<double, double> farthest)
    {
        Reach = reach;
        var land = Land;
        Parallel.For(0, Viewshed.Bearings, b =>
        {
            double far = farthest(Viewshed.AngleOf(b));
            for (int r = 0; r < Viewshed.Rings; r++)
            {
                int at = r * Viewshed.Bearings + b;
                bool seen = Viewshed.AwayOf(r, reach) <= far && (land == null || land.Seen[at] != 0);
                _seen[at] = seen ? Viewshed.SeenValue : (byte)0;
            }
        });
    }

    public bool Sees(Vec2 p) => Viewshed.Looked(_seen, Center, Reach, p);

    public static List<SightEye> Gather(IReadOnlyList<(string Id, Vec2 At)> pieces, double reach)
    {
        for (double near = Math.Max(reach * SharedShare, 1e-6); ; near *= 2)
        {
            var groups = new List<(Vec2 First, List<(string Id, Vec2 At)> Members)>();
            foreach (var piece in pieces)
            {
                int i = groups.FindIndex(g => (g.First - piece.At).Length <= near);
                if (i >= 0) groups[i].Members.Add(piece);
                else groups.Add((piece.At, new() { piece }));
            }
            if (groups.Count <= MostEyes) return groups.Select(g => new SightEye(Middle(g.Members.Select(m => m.At)), g.Members.Select(m => m.Id).ToList())).ToList();
        }
    }

    static Vec2 Middle(IEnumerable<Vec2> spots)
    {
        var all = spots.ToList();
        return new Vec2(all.Average(s => s.X), all.Average(s => s.Z));
    }
}
