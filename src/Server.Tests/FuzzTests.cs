using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

[Collection(HeavyTests.Name)]
public class FuzzTests
{
    public static IEnumerable<object[]> Seeds() => new[] { 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233 }.Select(s => new object[] { s });

    const string U = Layers.Umpire;

    internal sealed class Gen
    {
        public readonly Random Rng;
        readonly GameServer _s;
        int _n;
        public Gen(int seed, GameServer s) { Rng = new Random(seed); _s = s; }

        double D(double lo, double hi) => Math.Round(lo + Rng.NextDouble() * (hi - lo), 2);
        Vec2 P() => new(D(-20, 20), D(-20, 20));
        T Pick<T>(IReadOnlyList<T> l) => l[Rng.Next(l.Count)];
        bool Coin() => Rng.Next(2) == 0;
        string Text() => Pick(new[] { "", "Pogue", "Boggs' 2nd", "  spaced  ", "ünïcødé ✓", "line\nbreak", "\"quoted\"" }) + (_n++ % 7 == 0 ? _n.ToString() : "");

        List<PieceInstance> Pieces => _s.State.Layer(U);
        string? PieceId() => Pieces.Count == 0 ? null : Pick(Pieces).Id;

        public static readonly string[] Kinds =
        {
            "spawn", "move", "delete", "state", "counter", "text", "label", "note", "tint", "lock", "scale", "attach", "detach", "flip",
            "addsheet", "movesheet", "locksheet", "renamesheet", "removesheet", "cropsheet", "zone", "removezone", "draw", "erase",
            "addboard", "renameboard", "removeboard", "dice", "dicepieces", "clock", "advance", "batch",
        };

        public Command Next(bool zones = true, bool batch = true)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                var kind = Pick(Kinds);
                if (!zones && kind is "zone" or "removezone") continue;
                if (!batch && kind == "batch") continue;
                if (Make(kind) is { } c) return c;
            }
            return Spawn();
        }

        Command Spawn() => new SpawnPiece { Layer = U, Def = Pick(new[] { "inf", "marker", "die6" }), Pos = P(), RotDeg = D(0, 359), Label = Coin() ? Text() : null, Scale = D(0.5, 2) };

        public Command? Make(string kind)
        {
            var id = PieceId();
            var sheets = _s.State.Sheets;
            var strokes = _s.State.DrawLayer(U);
            var boards = _s.State.Boards;
            switch (kind)
            {
                case "spawn": return Spawn();
                case "move":
                {
                    if (id == null) return null;
                    var y = Coin() ? 0 : D(0, 2);
                    return new MovePiece { Layer = U, Id = id, Pos = P(), RotDeg = D(0, 359), Y = y };
                }
                case "delete": return id == null ? null : new DeletePiece { Layer = U, Id = id };
                case "state": return id == null ? null : new SetState { Layer = U, Id = id, State = Rng.Next(0, 6) };
                case "counter": return id == null ? null : new SetCounter { Layer = U, Id = id, Name = Pick(new[] { "str", "ammo", "x" }), Value = Rng.Next(-5, 20) };
                case "text":
                {
                    if (id == null) return null;
                    var name = Pick(new[] { "name", "orders" });
                    return new SetText { Layer = U, Id = id, Name = name, Value = Text() };
                }
                case "label": return id == null ? null : new SetLabel { Layer = U, Id = id, Value = Coin() ? Text() : null };
                case "note": return id == null ? null : new SetNote { Layer = U, Id = id, Value = Coin() ? Text() : null };
                case "tint": return id == null ? null : new SetTint { Layer = U, Id = id, Value = Coin() ? $"#{Rng.Next(0x1000000):X6}" : null };
                case "lock": return id == null ? null : new SetLocked { Layer = U, Id = id, Value = Coin() };
                case "scale": return id == null ? null : new SetScale { Layer = U, Id = id, Scale = D(0.1, 5) };
                case "flip": return id == null ? null : new FlipPiece { Layer = U, Id = id, FaceDown = Coin() };
                case "attach":
                {
                    if (Pieces.Count < 2) return null;
                    var a = Pick(Pieces).Id;
                    var b = Pick(Pieces).Id;
                    return a == b ? null : new Attach { Layer = U, Parent = a, Child = b };
                }
                case "detach":
                {
                    var attached = Pieces.SelectMany(p => p.Attached).ToList();
                    return attached.Count == 0 ? null : new Detach { Layer = U, Child = Pick(attached) };
                }
                case "addsheet": return new AddSheet { Sheet = new SheetDef { Name = Text(), Image = "sha", Package = "testpack", Pos = P(), Size = new(D(5, 50), D(5, 50)), Locked = Coin() } };
                case "movesheet" : return sheets.Count == 0 ? null
                    : new MoveSheet { Id = Pick(sheets).Id, Pos = P(), RotDeg = D(0, 359), Z = Rng.Next(-2, 3), Size = Coin() ? null : new Size2(D(5, 50), D(5, 50)) };
                case "locksheet": return sheets.Count == 0 ? null : new LockSheet { Id = Pick(sheets).Id, Locked = Coin() };
                case "renamesheet": return sheets.Count == 0 ? null : new RenameSheet { Id = Pick(sheets).Id, Name = Text() };
                case "removesheet": return sheets.Count == 0 ? null : new RemoveSheet { Id = Pick(sheets).Id };
                case "cropsheet":
                {
                    if (sheets.Count == 0) return null;
                    double x = D(0, 0.4), y = D(0, 0.4);
                    return new CropSheet { Id = Pick(sheets).Id, Crop = Coin() ? null : new FaceBox { X = x, Y = y, W = D(0.1, 0.6), H = D(0.1, 0.6) } };
                }
                case "zone":
                {
                    var c = P();
                    var w = D(2, 15);
                    return new SetZone { Zone = new Zone { Name = Text(), Owner = Pick(new[] { "Umpire Three", "Umpire Two", "Nobody" }), Polygon = { c, new(c.X + w, c.Z), new(c.X + w, c.Z + w), new(c.X, c.Z + w) } } };
                }
                case "removezone": return _s.State.Zones.Count == 0 ? null : new RemoveZone { Id = Pick(_s.State.Zones).Id };
                case "draw" : return new Draw { Layer = U,
                    Stroke =
                        new Stroke { Points = Enumerable.Range(0, Rng.Next(2, 6)).Select(_ => P()).ToList(), Width = D(0.1, 1), Color = $"#{Rng.Next(0x1000000):X6}", Board = boards.Count > 0 && Coin() ? Pick(boards).Id : "" } };
                case "erase": return strokes.Count == 0 ? null : new EraseStroke { Layer = U, StrokeId = Pick(strokes).Id };
                case "addboard": return new AddBoard { Name = Coin() ? Text() : "" };
                case "renameboard": return boards.Count == 0 ? null : new RenameBoard { Id = Pick(boards).Id, Name = Text() };
                case "removeboard": return boards.Count == 0 ? null : new RemoveBoard { Id = Pick(boards).Id };
                case "dice": return new RollDice { Sides = Pick(new[] { 6, 10, 20 }), Count = Rng.Next(1, 4), Label = Text() };
                case "dicepieces":
                {
                    var dice = Pieces.Where(p => p.Def == "die6").Select(p => p.Id).ToList();
                    return dice.Count == 0 ? null : new RollDice { Layer = U, PieceIds = dice.Take(3).ToList() };
                }
                case "clock": return new SetClock { Turn = Math.Max(0, _s.State.Clock.Turn + Rng.Next(-1, 3)), GameTime = $"1863-07-0{Rng.Next(1, 4)}T{Rng.Next(5, 20):D2}:00", IncrementMinutes = Pick(new[] { 10, 15, 30 }) };
                case "advance": return new AdvanceClock();
                case "batch":
                {
                    var b = new Batch();
                    for (int i = Rng.Next(2, 5); i > 0; i--)
                        if (Make(Pick(Kinds.Where(k => k is not ("batch" or "zone" or "removezone")).ToList())) is { } item) b.Items.Add(item);
                    return b.Items.Count == 0 ? null : b;
                }
            }
            throw new ArgumentException(kind);
        }
    }

    static bool Refused(List<Outgoing> res, out string? why)
    {
        why = res.Select(o => o.Message).OfType<ErrorMessage>().FirstOrDefault()?.Text;
        return why != null;
    }

    internal static string Canon(GameState g, bool counters = false)
    {
        var n = JsonNode.Parse(Json.Serialize(g))!.AsObject();
        if (!counters)
        {
            n.Remove("lastSeq");
            n.Remove("nextId");
            n.Remove("dice");
        }
        foreach (var layer in n["layers"]!.AsObject().Select(kv => kv.Value!.AsArray()))
        {
            foreach (var p in layer) Sort(p!["attached"]!.AsArray(), x => x!.GetValue<string>());
        }
        return Round(n)!.ToJsonString();
    }

    static void Sort(JsonArray a, Func<JsonNode?, string> key)
    {
        var items = a.Select(x => x?.DeepClone()).OrderBy(key, StringComparer.Ordinal).ToList();
        a.Clear();
        foreach (var x in items) a.Add(x);
    }

    static JsonNode? Round(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var k in o.Select(kv => kv.Key).ToList()) o[k] = Round(o[k]?.DeepClone());
                return o;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++) a[i] = Round(a[i]?.DeepClone());
                return a;
            case JsonValue v when v.TryGetValue<double>(out var d) && v.GetValueKind() == System.Text.Json.JsonValueKind.Number:
                var r = Math.Round(d, 9);
                return JsonValue.Create(r == 0 ? 0.0 : r);
            default:
                return n;
        }
    }

    static IEnumerable<Command> Items(Command c) => c is Batch b ? b.Items.SelectMany(Items) : new[] { c };

    static string Trail(List<string> log) => "\n" + string.Join("\n", log.TakeLast(12));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_refused_command_changes_nothing_and_an_undo_puts_the_table_back(int seed)
    {
        using var t = new Table();
        var s = t.Server;
        var gen = new Gen(seed, s);
        var log = new List<string>();
        int undone = 0;
        for (int i = 0; i < 150; i++)
        {
            var c = gen.Next();
            var exact = Canon(s.State, counters: true);
            var before = Canon(s.State);
            var removed = Items(c).OfType<RemoveBoard>().ToList();
            bool erasesDrawings = removed.Any(rb => s.State.Drawings.Values.Any(l => l.Any(st => st.Board == rb.Id))) || removed.Count > 0 && Items(c).OfType<Draw>().Any();
            log.Add(Json.Serialize<Command>(c));
            if (Refused(s.Handle(Table.Host, c), out _))
            {
                var now = Canon(s.State, counters: true);
                Assert.True(exact == now, $"seed {seed} step {i}: a refused command changed the table" + Trail(log) + Diff(exact, now));
                continue;
            }
            if (erasesDrawings) continue;
            if (gen.Rng.Next(3) == 0) continue;
            var seq = s.State.LastSeq;
            log.Add($"undo {seq}");
            if (Refused(s.Handle(Table.Host, new Undo { TargetSeq = seq }), out var why))
            {
                Assert.True(c is RollDice, $"seed {seed} step {i}: {c.GetType().Name} could not be undone ({why})" + Trail(log));
                continue;
            }
            var after = Canon(s.State);
            Assert.True(before == after, $"seed {seed} step {i}: undoing {c.GetType().Name} did not put the table back" + Trail(log) + Diff(before, after));
            undone++;
        }
        Assert.True(undone >= 20, $"seed {seed}: only {undone} undos were checked; the generator has gone stale");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_batch_with_a_failing_item_leaves_the_table_as_it_was(int seed)
    {
        using var t = new Table();
        var s = t.Server;
        var gen = new Gen(seed, s);
        var log = new List<string>();
        for (int i = 0; i < 80; i++)
        {
            var c = gen.Next();
            log.Add(Json.Serialize<Command>(c));
            s.Handle(Table.Host, c);
            var b = new Batch();
            for (int k = gen.Rng.Next(1, 5); k > 0; k--) b.Items.Add(gen.Next(batch: false, zones: false));
            Command bad = gen.Rng.Next(3) switch
            {
                0 => new MovePiece { Layer = U, Id = "p-999999", Pos = new(1, 1) },
                1 => new SetScale { Layer = U, Id = s.State.Layer(U).FirstOrDefault()?.Id ?? "p-999999", Scale = 500 },
                _ => new EraseStroke { Layer = U, StrokeId = "d-999999" },
            };
            b.Items.Insert(gen.Rng.Next(b.Items.Count + 1), bad);
            var exact = Canon(s.State, counters: true);
            log.Add(Json.Serialize<Command>(b));
            Assert.True(Refused(s.Handle(Table.Host, b), out _), $"seed {seed} step {i}: a batch with a failing item was taken" + Trail(log));
            var after = Canon(s.State, counters: true);
            Assert.True(exact == after, $"seed {seed} step {i}: a refused batch left a trace" + Trail(log) + Diff(exact, after));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_command_and_every_table_survive_a_JSON_round_trip(int seed)
    {
        using var t = new Table();
        var s = t.Server;
        var gen = new Gen(seed, s);
        for (int i = 0; i < 120; i++)
        {
            var c = gen.Next();
            RoundTrips(c, seed, i);
            var res = s.Handle(gen.Rng.Next(4) == 0 ? Table.Ump3 : Table.Host, c);
            RoundTrips(c, seed, i);
            foreach (var o in res)
            {
                var json = Json.Serialize<Message>(o.Message);
                Assert.Equal(json, Json.Serialize(Json.Deserialize<Message>(json)));
            }
            var st = Json.Serialize(s.State);
            Assert.True(st == Json.Serialize(Json.Deserialize<GameState>(st)), $"seed {seed} step {i}: the table does not survive a round trip");
        }
    }

    static void RoundTrips(Command c, int seed, int step)
    {
        var json = Json.Serialize<Command>(c);
        var back = Json.Deserialize<Command>(json);
        Assert.Equal(c.GetType(), back.GetType());
        Assert.True(json == Json.Serialize(back), $"seed {seed} step {step}: {c.GetType().Name} does not survive a round trip\n{json}\n{Json.Serialize(back)}");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void An_observer_never_receives_a_zone(int seed)
    {
        using var t = new Table();
        var s = t.Server;
        var gen = new Gen(seed, s);
        var log = new List<string>();
        var obs = s.Peer(Table.Obs)!;
        for (int i = 0; i < 150; i++)
        {
            Command c = gen.Rng.Next(10) switch
            {
                0 => gen.Make("zone")!,
                1 when s.State.LastSeq > 0 => new Undo { TargetSeq = s.State.LastSeq - gen.Rng.Next(0, 3) },
                2 when s.Turns.Count > 0 => new RestoreTurn { Index = gen.Rng.Next(s.Turns.Count) },
                3 => new Batch { Items = { gen.Make("zone")!, gen.Next(batch: false, zones: false) } },
                _ => gen.Next(),
            };
            log.Add(Json.Serialize<Command>(c));
            var res = s.Handle(gen.Rng.Next(3) == 0 ? Table.Lead : Table.Host, c);
            foreach (var o in res.Where(o => o.PeerId == Table.Obs))
                Assert.True(!CarriesZone(o.Message, s.State), $"seed {seed} step {i}: the observer was sent a zone: {Json.Serialize(o.Message)}" + Trail(log));
            Assert.Empty(Visibility.Filter(s.State, obs).Zones);
            foreach (var o in s.Resync(Table.Obs)) Assert.False(CarriesZone(o.Message, s.State));
        }
        Assert.NotEmpty(s.State.Zones.Count > 0 ? s.State.Zones : s.Log.OfType<SetZone>().Select(z => z.Zone));
    }

    static bool CarriesZone(Message m, GameState g)
    {
        bool InCommand(Command c) => c switch
        {
            SetZone or RemoveZone => true,
            Batch b => b.Items.Any(InCommand),
            RestoreTurn rt => rt.State?.Zones.Count > 0,
            _ => false,
        };
        if (m is CommandMessage cm && InCommand(cm.Command)) return true;
        if (m is Snapshot sn && sn.View.Zones.Count > 0) return true;
        if (m is JoinResult jr && jr.View?.Zones.Count > 0) return true;
        if (m is RoleChanged rc && rc.View.Zones.Count > 0) return true;
        var json = Json.Serialize(m);
        return g.Zones.Any(z => json.Contains(z.Id, StringComparison.Ordinal));
    }

    static string Piece(Table t, string id) => Json.Serialize(t.Server.State.Find(U, id));

    static void UndoLast(Table t) => t.Ok(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq });

    [Fact]
    public void Finding_1_undoing_a_first_text_line_takes_the_line_off_again()
    {
        using var t = new Table();
        var was = Piece(t, "p-000003");
        t.Ok(Table.Host, new SetText { Layer = U, Id = "p-000003", Name = "orders", Value = "hold the ford" });
        UndoLast(t);
        Assert.Equal(was, Piece(t, "p-000003"));

        t.Error(Table.Host, new Batch { Items = { new SetText { Layer = U, Id = "p-000003", Name = "orders", Value = "x" }, new MovePiece { Layer = U, Id = "p-999999" } } });
        Assert.Equal(was, Piece(t, "p-000003"));
    }

    [Fact]
    public void Finding_2_an_undo_puts_a_piece_sheet_drawing_and_zone_back_where_they_were()
    {
        using var t = new Table();
        var s = t.Server;
        t.Ok(Table.Host, new AddSheet { Sheet = new SheetDef { Name = "Second", Image = "sha", Package = "testpack" } });
        t.Ok(Table.Host, new Draw { Layer = U, Stroke = new Stroke { Points = { new(0, 0), new(2, 2) } } });
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Name = "A", Owner = "Umpire Three", Polygon = { new(0, 0), new(1, 0), new(1, 1) } } });
        t.Ok(Table.Lead, new SetZone { Zone = new Zone { Name = "B", Owner = "Umpire Three", Polygon = { new(0, 0), new(1, 0), new(1, 1) } } });
        var before = Json.Serialize(s.State.Layer(U)) + Json.Serialize(s.State.Sheets) + Json.Serialize(s.State.DrawLayer(U)) + Json.Serialize(s.State.Zones);
        t.Ok(Table.Host, new DeletePiece { Layer = U, Id = "p-000002" });
        UndoLast(t);
        t.Ok(Table.Host, new RemoveSheet { Id = "s-000001" });
        UndoLast(t);
        t.Ok(Table.Host, new EraseStroke { Layer = U, StrokeId = "d-000005" });
        UndoLast(t);
        t.Ok(Table.Lead, new RemoveZone { Id = s.State.Zones[0].Id });
        t.Ok(Table.Lead, new Undo { TargetSeq = s.State.LastSeq });
        Assert.Equal(before, Json.Serialize(s.State.Layer(U)) + Json.Serialize(s.State.Sheets) + Json.Serialize(s.State.DrawLayer(U)) + Json.Serialize(s.State.Zones));
    }

    [Fact]
    public void Finding_3_a_refused_batch_rolls_no_dice()
    {
        using var t = new Table();
        var s = t.Server;
        t.Ok(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Pos = new(5, 5) });
        var die = s.State.Layer(U).Last().Id;
        var before = Json.Serialize(s.State);
        for (int i = 0; i < 20 && Json.Serialize(s.State) == before; i++)
            t.Error(Table.Host, new Batch { Items = { new RollDice { Layer = U, PieceIds = { die } }, new MovePiece { Layer = U, Id = "p-999999" } } });
        Assert.Empty(s.State.Dice);
        Assert.Equal(before, Json.Serialize(s.State));
    }

    [Fact]
    public void Finding_4_undoing_a_layer_removal_brings_its_drawings_back()
    {
        using var t = new Table();
        var s = t.Server;
        var board = s.State.Boards[0].Id;
        t.Ok(Table.Host, new AddBoard { Name = "Plans" });
        var before = Json.Serialize(s.State.DrawLayer(U));
        t.Ok(Table.Host, new RemoveBoard { Id = board });
        UndoLast(t);
        Assert.Equal(before, Json.Serialize(s.State.DrawLayer(U)));
    }

    [Fact]
    public void Finding_5_a_refused_batch_that_removes_a_layer_keeps_its_drawings()
    {
        using var t = new Table();
        var s = t.Server;
        t.Ok(Table.Host, new AddBoard { Name = "Plans" });
        var before = Json.Serialize(s.State);
        t.Error(Table.Host, new Batch { Items = { new RemoveBoard { Id = s.State.Boards[0].Id }, new MovePiece { Layer = U, Id = "p-999999" } } });
        Assert.Single(s.State.DrawLayer(U));
        Assert.Equal(before, Json.Serialize(s.State));
    }

    [Fact]
    public void Finding_6_undoing_a_sheet_rename_gives_back_the_name_it_had()
    {
        using var t = new Table();
        var s = t.Server;
        t.Ok(Table.Host, new AddSheet { Sheet = new SheetDef { Name = "  Sketch  ", Image = "sha", Package = "testpack" } });
        var id = s.State.Sheets[^1].Id;
        t.Ok(Table.Host, new RenameSheet { Id = id, Name = "Ford" });
        UndoLast(t);
        Assert.Equal("  Sketch  ", s.State.Sheets[^1].Name);
    }

    [Fact]
    public void Finding_12_taking_back_a_carriers_move_down_puts_its_rider_back_at_its_height()
    {
        using var t = new Table();
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = "p-000002", Pos = new(0, 0), Y = 1.5 });
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = "p-000003", Pos = new(1, 0), Y = 0.1 });
        t.Ok(Table.Host, new Attach { Layer = U, Parent = "p-000002", Child = "p-000003" });
        var was = Piece(t, "p-000003");
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = "p-000002", Pos = new(0, 0), Y = 0 });
        UndoLast(t);
        Assert.Equal(was, Piece(t, "p-000003"));
    }

    static string Diff(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int from = Math.Max(0, i - 120);
        return $"\nfirst difference at {i}:\n  was: …{a[from..Math.Min(a.Length, i + 200)]}\n  now: …{b[from..Math.Min(b.Length, i + 200)]}";
    }
}
