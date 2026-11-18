using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class TrafficRoles
{
    public const string Farm = "farm", Through = "through", Rider = "rider", Carry = "carry", Local = "local", Military = "military", Tram = "tram";

    public static readonly IReadOnlyList<string> All = [Farm, Through, Rider, Carry, Local, Military, Tram];
}

public static class RigParts
{
    public const string Vehicle = "vehicle", Animal = "animal", Walk = "walk", Sit = "sit", Ride = "ride", Bear = "bear";

    public static readonly IReadOnlyList<string> All = [Vehicle, Animal, Walk, Sit, Ride, Bear];

    public static readonly IReadOnlyList<string> Vehicles = ["cart2", "wagon4", "coach", "chair", "barrow", "handcart", "car", "lorry", "tram", "bike"];

    public static readonly IReadOnlyList<string> Animals = ["horse", "pony", "ox", "buffalo", "donkey", "mule", "camel"];

    public static readonly IReadOnlyList<string> Tacks = ["collar", "saddle", "pack"];
}

public sealed class RigPart
{
    public string Is { get; set; } = RigParts.Walk;
    public string What { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tack { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Style { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Load { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Livery { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Y { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Lag { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Width { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Length { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Trot { get; set; }
}

public sealed class TrafficRig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }
    public string Role { get; set; } = TrafficRoles.Local;
    public double SpeedM { get; set; } = 1.2;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Night { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Share { get; set; }
    public List<RigPart> Parts { get; set; } = new();

    public (double Width, double Length) Footprint()
    {
        double w = 0.6, front = 0.4, back = -0.4;
        foreach (var p in Parts)
        {
            double halfL = p.Is == RigParts.Vehicle ? Math.Max(p.Length, 0.5) : p.Is == RigParts.Animal ? 1 : 0.3;
            w = Math.Max(w, Math.Abs(p.X) * 2 + (p.Is == RigParts.Vehicle ? p.Width * 2 : 0.6));
            front = Math.Max(front, p.Z - p.Lag + halfL);
            back = Math.Min(back, p.Z - p.Lag - halfL);
        }
        return (w, front - back);
    }

    public static string? Problem(TrafficRig r)
    {
        if (r.Id.Length == 0 || r.Name.Length == 0) return "a rig needs an id and a name";
        if (!TrafficRoles.All.Contains(r.Role)) return $"{r.Id}: no such role {r.Role}";
        if (!(r.SpeedM is > 0 and <= 40)) return $"{r.Id}: speed from 0 to 40 m/s";
        if (r.Parts.Count is 0 or > 16) return $"{r.Id}: 1 to 16 parts";
        foreach (var p in r.Parts)
        {
            if (!RigParts.All.Contains(p.Is)) return $"{r.Id}: no such part {p.Is}";
            if (p.Is == RigParts.Vehicle && !RigParts.Vehicles.Contains(p.What)) return $"{r.Id}: no such vehicle {p.What}";
            if (p.Is == RigParts.Animal && !RigParts.Animals.Contains(p.What)) return $"{r.Id}: no such animal {p.What}";
            if (p.Tack != null && !RigParts.Tacks.Contains(p.Tack)) return $"{r.Id}: no such tack {p.Tack}";
            if (p.Is == RigParts.Ride && !RigParts.Animals.Contains(p.Style ?? "")) return $"{r.Id}: a rider sits on an animal";
            if (!double.IsFinite(p.X + p.Y + p.Z + p.Lag + p.Width + p.Length) || Math.Abs(p.X) > 10 || Math.Abs(p.Z) > 60 || p.Lag is < 0 or > 60) return $"{r.Id}: parts stand within 60 m";
        }
        return null;
    }
}

public readonly record struct TrafficTrip(int Rig, int From, int To, int Depart, int Until, int Seed);

public static class CrowdTraffic
{
    const int Hour = CalendarDef.SecondsInHour, Stream = 41, TramEvery = 600;
    const double PerFarm = 30, PerThrough = 35, PerRider = 40, PerCarry = 35, PerLocal = 15, PerMilitary = 70, MarketBusier = 2, HolyQuieter = 0.3, EndShare = 0.6;
    const double TramFrom = 6, TramTo = 22, NightFrom = 4.5, LateHour = 23.5;

    public static int[] Ends(CrowdTown town)
    {
        var gates = town.Of(CrowdUse.Gate).ToList();
        if (gates.Count >= 2) return gates.ToArray();
        var streets = town.Of(CrowdUse.Street).ToList();
        if (streets.Count < 2) return gates.ToArray();
        var mid = new Vec2(streets.Average(i => town.Places[i].At.X), streets.Average(i => town.Places[i].At.Z));
        double Far(int i) => (town.Places[i].At - mid).Length;
        double most = streets.Max(Far);
        int Sector(int i) => (int)Math.Floor((Math.Atan2(town.Places[i].At.Z - mid.Z, town.Places[i].At.X - mid.X) + Math.PI) / (Math.PI / 2)) % 4;
        return gates.Concat(streets.Where(i => Far(i) >= most * EndShare).GroupBy(Sector).Select(g => g.MaxBy(Far))).Distinct().ToArray();
    }

    public static int SeedOf(string thing)
    {
        uint h = 2166136261;
        foreach (char c in thing) h = (h ^ c) * 16777619;
        return unchecked((int)h);
    }

    public static List<TrafficTrip> Plan(CrowdTown town, DayKind kind, long dayNumber, int shown)
    {
        var trips = new List<TrafficTrip>();
        if (town.People.Traffic is not { Count: > 0 } rigs || shown <= 0 || town.Places.Count == 0) return trips;
        var dice = new Dice(unchecked(SeedOf(town.Thing) ^ (int)dayNumber * 7919), Stream);
        var ends = Ends(town);
        double dark = town.People.Day?.DarkBy ?? 21, busy = kind == DayKind.Holy ? HolyQuieter : 1;
        int[] Of(params CrowdUse[] uses) => uses.SelectMany(town.Of).ToArray();
        var market = Of(CrowdUse.Market);
        var farms = town.Places.Select((p, i) => (p, i)).Where(x => x.p.Use == CrowdUse.Work && x.p.Calling == CrowdBaseline.Farmer).Select(x => x.i).ToArray();
        var fields = Of(CrowdUse.Fields);
        var barracks = Of(CrowdUse.Barracks);
        var stops = Of(CrowdUse.Market, CrowdUse.Square, CrowdUse.Worship, CrowdUse.Tavern);
        var works = Of(CrowdUse.Work);
        var homes = town.Residents.Where(r => r.Entry.Wealth == CrowdEntry.Rich && r.Entry.Band == CrowdEntry.Adult).Select(r => r.Home).Distinct().ToArray();
        if (homes.Length == 0) homes = Of(CrowdUse.Home);
        int Any(int[] from) => from.Length == 0 ? -1 : from[dice.Below(from.Length)];
        int Count(double per) => (int)Math.Round(shown / per * busy + dice.Between(0, 0.99));
        foreach (string role in TrafficRoles.All)
        {
            var mine = rigs.Select((r, i) => (r, i)).Where(x => x.r.Role == role).ToList();
            if (mine.Count == 0) continue;
            double weights = mine.Sum(x => x.r.Share > 0 ? x.r.Share : 1);
            int Rig()
            {
                double pick = dice.Next() * weights;
                foreach (var (r, i) in mine)
                    if ((pick -= r.Share > 0 ? r.Share : 1) < 0) return i;
                return mine[^1].i;
            }
            void Trip(int rig, int from, int to, double hour, double stay)
            {
                if (from < 0 || to < 0 || from == to) return;
                if (rigs[rig].Night && hour > NightFrom && hour < dark) hour = dice.Chance(0.5) ? dice.Between(dark, LateHour) : dice.Between(0.2, NightFrom);
                int depart = (int)(Math.Clamp(hour, 0, LateHour) * Hour), seed = unchecked((int)(dice.Next() * int.MaxValue));
                int until = stay > 0 ? Math.Min(depart + (int)(stay * Hour), CrowdDay.Day - 1) : 0;
                trips.Add(new TrafficTrip(rig, from, to, depart, until, seed));
                if (until > 0) trips.Add(new TrafficTrip(rig, to, from, until, 0, seed));
            }
            switch (role)
            {
                case TrafficRoles.Farm:
                    for (int k = Count(PerFarm / (kind == DayKind.Market ? MarketBusier : 1)); k > 0; k--)
                        Trip(Rig(), Any(ends.Length > 0 ? ends : fields), kind == DayKind.Market || farms.Length == 0 ? Any(market.Length > 0 ? market : stops) : Any(farms),
                            dice.Between(5.5, 10), dice.Between(0.5, 1.5));
                    break;
                case TrafficRoles.Through when ends.Length >= 2:
                    for (int k = Count(PerThrough) + 1; k > 0; k--)
                    {
                        int a = Any(ends), b = Any(ends.Where(e => e != a).ToArray());
                        Trip(Rig(), a, b, dice.Between(6, 19), 0);
                    }
                    break;
                case TrafficRoles.Rider:
                    for (int k = Count(PerRider) + 1; k > 0; k--) Trip(Rig(), Any(ends), Any(stops.Length > 0 ? stops : ends), dice.Between(7, 19), dice.Between(0.5, 2));
                    break;
                case TrafficRoles.Carry:
                    for (int k = Math.Min(Count(PerCarry) + 1, homes.Length * 3); k > 0; k--)
                        Trip(Rig(), Any(homes), Any(kind == DayKind.Holy ? Of(CrowdUse.Worship) is { Length: > 0 } w ? w : stops : stops), dice.Between(9, 17), dice.Between(1, 2.5));
                    break;
                case TrafficRoles.Local:
                    var about = Of(CrowdUse.Street, CrowdUse.Home);
                    for (int k = Count(PerLocal); k > 0; k--) Trip(Rig(), Any(works.Length > 0 ? works : homes), Any(dice.Chance(0.5) && market.Length > 0 ? market : about), dice.Between(7, 17.5), 0);
                    break;
                case TrafficRoles.Military when ends.Length > 0 && barracks.Length > 0:
                    for (int k = Count(PerMilitary) + 1; k > 0; k--) Trip(Rig(), Any(ends), Any(barracks), dice.Between(8, 16), dice.Between(1, 3));
                    break;
                case TrafficRoles.Tram when ends.Length >= 2:
                    var line = ends.SelectMany(a => ends.Select(b => (a, b))).MaxBy(p => (town.Places[p.a].At - town.Places[p.b].At).Length);
                    for (int s = (int)(TramFrom * Hour), n = 0; s < TramTo * Hour; s += TramEvery, n++)
                        trips.Add(new TrafficTrip(Rig(), n % 2 == 0 ? line.a : line.b, n % 2 == 0 ? line.b : line.a, s, 0, unchecked((int)(dice.Next() * int.MaxValue))));
                    break;
            }
        }
        trips.Sort((a, b) => a.Depart.CompareTo(b.Depart));
        return trips;
    }
}
