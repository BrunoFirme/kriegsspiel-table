namespace KTable.Core.Model;

public enum ZoneKind { Centre, Market, Edge, Country, Works, Military }

public enum WealthLevel { Poor, Middling, Rich }

public enum DayKind { Working, Market, Holy, Curfew }

public enum Season { Spring, Summer, Autumn, Winter }

public enum TownState { Peace, War }

public sealed record ZoneCulture(int Age, int Town, int Own, long Year);

public sealed record ZoneLiving(double Farming, IReadOnlyList<string> Trades, bool Industry, bool Docks, bool Garrison);

public sealed record ZoneTime(string? Clock, double Hour, string? WeekDay, DayKind Day, string? HolyDay, Season Season);

public sealed record ZoneHold(TownState State, double Ravaged, int HeldBy, string? Coat);

public sealed record PlaceZone(string Thing, int Part, ZoneCulture Culture, ZoneKind Kind, WealthLevel Wealth, ZoneLiving Living, ZoneTime Time, ZoneHold Hold);

public static class PlaceZones
{
    public const string War = "war", HeldBy = "heldby", Coat = "coat";
    public const string Sunday = "Sunday", Friday = "Friday";
    const double NearM = 3, Untilled = 1, DefaultHouses = 0.5;
    const int InBlock = 0, InArea = 1, OnLine = 2, InQuarter = 3, Seasons = 4, Opposite = 2;

    static readonly string[] RichClasses = [Callings.TownHouse, "domus"], PoorClasses = [Callings.Cottage, "insula"];
    static readonly string[] Defences = ["gatehouse", "wall", "rampart", "fosse", "glacis", "drymoat", "moat", "trench", "wire", "parade", "tower", "keep", "barracks", "arsenal", "magazine"];
    static readonly string[] Yards = ["railway", "station", "goodsshed", "dockshed", "dockcrane", "quay", "mole", "jetty", "slip", "customs", "warehouse"];
    static readonly string[] Marts = ["markethall", "bedesten", "square", "forum", "marketcross"];
    static readonly string[] Mills = ["factory", "gasholder", "powerhouse", "coolingtower", "elevator", "goodsshed"];
    static readonly string[] Wharves = ["quay", "mole", "jetty", "slip", "dockshed", "dockcrane", "customs"];
    static readonly string[] Soldiers = ["barracks", "arsenal", "magazine", "parade", "keep"];
    static readonly string[] Crafts = ["shop", "bakery", "tavern", "cooper", "wheelwright", "potter", "weaver", "butcher", "chandler", "taberna", "fuller", "smithy", "brewer"];
    static readonly Lazy<Dictionary<string, string>> Sections = new(() => Catalogue.Sections.SelectMany(s => s.Entries.Select(e => (e.Laid, s.Id)))
        .DistinctBy(e => e.Laid).ToDictionary(e => e.Laid, e => e.Id));

    static string? Section(string cls) => Sections.Value.GetValueOrDefault(cls);

    sealed record Found(BuiltThing Thing, SheetDef Sheet, Spot At, int Part, int Rank, double Span);

    public static PlaceZone? At(GameState g, Func<string, PieceDef?> def, Vec2 point)
    {
        if (Under(g, point) is not { } found) return null;
        var thing = found.Thing;
        var part = found.Part >= 0 ? thing.Parts[found.Part] : null;
        var culture = CultureOf(g, found, part);
        string? place = PlaceClocks.At(g, def, point);
        var clock = g.Clock.AsOf(place);
        return new PlaceZone(thing.Id, found.Part, culture with { Year = clock.Cal.Parts(clock.Now).Year }, KindOf(thing, part, found.At), WealthAt(thing, part, found.At),
            LivingOf(thing, PerMetre(g, thing)), TimeOf(clock, place, culture.Own, g.Lighting.Latitude < 0), HoldOf(thing, culture));
    }

    static double PerMetre(GameState g, BuiltThing thing) => thing.Setting(BuiltThings.Scale, g.Calibration.UnitsPerMeter ?? 0);

    static Found? Under(GameState g, Vec2 point)
    {
        Found? best = null;
        foreach (var thing in g.Built.Where(t => t.Kind != BuiltKinds.Water))
        {
            if (g.Sheets.FirstOrDefault(s => s.Id == thing.Sheet) is not { } sheet || Ground.Uv(sheet, point) == null) continue;
            if (Within(thing, sheet, Spot.Of(BuiltGeometry.ToSheet(sheet, point)), PerMetre(g, thing)) is not { } found) continue;
            if (best == null || found.Rank < best.Rank || found.Rank == best.Rank && found.Span < best.Span) best = found;
        }
        return best;
    }

    static Found? Within(BuiltThing thing, SheetDef sheet, Spot at, double perMetre)
    {
        int area = -1, line = -1;
        double nearest = Math.Max(0, perMetre) * NearM, span = BuiltThings.Round(thing).Reach;
        for (int i = 0; i < thing.Parts.Count; i++)
        {
            var part = thing.Parts[i];
            if (part.Class == BuiltThings.Quarter) continue;
            double reach = BuiltThings.Reach(part, at);
            if (part.Kind == PartKinds.Block && reach <= 0) return new(thing, sheet, at, i, InBlock, span);
            if (part.Kind == PartKinds.Area && reach <= 0 && area < 0) area = i;
            else if (part.Kind is PartKinds.Line or PartKinds.Sign && reach <= nearest)
            {
                nearest = reach;
                line = i;
            }
        }
        if (area >= 0) return new(thing, sheet, at, area, InArea, span);
        if (line >= 0) return new(thing, sheet, at, line, OnLine, span);
        return BuiltThings.StandsOver(thing, at) ? new(thing, sheet, at, -1, InQuarter, span) : null;
    }

    static ZoneCulture CultureOf(GameState g, Found found, BuiltPart? part)
    {
        int age = Manners.Age(g.Lighting.MapStyle), town = Manners.For(age, Manners.Of(found.Thing));
        int own = part == null ? town : Manners.For(age, CultureOf(found.Thing, found.Sheet, part));
        return new ZoneCulture(age, town, own, 0);
    }

    public static int LaidCulture(BuiltThing thing, SheetDef sheet, BuiltPart part) =>
        Manners.WardOf(thing, Ground.FullWidth(sheet), BuiltGeometry.FullHeight(sheet))(part) ? Manners.MixOf(thing).Ward : Manners.Of(thing);

    public static int CultureOf(BuiltThing thing, SheetDef sheet, BuiltPart part) =>
        ZoneOverrides.CultureId(part.Culture) is { } id ? Manners.Of(id) : LaidCulture(thing, sheet, part);

    static ZoneKind KindOf(BuiltThing thing, BuiltPart? part, Spot at)
    {
        if (thing.Kind == BuiltKinds.Fort) return ZoneKind.Military;
        if (part?.Class is { } cls)
        {
            string? section = Section(cls);
            if (Defences.Contains(cls) || section == "arms") return ZoneKind.Military;
            if (Yards.Contains(cls) || section == "works") return ZoneKind.Works;
            if (Marts.Contains(cls) || section == "market") return ZoneKind.Market;
            if (section == "farm") return ZoneKind.Country;
        }
        if (thing.Kind != BuiltKinds.Settlement || thing.Parts.FirstOrDefault(p => p.Class == BuiltThings.Quarter) is not { } quarter) return ZoneKind.Country;
        if (!BuiltThings.StandsOver(thing, at)) return ZoneKind.Edge;
        var (middle, reach) = BuiltSite.Round(quarter);
        var core = thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is "square" or "forum").Select(p => (Spot?)BuiltSite.Round(p).At).FirstOrDefault() ?? middle;
        return (at - core).Length < reach / Settlements.QuarterAt * Settlements.HardWithin ? ZoneKind.Centre : ZoneKind.Edge;
    }

    public static WealthLevel? Level(string? stored) => Enum.TryParse(stored, true, out WealthLevel level) && Enum.IsDefined(level) ? level : null;

    public static string Stored(WealthLevel level) => level.ToString().ToLowerInvariant();

    public static WealthLevel Laid(BuiltPart part) =>
        RichClasses.Contains(part.Class) ? WealthLevel.Rich : PoorClasses.Contains(part.Class) ? WealthLevel.Poor : WealthLevel.Middling;

    public static WealthLevel WealthOf(BuiltPart part) => Level(part.Wealth) ?? Laid(part);

    static WealthLevel WealthAt(BuiltThing thing, BuiltPart? part, Spot at)
    {
        if (part != null && Looks.Takes(part)) return WealthOf(part);
        var home = thing.Parts.Where(Looks.Takes).MinBy(p => BuiltThings.Reach(p, at));
        return home == null ? WealthLevel.Middling : WealthOf(home);
    }

    public static double FarmingOf(BuiltThing thing, double perMetre)
    {
        if (thing.Settings.TryGetValue(Settlements.Farming, out double set)) return Math.Clamp(set, 0, 1);
        if (thing.Kind != BuiltKinds.Settlement) return Untilled;
        int manner = Manners.Of(thing);
        double radiusM = perMetre > 0 ? BuiltThings.Round(thing).Reach / perMetre : 0;
        return Ways.Of(manner).Farming(Ways.Town(radiusM, thing.Setting(Settlements.Houses, DefaultHouses), Manners.Named(manner).Epoch == Manners.Modern));
    }

    static ZoneLiving LivingOf(BuiltThing thing, double perMetre)
    {
        var classes = thing.Parts.Select(p => p.Class).ToHashSet();
        var trades = classes.Where(c => Crafts.Contains(c) || Section(c) == "trade" && thing.Parts.Any(p => p.Class == c && p.Kind == PartKinds.Block)).Order(StringComparer.Ordinal).ToList();
        return new ZoneLiving(FarmingOf(thing, perMetre), trades, classes.Overlaps(Mills), classes.Overlaps(Wharves), thing.Kind == BuiltKinds.Fort || classes.Overlaps(Soldiers));
    }

    public static string? HolyDay(int manner) => Manners.Crossed(manner) ? Sunday : manner is Manners.Ottoman or Manners.Saracen or Manners.NorthAfrican ? Friday : null;

    public static Season SeasonOf(double yearTurn, bool south)
    {
        int quarter = (int)Math.Floor((yearTurn - Math.Floor(yearTurn)) * Seasons) % Seasons;
        var season = quarter switch { 0 => Season.Summer, 1 => Season.Autumn, 2 => Season.Winter, _ => Season.Spring };
        return south ? (Season)(((int)season + Opposite) % Seasons) : season;
    }

    static ZoneTime TimeOf(GameClock clock, string? place, int culture, bool south)
    {
        var cal = clock.Cal;
        long now = clock.Now;
        string? week = cal.WeekDay(now), holy = HolyDay(culture);
        var day = holy != null && string.Equals(week, holy, StringComparison.OrdinalIgnoreCase) ? DayKind.Holy : DayKind.Working;
        return new ZoneTime(place, cal.HourOf(now), week, day, holy, SeasonOf(cal.YearTurn(now), south));
    }

    public static string CoatText(double stored) => "#" + ((int)stored & 0xFFFFFF).ToString("x6", System.Globalization.CultureInfo.InvariantCulture);

    public static double? CoatOf(string? hex) =>
        hex?.Trim().TrimStart('#') is { Length: 6 } digits
            && int.TryParse(digits, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int rgb) ? rgb : null;

    static ZoneHold HoldOf(BuiltThing thing, ZoneCulture culture)
    {
        var state = thing.Setting(War, 0) >= 1 ? TownState.War : TownState.Peace;
        int held = thing.Settings.TryGetValue(HeldBy, out double by) && Manners.All.Any(m => m.Number == (int)by) ? Manners.For(culture.Age, (int)by) : culture.Town;
        return new ZoneHold(state, thing.Setting(Settlements.Ravaged, 0), held, thing.Settings.TryGetValue(Coat, out double coat) ? CoatText(coat) : null);
    }
}
