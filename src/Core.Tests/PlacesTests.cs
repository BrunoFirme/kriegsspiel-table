using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class PlacesTests
{
    const string U = Layers.Umpire;

    static SheetDef Sheet(string id, double x, double size) => new() { Id = id, Name = "Sheet " + id, Pos = new Vec2(x, 0), Size = new Size2(size, size) };

    static PieceDef House => new() { Id = "house", Name = "House", Size = new Size2(4, 2), Tags = { Doors.BuildingTag } };

    static PieceDef? Kind(string id) => id == "house" ? House : id == "man" ? new PieceDef { Id = "man", Name = "Man" } : null;

    static BuiltThing Town(SheetDef sheet, string id, string? name, Vec2 low, Vec2 high)
    {
        var corners = new[] { low, new Vec2(high.X, low.Z), high, new Vec2(low.X, high.Z) }.Select(p => BuiltGeometry.ToSheet(sheet, p));
        return new BuiltThing { Id = id, Sheet = sheet.Id, Kind = BuiltKinds.Settlement, Name = name, Outline = BuiltGeometry.Flat(corners) };
    }

    static GameState Table()
    {
        var north = Sheet("s-000001", 0, 100);
        var g = new GameState { Sheets = { north, Sheet("s-000002", 300, 50) } };
        g.Built.Add(Town(north, "w-000003", "Hougoumont", new Vec2(10, 10), new Vec2(30, 30)));
        g.Built.Add(Town(north, "w-000004", null, new Vec2(-30, -30), new Vec2(-10, -10)));
        g.Built.Add(Town(north, "w-000005", "Orchard", new Vec2(12, 12), new Vec2(16, 16)));
        g.Layers[U] = new()
        {
            new PieceInstance { Id = "p-000010", Def = "house", Label = "Chapel", Pos = new Vec2(20, 20) },
            new PieceInstance { Id = "p-000011", Def = "house", Pos = new Vec2(-40, 40), RotDeg = 90 },
            new PieceInstance { Id = "p-000012", Def = "man", Pos = new Vec2(20.5, 20.5) },
            new PieceInstance { Id = "p-000013", Def = "man", Pos = new Vec2(14, 14) },
            new PieceInstance { Id = "p-000014", Def = "man", Pos = new Vec2(500, 500) },
        };
        g.Places = new()
        {
            new PlacePin { Id = "pl-000020", Name = "The well", Sheet = "s-000001", Pos = new Vec2(25, 12) },
            new PlacePin { Id = "pl-000021", Name = "Crossroads", Sheet = "s-000001", Pos = new Vec2(-20, 30) },
            new PlacePin { Id = "pl-000022", Name = "The far shore", Pos = new Vec2(500, 501) },
        };
        g.NextId = 30;
        return g;
    }

    static readonly PlaceBody[] Bodies = { new("p-000012", "Ann"), new("p-000013", "Bob"), new("p-000014", "Cid"), new("p-000099", "Gone") };

    static string Shape(IEnumerable<MapPlace> tree) => string.Join(" ", tree.Select(p => p.Inside.Count == 0 ? p.Name : $"{p.Name} [{Shape(p.Inside)}]"));

    [Fact]
    public void A_pin_is_added_with_a_new_id_changed_and_forgotten_and_each_undo_puts_it_back_as_it_was()
    {
        var g = new GameState { Sheets = { Sheet("s-000001", 0, 100) }, NextId = 5 };
        var add = new SetPlace { Place = new PlacePin { Name = "  The Tavern ", Sheet = "s-000001", Pos = new Vec2(3, 4), View = [3, 4, 20, 3.1f, -1, 0] } };
        Assert.Null(StateMachine.Apply(g, add, out var unAdd));
        var pin = Assert.Single(g.Places!);
        Assert.Equal(("pl-000005", "The Tavern", 6), (pin.Id, pin.Name, pin.View!.Length));
        Assert.Equal(pin.Id, Assert.IsType<RemovePlace>(unAdd).Id);
        Assert.Null(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Id = pin.Id, Name = "The Inn", Pos = new Vec2(9, 9) } }, out var unChange));
        Assert.Equal(("The Inn", new Vec2(9, 9), (string?)null), (g.Places![0].Name, g.Places[0].Pos, g.Places[0].Sheet));
        Assert.Null(StateMachine.Apply(g, unChange!, out _));
        Assert.Equal(("The Tavern", new Vec2(3, 4), "s-000001"), (g.Places![0].Name, g.Places[0].Pos, g.Places[0].Sheet));
        Assert.Null(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Name = "Mill", Pos = new Vec2(1, 1) } }, out _));
        Assert.Null(StateMachine.Apply(g, new RemovePlace { Id = pin.Id }, out var unForget));
        Assert.Equal(new[] { "Mill" }, g.Places!.Select(p => p.Name));
        Assert.Null(StateMachine.Apply(g, unForget!, out _));
        Assert.Equal(new[] { "The Tavern", "Mill" }, g.Places!.Select(p => p.Name));
        Assert.Equal(new[] { 3f, 4, 20, 3.1f, -1, 0 }, g.Places[0].View);
        Assert.Null(StateMachine.Apply(g, unAdd!, out _));
        Assert.Null(StateMachine.Apply(g, new RemovePlace { Id = g.Places![0].Id }, out _));
        Assert.Null(g.Places);
        Assert.DoesNotContain("places", Json.Serialize(g));
    }

    [Fact]
    public void A_pin_without_a_name_on_a_missing_sheet_or_with_a_broken_view_is_refused_and_leaves_no_list()
    {
        var g = new GameState();
        Assert.NotNull(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Name = "   " } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Name = new string('a', PlacePin.MostNameLength + 1) } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Name = "Inn", Sheet = "s-404" } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Name = "Inn", View = [1, 2] } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPlace { Place = new PlacePin { Name = "Inn", Pos = new Vec2(double.NaN, 0) } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new RemovePlace { Id = "pl-000001" }, out _));
        Assert.Null(g.Places);
    }

    [Fact]
    public void Players_and_observers_never_see_the_pins_nor_hear_of_them_and_umpires_do()
    {
        var g = Table();
        foreach (var role in new[] { Role.Observer, Role.Player })
        {
            var watcher = new PeerInfo { PeerId = 5, Role = role };
            Assert.Null(Visibility.Filter(g, watcher).Places);
            Assert.Null(Visibility.ForPeer(new SetPlace { Place = new PlacePin { Name = "Ambush" } }, watcher, g));
            Assert.Null(Visibility.ForPeer(new RemovePlace { Id = "pl-000020" }, watcher, g));
            Assert.Null(Visibility.ForPeer(new Batch { Items = { new RemovePlace { Id = "pl-000020" } } }, watcher, g));
        }
        var umpire = new PeerInfo { PeerId = 6, Role = Role.Umpire };
        Assert.Equal(3, Visibility.Filter(g, umpire).Places!.Count);
        Assert.Equal(Sender.Umpire, new SetPlace().SentBy());
        Assert.True(new RemovePlace().OnUndoStack());
        Assert.Equal(3, g.DeepClone().Places!.Count);
    }

    [Fact]
    public void The_tree_hangs_named_built_things_under_their_sheet_and_buildings_and_pins_under_the_smallest_one_they_stand_in()
    {
        var tree = PlaceTree.Of(Table(), Kind);
        Assert.Equal("Sheet s-000001 [Crossroads Hougoumont [Chapel The well] House Orchard] Sheet s-000002 The far shore", Shape(tree));
        Assert.Equal(new[] { PlaceKind.Sheet, PlaceKind.Sheet, PlaceKind.Pin }, tree.Select(p => p.Kind));
        var town = PlaceTree.Find(tree, "hougoumont")!;
        Assert.Equal((PlaceKind.Built, "w-000003"), (town.Kind, town.Id));
        Assert.True(Math.Abs(town.At.X - 20) < 0.01 && Math.Abs(town.At.Z - 20) < 0.01, $"the town's middle {town.At}");
        Assert.Equal("Orchard", PlaceTree.Find(tree, "orch")!.Name);
        Assert.Null(PlaceTree.Find(tree, "Waterloo"));
    }

    [Fact]
    public void Who_is_here_is_worked_out_from_where_the_bodies_stand_in_each_place_s_outline()
    {
        var g = Table();
        var tree = PlaceTree.Of(g, Kind, Bodies);
        string Here(string name) => string.Join(",", PlaceTree.Find(tree, name)!.Here);
        Assert.Equal("Ann,Bob", Here("Sheet s-000001"));
        Assert.Equal("Ann,Bob", Here("Hougoumont"));
        Assert.Equal("Ann", Here("Chapel"));
        Assert.Equal("Bob", Here("Orchard"));
        Assert.Equal("", Here("The well"));
        Assert.Equal("Cid", Here("The far shore"));
        Assert.Equal("", Here("Sheet s-000002"));
        g.Find(U, "p-000012")!.Pos = new Vec2(25, 12.5);
        PlaceTree.Count(tree, g, Bodies);
        Assert.Equal("Ann", Here("The well"));
        Assert.Equal("", Here("Chapel"));
    }

    [Fact]
    public void Sending_moves_each_body_to_the_place_and_several_stand_apart_there_in_one_change()
    {
        var g = Table();
        var well = PlaceTree.Find(PlaceTree.Of(g, Kind), "The well")!;
        var one = Assert.IsType<MovePiece>(PlaceTree.Send(g, well, Bodies.Take(1)));
        Assert.Equal(("p-000012", well.At), (one.Id, one.Pos));
        var all = Assert.IsType<Batch>(PlaceTree.Send(g, well, Bodies));
        Assert.Equal(3, all.Items.Count);
        Assert.Null(StateMachine.Apply(g, all, out _));
        Assert.Equal("Ann,Bob,Cid", string.Join(",", PlaceTree.Find(PlaceTree.Of(g, Kind, Bodies), "The well")!.Here));
        Assert.Equal(3, all.Items.Cast<MovePiece>().Select(m => m.Pos).Distinct().Count());
        Assert.Null(PlaceTree.Send(g, well, [new PlaceBody("p-000099", "Gone")]));
    }

    [Fact]
    public void A_turned_building_holds_what_stands_on_its_own_footprint()
    {
        var house = PlaceTree.Find(PlaceTree.Of(Table(), Kind), "House")!;
        Assert.True(house.Contains(new Vec2(-40, 41.8)));
        Assert.False(house.Contains(new Vec2(-41.8, 40)));
    }

    [Fact]
    public void A_pin_the_map_s_scale_measures_reaches_some_metres_and_people_sent_to_a_place_stand_apart_around_it()
    {
        var g = new GameState { Calibration = new Calibration { UnitsPerMeter = 0.1 } };
        Assert.Equal(PlaceTree.PinMetres * 0.1, PlaceTree.PinReach(g), 6);
        Assert.Equal(PlaceTree.PinUnits, PlaceTree.PinReach(new GameState()));
        var spots = PlaceTree.Spread(new Vec2(10, 10), 5, 1);
        Assert.Equal(5, spots.Distinct().Count());
        Assert.All(spots, s => Assert.True((s - new Vec2(10, 10)).Length <= 1.5));
        Assert.Equal(new Vec2(10, 10), Assert.Single(PlaceTree.Spread(new Vec2(10, 10), 1, 1)));
    }

    [Fact]
    public void Pins_go_into_a_scenario_made_from_the_table_and_come_back_when_a_game_is_started_from_it()
    {
        var g = new GameState { ScenarioId = "s", Name = "Table" };
        g.Sheets.Add(new SheetDef { Id = "s-1", Name = "Ground" });
        g.Places = new() { new PlacePin { Id = "pl-000004", Name = "The Tavern", Sheet = "s-1", Pos = new Vec2(3, 3), View = [1, 2, 3, 4, 5, 0] } };
        var scenario = g.ToScenario("copy", "Copy");
        var kept = Assert.Single(scenario.Places!);
        Assert.NotSame(g.Places[0], kept);
        var again = GameState.FromScenario(Json.Deserialize<Scenario>(Json.Serialize(scenario)));
        var pin = Assert.Single(again.Places!);
        Assert.Equal(("pl-000004", "The Tavern", "s-1", new Vec2(3, 3), 6), (pin.Id, pin.Name, pin.Sheet, pin.Pos, pin.View!.Length));
        Assert.Null(new GameState().ToScenario("empty", "Empty").Places);
    }
}
