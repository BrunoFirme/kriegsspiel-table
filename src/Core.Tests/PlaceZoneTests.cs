using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class PlaceZoneTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 9;
    const int Seed = 4;
    static readonly Spot Middle = new(30, 30);
    static readonly SheetDef Sheet = new() { Id = "s", Name = "Ground", Size = new Size2(Side, Side) };

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1, Manner = manner };

    static BuiltThing Town(int manner = Manners.Gothic, Settlements.Shape? shape = null)
    {
        var town = Settlements.Lay(Site(manner), Sheet.Id, Middle, Reach, Seed, 1, 0, 0, false, null, shape);
        town.Id = "t1";
        return town;
    }

    static GameState Table(BuiltThing? town = null, string style = MapStyles.Medieval, string time = "1757-12-05T13:00")
    {
        var g = new GameState { Clock = GameClock.At(time), Lighting = new Lighting { MapStyle = style }, Calibration = new Calibration { UnitsPerMeter = 1 / MetresToTheUnit } };
        g.Sheets.Add(Json.Clone(Sheet));
        if (town != null) g.Built.Add(town);
        return g;
    }

    static PieceDef? NoKinds(string id) => null;

    static Vec2 World(Spot at) => BuiltGeometry.ToWorld(Sheet, at.Stored);

    static int HouseNearest(BuiltThing town, Spot to, bool farthest = false)
    {
        var houses = Enumerable.Range(0, town.Parts.Count).Where(i => town.Parts[i].Class == "house" && Looks.Takes(town.Parts[i])).ToList();
        return farthest ? houses.MaxBy(i => (BuiltSite.Round(town.Parts[i]).At - to).Length) : houses.MinBy(i => (BuiltSite.Round(town.Parts[i]).At - to).Length);
    }

    static PlaceZone ZoneOf(GameState g, BuiltThing town, int part) => PlaceZones.At(g, NoKinds, World(BuiltSite.Round(town.Parts[part]).At))!;

    [Fact]
    public void An_untouched_town_reads_the_generator_s_own_values()
    {
        var town = Town();
        var g = Table(town);
        int house = HouseNearest(town, Middle);
        var zone = ZoneOf(g, town, house);
        Assert.Equal(("t1", house), (zone.Thing, zone.Part));
        Assert.Equal(new ZoneCulture(Manners.Medieval, Manners.Gothic, Manners.Gothic, 1757), zone.Culture);
        Assert.Equal(ZoneKind.Centre, zone.Kind);
        Assert.Equal(ZoneKind.Edge, ZoneOf(g, town, HouseNearest(town, Middle, true)).Kind);
        Assert.Equal(WealthLevel.Middling, zone.Wealth);
        double farming = Ways.Of(Manners.Gothic).Farming(Ways.Town(Reach * MetresToTheUnit, 1));
        Assert.Equal(farming, zone.Living.Farming, 9);
        Assert.False(town.Settings.ContainsKey(Settlements.Farming));
        Assert.Equal(new ZoneHold(TownState.Peace, 0, Manners.Gothic, null), zone.Hold);
        Assert.Equal(13, zone.Time.Hour, 9);
        Assert.Null(zone.Time.Clock);
    }

    [Fact]
    public void A_spot_with_nothing_built_has_no_zone_and_a_plain_sheet_none_either()
    {
        Assert.Null(PlaceZones.At(Table(), NoKinds, World(Middle)));
        var g = Table(Town());
        Assert.Null(PlaceZones.At(g, NoKinds, World(new Spot(1, 1))));
        Assert.Null(PlaceZones.At(g, NoKinds, new Vec2(500, 500)));
        Assert.NotNull(PlaceZones.At(g, NoKinds, World(Middle)));
    }

    [Fact]
    public void What_a_town_lives_on_comes_from_its_parts_and_the_farming_share_from_its_setting_when_set()
    {
        var town = Town();
        var zone = PlaceZones.At(Table(town), NoKinds, World(Middle))!;
        var classes = town.Parts.Select(p => p.Class).ToHashSet();
        Assert.All(zone.Living.Trades, trade => Assert.Contains(trade, classes));
        Assert.Equal(classes.Contains("barracks") || classes.Contains("keep"), zone.Living.Garrison);
        var farmed = Town(shape: new Settlements.Shape(Farming: 0.9));
        Assert.Equal(0.9, farmed.Settings[Settlements.Farming]);
        Assert.Equal(0.9, PlaceZones.At(Table(farmed), NoKinds, World(Middle))!.Living.Farming, 9);
        Assert.Equal(0.9, Settlements.Shape.Of(farmed).Farming);
        Assert.Equal(1, PlaceZones.FarmingOf(new BuiltThing { Kind = BuiltKinds.Building }, 1));
    }

    [Fact]
    public void Each_override_is_read_back_and_one_that_matches_the_generator_is_not_stored()
    {
        var town = Town();
        int house = HouseNearest(town, Middle);
        var set = ZoneOverrides.WithCulture(town, Sheet, [house], "saracen");
        set = ZoneOverrides.WithWealth(set, [house], WealthLevel.Rich);
        set = ZoneOverrides.WithWar(set, true);
        set = ZoneOverrides.WithHolder(set, Manners.Rus, PlaceZones.CoatOf("#2a6ebb"));
        var zone = ZoneOf(Table(set), set, house);
        Assert.Equal((Manners.Gothic, Manners.Saracen), (zone.Culture.Town, zone.Culture.Own));
        Assert.Equal(WealthLevel.Rich, zone.Wealth);
        Assert.Equal(new ZoneHold(TownState.War, 0, Manners.Rus, "#2a6ebb"), zone.Hold);
        Assert.True(set.Parts[house].Kept);
        var back =
            ZoneOverrides.WithHolder(ZoneOverrides.WithWar(ZoneOverrides.WithWealth(ZoneOverrides.WithCulture(set, Sheet, [house], "gothic"), [house], WealthLevel.Middling), false), Manners.Gothic, null);
        Assert.Null(back.Parts[house].Culture);
        Assert.Null(back.Parts[house].Wealth);
        Assert.DoesNotContain(back.Settings.Keys, ZoneOverrides.TownKeys.Contains);
        var (was, now) = (ZoneOf(Table(town), town, house), ZoneOf(Table(back), back, house));
        Assert.Equal((was.Culture, was.Kind, was.Wealth, was.Hold, was.Living.Farming), (now.Culture, now.Kind, now.Wealth, now.Hold, now.Living.Farming));
    }

    [Fact]
    public void A_building_of_another_culture_keeps_its_own_and_a_ward_house_follows_its_quarter()
    {
        var town = Town();
        new Manners.Mix(Manners.Gothic, Manners.Gothic, Manners.Gothic, Manners.Rus, Manners.MostWard).Onto(town);
        var g = Table(town);
        var warded = Manners.WardOf(town, Side, Side);
        var houses = Enumerable.Range(0, town.Parts.Count).Where(i => town.Parts[i].Class == "house" && Looks.Takes(town.Parts[i])).ToList();
        Assert.Contains(houses, i => warded(town.Parts[i]));
        Assert.Contains(houses, i => !warded(town.Parts[i]));
        Assert.All(houses.Take(30), i => Assert.Equal(warded(town.Parts[i]) ? Manners.Rus : Manners.Gothic, ZoneOf(g, town, i).Culture.Own));
        int wardHouse = houses.First(i => warded(town.Parts[i]));
        var gothic = ZoneOverrides.WithCulture(town, Sheet, [wardHouse], "gothic");
        Assert.Equal(("gothic", Manners.Gothic), (gothic.Parts[wardHouse].Culture, ZoneOf(Table(gothic), gothic, wardHouse).Culture.Own));
        Assert.Null(ZoneOverrides.WithCulture(town, Sheet, [wardHouse], "rus").Parts[wardHouse].Culture);
        var later = Table(town, MapStyles.Survey);
        Assert.Equal((Manners.Later, Manners.Gothic), (ZoneOf(later, town, houses[0]).Culture.Age, ZoneOf(later, town, houses[0]).Culture.Town));
    }

    [Fact]
    public void The_kind_of_day_follows_the_week_day_and_the_religion_of_the_culture()
    {
        Assert.Equal(PlaceZones.Sunday, PlaceZones.HolyDay(Manners.Gothic));
        Assert.Equal(PlaceZones.Friday, PlaceZones.HolyDay(Manners.Saracen));
        Assert.Null(PlaceZones.HolyDay(Manners.Roman));
        var sunday = Table(Town(), time: "1757-12-04T10:00");
        Assert.Equal("Sunday", sunday.Clock.Cal.WeekDay(sunday.Clock.Now));
        Assert.Equal(DayKind.Holy, PlaceZones.At(sunday, NoKinds, World(Middle))!.Time.Day);
        var friday = Table(Town(), time: "1757-12-09T10:00");
        Assert.Equal("Friday", friday.Clock.Cal.WeekDay(friday.Clock.Now));
        Assert.Equal(DayKind.Working, PlaceZones.At(friday, NoKinds, World(Middle))!.Time.Day);
        var suq = Table(Town(Manners.Saracen), time: "1757-12-09T10:00");
        Assert.Equal((DayKind.Holy, PlaceZones.Friday), (PlaceZones.At(suq, NoKinds, World(Middle))!.Time.Day, PlaceZones.At(suq, NoKinds, World(Middle))!.Time.HolyDay));
        var roman = Table(Town(Manners.Roman), MapStyles.Ancient, "1757-12-04T10:00");
        Assert.Equal(DayKind.Working, PlaceZones.At(roman, NoKinds, World(Middle))!.Time.Day);
    }

    [Fact]
    public void The_season_turns_at_the_solstices_and_equinoxes_and_the_south_has_the_other()
    {
        Assert.Equal(new[] { Season.Summer, Season.Autumn, Season.Winter, Season.Spring }, new[] { 0.01, 0.3, 0.55, 0.8 }.Select(turn => PlaceZones.SeasonOf(turn, false)));
        Assert.Equal(Season.Winter, PlaceZones.SeasonOf(0.01, true));
        Assert.Equal(Season.Winter, PlaceZones.At(Table(Town(), time: "1757-12-25T12:00"), NoKinds, World(Middle))!.Time.Season);
        Assert.Equal(Season.Summer, PlaceZones.At(Table(Town(), time: "1757-07-01T12:00"), NoKinds, World(Middle))!.Time.Season);
        var south = Table(Town(), time: "1757-07-01T12:00");
        south.Lighting.Latitude = -35;
        Assert.Equal(Season.Winter, PlaceZones.At(south, NoKinds, World(Middle))!.Time.Season);
    }

    [Fact]
    public void A_place_with_its_own_clock_is_read_by_that_clock()
    {
        var g = Table(Town(), time: "1757-12-09T10:00");
        Assert.Null(StateMachine.Apply(g, new SetClockApart { Place = Sheet.Id, Clock = new ClockTime { Seconds = CalendarDef.Gregorian.Read("1757-12-04T18:30")!.Value } }, out _));
        var zone = PlaceZones.At(g, NoKinds, World(Middle))!;
        Assert.Equal((Sheet.Id, DayKind.Holy), (zone.Time.Clock, zone.Time.Day));
        Assert.Equal(18.5, zone.Time.Hour, 9);
    }

    [Fact]
    public void Only_a_new_or_changed_override_counts_as_setting_a_place()
    {
        var town = Town();
        int house = HouseNearest(town, Middle);
        Assert.False(ZoneOverrides.Introduced(town, BuiltGeometry.Moved(town, 5, 5)));
        Assert.False(ZoneOverrides.Introduced(null, town));
        var own = ZoneOverrides.WithCulture(town, Sheet, [house], "saracen");
        Assert.True(ZoneOverrides.Introduced(town, own));
        Assert.False(ZoneOverrides.Introduced(own, BuiltThings.WithPartMoved(own, house, 900, 0)));
        Assert.False(ZoneOverrides.Introduced(own, town));
        var war = ZoneOverrides.WithWar(town, true);
        Assert.True(ZoneOverrides.Introduced(town, war));
        Assert.True(ZoneOverrides.Introduced(war, town));
        Assert.True(ZoneOverrides.Introduced(null, war));
    }

    [Fact]
    public void The_wealth_brush_steps_the_houses_under_it_and_stores_only_what_differs_from_the_generator()
    {
        var town = Town();
        int house = HouseNearest(town, Middle);
        var at = BuiltSite.Round(town.Parts[house]).At;
        var richer = ZoneOverrides.Brushed(town, [at], 0.01, true)!;
        Assert.Equal("rich", richer.Parts[house].Wealth);
        Assert.Null(ZoneOverrides.Brushed(richer, [at], 0.01, true));
        var back = ZoneOverrides.Brushed(richer, [at], 0.01, false)!;
        Assert.Null(back.Parts[house].Wealth);
        Assert.Equal(WealthLevel.Poor, PlaceZones.WealthOf(ZoneOverrides.Brushed(back, [at], 0.01, false)!.Parts[house]));
        Assert.Null(ZoneOverrides.Brushed(town, [new Spot(1, 1)], 0.01, true));
        Assert.Equal(WealthLevel.Rich, PlaceZones.Laid(new BuiltPart { Class = "townhouse" }));
        Assert.Equal(WealthLevel.Poor, PlaceZones.Laid(new BuiltPart { Class = "cottage" }));
    }
}
