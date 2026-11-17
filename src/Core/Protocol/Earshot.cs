using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static class Earshot
{
    public const int Whisper = 0, Say = 1, Shout = 2, MostHearers = 200, MostLength = 500;
    const double ThroughWalls = 0.5, SameStoreyShare = 0.5;
    static readonly double[] Metres = { 2, 15, 60 };
    static readonly string[] Verbs = { "whispers", "says", "shouts" };

    public static int Loudness(int loud) => Math.Clamp(loud, Whisper, Shout);

    public static string Verb(int loud) => Verbs[Loudness(loud)];

    static double PerMetre(Calibration calibration) => calibration.UnitsPerMeter is > 0 and var perMetre ? perMetre : 1;

    public static double Reach(int loud, Calibration calibration) => Metres[Loudness(loud)] * PerMetre(calibration);

    static double Apart(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    public static List<PieceInstance> Hearers(GameState g, PieceInstance speaker, int loud, Func<string, PieceDef?>? kind = null, string? to = null)
    {
        loud = Loudness(loud);
        double reach = Reach(loud, g.Calibration);
        var pieces = g.Layer(Layers.Umpire);
        var near = pieces.Where(p => p.Id != speaker.Id && (to == null || p.Id == to) && Apart(p.Pos, speaker.Pos) <= reach).ToList();
        if (kind != null && near.Count > 0 && Walls.Of(pieces, kind, Footings.StoreyM * PerMetre(g.Calibration)) is { } walls)
            near = near.Where(p => walls.Hears(speaker, p, loud, reach)).ToList();
        return near.OrderBy(p => Apart(p.Pos, speaker.Pos)).Take(MostHearers).ToList();
    }

    sealed class Walls
    {
        readonly List<Building> _buildings;
        readonly double _storey;

        Walls(List<Building> buildings, double storey) => (_buildings, _storey) = (buildings, storey);

        public static Walls? Of(List<PieceInstance> pieces, Func<string, PieceDef?> kind, double storey)
        {
            var buildings = new List<Building>();
            foreach (var p in pieces)
                if (kind(p.Def) is { } d && Building.IsOne(d)) buildings.Add(new Building(p, d, kind));
            return buildings.Count > 0 ? new Walls(buildings, storey) : null;
        }

        Building? Around(PieceInstance p) => _buildings.FirstOrDefault(b => b.Piece.Id != p.Id && b.Holds(p.Pos));

        static double Level(PieceInstance p, Building b) => p.Y > 0 ? p.Y : b.Piece.Y;

        bool SameStorey(double a, double b) => Math.Abs(a - b) < _storey * SameStoreyShare;

        public bool Hears(PieceInstance speaker, PieceInstance hearer, int loud, double reach)
        {
            var from = Around(speaker);
            var at = Around(hearer);
            if (from == null && at == null) return true;
            bool shoutsThrough = loud == Shout && Apart(speaker.Pos, hearer.Pos) <= reach * ThroughWalls;
            if (from == at) return SameStorey(Level(speaker, from!), Level(hearer, at!)) || shoutsThrough;
            if (loud == Whisper) return false;
            return Through(speaker, from, hearer, at) <= reach || shoutsThrough;
        }

        IEnumerable<Vec2> Openings(PieceInstance p, Building? b) =>
            b == null ? [p.Pos] : b.OpenDoors.Where(d => SameStorey(d.Level, Level(p, b))).Select(d => d.At);

        double Through(PieceInstance speaker, Building? from, PieceInstance hearer, Building? at)
        {
            double best = double.MaxValue;
            var outs = Openings(speaker, from).ToList();
            var ins = Openings(hearer, at).ToList();
            foreach (var o in outs)
                foreach (var i in ins)
                    best = Math.Min(best, Apart(speaker.Pos, o) + Apart(o, i) + Apart(i, hearer.Pos));
            return best;
        }
    }
}
