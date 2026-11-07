using System.Reflection;
using System.Text.Json.Serialization;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class StateMachineTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void A_piece_cannot_ride_on_one_it_carries_however_far_down()
    {
        var g = new GameState();
        g.Layers[U] = new() { new PieceInstance { Id = "a" }, new PieceInstance { Id = "b" }, new PieceInstance { Id = "c" } };
        Assert.Null(StateMachine.Apply(g, new Attach { Layer = U, Parent = "a", Child = "b" }, out _));
        Assert.Null(StateMachine.Apply(g, new Attach { Layer = U, Parent = "b", Child = "c" }, out _));
        Assert.Equal("a piece cannot ride on one it carries", StateMachine.Apply(g, new Attach { Layer = U, Parent = "c", Child = "a" }, out _));
        Assert.Equal("a piece cannot ride on one it carries", StateMachine.Apply(g, new Attach { Layer = U, Parent = "b", Child = "a" }, out _));
        Assert.Null(StateMachine.Apply(g, new Attach { Layer = U, Parent = "a", Child = "c" }, out _));
    }

    [Fact]
    public void A_stowed_piece_is_off_the_table_with_what_it_carries_and_comes_back_where_it_is_put()
    {
        var g = Base();
        var holder = g.Find(U, "p-000006")!;
        Assert.Null(StateMachine.Apply(g, new Stow { Layer = U, Holder = "p-000006", Id = "p-000001" }, out var undo));
        Assert.Null(g.Find(U, "p-000001"));
        Assert.Null(g.Find(U, "p-000002"));
        Assert.Equal(new[] { "p-000001", "p-000002" }, holder.Stowed!.Select(s => s.Id));
        Assert.IsType<Unstow>(undo);
        Assert.Null(StateMachine.Apply(g, new Unstow { Layer = U, Holder = "p-000006", Id = "p-000001", Pos = new(10, 10), RotDeg = 90 }, out var back));
        var p = g.Find(U, "p-000001")!;
        Assert.Equal((new Vec2(10, 10), 90.0, 0.0, 0), (p.Pos, p.RotDeg, p.Y, p.Stack));
        Assert.NotNull(g.Find(U, "p-000002"));
        Assert.Contains("p-000002", p.Attached);
        Assert.Null(holder.Stowed);
        Assert.IsType<Stow>(back);
        Assert.Equal("nothing stowed there", StateMachine.Apply(g, new Unstow { Layer = U, Holder = "p-000006" }, out _));
        Assert.Null(StateMachine.Apply(g, new Attach { Layer = U, Parent = "p-000001", Child = "p-000006" }, out _));
        Assert.Equal("a piece cannot hold one it rides on", StateMachine.Apply(g, new Stow { Layer = U, Holder = "p-000006", Id = "p-000001" }, out _));
    }

    [Fact]
    public void Every_registered_command_has_its_case_in_apply()
    {
        var missing = new List<string>();
        foreach (var a in typeof(Command).GetCustomAttributes<JsonDerivedTypeAttribute>())
        {
            string? err;
            try { err = StateMachine.Apply(new GameState(), (Command)Activator.CreateInstance(a.DerivedType)!, out _); }
            catch (Exception e) { err = e.Message; }
            if (err == "unknown command") missing.Add(a.DerivedType.Name);
        }
        Assert.Empty(missing);
    }

    static string Canon(GameState g)
    {
        var c = g.DeepClone();
        c.NextId = 0;
        foreach (var k in c.Layers.Keys.ToList())
        {
            if (c.Layers[k].Count == 0) { c.Layers.Remove(k); continue; }
            c.Layers[k] = c.Layers[k].OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            foreach (var p in c.Layers[k])
            {
                p.Attached.Sort(StringComparer.Ordinal);
                p.Pos = R(p.Pos);
                p.RotDeg = R(p.RotDeg);
            }
        }
        foreach (var k in c.Drawings.Keys.ToList())
        {
            if (c.Drawings[k].Count == 0) { c.Drawings.Remove(k); continue; }
            c.Drawings[k] = c.Drawings[k].OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        }
        c.Sheets = c.Sheets.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        c.Layers = new(c.Layers.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        c.Drawings = new(c.Drawings.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        return Json.Serialize(c);
    }

    static double R(double d) { var x = Math.Round(d, 9); return x == 0 ? 0 : x; }
    static Vec2 R(Vec2 v) => new(R(v.X), R(v.Z));

    static GameState Base()
    {
        var g = CoreSamples.State();
        g.Find(U, "p-000001")!.Attached.Add("p-000002");
        g.Layer(U).Add(CoreSamples.Piece("p-000006", "def-c", -3, 2));
        g.Sheets.Add(CoreSamples.Sheet("s-000007"));
        g.NextId = 20;
        return g;
    }

    public static IEnumerable<object[]> Undoable()
    {
        object[] C(string name, Command c) => new object[] { name, Json.Serialize<Command>(c) };
        yield return C("spawn", new SpawnPiece { Layer = U, Def = "def-a", Pos = new(5, 5), RotDeg = 10 });
        yield return C("spawn-with-id", new SpawnPiece { Layer = "scratch:blue", Def = "def-a", Id = "p-000500" });
        yield return C("restore", new RestorePiece { Layer = U, Piece = CoreSamples.Piece("p-000099") });
        yield return C("move", new MovePiece { Layer = U, Id = "p-000006", Pos = new(1, 1), RotDeg = 400, Stack = 2 });
        yield return C("move-parent-rotating", new MovePiece { Layer = U, Id = "p-000001", Pos = new(-7, 3), RotDeg = 77, Stack = 0 });
        yield return C("delete", new DeletePiece { Layer = U, Id = "p-000006" });
        yield return C("delete-parent", new DeletePiece { Layer = U, Id = "p-000001" });
        yield return C("delete-attached-child", new DeletePiece { Layer = U, Id = "p-000002" });
        yield return C("state", new SetState { Layer = U, Id = "p-000001", State = 3 });
        yield return C("counter", new SetCounter { Layer = U, Id = "p-000001", Name = "str", Value = 9 });
        yield return C("text", new SetText { Layer = U, Id = "p-000001", Name = "name", Value = "Grenadiers" });
        yield return C("label", new SetLabel { Layer = U, Id = "p-000001", Value = null });
        yield return C("tint", new SetTint { Layer = U, Id = "p-000001", Value = "#00FF00" });
        yield return C("flip", new FlipPiece { Layer = U, Id = "p-000001", FaceDown = true });
        yield return C("attach-new", new Attach { Layer = U, Parent = "p-000006", Child = "p-000001" });
        yield return C("attach-reparent", new Attach { Layer = U, Parent = "p-000006", Child = "p-000002" });
        yield return C("attach-same-parent", new Attach { Layer = U, Parent = "p-000001", Child = "p-000002" });
        yield return C("stow", new Stow { Layer = U, Holder = "p-000006", Id = "p-000001" });
        yield return C("stow-attached-child", new Stow { Layer = U, Holder = "p-000006", Id = "p-000002" });
        yield return C("holds-on", new SetPieceOption { Layer = U, Id = "p-000006", Name = PieceOptions.Holds, Value = true });
        yield return C("holds-off", new SetPieceOption { Layer = U, Id = "p-000006", Name = PieceOptions.Holds, Value = false });
        yield return C("detach", new Detach { Layer = U, Child = "p-000002" });
        yield return C("add-sheet", new AddSheet { Sheet = CoreSamples.Sheet("") });
        yield return C("move-sheet", new MoveSheet { Id = "s-000001", Pos = new(9, 9), RotDeg = -90, Z = 5 });
        yield return C("lock-sheet", new LockSheet { Id = "s-000007", Locked = true });
        yield return C("remove-sheet", new RemoveSheet { Id = "s-000001" });
        yield return C("crop-sheet", new CropSheet { Id = "s-000001", Crop = new FaceBox { X = 0.1, Y = 0.05, W = 0.8, H = 0.9 } });
        yield return C("lighting", new SetLighting { Lighting = new Lighting { Warmth = -0.5, SunEnergy = 0.6, Ambient = 0.5, Shadows = false } });
        yield return C("wind", new SetWind { Wind = new Wind { From = 45, Strength = 2 } });
        yield return C("vision", new SetVisionRadius { Radius = 25 });
        yield return C("draw", new Draw { Layer = U, Stroke = CoreSamples.Stroke("") });
        yield return C("erase", new EraseStroke { Layer = U, StrokeId = "d-000004" });
        yield return C("clock", new SetClock { Turn = 9, GameTime = "1809-05-22T06:00", IncrementMinutes = 60 });
        yield return C("advance", new AdvanceClock());
        yield return C("calibration", new SetCalibration { UnitsPerMeter = null });
        yield return C("batch", new Batch
        {
            Items = new()
            {
                new MovePiece { Layer = U, Id = "p-000001", Pos = new(2, 2), RotDeg = 180 },
                new SetState { Layer = U, Id = "p-000001", State = 2 },
                new DeletePiece { Layer = U, Id = "p-000006" },
                new SpawnPiece { Layer = U, Def = "def-z" },
            },
        });
    }

    [Theory]
    [MemberData(nameof(Undoable))]
    public void Inverse_restores_prior_state(string name, string commandJson)
    {
        var g = Base();
        var before = Canon(g);
        var c = Json.Deserialize<Command>(commandJson);
        Assert.Null(StateMachine.Apply(g, c, out var inv));
        Assert.NotNull(inv);
        if (name != "attach-same-parent") Assert.NotEqual(before, Canon(g));
        Assert.Null(StateMachine.Apply(g, Json.Clone(inv!), out var redo));
        Assert.True(before == Canon(g), $"{name}: inverse did not restore.\nwant {before}\ngot  {Canon(g)}");

        var afterOnce = Base();
        Assert.Null(StateMachine.Apply(afterOnce, Json.Deserialize<Command>(commandJson), out _));
        Assert.NotNull(redo);
        Assert.Null(StateMachine.Apply(g, redo!, out _));
        if (c is not SpawnPiece && c is not AddSheet && c is not Draw && c is not Batch)
            Assert.Equal(Canon(afterOnce), Canon(g));
    }

    [Fact]
    public void Dice_and_old_sightings_have_no_inverse()
    {
        var g = Base();
        Assert.Null(StateMachine.Apply(g, new RollDice { Results = new() { 3 } }, out var i1));
        Assert.Null(i1);
        var before = Canon(g);
        Assert.Null(StateMachine.Apply(g, new PublishSighting(), out var i2));
        Assert.Null(i2);
        Assert.Equal(before, Canon(g));
        Assert.Equal("undo is resolved by the server", StateMachine.Apply(g, new Undo(), out _));
    }

    [Fact]
    public void A_rolled_die_lands_level_on_its_face_and_a_batch_that_fails_after_the_roll_leaves_it_leaning()
    {
        var g = Base();
        var die = g.Find(U, "p-000006")!;
        (die.Face, die.TiltX, die.TiltZ) = (1, 20, -5);
        RollDice Roll() => new() { Layer = U, PieceIds = new() { die.Id }, Results = new() { 4 } };

        Assert.NotNull(StateMachine.Apply(g, new Batch { Items = { Roll(), new MovePiece { Layer = U, Id = "nobody" } } }, out _));
        Assert.Equal((1, 20.0, -5.0), (die.Face, die.TiltX, die.TiltZ));
        Assert.Null(StateMachine.Apply(g, Roll(), out _));
        Assert.Equal((3, 0.0, 0.0), (die.Face, die.TiltX, die.TiltZ));
    }

    [Fact]
    public void Turning_a_piece_over_moves_nothing()
    {
        var g = Base();
        var p = g.Find(U, "p-000001")!;
        var child = g.Find(U, "p-000002")!;
        p.Pos = new(3, -8);
        p.RotDeg = 47;
        p.Y = 1.25;
        p.Stack = 2;
        p.Scale = 1.4;
        var (pos, rot, y, stack, scale) = (p.Pos, p.RotDeg, p.Y, p.Stack, p.Scale);
        var childPos = child.Pos;
        Assert.Null(StateMachine.Apply(g, new FlipPiece { Layer = U, Id = p.Id, FaceDown = true }, out var inv));
        Assert.True(p.FaceDown);
        Assert.Equal((pos, rot, y, stack, scale), (p.Pos, p.RotDeg, p.Y, p.Stack, p.Scale));
        Assert.Equal(childPos, child.Pos);
        Assert.Null(StateMachine.Apply(g, inv!, out _));
        Assert.False(p.FaceDown);
        Assert.Equal((pos, rot, y, stack, scale), (p.Pos, p.RotDeg, p.Y, p.Stack, p.Scale));
    }

    [Fact]
    public void Attached_markers_move_with_parent_translation()
    {
        var g = Base();
        var parent = g.Find(U, "p-000001")!;
        var child = g.Find(U, "p-000002")!;
        Assert.Null(StateMachine.Apply(g, new MovePiece { Layer = U, Id = parent.Id, Pos = new(10, -4), RotDeg = 30 }, out _));
        Assert.Equal(new Vec2(10, -4), parent.Pos);
        Assert.Equal(new Vec2(15, 1), child.Pos);
        Assert.Equal(30, child.RotDeg);
        Assert.Equal(new Vec2(-3, 2), g.Find(U, "p-000006")!.Pos);
    }

    [Fact]
    public void Attached_markers_rotate_rigidly_with_parent()
    {
        var g = new GameState();
        g.Layer(U).Add(new PieceInstance { Id = "a", Pos = new(0, 0), RotDeg = 0, Attached = { "b" } });
        g.Layer(U).Add(new PieceInstance { Id = "b", Pos = new(1, 0), RotDeg = 10 });
        Assert.Null(StateMachine.Apply(g, new MovePiece { Layer = U, Id = "a", Pos = new(5, 5), RotDeg = 90 }, out var inv));
        var b = g.Find(U, "b")!;
        Assert.Equal(5, b.Pos.X, 9);
        Assert.Equal(4, b.Pos.Z, 9);
        Assert.Equal(100, b.RotDeg, 9);
        Assert.Equal(1, (b.Pos - g.Find(U, "a")!.Pos).Length, 9);
        Assert.Null(StateMachine.Apply(g, inv!, out _));
        Assert.Equal(1, b.Pos.X, 9);
        Assert.Equal(0, b.Pos.Z, 9);
        Assert.Equal(10, b.RotDeg, 9);
    }

    [Fact]
    public void Markers_in_other_layers_do_not_move()
    {
        var g = new GameState();
        g.Layer(U).Add(new PieceInstance { Id = "a", Attached = { "b" } });
        g.Layer("scratch:red").Add(new PieceInstance { Id = "b", Pos = new(1, 1) });
        Assert.Null(StateMachine.Apply(g, new MovePiece { Layer = U, Id = "a", Pos = new(5, 5) }, out _));
        Assert.Equal(new Vec2(1, 1), g.Find("scratch:red", "b")!.Pos);
    }

    [Fact]
    public void Delete_detaches_from_parent_and_undo_reattaches()
    {
        var g = Base();
        Assert.Null(StateMachine.Apply(g, new DeletePiece { Layer = U, Id = "p-000002" }, out var inv));
        Assert.DoesNotContain("p-000002", g.Find(U, "p-000001")!.Attached);
        Assert.Null(StateMachine.Apply(g, inv!, out _));
        Assert.Contains("p-000002", g.Find(U, "p-000001")!.Attached);
    }

    [Fact]
    public void Errors_leave_state_untouched()
    {
        var g = Base();
        var before = Json.Serialize(g);
        Command[] bad =
        {
            new MovePiece { Layer = U, Id = "nope" }, new DeletePiece { Layer = U, Id = "nope" },
            new SetState { Layer = U, Id = "nope" }, new SetCounter { Layer = U, Id = "nope" },
            new SetText { Layer = U, Id = "nope" }, new SetLabel { Layer = U, Id = "nope" }, new SetTint { Layer = U, Id = "nope" },
            new Attach { Layer = U, Parent = "p-000001", Child = "p-000001" }, new Attach { Layer = U, Parent = "p-000001", Child = "nope" },
            new Detach { Layer = U, Child = "p-000006" }, new MoveSheet { Id = "nope" }, new LockSheet { Id = "nope" },
            new CropSheet { Id = "nope" }, new CropSheet { Id = "s-000001", Crop = new FaceBox { X = 0.5, Y = 0, W = 0.8, H = 1 } },
            new FlipPiece { Layer = U, Id = "nope", FaceDown = true },
            new RemoveSheet { Id = "nope" }, new EraseStroke { Layer = U, StrokeId = "nope" },
            new SpawnPiece { Layer = U, Id = "p-000001" }, new RestorePiece { Layer = U, Piece = new PieceInstance { Id = "p-000001" } },
            new AddSheet { Sheet = new SheetDef { Id = "s-000001" } },
            new MovePiece { Layer = "scratch:blue", Id = "p-000001" },
        };
        foreach (var c in bad)
        {
            Assert.NotNull(StateMachine.Apply(g, c, out var inv));
            Assert.Null(inv);
            Assert.Equal(before, Json.Serialize(g));
        }
    }

    [Fact]
    public void Spawn_assigns_ids_and_stacks()
    {
        var g = new GameState();
        var a = new SpawnPiece { Layer = U, Def = "d", Pos = new(1, 1) };
        var b = new SpawnPiece { Layer = U, Def = "d", Pos = new(1, 1) };
        Assert.Null(StateMachine.Apply(g, a, out _));
        Assert.Null(StateMachine.Apply(g, b, out _));
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(0, g.Find(U, a.Id!)!.Stack);
        Assert.Equal(1, g.Find(U, b.Id!)!.Stack);
        Assert.Null(StateMachine.Apply(g, new SpawnPiece { Layer = U, Id = "p-000050" }, out _));
        Assert.Equal(51, g.NextId);
    }

    [Fact]
    public void Explicit_stroke_sheet_and_zone_ids_bump_the_id_counter()
    {
        var g = new GameState();
        Assert.Null(StateMachine.Apply(g, new Draw { Layer = U, Stroke = new Stroke { Id = "d-000010" } }, out _));
        Assert.Equal(11, g.NextId);
        Assert.Null(StateMachine.Apply(g, new AddSheet { Sheet = new SheetDef { Id = "s-000020" } }, out _));
        Assert.Equal(21, g.NextId);
        Assert.Null(StateMachine.Apply(g, new SetZone { Zone = new Zone { Id = "z-000030", Polygon = { new(0, 0), new(1, 0), new(1, 1) } } }, out _));
        Assert.Equal(31, g.NextId);
        var d = new Draw { Layer = U, Stroke = new Stroke() };
        Assert.Null(StateMachine.Apply(g, d, out _));
        Assert.Equal("d-000031", d.Stroke.Id);
    }

    [Fact]
    public void Clock_and_dice_log()
    {
        var g = new GameState { Clock = new GameClock { Turn = 1, GameTime = "1809-05-21T23:50", IncrementMinutes = 15 } };
        Assert.Null(StateMachine.Apply(g, new AdvanceClock(), out _));
        Assert.Equal(2, g.Clock.Turn);
        Assert.Equal("1809-05-22T00:05", g.Clock.GameTime);
        Assert.Null(StateMachine.Apply(g, new SetClock { Turn = 3, GameTime = "x", IncrementMinutes = 0 }, out _));
        Assert.Equal(1, g.Clock.IncrementMinutes);
        for (int i = 0; i < 510; i++) StateMachine.Apply(g, new RollDice { Seq = i, Results = new() { 1 } }, out _);
        Assert.Equal(500, g.Dice.Count);
        Assert.Equal(10, g.Dice[0].Seq);
    }

    [Fact]
    public void Operational_turns_last_hours_or_days()
    {
        var g = new GameState { Clock = new GameClock { Turn = 1, GameTime = "1812-09-05T18:00", IncrementMinutes = 3 * GameClock.MinutesInDay } };
        Assert.Null(StateMachine.Apply(g, new AdvanceClock(), out _));
        Assert.Equal("1812-09-08T18:00", g.Clock.GameTime);
        Assert.Equal(new[] { "15′", "1 h", "1 h 30′", "6 h", "1 day", "3 days", "25 h" },
            new[] { 15, 60, 90, 360, 1440, 4320, 1500 }.Select(GameClock.StepText));
    }

    [Fact]
    public void LayerOf_reports_batches()
    {
        Assert.Equal(U, StateMachine.LayerOf(new MovePiece { Layer = U }));
        Assert.Null(StateMachine.LayerOf(new AdvanceClock()));
        Assert.Equal("scratch:red", StateMachine.LayerOf(new Batch { Items = { new MovePiece { Layer = "scratch:red" }, new SetState { Layer = "scratch:red" } } }));
        Assert.Equal("*mixed*", StateMachine.LayerOf(new Batch { Items = { new MovePiece { Layer = "scratch:red" }, new SetState { Layer = U } } }));
    }

    [Fact]
    public void FromScenario_does_not_alias_the_scenario()
    {
        var s = CoreSamples.Scenario();
        var before = Json.Serialize(s);
        var g = GameState.FromScenario(s);
        Assert.Null(StateMachine.Apply(g, new MoveSheet { Id = "s-000001", Pos = new(99, 99) }, out _));
        Assert.Null(StateMachine.Apply(g, new LockSheet { Id = "s-000001", Locked = true }, out _));
        Assert.Null(StateMachine.Apply(g, new MovePiece { Layer = U, Id = "p-000001", Pos = new(99, 99) }, out _));
        Assert.Null(StateMachine.Apply(g, new SetCounter { Layer = U, Id = "p-000003", Name = "str", Value = 0 }, out _));
        g.DrawLayer(U)[0].Color = "#changed";
        Assert.Equal(before, Json.Serialize(s));
    }

    [Fact]
    public void FromScenario_builds_layers_and_next_id()
    {
        var g = GameState.FromScenario(CoreSamples.Scenario());
        Assert.Equal(3, g.Layer(U).Count);
        Assert.Single(g.Layers);
        Assert.Equal(2, g.DrawLayer(U).Count);
        Assert.Single(g.Boards);
        Assert.Equal("b-000006", g.Boards[0].Id);
        Assert.Equal(7, g.NextId);
        Assert.Null(StateMachine.Apply(g, new Draw { Layer = U, Stroke = new Stroke() }, out _));
        Assert.Equal(g.DrawLayer(U).Count, g.DrawLayer(U).Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void A_drawing_that_names_no_layer_belongs_to_the_first_one()
    {
        var g = GameState.FromScenario(CoreSamples.Scenario());
        var first = g.Boards[0].Id;
        Assert.All(g.DrawLayer(U), s => Assert.Equal(first, s.Board));
        Assert.Null(StateMachine.Apply(g, new Draw { Layer = U, Stroke = new Stroke { Points = { new(0, 0), new(1, 1) } } }, out _));
        Assert.Equal(first, g.DrawLayer(U)[^1].Board);
    }

    [Fact]
    public void The_last_layer_is_cleared_rather_than_taken_away()
    {
        var g = GameState.FromScenario(CoreSamples.Scenario());
        var first = g.Boards[0].Id;
        Assert.Null(StateMachine.Apply(g, new RenameBoard { Id = first, Name = "Plan" }, out _));
        Assert.Null(StateMachine.Apply(g, new AddBoard { Name = "Orders" }, out _));
        var second = g.Boards[1].Id;

        Assert.Null(StateMachine.Apply(g, new Draw { Layer = U, Stroke = new Stroke { Board = second, Points = { new(0, 0), new(1, 1) } } }, out _));
        Assert.Null(StateMachine.Apply(g, new RemoveBoard { Id = second }, out _));
        Assert.Single(g.Boards);
        Assert.DoesNotContain(g.DrawLayer(U), s => s.Board == second);

        int on = g.DrawLayer(U).Count(s => s.Board == first);
        Assert.True(on > 0);
        Assert.Null(StateMachine.Apply(g, new RemoveBoard { Id = first }, out var inv));
        Assert.Single(g.Boards);
        Assert.Equal(first, g.Boards[0].Id);
        Assert.Equal("Layer 1", g.Boards[0].Name);
        Assert.DoesNotContain(g.DrawLayer(U), s => s.Board == first);
        Assert.Null(StateMachine.Apply(g, inv!, out _));
        Assert.Equal("Plan", g.Boards[0].Name);
    }
}

public class ScenarioRoundTripTests
{
    [Fact]
    public void TableSavedAsScenarioReloadsTheSame()
    {
        var sc = new KTable.Core.Model.Scenario
        {
            Id = "t", Name = "T", Packs = { "p1" },
            Sheets = { new() { Id = "s1", Image = "abc", Package = "sheets", Size = new(10, 5) } },
            UmpireBoard = { new() { Id = "p-000001", Def = "d", Pos = new(1, 2) } },
        };
        var g = KTable.Core.Model.GameState.FromScenario(sc);
        KTable.Core.Protocol.StateMachine.Apply(g, new KTable.Core.Protocol.MoveSheet { Id = "s1", Pos = new(3, 4), Size = new(20, 10) }, out var inv);
        Assert.Equal(20, g.Sheets[0].Size.W);
        var back = KTable.Core.Model.GameState.FromScenario(g.ToScenario("t2", "T2"));
        Assert.Equal(KTable.Core.Json.Serialize(g.Sheets), KTable.Core.Json.Serialize(back.Sheets));
        Assert.Equal(KTable.Core.Json.Serialize(g.Layers), KTable.Core.Json.Serialize(back.Layers));
        KTable.Core.Protocol.StateMachine.Apply(g, inv!, out _);
        Assert.Equal(10, g.Sheets[0].Size.W);
    }
}
