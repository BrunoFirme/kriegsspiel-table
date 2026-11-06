using System.Collections;
using System.Reflection;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

[Collection(HeavyTests.Name)]
public class MalformedInputTests
{
    const string U = Layers.Umpire;

    static void Survives(Table t, string what, Action feed, bool bigStack = false)
    {
        Exception? thrown = null;
        if (bigStack)
        {
            var th = new Thread(() => { try { feed(); } catch (Exception e) { thrown = e; } }, 256 * 1024 * 1024);
            th.Start();
            Assert.True(th.Join(TimeSpan.FromSeconds(60)), $"{what}: still running after a minute");
        }
        else
        {
            var run = Task.Run(() => { try { feed(); } catch (Exception e) { thrown = e; } });
            Assert.True(run.Wait(TimeSpan.FromSeconds(60)), $"{what}: still running after a minute");
        }
        Assert.True(thrown == null, $"{what}: threw out of the server: {thrown}");
        StillWhole(t, what);
    }

    static void StillWhole(Table t, string what)
    {
        var json = Json.Serialize(t.Server.State);
        Assert.Equal(json, Json.Serialize(Json.Deserialize<GameState>(json)));
        Assert.NotEmpty(t.Server.SaveBytes());
        var host = t.Server.Peer(Table.Host);
        Assert.True(host != null, $"{what}: the host is no longer at the table");
        var res = t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "marker", Pos = new(1, 1) });
        var err = res.Select(o => o.Message).OfType<ErrorMessage>().FirstOrDefault();
        Assert.True(err == null, $"{what}: the table no longer takes a change: {err?.Text}");
    }

    static Batch Nested(int depth)
    {
        Command c = new MovePiece { Layer = U, Id = "p-000002", Pos = new(2, 2) };
        for (int i = 0; i < depth; i++) c = new Batch { Items = { c } };
        return (Batch)c;
    }

    static readonly Dictionary<string, Action<Table>> Cases = new()
    {
        ["null command"] = t => t.Server.Handle(Table.Host, null!),
        ["null command from an unknown peer"] = t => t.Server.Handle(777, null!),
        ["batch with no list"] = t => t.Server.Handle(Table.Host, new Batch { Items = null! }),
        ["batch with a null item"] = t => t.Server.Handle(Table.Host, new Batch { Items = { new SetState { Layer = U, Id = "p-000002", State = 1 }, null! } }),
        ["batch nested 30 deep"] = t => t.Server.Handle(Table.Host, Nested(30)),
        ["batch nested 2000 deep"] = t => t.Server.Handle(Table.Host, Nested(2000)),
        ["batch of 20000 moves"] = t => t.Server.Handle(Table.Host, new Batch { Items = Enumerable.Range(0, 20000).Select(i => (Command)new MovePiece { Layer = U, Id = "p-000002", Pos = new(i % 7, i % 5) }).ToList() }),
        ["batch with an undo and a restore inside"] = t => t.Server.Handle(Table.Host, new Batch { Items = { new Undo { TargetSeq = 1 }, new RestoreTurn { Index = 0 } } }),
        ["spawn with null def and layer"] = t => t.Server.Handle(Table.Host, new SpawnPiece { Def = null!, Layer = null! }),
        ["spawn with a null tag"] = t => t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "inf", Tags = new() { "a", null! } }),
        ["spawn at NaN"] = t => t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "inf", Pos = new(double.NaN, 0) }),
        ["spawn at infinite scale"] = t => t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "inf", Scale = double.PositiveInfinity }),
        ["spawn over an existing id"] = t => t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "inf", Id = "p-000002" }),
        ["move with a null id"] = t => t.Server.Handle(Table.Host, new MovePiece { Layer = U, Id = null! }),
        ["move on a null layer"] = t => t.Server.Handle(Table.Host, new MovePiece { Layer = null!, Id = "p-000002" }),
        ["move to infinity"] = t => t.Server.Handle(Table.Host, new MovePiece { Layer = U, Id = "p-000002", Pos = new(double.PositiveInfinity, 1) }),
        ["move to a huge place"] = t => t.Server.Handle(Table.Host, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1e308, -1e308), RotDeg = 1e308, Y = 1e308 }),
        ["delete with a null id"] = t => t.Server.Handle(Table.Host, new DeletePiece { Layer = U, Id = null! }),
        ["delete on a null layer"] = t => t.Server.Handle(Table.Host, new DeletePiece { Layer = null!, Id = "p-000002" }),
        ["text with a null name"] = t => t.Server.Handle(Table.Host, new SetText { Layer = U, Id = "p-000002", Name = null! }),
        ["text with a null value"] = t => t.Server.Handle(Table.Host, new SetText { Layer = U, Id = "p-000002", Name = "name", Value = null! }),
        ["counter with a null name"] = t => t.Server.Handle(Table.Host, new SetCounter { Layer = U, Id = "p-000002", Name = null!, Value = 3 }),
        ["counter at int extremes"] = t =>
            { t.Server.Handle(Table.Host, new SetCounter { Layer = U, Id = "p-000002", Name = "str", Value = int.MinValue }); t.Server.Handle(Table.Host,
                new SetCounter { Layer = U, Id = "p-000003", Name = "x", Value = int.MaxValue }); },
        ["state at int extremes"] = t => { t.Server.Handle(Table.Host, new SetState { Layer = U, Id = "p-000002", State = int.MinValue }); t.Server.Handle(Table.Host, new SetState { Layer = U, Id = "p-000003", State = int.MaxValue }); },
        ["attach a piece to itself and to nothing"] = t => { t.Server.Handle(Table.Host, new Attach { Layer = U, Parent = "p-000002", Child = "p-000002" }); t.Server.Handle(Table.Host, new Attach { Layer = U, Parent = "p-404", Child = "p-000002" }); },
        ["attach in a ring then move"] = t =>
        {
            t.Server.Handle(Table.Host, new Attach { Layer = U, Parent = "p-000002", Child = "p-000003" });
            t.Server.Handle(Table.Host, new Attach { Layer = U, Parent = "p-000003", Child = "p-000002" });
            t.Server.Handle(Table.Host, new MovePiece { Layer = U, Id = "p-000002", Pos = new(4, 4), RotDeg = 90 });
        },
        ["detach a null child"] = t => t.Server.Handle(Table.Host, new Detach { Layer = U, Child = null! }),
        ["stow a null piece into a null holder"] = t => t.Server.Handle(Table.Host, new Stow { Layer = U, Holder = null!, Id = null! }),
        ["unstow a null piece from a null holder"] = t => t.Server.Handle(Table.Host, new Unstow { Layer = U, Holder = null!, Id = null! }),
        ["scale NaN and negative"] = t => { t.Server.Handle(Table.Host, new SetScale { Layer = U, Id = "p-000002", Scale = double.NaN }); t.Server.Handle(Table.Host, new SetScale { Layer = U, Id = "p-000002", Scale = -1 }); },
        ["label of ten megabytes"] = t => t.Server.Handle(Table.Host, new SetLabel { Layer = U, Id = "p-000002", Value = new string('x', 10_000_000) }),
        ["add a null sheet"] = t => t.Server.Handle(Table.Host, new AddSheet { Sheet = null! }),
        ["add a sheet twice under one id"] = t => { t.Server.Handle(Table.Host, new AddSheet { Sheet = new SheetDef { Id = "s-000001" } }); },
        ["rename a sheet to null"] = t => t.Server.Handle(Table.Host, new RenameSheet { Id = "s-000001", Name = null! }),
        ["crop a sheet to NaN"] = t => t.Server.Handle(Table.Host, new CropSheet { Id = "s-000001", Crop = new FaceBox { X = double.NaN, W = 0.5, H = 0.5 } }),
        ["move a sheet to a zero size"] = t => t.Server.Handle(Table.Host, new MoveSheet { Id = "s-000001", Size = new Size2(0, -1) }),
        ["draw a null stroke"] = t => t.Server.Handle(Table.Host, new Draw { Layer = U, Stroke = null! }),
        ["draw a stroke with no points"] = t => t.Server.Handle(Table.Host, new Draw { Layer = U, Stroke = new Stroke { Points = null! } }),
        ["draw through NaN"] = t => t.Server.Handle(Table.Host, new Draw { Layer = U, Stroke = new Stroke { Points = { new(0, 0), new(double.NaN, 1) } } }),
        ["erase a null stroke id"] = t => t.Server.Handle(Table.Host, new EraseStroke { Layer = U, StrokeId = null! }),
        ["rename and remove a null board"] = t =>
        {
            t.Server.Handle(Table.Host, new RenameBoard { Id = null!, Name = null! });
            t.Server.Handle(Table.Host, new RemoveBoard { Id = null! });
            t.Server.Handle(Table.Host, new AddBoard { Name = null! });
        },
        ["a null zone"] = t => t.Server.Handle(Table.Lead, new SetZone { Zone = null! }),
        ["a zone with no outline, then an umpire moves"] = t =>
        {
            t.Server.Handle(Table.Lead, new SetZone { Zone = new Zone { Owner = "Umpire Three", Polygon = null! } });
            t.Server.Handle(Table.Ump3, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) });
        },
        ["a zone with a NaN corner"] = t => t.Server.Handle(Table.Lead, new SetZone { Zone = new Zone { Owner = "Umpire Three", Polygon = { new(0, 0), new(double.NaN, 0), new(1, 1) } } }),
        ["a zone with a null owner, then an umpire moves"] = t =>
        {
            t.Server.Handle(Table.Lead, new SetZone { Zone = new Zone { Owner = null!, Polygon = { new(0, 0), new(5, 0), new(5, 5) } } });
            t.Server.Handle(Table.Ump3, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) });
        },
        ["remove a null zone"] = t => t.Server.Handle(Table.Lead, new RemoveZone { Id = null! }),
        ["dice at int extremes"] = t =>
            { t.Server.Handle(Table.Host, new RollDice { Sides = int.MaxValue, Count = int.MaxValue }); t.Server.Handle(Table.Host, new RollDice { Sides = int.MinValue, Count = int.MinValue }); },
        ["dice on a null list of pieces"] = t => t.Server.Handle(Table.Host, new RollDice { Layer = U, PieceIds = null! }),
        ["dice on pieces that are not there"] = t => t.Server.Handle(Table.Host, new RollDice { Layer = U, PieceIds = { "p-404", null!, "p-000002" } }),
        ["clock with a null time, then advanced"] = t => { t.Server.Handle(Table.Host, new SetClock { GameTime = null! }); t.Server.Handle(Table.Host, new AdvanceClock()); },
        ["clock at the end of time, then advanced"] = t => { t.Server.Handle(Table.Host, new SetClock { GameTime = "9999-12-31T23:00", IncrementMinutes = 120 }); t.Server.Handle(Table.Host, new AdvanceClock()); },
        ["clock with a huge step, then advanced"] = t => { t.Server.Handle(Table.Host, new SetClock { IncrementMinutes = int.MaxValue }); t.Server.Handle(Table.Host, new AdvanceClock()); },
        ["clock at a negative turn"] = t => t.Server.Handle(Table.Host, new SetClock { Turn = int.MinValue }),
        ["null lighting, wind and grid"] = t =>
        {
            t.Server.Handle(Table.Host, new SetLighting { Lighting = null! });
            t.Server.Handle(Table.Host, new SetWind { Wind = null! });
            t.Server.Handle(Table.Host, new SetGrid { Grid = null! });
        },
        ["NaN wind, calibration and vision"] = t =>
        {
            t.Server.Handle(Table.Host, new SetWind { Wind = new Wind { From = double.NaN } });
            t.Server.Handle(Table.Host, new SetCalibration { UnitsPerMeter = double.NaN });
            t.Server.Handle(Table.Host, new SetVisionRadius { Radius = double.NaN });
        },
        ["a pack with a null id"] = t => t.Server.Handle(Table.Host, new AddPack { Id = null!, Hash = null! }),
        ["a pack that is not there"] = t => t.Server.Handle(Table.Host, new AddPack { Id = "nowhere", Hash = "x" }),
        ["undo of nothing"] = t => { t.Server.Handle(Table.Host, new Undo { TargetSeq = -1 }); t.Server.Handle(Table.Host, new Undo { TargetSeq = long.MaxValue }); },
        ["undo twice"] = t =>
        {
            t.Server.Handle(Table.Host, new SetState { Layer = U, Id = "p-000002", State = 2 });
            var s = t.Server.State.LastSeq;
            t.Server.Handle(Table.Host, new Undo { TargetSeq = s });
            t.Server.Handle(Table.Host, new Undo { TargetSeq = s });
        },
        ["restore a turn that is not there"] = t => { t.Server.Handle(Table.Host, new RestoreTurn { Index = -1 }); t.Server.Handle(Table.Host, new RestoreTurn { Index = 999 }); },
        ["restore a turn with a state of its own"] = t => { t.Server.Handle(Table.Host, new AdvanceClock()); t.Server.Handle(Table.Host, new RestoreTurn { Index = 0, State = null }); },
        ["restore a null piece"] = t => t.Server.Handle(Table.Host, new RestorePiece { Layer = U, Piece = null! }),
        ["restore a piece full of nulls"] = t =>
            t.Server.Handle(Table.Host,
                new RestorePiece { Layer = U, Piece = new PieceInstance { Id = "p-000050", Counters = null!, Text = null!, Attached = null!, Widgets = null!, MenuItems = null!, Lines = null!, Ui = null! } }),
        ["a piece becomes a null kind"] = t => t.Server.Handle(Table.Host, new SetDef { Layer = U, Id = "p-000002", Def = null! }),
        ["a piece script of a megabyte"] = t => t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000002", Text = "-- " + new string('x', 1_000_000) }),
        ["a global script that does not compile"] = t => t.Server.Handle(Table.Host, new SetGlobalScript { Text = "function (((( end" }),
        ["widget and menu clicks on nothing"] = t =>
        {
            t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = "p-000002", Index = int.MaxValue });
            t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = "p-000002", Index = 1, Value = null! });
            t.Server.Handle(Table.Host, new PickMenuItem { Layer = U, Id = null!, Index = -1 });
            t.Server.Handle(Table.Host, new PickGlobalMenuItem { Index = int.MinValue });
            t.Server.Handle(Table.Host, new Hotkey { Index = 1, Selected = null! });
            t.Server.Handle(Table.Host, new NumberTyped { Layer = U, Id = null!, Number = int.MaxValue });
            t.Server.Handle(Table.Host, new HoldPiece { Layer = U, Id = "p-404", Held = true });
        },
        ["panel events and dialog answers with nulls"] = t =>
        {
            t.Server.Handle(Table.Host, new UiEvent { Id = null!, Event = null!, Value = null! });
            t.Server.Handle(Table.Host, new DialogResult { Token = null!, Value = null });
            t.Server.Handle(Table.Host, new Say { Text = null!, To = null, Color = null });
        },
        ["runtime-only commands from a peer"] = t =>
        {
            foreach (Command c in new Command[] { new SetUi { Tree = null! }, new SetWidgets { Id = "p-000002", Widgets = null! }, new SelectPiece(), new ShowDialog(), new SetHotkeys { Items = null! } })
                t.Server.Handle(Table.Host, c);
        },
        ["everything from an observer"] = t =>
        {
            foreach (var (type, _) in CommandAnswersSnapshotTests.Registered()) t.Server.Handle(Table.Obs, (Command)Activator.CreateInstance(type)!);
        },
        ["a null knock"] = t => t.Server.Join(40, null!),
        ["a knock full of nulls"] = t => t.Server.Join(41, new JoinRequest { Name = null!, Password = null!, Token = null, Protocol = ProtocolVersion.Current }),
        ["a knock with a huge name and a wrong token"] = t => t.Server.Join(42, new JoinRequest { Name = new string('n', 1_000_000), Password = Table.Password, Token = "not-a-token", Protocol = ProtocolVersion.Current }),
        ["the same peer knocks twice and is let in twice"] = t =>
        {
            t.Server.Join(43, new JoinRequest { Name = "Twice", Password = Table.Password, Protocol = ProtocolVersion.Current });
            t.Server.Join(43, new JoinRequest { Name = "Twice", Password = Table.Password, Protocol = ProtocolVersion.Current });
            t.Server.Control(Table.Host, new Admit { PeerId = 43, Role = Role.Umpire });
            t.Server.Control(Table.Host, new Admit { PeerId = 43, Role = Role.Umpire });
        },
        ["the host's id knocks again"] = t => t.Server.Join(Table.Host, new JoinRequest { Name = "Impostor" }),
        ["peers 0 and negative knock"] = t =>
            { t.Server.Join(0, new JoinRequest { Name = "zero", Password = Table.Password, Protocol = ProtocolVersion.Current }); t.Server.Join(-5,
                new JoinRequest { Name = "minus", Password = Table.Password, Protocol = ProtocolVersion.Current }); },
        ["three hundred knocks"] = t =>
        {
            for (int i = 0; i < 300; i++) t.Server.Join(1000 + i, new JoinRequest { Name = "k", Password = Table.Password, Protocol = ProtocolVersion.Current });
        },
        ["a null door message"] = t => t.Server.Control(Table.Host, null!),
        ["door messages from nobody"] = t => { t.Server.Control(777, new Admit { PeerId = 1 }); t.Server.Control(Table.Obs, new SetPassword { Password = "x" }); },
        ["admit, role and revoke with odd ids and roles"] = t =>
        {
            t.Server.Join(44, new JoinRequest { Name = "Odd", Password = Table.Password, Protocol = ProtocolVersion.Current });
            t.Server.Control(Table.Host, new Admit { PeerId = 44, Role = (Role)99 });
            t.Server.Control(Table.Host, new SetRole { PeerId = Table.Ump3, Role = (Role)(-1) });
            t.Server.Control(Table.Host, new SetRole { PeerId = Table.Host, Role = Role.Observer });
            t.Server.Control(Table.Host, new Revoke { PeerId = Table.Host });
            t.Server.Control(Table.Host, new Revoke { PeerId = 404 });
            t.Server.Control(Table.Host, new Refuse { PeerId = 404 });
        },
        ["a null password"] = t => t.Server.Control(Table.Host, new SetPassword { Password = null! }),
        ["every message through the door"] = t => { foreach (var m in AllMessages()) t.Server.Control(Table.Host, m); },
        ["a null request"] = t => t.Server.Request(Table.Host, null!),
        ["lua with nulls, garbage and a loop"] = t =>
        {
            t.Server.Request(Table.Host, new RunLua { Target = null!, Code = null! });
            t.Server.Request(Table.Host, new RunLua { Target = "p-404", Code = "x" });
            t.Server.Request(Table.Host, new RunLua { Target = "", Code = "(((( end" });
            t.Server.Request(Table.Host, new RunLua { Target = "", Code = "error('boom')" });
            t.Server.Request(Table.Host, new RunLua { Target = "", Code = "while true do end" });
        },
        ["every message as a request"] = t => { foreach (var m in AllMessages()) t.Server.Request(Table.Host, m); },
        ["turn history out of range"] = t =>
        {
            t.Server.TurnSnapshot(Table.Host, -1);
            t.Server.TurnSnapshot(Table.Host, int.MaxValue);
            t.Server.TurnHistory(Table.Obs);
            t.Server.TurnHistory(777);
            t.Server.Resync(777);
            t.Server.Leave(777);
        },
    };

    static IEnumerable<Message> AllMessages() =>
        typeof(Message).GetCustomAttributes<System.Text.Json.Serialization.JsonDerivedTypeAttribute>().Select(a => (Message)Activator.CreateInstance(a.DerivedType)!);

    [Fact]
    public void Every_case_has_a_row()
    {
        var rows = typeof(MalformedInputTests).GetMethod(nameof(Garbage_never_throws_out_of_the_server))!
            .GetCustomAttributes<InlineDataAttribute>().Select(a => (string)a.GetData(null!).First()[0]).ToHashSet();
        Assert.Empty(Cases.Keys.Except(rows));
        Assert.Empty(rows.Except(Cases.Keys));
    }

    [Theory]
    [InlineData("null command")]
    [InlineData("null command from an unknown peer")]
    [InlineData("batch with no list")]
    [InlineData("batch with a null item")]
    [InlineData("batch nested 30 deep")]
    [InlineData("batch nested 2000 deep")]
    [InlineData("batch of 20000 moves")]
    [InlineData("batch with an undo and a restore inside")]
    [InlineData("spawn with null def and layer")]
    [InlineData("spawn with a null tag")]
    [InlineData("spawn at NaN")]
    [InlineData("spawn at infinite scale")]
    [InlineData("spawn over an existing id")]
    [InlineData("move with a null id")]
    [InlineData("move on a null layer")]
    [InlineData("move to infinity")]
    [InlineData("move to a huge place")]
    [InlineData("delete with a null id")]
    [InlineData("delete on a null layer")]
    [InlineData("text with a null name")]
    [InlineData("text with a null value")]
    [InlineData("counter with a null name")]
    [InlineData("counter at int extremes")]
    [InlineData("state at int extremes")]
    [InlineData("attach a piece to itself and to nothing")]
    [InlineData("attach in a ring then move")]
    [InlineData("detach a null child")]
    [InlineData("stow a null piece into a null holder")]
    [InlineData("unstow a null piece from a null holder")]
    [InlineData("scale NaN and negative")]
    [InlineData("label of ten megabytes")]
    [InlineData("add a null sheet")]
    [InlineData("add a sheet twice under one id")]
    [InlineData("rename a sheet to null")]
    [InlineData("crop a sheet to NaN")]
    [InlineData("move a sheet to a zero size")]
    [InlineData("draw a null stroke")]
    [InlineData("draw a stroke with no points")]
    [InlineData("draw through NaN")]
    [InlineData("erase a null stroke id")]
    [InlineData("rename and remove a null board")]
    [InlineData("a null zone")]
    [InlineData("a zone with no outline, then an umpire moves")]
    [InlineData("a zone with a NaN corner")]
    [InlineData("a zone with a null owner, then an umpire moves")]
    [InlineData("remove a null zone")]
    [InlineData("dice at int extremes")]
    [InlineData("dice on a null list of pieces")]
    [InlineData("dice on pieces that are not there")]
    [InlineData("clock with a null time, then advanced")]
    [InlineData("clock at the end of time, then advanced")]
    [InlineData("clock with a huge step, then advanced")]
    [InlineData("clock at a negative turn")]
    [InlineData("null lighting, wind and grid")]
    [InlineData("NaN wind, calibration and vision")]
    [InlineData("a pack with a null id")]
    [InlineData("a pack that is not there")]
    [InlineData("undo of nothing")]
    [InlineData("undo twice")]
    [InlineData("restore a turn that is not there")]
    [InlineData("restore a turn with a state of its own")]
    [InlineData("restore a null piece")]
    [InlineData("restore a piece full of nulls")]
    [InlineData("a piece becomes a null kind")]
    [InlineData("a piece script of a megabyte")]
    [InlineData("a global script that does not compile")]
    [InlineData("widget and menu clicks on nothing")]
    [InlineData("panel events and dialog answers with nulls")]
    [InlineData("runtime-only commands from a peer")]
    [InlineData("everything from an observer")]
    [InlineData("a null knock")]
    [InlineData("a knock full of nulls")]
    [InlineData("a knock with a huge name and a wrong token")]
    [InlineData("the same peer knocks twice and is let in twice")]
    [InlineData("the host's id knocks again")]
    [InlineData("peers 0 and negative knock")]
    [InlineData("three hundred knocks")]
    [InlineData("a null door message")]
    [InlineData("door messages from nobody")]
    [InlineData("admit, role and revoke with odd ids and roles")]
    [InlineData("a null password")]
    [InlineData("every message through the door")]
    [InlineData("a null request")]
    [InlineData("lua with nulls, garbage and a loop")]
    [InlineData("every message as a request")]
    [InlineData("turn history out of range")]
    public void Garbage_never_throws_out_of_the_server(string name)
    {
        using var t = new Table();
        Survives(t, name, () => Cases[name](t), bigStack: name.Contains("2000 deep"));
    }

    [Fact]
    public void A_missing_field_is_refused_in_words_and_changes_nothing()
    {
        using var t = new Table();
        var before = t.StateJson;
        string Refused(Command c) => Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Handle(Table.Host, c)).Message).Text;
        Assert.Equal("the message carried no command", Refused(null!));
        Assert.Equal("MovePiece refused: layer is missing", Refused(new MovePiece { Layer = null!, Id = "p-000002" }));
        Assert.Equal("SetClock refused: gameTime is missing", Refused(new SetClock { GameTime = null! }));
        Assert.Equal("RollDice refused: label is missing", Refused(new RollDice { Label = null! }));
        Assert.Equal("SpawnPiece refused: tags[1] is missing", Refused(new SpawnPiece { Layer = U, Def = "marker", Tags = new() { "a", null! } }));
        Assert.Equal("RestorePiece refused: piece.counters is missing", Refused(new RestorePiece { Layer = U, Piece = new PieceInstance { Id = "p-000009", Def = "marker", Counters = null! } }));
        Assert.Equal(before, t.StateJson);
        Assert.Contains("missing", Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Control(Table.Host, new SetPassword { Password = null! })).Message).Text);
        Assert.Equal(Table.Password, t.Server.Access.Password);
        Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Control(Table.Host, null!)).Message);
        Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Request(Table.Host, null!)).Message);
    }

    [Fact]
    public void The_clock_stops_at_the_end_of_the_calendar_and_says_so()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetClock { Turn = 4, GameTime = "9999-12-31T23:00", IncrementMinutes = 120 });
        var err = t.Server.Handle(Table.Host, new AdvanceClock()).Select(o => o.Message).OfType<ErrorMessage>().Single();
        Assert.Contains("9999", err.Text);
        Assert.Equal(4, t.Server.State.Clock.Turn);
        Assert.Equal("9999-12-31T23:00", t.Server.State.Clock.GameTime);
        t.Ok(Table.Host, new SetClock { Turn = 4, GameTime = "9999-12-31T21:00", IncrementMinutes = 120 });
        t.Ok(Table.Host, new AdvanceClock());
        Assert.Equal("9999-12-31T23:00", t.Server.State.Clock.GameTime);
    }

    static IEnumerable<(string variant, Command c)> Variants(Type type, bool nulls)
    {
        Command Make(Action<object> fill)
        {
            var c = (Command)Activator.CreateInstance(type)!;
            fill(c);
            return c;
        }
        if (nulls)
        {
            yield return ("references null", Make(o => Fill(o, nulls: true)));
            yield return ("nulls one level in", Make(o => Fill(o, inner: true)));
            yield return ("a null in every list", Make(o => Fill(o, nullItems: true)));
            yield break;
        }
        yield return ("defaults", Make(_ => { }));
        yield return ("numbers NaN", Make(o => Fill(o, number: double.NaN)));
        yield return ("numbers at their maximum", Make(o => Fill(o, number: double.MaxValue)));
        yield return ("numbers at their minimum", Make(o => Fill(o, number: double.MinValue)));
        yield return ("huge strings", Make(o => Fill(o, text: new string('w', 200_000) + "\0\uFFFF\uD800")));
        yield return ("aimed at a piece, numbers at their maximum", Make(o => Fill(o, text: "p-000002", number: double.MaxValue)));
        yield return ("aimed at a piece, numbers at their minimum", Make(o => Fill(o, text: "p-000002", number: double.MinValue)));
    }


    static void Fill(object o, bool nulls = false, bool inner = false, bool nullItems = false, double? number = null, string? text = null)
    {
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            if (p.DeclaringType == typeof(Command)) continue;
            var t = p.PropertyType;
            if (p.Name == nameof(LayerCommand.Layer) && text != null) continue;
            if (nulls && !t.IsValueType) { p.SetValue(o, null); continue; }
            if (inner && !t.IsValueType && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            {
                var v = Activator.CreateInstance(t)!;
                foreach (var q in t.GetProperties().Where(q => q.CanWrite && !q.PropertyType.IsValueType)) q.SetValue(v, null);
                p.SetValue(o, v);
                continue;
            }
            if (nullItems && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) && !t.GetGenericArguments()[0].IsValueType)
            {
                var list = (IList)Activator.CreateInstance(t)!;
                list.Add(null);
                p.SetValue(o, list);
                continue;
            }
            if (text != null && t == typeof(string)) p.SetValue(o, text);
            if (text != null && t == typeof(List<string>)) p.SetValue(o, new List<string> { text });
            if (number is { } n)
            {
                var u = Nullable.GetUnderlyingType(t) ?? t;
                if (u == typeof(double)) p.SetValue(o, n);
                else if (u == typeof(int)) p.SetValue(o, double.IsNaN(n) ? 0 : n > 0 ? int.MaxValue : int.MinValue);
                else if (u == typeof(long)) p.SetValue(o, double.IsNaN(n) ? 0L : n > 0 ? long.MaxValue : long.MinValue);
                else if (u == typeof(Vec2)) p.SetValue(o, new Vec2(n, n));
                else if (u == typeof(Size2)) p.SetValue(o, new Size2(n, n));
            }
        }
    }

    public static IEnumerable<object[]> CommandTypes() => CommandAnswersSnapshotTests.Registered().Select(r => new object[] { r.name });

    static void EveryVariant(string type, bool nulls)
    {
        var cmdType = CommandAnswersSnapshotTests.Registered().Single(r => r.name == type).type;
        using var t = new Table();
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Name = "SW", Owner = "Umpire Three", Polygon = { new(-5, -5), new(5, -5), new(5, 5), new(-5, 5) } } });
        var failed = new List<string>();
        foreach (var peer in new[] { Table.Host, Table.Ump3 })
            foreach (var (variant, c) in Variants(cmdType, nulls))
            {
                try { Survives(t, $"{type} ({variant}) from peer {peer}", () => t.Server.Handle(peer, c)); }
                catch (Xunit.Sdk.XunitException e) { failed.Add(string.Join(" ", e.Message.Split('\n').Take(2)).Trim()); }
            }
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }

    [Theory]
    [MemberData(nameof(CommandTypes))]
    public void Every_command_with_bad_numbers_or_huge_strings_never_throws_out_of_the_server(string type) => EveryVariant(type, nulls: false);

    [Fact]
    public void Every_command_type_has_a_null_row()
    {
        var rows = typeof(MalformedInputTests).GetMethod(nameof(Every_command_with_nulls_in_it_never_throws_out_of_the_server))!
            .GetCustomAttributes<InlineDataAttribute>().Select(a => (string)a.GetData(null!).First()[0]).ToHashSet();
        Assert.Equal(CommandAnswersSnapshotTests.Registered().Select(r => r.name).ToHashSet(), rows);
    }

    [Theory]
    [InlineData("AddBoard")]
    [InlineData("AddPack")]
    [InlineData("AddSheet")]
    [InlineData("AdvanceClock")]
    [InlineData("Attach")]
    [InlineData("Batch")]
    [InlineData("ClickWidget")]
    [InlineData("CropSheet")]
    [InlineData("DeletePiece")]
    [InlineData("Detach")]
    [InlineData("Stow")]
    [InlineData("Unstow")]
    [InlineData("DialogResult")]
    [InlineData("Draw")]
    [InlineData("EditWidget")]
    [InlineData("EraseStroke")]
    [InlineData("StampDecal")]
    [InlineData("RemoveDecal")]
    [InlineData("SetDecalPalette")]
    [InlineData("FlipPiece")]
    [InlineData("HoldPiece")]
    [InlineData("Hotkey")]
    [InlineData("LoadSave")]
    [InlineData("LockSheet")]
    [InlineData("MovePiece")]
    [InlineData("MoveSheet")]
    [InlineData("NumberTyped")]
    [InlineData("PickGlobalMenuItem")]
    [InlineData("PickMenuItem")]
    [InlineData("PublishSighting")]
    [InlineData("RemoveBoard")]
    [InlineData("RemoveSheet")]
    [InlineData("RemoveZone")]
    [InlineData("RenameBoard")]
    [InlineData("RenameSheet")]
    [InlineData("RestorePiece")]
    [InlineData("RestoreTurn")]
    [InlineData("RollDice")]
    [InlineData("Say")]
    [InlineData("SelectPiece")]
    [InlineData("SetCalibration")]
    [InlineData("SetClock")]
    [InlineData("SetCounter")]
    [InlineData("SetDecals")]
    [InlineData("SetDef")]
    [InlineData("SetGlobalMenuItems")]
    [InlineData("SetGlobalScript")]
    [InlineData("SetGlobalScriptState")]
    [InlineData("SetGrid")]
    [InlineData("SetHotkeys")]
    [InlineData("SetLabel")]
    [InlineData("SetLighting")]
    [InlineData("SetLines")]
    [InlineData("SetLocked")]
    [InlineData("SetPieceOption")]
    [InlineData("SetPointerRule")]
    [InlineData("SetBriefing")]
    [InlineData("SetMemo")]
    [InlineData("SetMenuItems")]
    [InlineData("SetNote")]
    [InlineData("SetPicture")]
    [InlineData("SetScale")]
    [InlineData("SetScript")]
    [InlineData("SetScriptState")]
    [InlineData("SetSheetPicture")]
    [InlineData("SetSheetRelief")]
    [InlineData("SetState")]
    [InlineData("SetTags")]
    [InlineData("SetText")]
    [InlineData("SetTint")]
    [InlineData("SetUi")]
    [InlineData("SetUiAttributes")]
    [InlineData("SetVisionRadius")]
    [InlineData("SetWidgets")]
    [InlineData("SetWind")]
    [InlineData("SetZone")]
    [InlineData("ShowDialog")]
    [InlineData("SpawnPiece")]
    [InlineData("UiEvent")]
    [InlineData("Undo")]
    public void Every_command_with_nulls_in_it_never_throws_out_of_the_server(string type) => EveryVariant(type, nulls: true);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTests { public const string Name = "Heavy garbage, run alone"; }
