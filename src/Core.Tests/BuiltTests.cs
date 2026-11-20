using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class BuiltTests
{
    [Fact]
    public void A_place_saved_before_the_finer_grain_reads_where_it_stood()
    {
        var thing = Json.Load<Scenario>(Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats", "65", "scenario.json")).Built[0];
        Assert.Equal(BuiltGeometry.PerUnit, thing.Grain);
        Assert.Equal(new[] { 1.0, 1.0, 3.0, 1.0 }, thing.Outline.Select(v => v / (double)BuiltGeometry.PerUnit));
        Assert.Equal(new Spot(2.2, 1.8), Spot.Of(BuiltGeometry.Points(thing.Parts[0].At)[2]));
        Assert.Equal(thing.Outline, Json.Deserialize<BuiltThing>(Json.Serialize(thing)).Outline);
    }

    [Fact]
    public void A_place_too_wide_for_the_finer_grain_is_refused_on_load_not_squashed()
    {
        const string Saved = """{"id":"w","sheet":"s","kind":"building","outline":[300000000,0,300000010,0],"parts":[],"grain":1000}""";
        var e = Assert.Throws<System.Text.Json.JsonException>(() => Json.Deserialize<BuiltThing>(Saved));
        Assert.Equal(BuiltThing.TooWide, e.Message);
    }

    [Fact]
    public void A_point_past_the_grain_s_reach_is_held_at_the_edge_not_wrapped()
    {
        Assert.Equal(int.MaxValue, BuiltGeometry.Stored(300000));
        Assert.Equal(int.MinValue, BuiltGeometry.Stored(-300000));
        Assert.Equal(new SheetPoint(int.MaxValue, 0), new Spot(300000, 0).Stored);
    }

    [Fact]
    public void A_house_on_a_planet_sized_map_keeps_its_corners()
    {
        const double MetresToTheUnit = 24414.5;
        var house = BuiltSite.Block("house", new Spot(200, 100), new Spot(1, 0), 10 / MetresToTheUnit, 8 / MetresToTheUnit);
        Assert.Equal(4, BuiltGeometry.Points(house.At).Distinct().Count());
    }

    static GameState Table()
    {
        var g = CoreSamples.State();
        g.Sheets.Add(new SheetDef { Id = "s-000040", Pos = new Vec2(10, -4), RotDeg = 90, Size = new Size2(40, 20), Crop = new FaceBox { X = 0.25, Y = 0, W = 0.5, H = 1 } });
        g.NextId = 60;
        return g;
    }

    static Command Do(GameState g, Command c)
    {
        Assert.Null(StateMachine.Apply(g, c, out var inverse));
        return inverse!;
    }

    [Fact]
    public void A_thing_is_laid_changed_and_removed_and_each_step_undoes()
    {
        var g = Table();
        var undoLay = Do(g, new LayBuilt { Thing = CoreSamples.Built("", "s-000040") });
        var laid = Assert.Single(g.Built);
        Assert.StartsWith("w-", laid.Id);
        var moved = BuiltGeometry.Moved(laid, 500, 0);
        var undoSet = Do(g, new SetBuilt { Thing = moved });
        Assert.Equal(1500, g.Built[0].Parts[0].At[0]);
        Do(g, undoSet);
        Assert.Equal(1000, g.Built[0].Parts[0].At[0]);
        var undoRemove = Do(g, new RemoveBuilt { Id = laid.Id });
        Assert.Empty(g.Built);
        Do(g, undoRemove);
        Assert.Equal(Json.Serialize(laid), Json.Serialize(g.Built[0]));
        Do(g, undoLay);
        Assert.Empty(g.Built);
    }

    [Fact]
    public void A_thing_needs_its_sheet_and_cannot_be_laid_twice()
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new LayBuilt { Thing = CoreSamples.Built("", "nope") }, out _));
        Do(g, new LayBuilt { Thing = CoreSamples.Built("w-000070", "s-000040") });
        Assert.NotNull(StateMachine.Apply(g, new LayBuilt { Thing = CoreSamples.Built("w-000070", "s-000040") }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetBuilt { Thing = CoreSamples.Built("w-000071", "s-000040") }, out _));
        Assert.NotNull(StateMachine.Apply(g, new RemoveBuilt { Id = "w-000071" }, out _));
        Assert.True(g.NextId > 70);
    }

    [Fact]
    public void A_point_goes_to_the_sheet_and_back_on_a_turned_cropped_sheet()
    {
        var sheet = Table().Sheets.Last();
        foreach (var world in new[] { new Vec2(10, -4), new Vec2(3.5, 6.25), new Vec2(14, -20) })
        {
            var back = BuiltGeometry.ToWorld(sheet, BuiltGeometry.ToSheet(sheet, world));
            Assert.Equal(world.X, back.X, 2);
            Assert.Equal(world.Z, back.Z, 2);
        }
        var a = BuiltGeometry.ToSheet(sheet, new Vec2(10, -4));
        var b = BuiltGeometry.ToSheet(sheet, new Vec2(10, -3));
        Assert.Equal(BuiltGeometry.PerUnit, Math.Round(Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2))), 0);
    }

    [Fact]
    public void A_scenario_keeps_what_was_built_and_a_copy_does_not_share_it()
    {
        var g = Table();
        Do(g, new LayBuilt { Thing = CoreSamples.Built("", "s-000040") });
        var back = GameState.FromScenario(g.ToScenario("scn", "Scenario"));
        Assert.Equal(Json.Serialize(g.Built), Json.Serialize(back.Built));
        back.Built[0].Parts[0].At[0] = 9;
        Assert.Equal(1000, g.Built[0].Parts[0].At[0]);
        var box = BuiltGeometry.Box(g.Built[0]);
        Assert.Equal((new SheetPoint(0, 0), new SheetPoint(3000, 1800)), box);
    }
}
