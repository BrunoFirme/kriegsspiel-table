using E = KTable.Core.Model.CrowdEntry;
using B = KTable.Core.Model.CrowdBaseline;

namespace KTable.Core.Model;

public enum CrowdUse : byte { Home, Work, Worship, Market, Well, Tavern, Fields, Barracks, Square, Street, Gate, Quay }

public sealed record CrowdPlace(int Part, string Class, Vec2 At, CrowdUse Use, bool Outdoor, double ReachM, string? Calling, int Room);

public sealed class CrowdResident
{
    public required E Entry { get; init; }
    public required int Home { get; init; }
    public required int Part { get; init; }
    public required double Rank { get; init; }
    public int Work { get; init; } = -1;
    public double SpeedM { get; init; } = CrowdRoster.WalkM;
    public int Leader { get; init; } = -1;
    public int File { get; init; }
}

public sealed class CrowdTown
{
    public required string Thing { get; init; }
    public required Nationality People { get; init; }
    public required string Coat { get; init; }
    public required List<CrowdPlace> Places { get; init; }
    public required List<CrowdResident> Residents { get; init; }
    public bool Made { get; init; }

    readonly Dictionary<(int, CrowdUse), int[]> _near = new();

    public IEnumerable<int> Of(CrowdUse use) => Places.Select((p, i) => (p, i)).Where(x => x.p.Use == use).Select(x => x.i);

    public int[] Nearest(int home, CrowdUse use, int count)
    {
        if (_near.TryGetValue((home, use), out var near)) return near;
        var at = Places[home].At;
        return _near[(home, use)] = Of(use).OrderBy(i => (Places[i].At - at).Length).Take(count).ToArray();
    }
}

public static class CrowdRoster
{
    public const double WalkM = 1.3;
    const double WalkPlay = 0.2, PerHouseholdM2 = 120, MostHouseholds = 3, Lots = (double)Population.Most / Population.Usual;
    const double MarketReachM = 18, WellReachM = 4, FieldReachM = 25, SquareReachM = 10, ChildShare = 0.45, ElderShare = 0.25, YouthShare = 0.3, MostChildren = 4;
    const int BarracksSoldiers = 8, NearSlots = 6, ManStream = 21, Squad = 4, PerWatchman = 150, LeastWatch = 4, MostStreets = 240;
    const double StreetEveryM = 60, StreetReachM = 5, QuayReachM = 15;
    static readonly string[] Ways = ["street", "lane", "road", "chaussee", "boulevard", "track", "paving"];
    static readonly string[] Wharves = ["quay", "jetty", "mole", "slip", "dockshed", "dockcrane", "wharf"];

    static readonly string[] Worships = ["church", "chapel", "mosque", "temple", "minster", "cloister", "friary", "basilica", "pagoda", "shrine", "madrasa", "rotunda"];
    static readonly string[] Soldiers = ["barracks", "arsenal", "magazine", "keep", "guardhouse"];
    static readonly string[] Taverns = ["tavern", "inn", "coffeehouse", "teahouse", "taberna", "han", "khan", "publichouse"];
    static readonly string[] Shops = ["shop", "markethall", "bedesten", "warehouse", "customs", "bank"];
    static readonly string[] Farms = ["mill", "watermill", "windmill", "barn", "granary", "stable", "farmhouse"];
    static readonly string[] Works = ["factory", "works", "gasholder", "powerhouse", "station", "goodsshed", "dockshed", "brickworks", "kiln", "foundry", "sawmill"];
    static readonly string[] Crafts = ["bakery", "butcher", "chandler", "potter", "weaver", "cooper", "wheelwright", "fuller", "dyer", "tanner", "brewer", "smithy", "forge"];
    static readonly string[] Marts = ["square", "forum", "market", "marketplace"];
    static readonly string[] Greens = ["green", "churchyard", "parade", "yard"];
    static readonly string[] Fieldwork = ["field", "fields", "paddies", "vineyard", "orchard", "meadow", "threshing", "pen", "plantation", "grove"];
    static readonly string[] Wells = ["well", "fountain", "pump", "cistern"];

    public static List<CrowdTown> Towns(GameState g, Func<string, Nationality?> people, long now)
    {
        double upm = g.Calibration.UnitsPerMeter ?? 0;
        var towns = new List<CrowdTown>();
        if (upm <= 0) return towns;
        foreach (var thing in g.Built.Where(b => b.Kind == BuiltKinds.Settlement))
            if (g.Sheets.FirstOrDefault(s => s.Id == thing.Sheet) is { } sheet && people(NationalityOf(thing, sheet, g)) is { } flat)
                towns.Add(Town(thing, sheet, upm, flat, now));
        return towns;
    }

    public static string NationalityOf(BuiltThing thing, SheetDef sheet, GameState g)
    {
        int age = Manners.Age(g.Lighting.MapStyle), manner = Manners.For(age, Manners.Of(thing));
        string culture = Manners.Id(manner);
        return Nationalities.All.FirstOrDefault(n => n.Culture == culture)?.Id
            ?? Nationalities.All.FirstOrDefault(n => Manners.All.Any(m => m.Id == n.Culture && m.Kin == Manners.Kin(manner) && m.Epoch == age))?.Id
            ?? Nationalities.All.FirstOrDefault(n => Manners.All.Any(m => m.Id == n.Culture && m.Epoch == age))?.Id
            ?? B.Id;
    }

    public static CrowdTown Town(BuiltThing thing, SheetDef sheet, double upm, Nationality flat, long now)
    {
        var places = Places(thing, sheet, upm);
        var dice = new Dice(thing.Seed, ManStream);
        var residents = new List<CrowdResident>();
        int id = 0;
        for (int h = 0; h < places.Count; h++)
        {
            var home = places[h];
            if (home.Use is not (CrowdUse.Home or CrowdUse.Barracks)) continue;
            int households = home.Use == CrowdUse.Barracks ? 1 : (int)Math.Round(Math.Clamp(home.Room / PerHouseholdM2, 1, MostHouseholds) * Lots);
            string wealth = WealthOf(thing.Parts[home.Part]);
            for (int k = 0; k < households; k++)
            {
                double rank = dice.Next();
                foreach (var (sex, band, calling) in Household(home, dice))
                {
                    int seed = unchecked((int)(dice.Next() * int.MaxValue));
                    var entry = CrowdDress.Person(flat, id++, seed, now, sex, band, calling);
                    if (calling != B.Soldier) entry.Wealth = wealth;
                    entry.Household = h * (int)(MostHouseholds * Lots + 1) + k;
                    residents.Add(new CrowdResident { Entry = entry, Home = h, Part = home.Part, Rank = rank, SpeedM = WalkM * dice.Between(1 - WalkPlay, 1 + WalkPlay) });
                }
            }
        }
        return new CrowdTown
        {
            Thing = thing.Id, People = flat, Coat = CrowdDress.ArmyCoat(flat, thing.Seed), Places = places,
            Residents = Squads(Watch(places, Employ(places, residents, PlaceZones.FarmingOf(thing, upm), new Dice(thing.Seed, ManStream + 1)))),
        };
    }

    static List<CrowdResident> Watch(List<CrowdPlace> places, List<CrowdResident> people)
    {
        if (people.Any(p => p.Entry.Calling == B.Soldier)) return people;
        int post = places.FindIndex(p => p.Use == CrowdUse.Gate);
        if (post < 0) post = places.FindIndex(p => p.Use == CrowdUse.Square);
        if (post < 0) post = places.FindIndex(p => p.Use == CrowdUse.Market);
        if (post < 0) return people;
        int watch = Math.Max(LeastWatch, people.Count(p => p.Rank < Population.Share(Population.Usual)) / PerWatchman);
        var picked = people.Where(p => p.Entry.Sex == E.Man && p.Entry.Band == E.Adult && p.Entry.Calling == B.Labourer)
            .OrderBy(p => p.Rank).ThenBy(p => p.Entry.Id).Take(watch).Select(p => p.Entry.Id).ToHashSet();
        return people.Select(p =>
        {
            if (!picked.Contains(p.Entry.Id)) return p;
            p.Entry.Calling = B.Soldier;
            return new CrowdResident { Entry = p.Entry, Home = p.Home, Part = p.Part, Rank = p.Rank, Work = post, SpeedM = p.SpeedM };
        }).ToList();
    }

    static List<CrowdResident> Squads(List<CrowdResident> people)
    {
        var lead = new Dictionary<int, (int Leader, int File, double Speed)>();
        foreach (var post in people.Where(p => p.Entry.Calling == B.Soldier).GroupBy(p => p.Work))
        {
            var men = post.OrderBy(p => p.Rank).ThenBy(p => p.Entry.Id).ToList();
            for (int i = 0; i < men.Count; i++)
            {
                var head = men[i - i % Squad];
                if (i % Squad != 0) lead[men[i].Entry.Id] = (head.Entry.Id, i % Squad, head.SpeedM);
            }
        }
        return people.Select(p => lead.TryGetValue(p.Entry.Id, out var l)
            ? new CrowdResident { Entry = p.Entry, Home = p.Home, Part = p.Part, Rank = p.Rank, Work = p.Work, SpeedM = l.Speed, Leader = l.Leader, File = l.File }
            : p).ToList();
    }

    static string WealthOf(BuiltPart part) => PlaceZones.WealthOf(part) switch { WealthLevel.Poor => E.Poor, WealthLevel.Rich => E.Rich, _ => E.Middle };

    static IEnumerable<(string Sex, string Band, string? Calling)> Household(CrowdPlace home, Dice dice)
    {
        if (home.Use == CrowdUse.Barracks)
        {
            for (int i = 0; i < BarracksSoldiers; i++) yield return (E.Man, E.Adult, B.Soldier);
            yield break;
        }
        yield return (E.Man, dice.Chance(ElderShare) ? E.Elder : E.Adult, null);
        yield return (E.Woman, E.Adult, null);
        int children = dice.Below((int)MostChildren + 1);
        for (int i = 0; i < children; i++) yield return (dice.Chance(0.5) ? E.Man : E.Woman, dice.Chance(YouthShare) ? E.Youth : E.Child, null);
        if (dice.Chance(ChildShare * ElderShare)) yield return (dice.Chance(0.5) ? E.Man : E.Woman, E.Elder, null);
    }

    static List<CrowdResident> Employ(List<CrowdPlace> places, List<CrowdResident> people, double farming, Dice dice)
    {
        var room = places.Select(p => p.Use is CrowdUse.Work or CrowdUse.Worship or CrowdUse.Tavern or CrowdUse.Gate or CrowdUse.Quay ? (int)Math.Round(p.Room * Lots) : 0).ToArray();
        var fields = places.Select((p, i) => (p, i)).Where(x => x.p.Use == CrowdUse.Fields).Select(x => x.i).ToList();
        var wells = places.Select((p, i) => (p, i)).Where(x => x.p.Use == CrowdUse.Well).Select(x => x.i).ToList();
        var placed = new List<CrowdResident>(people.Count);
        foreach (var r in people.OrderBy(p => p.Rank).ThenBy(p => p.Entry.Id))
        {
            var e = r.Entry;
            int work = -1;
            if (places[r.Home].Use == CrowdUse.Barracks) work = r.Home;
            else if (e.Sex == E.Man && e.Band is E.Adult or E.Youth)
            {
                var at = places[r.Home].At;
                work = Enumerable.Range(0, places.Count).Where(i => room[i] > 0).OrderBy(i => (places[i].At - at).Length).Take(NearSlots).Select(i => (int?)i).FirstOrDefault() ?? -1;
                if (work >= 0 && e.Band == E.Adult)
                {
                    room[work]--;
                    e.Calling = places[work].Calling ?? B.Labourer;
                }
                else if (fields.Count > 0 && dice.Chance(Math.Max(farming, 0.3)))
                {
                    work = fields.MinBy(f => (places[f].At - at).Length);
                    e.Calling = B.Farmer;
                }
                else e.Calling = B.Labourer;
            }
            else if (e.Calling == B.Soldier) e.Calling = B.Labourer;
            else if (e.Calling == B.Washer && wells.Count > 0) work = wells.MinBy(w => (places[w].At - places[r.Home].At).Length);
            placed.Add(work == r.Work ? r : new CrowdResident { Entry = e, Home = r.Home, Part = r.Part, Rank = r.Rank, Work = work, SpeedM = r.SpeedM });
        }
        return placed.OrderBy(p => p.Entry.Id).ToList();
    }

    static double Footprint(BuiltPart part)
    {
        var p = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        double twice = 0;
        for (int i = 0; i < p.Count; i++) twice += p[i].X * p[(i + 1) % p.Count].Y - p[(i + 1) % p.Count].X * p[i].Y;
        return Math.Abs(twice) / 2;
    }

    static List<CrowdPlace> Places(BuiltThing thing, SheetDef sheet, double upm)
    {
        var places = new List<CrowdPlace>();
        for (int i = 0; i < thing.Parts.Count; i++)
        {
            var part = thing.Parts[i];
            if (part.At.Count < 2) continue;
            var (mid, reach) = BuiltSite.Round(part);
            var at = BuiltGeometry.ToWorld(sheet, mid.Stored);
            double reachM = reach / upm, m2 = Footprint(part) / (upm * upm);
            string cls = part.Class;
            if (part.Kind == PartKinds.Block)
            {
                if (Soldiers.Contains(cls)) places.Add(new(i, cls, at, cls == "barracks" ? CrowdUse.Barracks : CrowdUse.Work, false, 0, B.Soldier, 6));
                else if (cls == "gatehouse") places.Add(new(i, cls, at, CrowdUse.Gate, true, StreetReachM, B.Soldier, 4));
                else if (Wharves.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Quay, true, QuayReachM, B.Labourer, 10));
                else if (Worships.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Worship, false, 0, B.Cleric, 2));
                else if (Taverns.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Tavern, false, 0, B.Merchant, 1));
                else if (Shops.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Work, false, 0, B.Merchant, 2));
                else if (Crafts.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Work, false, 0, cls is "smithy" or "forge" ? B.Smith : B.Labourer, 2));
                else if (Farms.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Work, false, 0, B.Farmer, 3));
                else if (Works.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Work, false, 0, B.Labourer, 12));
                if (Brushed.Home(cls)) places.Add(new(i, cls, at, CrowdUse.Home, false, 0, null, (int)Math.Max(m2, 1)));
            }
            else if (part.Kind == PartKinds.Area)
            {
                if (Marts.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Market, true, Math.Min(reachM * 0.6, MarketReachM), null, 0));
                else if (Greens.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Square, true, Math.Min(reachM * 0.6, SquareReachM), null, 0));
                else if (Fieldwork.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Fields, true, Math.Min(reachM * 0.7, FieldReachM), null, 0));
            }
            else if (part.Kind == PartKinds.Sign && Wells.Contains(cls)) places.Add(new(i, cls, at, CrowdUse.Well, true, WellReachM, null, 0));
            else if (part.Kind == PartKinds.Line && Ways.Contains(cls)) Streets(places, part, i, sheet, upm);
        }
        return places;
    }

    static void Streets(List<CrowdPlace> places, BuiltPart part, int index, SheetDef sheet, double upm)
    {
        var line = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        double every = StreetEveryM * upm, left = every / 2;
        for (int k = 1; k < line.Count && places.Count(p => p.Use == CrowdUse.Street) < MostStreets; k++)
        {
            var a = line[k - 1];
            var d = line[k] - a;
            double run = d.Length;
            for (; left <= run; left += every)
            {
                var at = a + d * (left / run);
                places.Add(new(index, part.Class, BuiltGeometry.ToWorld(sheet, at.Stored), CrowdUse.Street, true, StreetReachM, null, 0));
            }
            left -= run;
        }
    }
}
