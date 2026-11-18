using KTable.Core.Model;

namespace KTable.Client;

public sealed class SightEye
{
    public const int MostEyes = 64;
    public const double SharedShare = 0.02;
    public const int Channels = 2;

    readonly byte[] _seen = new byte[Viewshed.Bearings * Viewshed.Rings * Channels];

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

    public void Lay(double reach, Func<double, SightRay> along, double ramp)
    {
        Reach = reach;
        var land = Land;
        Parallel.For(0, Viewshed.Bearings, b =>
        {
            var ray = along(Viewshed.AngleOf(b));
            for (int r = 0; r < Viewshed.Rings; r++)
            {
                int at = r * Viewshed.Bearings + b;
                double away = Viewshed.AwayOf(r, reach);
                bool open = away <= ray.Open && (land == null || land.Seen[at] != 0);
                bool seen = away <= ray.Reach && (open || ray.Shown.Any(s => away >= s.From && away <= s.To));
                _seen[Channels * at] = seen ? Viewshed.SeenValue : (byte)0;
                _seen[Channels * at + 1] = seen ? (byte)Math.Round(Blurred(ray.Blurred, away, ramp) / SightingDraft.MostBlur * Viewshed.SeenValue) : (byte)0;
            }
        });
    }

    static double Blurred(List<(double At, double Blur)> lines, double away, double ramp)
    {
        double most = 0;
        foreach (var (at, blur) in lines)
        {
            if (at > away) break;
            most = Math.Max(most, blur * Math.Min(1, (away - at) / Math.Max(ramp, 1e-9)));
        }
        return most;
    }

    public bool Sees(Vec2 p) => Viewshed.CellOf(Center, Reach, p) is int at && _seen[Channels * at] != 0;

    public double BlurAt(Vec2 p) => Viewshed.CellOf(Center, Reach, p) is int at && _seen[Channels * at] != 0 ? _seen[Channels * at + 1] / (double)Viewshed.SeenValue * SightingDraft.MostBlur : 0;

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
