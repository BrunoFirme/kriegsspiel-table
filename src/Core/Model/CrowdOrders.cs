using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class CrowdCommands
{
    public const string GoTo = "goto", Halt = "halt", Home = "home", Gather = "gather", Form = "form", Follow = "follow", LineTheWay = "line", Surround = "surround";
    public const string Attack = "attack", FireAt = "fire", ThrowAt = "throw", Drive = "drive", Flee = "flee", Clear = "clear", Yield = "yield", Leave = "leave";

    public static readonly IReadOnlyList<(string Id, string Name)> All =
    [
        (GoTo, "Go to"), (Follow, "Follow"), (Halt, "Halt"), (Home, "Go home"), (Gather, "Gather"), (Form, "Form"), (LineTheWay, "Line the way"), (Surround, "Surround"),
        (Attack, "Attack"), (FireAt, "Fire at"), (ThrowAt, "Throw at"), (Drive, "Drive"), (Flee, "Flee"), (Clear, "Clear the streets"), (Yield, "Yield"), (Leave, "Leave the map"),
    ];

    public static string NameOf(string id) => All.FirstOrDefault(c => c.Id == id).Name ?? id;
}

public static class CrowdShapes
{
    public const string Loose = "loose", Line = "line", Column = "column", Ring = "ring", Square = "square", Wedge = "wedge", Across = "across", Sides = "sides";
    public static readonly IReadOnlyList<string> All = [Loose, Line, Column, Ring, Square, Wedge, Across, Sides];
}

public static class CrowdPaces
{
    public const string Walk = "walk", March = "march", Run = "run", Sneak = "sneak";
    public static readonly IReadOnlyList<(string Id, double MetresPerSecond)> All = [(Walk, 1.3), (March, 1.5), (Run, 3.5), (Sneak, 0.8)];

    public static double Of(string? pace) => All.FirstOrDefault(p => p.Id == pace).MetresPerSecond is > 0 and var m ? m : 1.3;
}

public sealed class CrowdOrder
{
    public const int MostMembers = 5000, MostPoints = 64, MostName = 80;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, List<int>> Members { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Said { get; set; }
    public string Do { get; set; } = CrowdCommands.GoTo;
    public List<Vec2> To { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Piece { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Against { get; set; }
    public string Shape { get; set; } = CrowdShapes.Loose;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Facing { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double WidthM { get; set; }
    public string Pace { get; set; } = CrowdPaces.Walk;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Act { get; set; }
    public long Start { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? Until { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Vec2? From { get; set; }

    [JsonIgnore] public int Count => Members.Values.Sum(m => m.Count);

    public CrowdOrder Clone() => Json.Clone(this);

    public bool Holds(string town, int person) => Members.TryGetValue(town, out var ids) && ids.BinarySearch(person) >= 0;

    public string? Problem()
    {
        if (CrowdCommands.All.All(c => c.Id != Do)) return "no such command";
        if (!CrowdShapes.All.Contains(Shape)) return "no such shape";
        if (CrowdPaces.All.All(p => p.Id != Pace)) return "no such pace";
        if (!RigMotion.KnownAct(Act)) return "no such act";
        if (Count is 0 or > MostMembers) return $"an order is for 1 to {MostMembers} people";
        if (To.Count > MostPoints || To.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Z))) return "the order's places are off the table";
        if (Name.Length > MostName) return "a shorter name";
        if (!double.IsFinite(Facing) || !double.IsFinite(WidthM) || WidthM < 0) return "a facing and width on the table";
        return Until is { } until && until < Start ? "an order ends after it starts" : null;
    }

    public void Tidy()
    {
        foreach (var key in Members.Keys.ToList())
        {
            var ids = Members[key].Distinct().Order().ToList();
            if (ids.Count == 0) Members.Remove(key);
            else Members[key] = ids;
        }
    }
}

public readonly record struct CrowdStep(long Start, long End, CrowdPath? Path, Vec2 At, double Heading, string? Act, double Speed);

public sealed class CrowdTrack
{
    public List<CrowdStep> Steps { get; } = new();
    public long Ends { get; set; } = long.MaxValue;
    public long Begins { get; set; }

    public bool Covers(long t) => t >= Begins && t < Ends;

    public bool At(long t, double upm, out CrowdWhere where, out string? act)
    {
        where = default;
        act = null;
        if (Steps.Count == 0 || t < Steps[0].Start || t >= Ends) return false;
        foreach (var s in Steps)
        {
            if (t >= s.End) continue;
            if (s.Path is { } route)
            {
                double along = Math.Clamp((t - s.Start) * s.Speed * upm, 0, route.Length);
                var (point, heading) = route.At(along);
                where = new CrowdWhere(point, heading, along, along < route.Length, -1, route.HeightAt(along));
                return true;
            }
            where = new CrowdWhere(s.At, s.Heading, 0, false, -1);
            act = s.Act;
            return true;
        }
        return false;
    }
}

public static class CrowdOrders
{
    const double SpacingM = 0.9, RankM = 1.1, LooseM = 0.9, StandOffM = 30, FleeM = 120, AttackM = 2.5, SurroundM = 6, Abreast = 4, ShakeM = 0.6;
    const double Turn = Math.PI * 2;

    public static (Vec2 At, double Heading) Slot(CrowdOrder o, int rank, int count, double upm, Vec2 centre, double facing, Vec2? from = null)
    {
        double m = upm, w = o.WidthM > 0 ? o.WidthM : Math.Max(count * SpacingM / Math.Max(1, Math.Ceiling(count * SpacingM / Math.Max(SpacingM, 40))), SpacingM);
        var ahead = new Vec2(Math.Sin(facing), Math.Cos(facing));
        var right = new Vec2(ahead.Z, -ahead.X);
        Vec2 At(double across, double back) => new(centre.X + (right.X * across - ahead.X * back) * m, centre.Z + (right.Z * across - ahead.Z * back) * m);
        switch (o.Shape)
        {
            case CrowdShapes.Line or CrowdShapes.Across:
            {
                int perRank = Math.Max(1, (int)Math.Floor(w / SpacingM) + 1);
                int r = rank / perRank, f = rank % perRank, inRank = Math.Min(perRank, count - r * perRank);
                return (At((f - (inRank - 1) / 2.0) * SpacingM, r * RankM), facing);
            }
            case CrowdShapes.Column:
            {
                int files = (int)Abreast;
                return (At((rank % files - (files - 1) / 2.0) * SpacingM, rank / files * RankM), facing);
            }
            case CrowdShapes.Ring or CrowdShapes.Square:
            {
                double radius = Math.Max(SurroundM, count * SpacingM / Turn), a = Turn * rank / Math.Max(count, 1);
                var p = new Vec2(Math.Sin(a) * radius, Math.Cos(a) * radius);
                if (o.Shape == CrowdShapes.Square) p = new Vec2(Math.Clamp(p.X * 1.4, -radius, radius), Math.Clamp(p.Z * 1.4, -radius, radius));
                bool outward = o.Act == RigMotion.Guard;
                return (new Vec2(centre.X + p.X * m, centre.Z + p.Z * m), Math.Atan2(-p.X, -p.Z) + (outward ? Math.PI : 0));
            }
            case CrowdShapes.Wedge:
            {
                int arm = (rank + 1) / 2, side = rank % 2 == 0 ? 1 : -1;
                return (At(side * arm * SpacingM, arm * RankM), facing);
            }
            case CrowdShapes.Sides when o.To.Count >= 2:
            {
                var a = o.To[0];
                var b = o.To[^1];
                int side = rank % 2 == 0 ? 1 : -1, k = rank / 2, along = Math.Max(1, (count + 1) / 2);
                double t = (k + 0.5) / along;
                var d = b - a;
                double len = Math.Max(d.Length, 1e-9), half = (o.WidthM > 0 ? o.WidthM : 6) / 2 * m;
                var n = new Vec2(-d.Z / len, d.X / len);
                var p = new Vec2(a.X + d.X * t + n.X * half * side, a.Z + d.Z * t + n.Z * half * side);
                return (p, Math.Atan2(-n.X * side, -n.Z * side));
            }
            default:
            {
                double radius = Math.Sqrt(Math.Max(count, 1)) * LooseM, golden = rank * 2.39996, r = radius * Math.Sqrt((rank + 0.5) / Math.Max(count, 1));
                var p = new Vec2(Math.Sin(golden) * r * m, Math.Cos(golden) * r * m);
                var at = new Vec2(centre.X + p.X, centre.Z + p.Z);
                return (at, from is { } look ? Math.Atan2(look.X - at.X, look.Z - at.Z) : facing);
            }
        }
    }

    public static (Vec2 Centre, double Facing) Aim(CrowdOrder o, Vec2 centre, Vec2 start, double upm)
    {
        var away = start - centre;
        double len = Math.Max(away.Length, 1e-9), toward = Math.Atan2(-away.X, -away.Z);
        return o.Do switch
        {
            CrowdCommands.FireAt or CrowdCommands.ThrowAt => (new Vec2(centre.X + away.X / len * StandOffM * upm, centre.Z + away.Z / len * StandOffM * upm), toward),
            CrowdCommands.Attack => (centre, toward),
            CrowdCommands.Flee => (new Vec2(centre.X + away.X / len * FleeM * upm, centre.Z + away.Z / len * FleeM * upm), Math.Atan2(away.X, away.Z)),
            _ => (centre, o.Facing),
        };
    }

    public static string? ActOf(CrowdOrder o) => o.Do switch
    {
        CrowdCommands.Attack => o.Act ?? RigMotion.Melee,
        CrowdCommands.FireAt => o.Act ?? RigMotion.Ranged,
        CrowdCommands.ThrowAt => o.Act ?? RigMotion.Throw,
        CrowdCommands.Drive => o.Act ?? RigMotion.Brace,
        CrowdCommands.Flee => o.Act ?? RigMotion.Cower,
        CrowdCommands.Yield => o.Act ?? RigMotion.Yield,
        CrowdCommands.Surround => o.Act ?? RigMotion.Guard,
        _ => o.Act,
    };

    public static CrowdTrack Compile(CrowdOrder o, int rank, int count, Vec2 start, double startHeading, double upm, Vec2 target, Func<Vec2, Vec2, CrowdPath?> route)
    {
        var track = new CrowdTrack { Begins = o.Start };
        double speed = CrowdPaces.Of(o.Pace);
        if (o.Do == CrowdCommands.Halt || o.Do == CrowdCommands.Yield && o.To.Count == 0)
        {
            track.Steps.Add(new CrowdStep(o.Start, long.MaxValue, null, start, startHeading, ActOf(o), speed));
        }
        else if (o.Do is CrowdCommands.Clear or CrowdCommands.Leave)
        {
            var home = route(start, target) ?? new CrowdPath([start, target]);
            track.Steps.Add(new CrowdStep(o.Start, o.Start + (long)Math.Ceiling(home.Length / Math.Max(speed * upm, 1e-9)), home, target, startHeading, null, speed));
        }
        else if (o.Do != CrowdCommands.Home)
        {
            var (centre, facing) = Aim(o, target, start, upm);
            if (o.Do == CrowdCommands.Attack) centre = new Vec2(centre.X + Math.Sin(rank * 2.39996) * AttackM * upm, centre.Z + Math.Cos(rank * 2.39996) * AttackM * upm);
            var (slot, heading) = Slot(o, rank, count, upm, centre, facing, o.Do == CrowdCommands.Gather ? target : null);
            var path = route(start, slot) ?? new CrowdPath([start, slot]);
            long arrive = o.Start + (long)Math.Ceiling(path.Length / Math.Max(speed * upm, 1e-9));
            track.Steps.Add(new CrowdStep(o.Start, arrive, path, slot, heading, null, speed));
            track.Steps.Add(new CrowdStep(arrive, long.MaxValue, null, slot, heading, ActOf(o), speed));
        }
        if (o.Until is { } until) Stop(track, until);
        else if (o.Do == CrowdCommands.Home) track.Ends = o.Start;
        return track;
    }

    static void Stop(CrowdTrack track, long until)
    {
        track.Ends = until;
        for (int i = 0; i < track.Steps.Count; i++)
            if (track.Steps[i].End > until) track.Steps[i] = track.Steps[i] with { End = Math.Max(track.Steps[i].Start, until) };
    }

    public static double Shake(string? act, long t, int seed) => act == RigMotion.Cheer ? Math.Sin(t * 7.0 + seed) * ShakeM : 0;
}
