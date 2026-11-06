using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class VisibilityTests
{
    sealed class World
    {
        public GameState State = new();
        public readonly HashSet<string> Forbidden = new();
        public readonly HashSet<string> Required = new();
    }

    static string Tok(Random r, string prefix)
    {
        var b = new byte[12];
        r.NextBytes(b);
        return prefix + Convert.ToHexString(b) + "Q";
    }

    static World MakeWorld(int seed)
    {
        var r = new Random(seed);
        var w = new World();
        var g = w.State;
        g.Sheets.Add(new SheetDef { Id = Tok(r, "SHEET"), Image = Tok(r, "IMG") });
        w.Required.Add(g.Sheets[0].Id);
        w.Required.Add(g.Sheets[0].Image);
        g.VisionRadius = r.Next(10, 90);

        var ump = g.Layer(Layers.Umpire);
        for (int i = 0, n = r.Next(0, 15); i < n; i++) ump.Add(RandomPiece(r, "U", w.Required));
        for (int i = 1; i < ump.Count; i++) if (r.Next(3) == 0) ump[r.Next(i)].Attached.Add(ump[i].Id);
        foreach (var s in Enumerable.Range(0, r.Next(0, 4)).Select(_ => RandomStroke(r, "U", w.Required))) g.DrawLayer(Layers.Umpire).Add(s);

        for (int i = 0, n = r.Next(0, 4); i < n; i++)
        {
            var z = new Zone { Id = Tok(r, "ZONEID"), Name = Tok(r, "ZONENAME"), Owner = Tok(r, "ZONEOWNER"), Color = Tok(r, "ZONECOLOR"), Polygon = { new(0, 0), new(1, 0), new(1, 1) } };
            w.Forbidden.Add(z.Id);
            w.Forbidden.Add(z.Name);
            w.Forbidden.Add(z.Owner);
            w.Forbidden.Add(z.Color);
            g.Zones.Add(z);
        }

        for (int i = 0, n = r.Next(0, 6); i < n; i++)
        {
            var d = new DiceRoll { Seq = i, Actor = Tok(r, "DICEACTOR"), Sides = 6, Results = new() { r.Next(1, 7) }, Label = Tok(r, "DICE") };
            w.Forbidden.Add(d.Label);
            w.Forbidden.Add(d.Actor);
            g.Dice.Add(d);
        }
        return w;
    }

    static PieceInstance RandomPiece(Random r, string tag, HashSet<string> set)
    {
        var p = new PieceInstance
        {
            Id = Tok(r, tag + "ID"), Def = Tok(r, tag + "DEF"), Pos = new(r.NextDouble() * 100, r.NextDouble() * 100),
            RotDeg = r.Next(360), State = r.Next(3), Scale = 1,
            Label = r.Next(2) == 0 ? Tok(r, tag + "LBL") : null,
            TintOverride = r.Next(2) == 0 ? Tok(r, tag + "TINT") : null,
            Note = r.Next(2) == 0 ? Tok(r, tag + "NOTE") : null,
        };
        for (int i = 0, n = r.Next(0, 4); i < n; i++) p.Counters[Tok(r, tag + "CNT")] = r.Next(-5, 20);
        for (int i = 0, n = r.Next(0, 4); i < n; i++) p.Text[Tok(r, tag + "TXK")] = Tok(r, tag + "TXV");
        set.Add(p.Id);
        set.Add(p.Def);
        if (p.Label != null) set.Add(p.Label);
        if (p.TintOverride != null) set.Add(p.TintOverride);
        if (p.Note != null) set.Add(p.Note);
        foreach (var k in p.Counters.Keys) set.Add(k);
        foreach (var (k, v) in p.Text) { set.Add(k); set.Add(v); }
        return p;
    }

    static Stroke RandomStroke(Random r, string tag, HashSet<string> set)
    {
        var s = new Stroke { Id = Tok(r, tag + "STROKE"), Color = Tok(r, tag + "COLOR"), Points = new() { new(r.NextDouble(), r.NextDouble()) } };
        set.Add(s.Id);
        set.Add(s.Color);
        return s;
    }

    static readonly PeerInfo Obs = new() { PeerId = 6, Name = "obs", Role = Role.Observer };

    [Fact]
    public void Observer_view_is_the_board_without_zones_or_dice_1000_random_states()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var w = MakeWorld(seed);
            var before = Json.Serialize(w.State);
            var view = Visibility.Filter(w.State, Obs);
            var json = Json.Serialize(view);
            foreach (var f in w.Forbidden)
                Assert.False(json.Contains(f, StringComparison.Ordinal), $"seed {seed}: observer view leaks '{f}'");
            foreach (var q in w.Required)
                Assert.True(json.Contains(q, StringComparison.Ordinal), $"seed {seed}: observer view misses '{q}'");
            Assert.Equal(Json.Serialize(w.State.Layer(Layers.Umpire)), Json.Serialize(view.Layers[Layers.Umpire]));
            Assert.Equal(w.State.VisionRadius, view.VisionRadius);
            Assert.Equal(w.State.Clock.Turn, view.Clock.Turn);
            Assert.Equal(before, Json.Serialize(w.State));
            var msg = Json.Serialize<Message>(new Snapshot { View = view });
            foreach (var f in w.Forbidden) Assert.DoesNotContain(f, msg);
        }
    }

    [Theory]
    [InlineData(Role.Umpire)]
    [InlineData(Role.LeadUmpire)]
    [InlineData(Role.HostUmpire)]
    public void Every_umpire_sees_everything(Role role)
    {
        var w = MakeWorld(7);
        var ump = new PeerInfo { PeerId = 1, Name = "U", Role = role };
        var view = Visibility.Filter(w.State, ump);
        Assert.Equal(Json.Serialize(w.State), Json.Serialize(view));
        Assert.NotSame(w.State, view);
        foreach (var c in CoreSamples.AllCommands()) Assert.Same(c, Visibility.ForPeer(c, ump, w.State));
    }

    [Fact]
    public void An_unknown_role_gets_an_empty_table_and_no_commands()
    {
        var w = MakeWorld(3);
        var stranger = new PeerInfo { PeerId = 2, Name = "?", Role = (Role)99 };
        var view = Visibility.Filter(w.State, stranger);
        Assert.Empty(view.Layers);
        Assert.Empty(view.Drawings);
        Assert.Empty(view.Zones);
        Assert.Empty(view.Dice);
        Assert.Equal(w.State.Sheets.Count, view.Sheets.Count);
        foreach (var c in CoreSamples.AllCommands()) Assert.Null(Visibility.ForPeer(c, stranger, w.State));
    }

    public static TheoryData<string> LayerCommandTypes()
    {
        var d = new TheoryData<string>();
        foreach (var c in CoreSamples.AllCommands().OfType<LayerCommand>()) d.Add(c.GetType().Name);
        return d;
    }

    [Theory]
    [MemberData(nameof(LayerCommandTypes))]
    public void ForPeer_forwards_every_board_change_to_observers(string typeName)
    {
        var g = CoreSamples.State();
        var c = (LayerCommand)CoreSamples.AllCommands().Single(x => x.GetType().Name == typeName);
        c.Layer = Layers.Umpire;
        Assert.Same(c, Visibility.ForPeer(c, Obs, g));
        var b = Assert.IsType<Batch>(Visibility.ForPeer(new Batch { Seq = 9, Items = new() { c } }, Obs, g));
        Assert.Same(c, Assert.Single(b.Items));
    }

    [Fact]
    public void ForPeer_strips_zones_from_mixed_batches()
    {
        var g = CoreSamples.State();
        var b = new Batch
        {
            Seq = 3, Actor = 1, At = 2,
            Items = new()
            {
                new SetZone { Zone = new Zone { Id = "z-1", Name = "ZONESECRET", Owner = "o", Polygon = { new(0, 0), new(1, 0), new(1, 1) } } },
                new SetClock { Turn = 2 },
                new Batch { Items = new() { new RemoveZone { Id = "ZONESECRET2" } } },
                new MovePiece { Layer = Layers.Umpire, Id = "p-000001" },
            },
        };
        var got = Assert.IsType<Batch>(Visibility.ForPeer(b, Obs, g));
        var json = Json.Serialize<Command>(got);
        Assert.DoesNotContain("SECRET", json);
        Assert.Equal(2, got.Items.Count);
        Assert.IsType<SetClock>(got.Items[0]);
        Assert.IsType<MovePiece>(got.Items[1]);
        Assert.Equal(3, got.Seq);
        Assert.Null(Visibility.ForPeer(new Batch { Items = new() { new SetZone { Zone = new Zone { Id = "z-2" } } } }, Obs, g));
    }

    [Fact]
    public void ForPeer_table_wide_commands_zones_and_dice()
    {
        var g = CoreSamples.State();
        foreach (var c in new Command[] { new AddSheet(), new MoveSheet(), new LockSheet(), new RemoveSheet(), new CropSheet(), new SetClock(), new AdvanceClock(), new SetCalibration(),
            new SetLighting(), new SetWind(), new SetVisionRadius { Radius = 30 }, new AddPack { Id = "x" }, new PublishSighting() })
            Assert.Same(c, Visibility.ForPeer(c, Obs, g));
        Assert.Null(Visibility.ForPeer(new SetZone { Zone = new Zone { Id = "z-1" } }, Obs, g));
        Assert.Null(Visibility.ForPeer(new RemoveZone { Id = "z-1" }, Obs, g));
        Assert.Null(Visibility.ForPeer(new RollDice { Actor = 77 }, Obs, g));
        Assert.Null(Visibility.ForPeer(new RollDice { Actor = Obs.PeerId }, Obs, g));
        var onBoard = new RollDice { Layer = Layers.Umpire, PieceIds = { "p-000001" } };
        Assert.Same(onBoard, Visibility.ForPeer(onBoard, Obs, g));
        var rt = new RestoreTurn { Index = 0, State = CoreSamples.State() };
        rt.State.Zones.Add(new Zone { Id = "z-9", Name = "ZONESECRET" });
        var got = Assert.IsType<RestoreTurn>(Visibility.ForPeer(rt, Obs, g));
        Assert.Empty(got.State!.Zones);
        Assert.Equal(rt.State.Layer(Layers.Umpire).Count, got.State.Layer(Layers.Umpire).Count);
    }

    [Fact]
    public void ForPeer_keeps_script_dialogs_and_their_answers_from_observers()
    {
        var g = CoreSamples.State();
        var ask = new ShowDialog { Title = "DIALOGSECRET", Text = "Which ford? SECRET", Kind = "input", Token = "t-1" };
        var answer = new DialogResult { Token = "t-1", Value = "the lower ford SECRET" };
        Assert.Null(Visibility.ForPeer(ask, Obs, g));
        Assert.Null(Visibility.ForPeer(answer, Obs, g));
        var b = new Batch { Seq = 4, Items = new() { ask, new SetClock { Turn = 2 }, answer } };
        var got = Assert.IsType<Batch>(Visibility.ForPeer(b, Obs, g));
        Assert.DoesNotContain("SECRET", Json.Serialize<Command>(got));
        Assert.IsType<SetClock>(Assert.Single(got.Items));
        var ump = new PeerInfo { PeerId = 3, Name = "u", Role = Role.Umpire };
        Assert.Same(ask, Visibility.ForPeer(ask, ump, g));
        Assert.Same(answer, Visibility.ForPeer(answer, ump, g));
    }
}
