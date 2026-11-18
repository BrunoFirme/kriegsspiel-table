using System.Globalization;
using System.Text.Json.Serialization;
using E = KTable.Core.Model.CrowdEntry;
using B = KTable.Core.Model.CrowdBaseline;

namespace KTable.Core.Model;

public sealed class MadeCrowd
{
    public const int MostOne = 2000, UsualMost = 1000, LeastCap = 100, MostCap = 5000, MostCallings = 16, MostCalling = 40, MostWeight = 100;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string Mix { get; set; } = MadeMixes.Townsfolk;
    public string Nationality { get; set; } = "";
    public Vec2 At { get; set; }
    public int Seed { get; set; }
    public long Start { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Wealth { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Settled { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, double>? Callings { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Men { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Children { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Elders { get; set; }

    public MadeCrowd Clone()
    {
        var copy = (MadeCrowd)MemberwiseClone();
        copy.Callings = Callings == null ? null : new(Callings);
        return copy;
    }

    public string? Problem() =>
        Count is < 1 or > MostOne ? $"a made crowd is 1 to {MostOne} people"
        : MadeMixes.All.All(m => m.Id != Mix) ? "no such mix"
        : !double.IsFinite(At.X) || !double.IsFinite(At.Z) ? "the crowd's place is off the table"
        : Wealth != null && !B.Wealths.Contains(Wealth) ? "no such wealth"
        : new[] { Men, Children, Elders }.Any(v => v is not null and not (>= 0 and <= 1)) || Children + Elders > 1 ? "shares from 0 to 1"
        : Callings is { } c && (c.Count is 0 or > MostCallings || c.Any(k => k.Key.Length is 0 or > MostCalling || k.Value is not (>= 0 and <= MostWeight)) || c.Values.Sum() <= 0)
            ? $"1 to {MostCallings} callings, each weighed 0 to {MostWeight}"
        : Name.Length > CrowdOrder.MostName ? "a shorter name" : null;
}

public sealed record MadeMix(string Id, string Name, double Men, double Children, double Elders, IReadOnlyDictionary<string, double> Callings, string Wealth, string? Act)
{
    public MadeRecipe Fill() => new() { Men = Men, Children = Children, Elders = Elders, Callings = new(Callings) };
}

public sealed class MadeRecipe
{
    public double Men { get; set; }
    public double Children { get; set; }
    public double Elders { get; set; }
    public Dictionary<string, double> Callings { get; set; } = new();
    public string? Wealth { get; set; }
    public bool Edge { get; set; }

    public void Into(MadeCrowd crowd) =>
        (crowd.Men, crowd.Children, crowd.Elders, crowd.Callings, crowd.Wealth) = (Men, Children, Elders, new(Callings), Wealth ?? crowd.Wealth);
}

public static class MadeMixes
{
    public const string Mob = "mob", Townsfolk = "townsfolk", Soldiers = "soldiers", Pilgrims = "pilgrims", Refugees = "refugees";

    public static readonly IReadOnlyList<MadeMix> All =
    [
        new(Mob, "Mob", 0.8, 0.05, 0.05, new Dictionary<string, double> { [B.Labourer] = 6, [B.Smith] = 1, [B.Farmer] = 2 }, E.Poor, RigMotion.Cheer),
        new(Townsfolk, "Townsfolk", 0.5, 0.25, 0.12, new Dictionary<string, double> { [B.Labourer] = 4, [B.Merchant] = 1, [B.Farmer] = 2 }, E.Middle, null),
        new(Soldiers, "Soldiers", 1, 0, 0, new Dictionary<string, double> { [B.Soldier] = 1 }, E.Poor, null),
        new(Pilgrims, "Pilgrims", 0.5, 0.1, 0.25, new Dictionary<string, double> { [B.Labourer] = 2, [B.Farmer] = 2, [B.Cleric] = 1 }, E.Poor, null),
        new(Refugees, "Refugees", 0.35, 0.35, 0.2, new Dictionary<string, double> { [B.Farmer] = 3, [B.Labourer] = 2 }, E.Poor, RigMotion.Carry),
    ];

    public static MadeMix Of(string id) => All.FirstOrDefault(m => m.Id == id) ?? All[1];
}

public static class CrowdMade
{
    const double MillPerSqrt = 1.2, WalkPlay = 0.15, StandM = 0.65, Golden = 2.39996;
    const int MadeStream = 41;

    public static CrowdTown Town(MadeCrowd made, Nationality flat)
    {
        var mix = MadeMixes.Of(made.Mix);
        var callings = made.Callings is { Count: > 0 } own ? own : mix.Callings;
        double men = made.Men ?? mix.Men, children = made.Children ?? mix.Children, elders = made.Elders ?? mix.Elders;
        bool soldiers = callings.Count == 1 && callings.ContainsKey(B.Soldier);
        double reach = Math.Max(3, Math.Sqrt(made.Count) * MillPerSqrt);
        string name = made.Name.Length > 0 ? made.Name : mix.Name;
        var places = new List<CrowdPlace> { new(-1, name, made.At, CrowdUse.Square, true, reach, null, 0) };
        var dice = new Dice(made.Seed, MadeStream);
        var purse = new Dice(made.Seed, MadeStream + 2);
        var people = new List<CrowdResident>(made.Count);
        string coat = CrowdDress.ArmyCoat(flat, made.Seed);
        for (int i = 0; i < made.Count; i++)
        {
            bool child = dice.Chance(children), elder = !child && dice.Chance(elders);
            string sex = dice.Chance(men) ? E.Man : E.Woman, band = child ? E.Child : elder ? E.Elder : E.Adult;
            string calling = child ? B.Youngster : sex == E.Woman && !soldiers ? B.Housewife : Weighted(dice, callings);
            if (soldiers) (sex, band, calling) = (E.Man, E.Adult, B.Soldier);
            int seed = unchecked((int)(dice.Next() * int.MaxValue));
            var entry = CrowdDress.Person(flat, i, seed, made.Start, sex, band, calling);
            entry.Wealth = made.Wealth ?? (calling is B.Soldier or B.Cleric ? mix.Wealth : CrowdRoster.WealthIn(mix.Wealth, purse));
            people.Add(new CrowdResident { Entry = entry, Home = 0, Part = -1, Rank = 0, SpeedM = CrowdRoster.WalkM * dice.Between(1 - WalkPlay, 1 + WalkPlay) });
        }
        return new CrowdTown { Thing = made.Id, People = flat, Coat = coat, Places = places, Residents = people, Made = true };
    }

    public static string Line(MadeCrowd made) => $"{(made.Name.Length > 0 ? made.Name : MadeMixes.Of(made.Mix).Name)} · {made.Count} people appear";

    public static CrowdTown Settle(MadeCrowd made, Nationality flat, CrowdTown town, double farming)
    {
        var loose = Town(made, flat);
        var homes = town.Of(CrowdUse.Home).ToList();
        if (homes.Count == 0) return loose;
        var dice = new Dice(made.Seed, MadeStream + 1);
        var residents = loose.Residents.Select(r =>
        {
            int home = homes[Math.Min(homes.Count - 1, (int)(dice.Next() * homes.Count))];
            return new CrowdResident { Entry = r.Entry, Home = home, Part = town.Places[home].Part, Rank = 0, SpeedM = r.SpeedM };
        }).ToList();
        return new CrowdTown { Thing = made.Id, People = flat, Coat = town.Coat, Places = town.Places, Residents = CrowdRoster.Employ(town.Places, residents, farming, dice), Made = true };
    }

    public static string? Unsettled(GameState g, MadeCrowd made) =>
        made.Settled is { } town && g.Built.All(b => b.Id != town || b.Kind != BuiltKinds.Settlement) ? "no such town to settle in" : null;

    public static CrowdWhere Stand(CrowdTown town, int i, int seed, double upm)
    {
        var middle = town.Places[0].At;
        double r = StandM * Math.Sqrt(i + 0.5) * upm, a = i * Golden, heading = (seed & 1023) / 1024.0 * 2 * Math.PI;
        return new CrowdWhere(new Vec2(middle.X + Math.Sin(a) * r, middle.Z + Math.Cos(a) * r), heading, 0, false, 0);
    }

    public static string NationalityAt(GameState g, Vec2 at)
    {
        var sheet = g.Sheets.FirstOrDefault(s => s.Id == PlaceTree.SheetUnder(g, at));
        if (sheet != null && g.Built.FirstOrDefault(b => b.Kind == BuiltKinds.Settlement && b.Sheet == sheet.Id) is { } town) return CrowdRoster.NationalityOf(town, sheet, g);
        int age = Manners.Age(g.Lighting.MapStyle);
        return Nationalities.All.FirstOrDefault(n => Manners.All.Any(m => m.Id == n.Culture && m.Epoch == age))?.Id ?? B.Id;
    }

    public static string? Refusal(GameState g, Vec2 at) =>
        g.Calibration.UnitsPerMeter is not > 0 ? GroundKinds.NeedsScale
        : g.Sheets.FirstOrDefault(s => s.Id == PlaceTree.SheetUnder(g, at)) is not { } sheet ? "made crowds stand on a map"
        : sheet.HasPicture ? "made crowds stand on laid ground, not on a picture map" : null;

    public static CrowdOrder WalkIn(MadeCrowd crowd, Vec2 to) => new()
    {
        Name = crowd.Name, Members = new() { [crowd.Id] = Enumerable.Range(0, crowd.Count).ToList() }, Do = CrowdCommands.GoTo, To = [to], Start = crowd.Start,
    };

    public static MadeRecipe? Read(string words, MadeRecipe recipe)
    {
        foreach (string part in words.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double? share = bits.Length == 2 && double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            string head = bits[0];
            if (head == "men" && share is { } m) recipe.Men = m;
            else if (head == "children" && share is { } c) recipe.Children = c;
            else if (head == "elders" && share is { } e) recipe.Elders = e;
            else if (head == "edge") recipe.Edge = true;
            else if (B.Wealths.Contains(head)) recipe.Wealth = head;
            else if (CallingsOf(part) is { } callings) recipe.Callings = callings;
            else return null;
        }
        return recipe;
    }

    static Dictionary<string, double>? CallingsOf(string part)
    {
        var callings = new Dictionary<string, double>();
        foreach (var kw in part.Split('|').Select(o => o.Split(':')))
        {
            if (kw[0].Length == 0 || kw.Length > 2) return null;
            callings[kw[0]] = kw.Length == 2 && double.TryParse(kw[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 1;
        }
        return callings;
    }

    public static Vec2 EdgeOf(SheetDef sheet, Vec2 from)
    {
        var (x, z) = Ground.Local(sheet, from);
        double w = sheet.Size.W / 2, h = sheet.Size.H / 2;
        if (w - Math.Abs(x) < h - Math.Abs(z)) x = Math.Sign(x == 0 ? 1 : x) * w;
        else z = Math.Sign(z == 0 ? 1 : z) * h;
        return Ground.FromLocal(sheet, x, z);
    }

    static string Weighted(Dice dice, IReadOnlyDictionary<string, double> shares)
    {
        double x = dice.Next() * shares.Values.Sum();
        foreach (var (key, share) in shares.OrderBy(s => s.Key, StringComparer.Ordinal))
            if ((x -= share) < 0) return key;
        return shares.Keys.First();
    }
}
