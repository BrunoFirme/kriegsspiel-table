using System.Globalization;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class MapTravelTests
{
    const double Side = 60, MetresPerUnit = 50;
    const string Sheet = "s-000001";

    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static Table Laid(bool relief)
    {
        var t = new Table();
        var state = t.Server.State;
        state.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        var sheet = state.Sheets.Single(s => s.Id == Sheet);
        sheet.Pos = new Vec2(Side / 2, Side / 2);
        sheet.Size = new Size2(Side, Side);
        if (relief) sheet.Relief = ReliefFiles.Save(t.Store.PackageDir(UploadAsset.ReliefPackage), Grass()).sha;
        return t;
    }

    static ReliefGrid Grass()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        Array.Fill(grid.Kind, GroundKinds.Grass);
        return grid;
    }

    static BuiltThing Road(string cls, double z) =>
        BuiltRoads.Lay(new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, HeightUnit = Side }, Sheet, [new Spot(8, z), new Spot(52, z)], cls, false);

    static BuiltThing Path(double z) =>
        new() { Sheet = Sheet, Kind = BuiltKinds.Road, Parts = { BuiltSite.Line(Footpaths.Path, [new Spot(8, z), new Spot(52, z)]) } };

    static string P(GameState g, double x, double z, string sheet = Sheet)
    {
        var at = Ground.FromUv(g.Sheets.Single(s => s.Id == sheet), x / Side, z / Side);
        return string.Create(CultureInfo.InvariantCulture, $"{{ x = {at.X}, z = {at.Z} }}");
    }

    static string Said(Table t, string code)
    {
        int before = Flat(t.Server.Log).OfType<Say>().Count();
        var res = t.Server.Request(Table.Host, new RunLua { Target = "", Code = "game.say(tostring(" + code + "))" });
        var said = Flat(t.Server.Log).OfType<Say>().Skip(before).Select(s => s.Text).ToList();
        if (said.Count == 1) return said[0];
        return string.Join("\n", res.Select(o => o.Message).OfType<ScriptConsole>().SelectMany(c => c.Lines).Select(l => l.Text));
    }

    static double Seconds(Table t, string code) => double.Parse(Said(t, code), CultureInfo.InvariantCulture);

    [Fact]
    public void A_road_beats_the_fields_and_its_route_adds_up_to_the_time()
    {
        using var t = Laid(relief: true);
        t.Server.State.Built.Add(Road("road", 30));
        var g = t.Server.State;
        double road = Seconds(t, $"map.travel({P(g, 10, 30.3)}, {P(g, 50, 30.3)})");
        double fields = Seconds(t, $"map.travel({P(g, 10, 52)}, {P(g, 50, 52)}, 'foot')");
        Assert.True(road < fields * 0.8, $"the road takes {road:0} s against {fields:0} s across the fields");
        Assert.InRange(fields, 40 * MetresPerUnit / Travellers.Default(Manners.Later)[Travellers.Foot].Open * 0.99, double.MaxValue);
        string route = $"map.route({P(g, 10, 30.3)}, {P(g, 50, 30.3)})";
        Assert.Equal("0", Said(t, route + "[1].seconds"));
        double sum = Seconds(t, $"(function() local s = 0 for _, p in ipairs({route}) do s = s + p.seconds end return s end)()");
        Assert.InRange(sum, road - 1e-6, road + 1e-6);
        Assert.Equal("true", Said(t, $"#{route} >= 2 and {route}[1].x ~= nil and {route}[1].z ~= nil"));
    }

    [Fact]
    public void A_plain_picture_sheet_gives_no_answer()
    {
        using var t = Laid(relief: false);
        var g = t.Server.State;
        Assert.Equal("nil", Said(t, $"map.travel({P(g, 10, 30)}, {P(g, 50, 30)})"));
        Assert.Equal("nil", Said(t, $"map.route({P(g, 10, 30)}, {P(g, 50, 30)}, 'cart')"));
    }

    [Fact]
    public void Two_points_on_two_sheets_give_no_answer()
    {
        using var t = Laid(relief: true);
        var g = t.Server.State;
        g.Sheets.Add(new SheetDef { Id = "s-000009", Name = "East", Pos = new Vec2(Side * 1.5, Side / 2), Size = new Size2(Side, Side), Relief = g.Sheets[0].Relief });
        Assert.NotEqual("nil", Said(t, $"map.travel({P(g, 10, 30)}, {P(g, 50, 30)})"));
        Assert.Equal("nil", Said(t, $"map.travel({P(g, 10, 30)}, {P(g, 20, 30, "s-000009")})"));
    }

    [Fact]
    public void A_cart_keeps_to_the_ways_it_can_use_and_an_unknown_traveller_is_an_error()
    {
        using var t = Laid(relief: false);
        t.Server.State.Built.Add(Path(30));
        var g = t.Server.State;
        Assert.NotEqual("nil", Said(t, $"map.travel({P(g, 10, 30)}, {P(g, 50, 30)}, 'foot')"));
        Assert.Equal("nil", Said(t, $"map.travel({P(g, 10, 30)}, {P(g, 50, 30)}, 'cart')"));
        string error = Said(t, $"map.travel({P(g, 10, 30)}, {P(g, 50, 30)}, 'camel')");
        Assert.Contains("no traveller \"camel\"", error);
        Assert.Contains("cart, coach, foot, rider", error);
    }

    [Fact]
    public void A_packs_paces_change_the_answer()
    {
        using var t = Laid(relief: false);
        t.Server.State.Built.Add(Road("road", 30));
        var g = t.Server.State;
        string trip = $"map.travel({P(g, 10, 30)}, {P(g, 50, 30)})";
        double before = Seconds(t, trip);
        t.Store.Pack("testpack")!.Paces = [new PaceDef { Traveller = "foot", Way = "road", MetresPerSecond = 3 }];
        double after = Seconds(t, trip);
        Assert.InRange(after / before, 0.45, 0.55);
        t.Store.Pack("testpack")!.Paces = [new PaceDef { Traveller = "camel", Way = "road", MetresPerSecond = 2 }];
        Assert.NotEqual("nil", Said(t, trip.Replace(")", ", 'camel')")));
    }

    [Fact, Trait("Category", "Timing")]
    public void The_map_is_built_once_for_many_calls_and_again_after_a_road_is_laid()
    {
        using var t = Laid(relief: true);
        var g = t.Server.State;
        string trip = $"map.travel({P(g, 10, 30.3)}, {P(g, 50, 30.3)})";
        Assert.Equal("200", Said(t, $"(function() local n = 0 for i = 1, 200 do if {trip} then n = n + 1 end end return n end)()"));
        Assert.Equal(1, t.Server.Travel.MapsBuilt);
        double fields = Seconds(t, trip);
        t.Ok(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new Vec2(3, 4) });
        Assert.Equal(1, t.Server.Travel.MapsBuilt);
        t.Ok(Table.Host, new LayBuilt { Thing = Road("road", 30) });
        double road = Seconds(t, trip);
        Assert.Equal(2, t.Server.Travel.MapsBuilt);
        Assert.True(road < fields * 0.8, $"the laid road takes {road:0} s against {fields:0} s");
        Seconds(t, trip);
        Assert.Equal(2, t.Server.Travel.MapsBuilt);
    }
}
