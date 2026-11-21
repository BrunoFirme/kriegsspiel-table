using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class BuildingScaleTests
{
    const double Side = 2000, MetresToTheUnit = 25, By = 10, Radius = 6, Houses = 0.75, Rounding = 2e-4, Far = 1e5;
    const int Seed = 4;
    static readonly Spot Middle = new(1000, 1000);

    static BuiltSite Site(double scale, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = scale / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others, Manner = Manners.Western };

    static BuiltThing Stamped(BuiltThing thing, double scale)
    {
        BuiltThings.Stamp(thing, scale / MetresToTheUnit);
        thing.Id = "w-1";
        return thing;
    }

    static BuiltThing Town(double scale, double radius) =>
        Stamped(Settlements.Lay(Site(scale), "s", Middle, radius, Seed, Houses, 0, Plans.Value(Plans.Offered(Manners.Western)[0])), scale);

    static List<Spot> Spots(List<int> at) => BuiltGeometry.Points(at).Select(Spot.Of).ToList();

    static double Span(BuiltThing thing) => BuiltGeometry.Box(thing) is var (low, high) ? (high.X - low.X) / (double)BuiltGeometry.PerUnit : 0;

    [Fact]
    public void A_town_laid_again_at_ten_times_is_the_town_laid_there_and_keeps_its_streets_and_landmarks()
    {
        var small = Town(1, Radius);
        var grown = BuildingScales.Changed(Site(By), small);
        Assert.NotNull(grown);
        var fresh = Town(By, Radius * By);
        Assert.Equal(fresh.Parts.Select(p => Json.Serialize(p)), grown!.Parts.Select(p => Json.Serialize(p)));
        string[] landmarks = ["street", "church", "steeple", "square", "quarter", "insula", "townhouse", "inn", "well", "pump", "memorial"];
        int Count(BuiltThing t, string cls) => t.Parts.Count(p => p.Class == cls);
        foreach (var cls in landmarks) Assert.Equal(Count(small, cls), Count(grown, cls));
        Assert.InRange(Span(grown) / Span(small), By * 0.9, By * 1.1);
        Assert.Equal(By / MetresToTheUnit, grown.Setting(BuiltThings.Scale, 0), 9);
        Assert.Equal(BuiltGeometry.Points(small.Outline)[0], BuiltGeometry.Points(grown.Outline)[0]);
    }

    [Fact]
    public void A_road_keeps_its_course_and_only_widens()
    {
        var course = new List<Spot> { new(900, 1000), new(1100, 1000) };
        var road = Stamped(BuiltRoads.Lay(Site(1), "s", course, "road", false), 1);
        var wider = BuildingScales.Changed(Site(By), road);
        Assert.NotNull(wider);
        Assert.Equal(road.Outline, wider!.Outline);
        Assert.Equal(By / MetresToTheUnit, wider.Setting(BuiltThings.Scale, 0), 9);
        var line = road.Parts.First(p => p.Kind == PartKinds.Line);
        var widened = wider.Parts.First(p => p.Kind == PartKinds.Line);
        Assert.Equal(Spots(line.At).First(), Spots(widened.At).First());
    }

    [Fact]
    public void A_road_laid_without_a_stamp_is_taken_as_laid_at_the_scale_before()
    {
        var course = new List<Spot> { new(900, 1000), new(1100, 1000) };
        var road = BuiltRoads.Lay(Site(1), "s", course, "road", false);
        road.Id = "w-2";
        Assert.Null(BuildingScales.Changed(Site(By), road));
        var wider = BuildingScales.Changed(Site(By), road, 1 / MetresToTheUnit);
        Assert.NotNull(wider);
        Assert.Equal(road.Outline, wider!.Outline);
        Assert.Equal(By / MetresToTheUnit, wider.Setting(BuiltThings.Scale, 0), 9);
    }

    [Fact]
    public void A_lone_building_grows_about_its_spot()
    {
        var house = Stamped(BuiltThings.Building(Site(1), "s", "house", Middle, new Spot(1, 0)), 1);
        var grown = BuildingScales.Changed(Site(By), house);
        Assert.NotNull(grown);
        var before = Spots(house.Parts[0].At);
        var after = Spots(grown!.Parts[0].At);
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(Middle.X + (before[i].X - Middle.X) * By, after[i].X, Rounding * By);
            Assert.Equal(Middle.Y + (before[i].Y - Middle.Y) * By, after[i].Y, Rounding * By);
        }
    }

    [Fact]
    public void A_thing_at_the_scale_it_was_laid_at_is_left_alone()
    {
        var house = Stamped(BuiltThings.Building(Site(By), "s", "house", Middle, new Spot(1, 0)), By);
        Assert.Null(BuildingScales.Changed(Site(By), house));
    }

    [Fact]
    public void Growth_past_what_a_sheet_holds_is_refused_not_squashed()
    {
        var house = Stamped(BuiltThings.Building(Site(1), "s", "house", new Spot(Far / 10, Far / 10), new Spot(1, 0)), 1);
        house.Outline = BuiltGeometry.Flat([new SheetPoint(0, 0), BuiltGeometry.Points(house.Outline)[1]]);
        double huge = Lighting.MostBuildingScale / MetresToTheUnit;
        Assert.False(BuildingScales.Fits(house, huge));
        Assert.Null(BuildingScales.Changed(Site(Lighting.MostBuildingScale), house));
    }

    [Fact]
    public void The_scale_is_kept_within_its_range_and_life_size_writes_nothing()
    {
        var g = CoreSamples.State();
        var lit = g.Lighting.Clone();
        lit.BuildingScale = double.NaN;
        Assert.NotNull(StateMachine.Apply(g, new SetLighting { Lighting = lit }, out _));
        lit.BuildingScale = Lighting.MostBuildingScale * 10;
        Assert.Null(StateMachine.Apply(g, new SetLighting { Lighting = lit }, out _));
        Assert.Equal(Lighting.MostBuildingScale, g.Lighting.Building);
        Assert.Equal(0, Lighting.HeldBuildingScale(1));
        Assert.DoesNotContain("BuildingScale", Json.Serialize(new Lighting { BuildingScale = Lighting.HeldBuildingScale(1) }));
        Assert.Equal(250, Json.Deserialize<Lighting>(Json.Serialize(new Lighting { BuildingScale = 250 }))!.Building);
    }

    [Fact]
    public void A_newly_laid_thing_is_stamped_with_the_building_scale()
    {
        var g = CoreSamples.State();
        g.Sheets.Add(new SheetDef { Id = "s-000040", Size = new Size2(40, 20) });
        g.Calibration.UnitsPerMeter = 1 / MetresToTheUnit;
        g.Lighting.BuildingScale = 200;
        Assert.Null(StateMachine.Apply(g, new LayBuilt { Thing = CoreSamples.Built("", "s-000040") }, out _));
        Assert.Equal(200 / MetresToTheUnit, g.Built[^1].Setting(BuiltThings.Scale, 0), 9);
    }
}
