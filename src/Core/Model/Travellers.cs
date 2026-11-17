using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public sealed class PaceDef
{
    public string Traveller { get; set; } = "";
    public string Way { get; set; } = "";
    public double MetresPerSecond { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Age { get; set; }
}

public static class HillPace
{
    public const double Free = 0.27, Pull = 1, SteepestDegrees = 46;

    public static readonly double Steepest = Math.Tan(SteepestDegrees * Math.PI / 180);

    public static double Factor(double grade) => Math.Exp(-Pull * Math.Max(0, grade - Free));
}

public sealed class Traveller
{
    readonly Dictionary<string, double> _paces;

    public Traveller(string name, IReadOnlyDictionary<string, double> paces)
    {
        Name = name;
        _paces = paces.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
        Fastest = _paces.Count == 0 ? 0 : _paces.Values.Max();
    }

    public string Name { get; }
    public double Fastest { get; }
    public IReadOnlyDictionary<string, double> Paces => _paces;

    public double On(string way) => _paces.TryGetValue(way, out double pace) ? pace : 0;

    public double Open => On(Travellers.Open);
}

public static class Travellers
{
    public const string Foot = "foot", Rider = "rider", Cart = "cart", Coach = "coach";
    public const string Open = "open", Bridge = "bridge", Ford = "ford", Ferry = "ferry";
    public const double MostPace = 100;
    const double WalkMps = 1.5, OffPathShare = 0.6, HorseWalkMps = 1.8, RiderMps = 2.2, OxCartMps = 0.9, HorseCartMps = 1.4, OldCoachMps = 1.7, CoachMps = 2.8, FerryMps = 1;

    static readonly string[] Made = ["road", "chaussee", "highway", "boulevard", "street", "causeway", "paving"];
    static readonly string[] Rough = ["track", "lane", "jetty"];
    public static readonly IReadOnlyList<string> WayClasses = [.. Made, .. Rough, Footpaths.Path, "railway"];
    public static readonly IReadOnlyList<string> Names = [Foot, Rider, Cart, Coach];

    public static int AgeOf(GameState state) => Manners.Age(state.Lighting.MapStyle);

    static bool Old(int epoch) => epoch is Manners.Ancient or Manners.Medieval;

    static Dictionary<string, double> Pace(double made, double rough, double open, double path = 0, double stairs = 0, double ramp = 0)
    {
        var paces = new Dictionary<string, double> { [Open] = open, [Ford] = open, [Ferry] = FerryMps, [Bridge] = made, [Footpaths.Path] = path, [Grades.Stairs] = stairs, [Grades.Ramp] = ramp };
        foreach (var way in Made) paces[way] = made;
        foreach (var way in Rough) paces[way] = rough;
        return paces;
    }

    static Dictionary<string, Dictionary<string, double>> Defaults(int epoch)
    {
        double cart = Old(epoch) ? OxCartMps : HorseCartMps, coach = Old(epoch) ? OldCoachMps : CoachMps;
        return new()
        {
            [Foot] = Pace(WalkMps, WalkMps, WalkMps * OffPathShare, WalkMps, WalkMps, WalkMps),
            [Rider] = Pace(RiderMps, RiderMps, RiderMps * OffPathShare, HorseWalkMps, 0, HorseWalkMps),
            [Cart] = Pace(cart, cart, cart * OffPathShare),
            [Coach] = Pace(coach, cart, cart * OffPathShare),
        };
    }

    public static IReadOnlyDictionary<string, Traveller> Default(int epoch) => Merged(epoch, []);

    static string Key(string? text) => (text ?? "").Trim().ToLowerInvariant();

    static bool Usable(PaceDef pace) => Key(pace.Traveller).Length > 0 && Key(pace.Way).Length > 0 && double.IsFinite(pace.MetresPerSecond) && pace.MetresPerSecond >= 0
        && pace.MetresPerSecond <= MostPace;

    public static IReadOnlyDictionary<string, Traveller> Merged(int epoch, IEnumerable<PackManifest> packs)
    {
        var table = Defaults(epoch);
        string age = Manners.AgeName(epoch);
        foreach (var pack in packs)
            foreach (var pace in (pack.Paces ?? []).Where(Usable).OrderBy(p => p.Age == null ? 0 : 1))
            {
                if (pace.Age != null && Key(pace.Age) != age) continue;
                string who = Key(pace.Traveller);
                if (!table.TryGetValue(who, out var ways)) table[who] = ways = new();
                ways[Key(pace.Way)] = pace.MetresPerSecond;
            }
        return table.ToDictionary(t => t.Key, t => new Traveller(t.Key, t.Value));
    }
}
