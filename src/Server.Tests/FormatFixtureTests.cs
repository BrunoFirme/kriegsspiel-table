using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class FormatFixtureTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-format-fixtures", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        BestEffort.DeleteFolder(_root);
    }

    static string FormatsDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats");

    public static IEnumerable<object[]> Formats() =>
        Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats"))
            .Select(d => int.Parse(Path.GetFileName(d))).OrderBy(n => n).Select(n => new object[] { n });

    (string save, string scenario, PackageStore store, PackManifest pack) Copy(int format)
    {
        var dir = Path.Combine(_root, format.ToString());
        Directory.CreateDirectory(dir);
        var src = Path.Combine(FormatsDir, format.ToString());
        var save = Path.Combine(dir, "game.kstable");
        var scenario = Path.Combine(dir, "scenario.json");
        File.Copy(Path.Combine(src, "game.kstable"), save);
        File.Copy(Path.Combine(src, "scenario.json"), scenario);
        var pack = Json.Load<PackManifest>(Path.Combine(src, "pack.json"));
        var storeRoot = Path.Combine(dir, "packs");
        Directory.CreateDirectory(Path.Combine(storeRoot, pack.Id));
        File.Copy(Path.Combine(src, "pack.json"), Path.Combine(storeRoot, pack.Id, "pack.json"));
        return (save, scenario, new PackageStore(storeRoot), pack);
    }

    [Fact]
    public void There_is_a_fixture_set_for_the_current_save_format()
    {
        Assert.True(Directory.Exists(Path.Combine(FormatsDir, SaveFile.CurrentFormat.ToString())),
            $"no old-save fixtures for format {SaveFile.CurrentFormat}: add game.kstable, scenario.json and pack.json under Core.Tests/Fixtures/formats/{SaveFile.CurrentFormat}/");
        foreach (var f in new[] { "game.kstable", "scenario.json", "pack.json" })
            foreach (var n in Formats().Select(o => (int)o[0]))
                Assert.True(File.Exists(Path.Combine(FormatsDir, n.ToString(), f)), $"format {n} has no {f}");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void An_old_save_opens_takes_changes_saves_and_opens_the_same(int format)
    {
        var (save, _, store, _) = Copy(format);
        var srv = SaveLoader.Load(save, store);
        Assert.Empty(srv.MissingPacks);
        srv.Join(1, new JoinRequest { Name = "Host" });
        var piece = srv.State.Layer(Layers.Umpire).First(p => p.Def == "inf");
        Ok(srv.Handle(1, new MovePiece { Layer = Layers.Umpire, Id = piece.Id, Pos = new(2, 2), RotDeg = 10 }));
        Ok(srv.Handle(1, new SetCounter { Layer = Layers.Umpire, Id = piece.Id, Name = "str", Value = 99 }));
        Assert.Equal(12, piece.Counters["str"]);
        Ok(srv.Handle(1, new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = { new(0, 0), new(1, 1) } } }));
        int turns = srv.Turns.Count;
        Ok(srv.Handle(1, new AdvanceClock()));
        Assert.Equal(turns + 1, srv.Turns.Count);

        var again = Path.Combine(Path.GetDirectoryName(save)!, "again.kstable");
        srv.SaveTo(again);
        var back = SaveLoader.Load(again, store);
        Assert.Equal(Json.Serialize(srv.State), Json.Serialize(back.State));
        Assert.Equal(srv.Turns.Select(t => Json.Serialize(t)), back.Turns.Select(t => Json.Serialize(t)));
        Assert.Equal(srv.Turns.Select(t => Json.Serialize(t.Unpack())), back.Turns.Select(t => Json.Serialize(t.Unpack())));
        Assert.Equal(Json.Serialize(srv.Access), Json.Serialize(back.Access));
        Assert.Equal(srv.Created, back.Created);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void An_old_scenario_opens_takes_changes_and_saves_the_same(int format)
    {
        var (_, scenario, store, _) = Copy(format);
        var srv = GameServer.FromScenario(Json.Load<Scenario>(scenario), store);
        srv.Join(1, new JoinRequest { Name = "Host" });
        Ok(srv.Handle(1, new SpawnPiece { Layer = Layers.Umpire, Def = "gun", Pos = new(1, 1) }));
        Ok(srv.Handle(1, new SetWind { Wind = new Wind { From = 10, Strength = 2 } }));
        var s = srv.State.ToScenario("scn-again", "Again");
        var path = Path.Combine(Path.GetDirectoryName(scenario)!, "again.json");
        Json.Save(path, s);
        Assert.Equal(Json.Serialize(s), Json.Serialize(Json.Load<Scenario>(path)));
        Assert.Equal(Json.Serialize(srv.State.Layer(Layers.Umpire)), Json.Serialize(GameState.FromScenario(Json.Load<Scenario>(path)).Layer(Layers.Umpire)));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void An_old_pack_installs_and_saves_the_same(int format)
    {
        var (_, _, store, pack) = Copy(format);
        var read = store.Pack(pack.Id);
        Assert.True(read != null, store.Problem(pack.Id));
        Assert.All(read!.Pieces, d => Assert.Same(d, store.Def(d.Id)));
        var path = Path.Combine(store.Root, "again.json");
        Json.Save(path, read);
        Assert.Equal(Json.Serialize(read), Json.Serialize(Json.Load<PackManifest>(path)));
    }

    [Fact]
    public void The_current_format_files_read_back_whole()
    {
        var dir = Path.Combine(FormatsDir, SaveFile.CurrentFormat.ToString());
        Whole<SaveFile>(Path.Combine(dir, "game.kstable"));
        Whole<Scenario>(Path.Combine(dir, "scenario.json"));
        Whole<PackManifest>(Path.Combine(dir, "pack.json"));
    }

    static void Whole<T>(string path)
    {
        var file = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Json.Unpacked(File.ReadAllBytes(path))));
        var again = JsonNode.Parse(Json.Serialize(Json.Load<T>(path)));
        Assert.True(JsonNode.DeepEquals(file, again),
            $"{Path.GetFileName(path)} does not read back whole: a property in it is no longer read the same (renamed, re-typed or gone). That needs a save format bump and a migration.\nfile:  {file!.ToJsonString()}\nagain: {again!.ToJsonString()}");
    }

    [Fact]
    public void The_format_4_save_keeps_everything_it_was_made_with()
    {
        var (save, _, store, _) = Copy(4);
        var srv = SaveLoader.Load(save, store);
        var g = srv.State;
        var inf = g.Layer(Layers.Umpire).Single(p => p.Label == "1st Brigade");
        Assert.Equal(new Vec2(6, 4), inf.Pos);
        Assert.Equal(45, inf.RotDeg);
        Assert.Equal(1.5, inf.Scale);
        Assert.Equal(5, inf.Counters["str"]);
        Assert.Equal(2, inf.Counters["ammo"]);
        Assert.Equal("Pogue's Foot", inf.Text["name"]);
        Assert.Equal("Raised in the valley.", inf.Note);
        Assert.Equal("#AA3322", inf.TintOverride);
        Assert.Equal(new[] { "p-000003" }, inf.Attached);
        Assert.Equal("function onLoad() end", inf.Script);
        Assert.Equal("{\"str\":5}", inf.ScriptState);
        Assert.Equal(new[] { "unit", "blue" }, inf.Tags);
        Assert.Equal("state-7", inf.Memo);
        Assert.Equal(2, inf.Widgets.Count);
        Assert.Equal("fire", inf.Widgets[0].OnClick);
        Assert.Equal("12", inf.Widgets[1].Value);
        Assert.Equal("rally", Assert.Single(inf.MenuItems).OnSelect);
        Assert.True(Assert.Single(inf.Lines).Loop);
        Assert.True(g.Layer(Layers.Umpire).Single(p => p.Id == "p-000002").FaceDown);
        Assert.Equal("marker", g.Layer(Layers.Umpire).Single(p => p.Id == "p-000002").Picture);
        Assert.True(g.Layer(Layers.Umpire).Single(p => p.Id == "p-000003").Locked);
        Assert.Equal(3, g.Layer(Layers.Umpire).Single(p => p.Def == "die6").ShownFace);

        var sheet = Assert.Single(g.Sheets);
        Assert.Equal(0.8, sheet.Crop!.W);
        Assert.Equal(1, sheet.Z);
        Assert.Equal(2, g.Boards.Count);
        Assert.Equal("Plans", g.Boards[1].Name);
        var stroke = g.Drawings[Layers.Umpire].Single(s => s.Board == g.Boards[1].Id);
        Assert.Equal("#3366CC", stroke.Color);
        var zone = Assert.Single(g.Zones);
        Assert.Equal("Umpire Three", zone.Owner);
        Assert.Equal(4, zone.Polygon.Count);
        Assert.Equal(135, g.Wind.From);
        Assert.Equal(3, g.Wind.Strength);
        Assert.True(g.Lighting.FromClock);
        Assert.Equal("hex", g.Grid.Kind);
        Assert.Equal(0.01, g.Calibration.UnitsPerMeter);
        Assert.Equal("pace", g.Calibration.Units);
        Assert.Equal(55, g.VisionRadius);
        Assert.Equal(1, g.Clock.Turn);
        Assert.Equal("1863-07-01T05:20", g.Clock.GameTime);
        Assert.Equal("morale", Assert.Single(g.Dice).Label);
        Assert.Equal("function onLoad() end", g.Script);
        Assert.Equal("{\"turn\":0}", g.ScriptState);
        Assert.Equal("Volley", Assert.Single(g.Hotkeys).Label);
        Assert.Equal("Night", Assert.Single(g.MenuItems).Label);
        Assert.Equal("05:00", g.UiNode("clock")!.Attributes["text"]);

        var turn = Assert.Single(srv.Turns);
        Assert.Equal(0, turn.Turn);
        Assert.Equal("1863-07-01T05:00", turn.GameTime);
        Assert.NotNull(turn.Packed);
        var then = turn.Unpack();
        Assert.Equal(new Vec2(3, 4), then.Layer(Layers.Umpire).Single(p => p.Label == "1st Brigade").Pos);
        Assert.Equal(135, then.Wind.From);
        Assert.Equal(turn.Pieces, then.Layer(Layers.Umpire).Count);

        Assert.Equal("abc", srv.Access.Password);
        Assert.Equal(new[] { "Umpire Two", "Umpire Three", "Watcher" }, srv.Access.Approved.Select(a => a.Name));
        Assert.Equal(Role.LeadUmpire, srv.Access.Approved[0].Role);
    }

    [Fact]
    public void The_format_4_pack_keeps_everything_it_was_made_with()
    {
        var (_, _, store, pack) = Copy(4);
        var p = store.Pack(pack.Id)!;
        Assert.True(p.Partial);
        Assert.Equal("123456", p.Source.WorkshopId);
        Assert.Equal(256, p.Assets["aa11"].W);
        var gun = store.Def("gun")!;
        Assert.Equal("rounded", gun.Shape);
        Assert.Equal(new Size2(1.4, 0.7), gun.Size);
        Assert.Equal("marker", gun.Counters[0].SpawnsMarker);
        Assert.Equal(new Vec2(0.5, 0), gun.Attachments[0].Offset);
        Assert.Equal("-- kept", gun.States[0].Script);
        Assert.Equal(new[] { 0.0, 1, 0 }, gun.States[1].Up);
        Assert.Equal(0.25, gun.NameBox!.H);
        Assert.Equal("0badf00d", gun.ScriptHash);
        Assert.Equal("cannon", gun.Effect!.Kind);
        Assert.Equal("rolling", gun.Effect.Volley);
        Assert.Equal(new[] { 0.1, 0.2 }, gun.Effect.At);
        Assert.Equal(6, store.Def("die6")!.Faces.Count);
    }

    [Fact]
    public void The_format_8_pack_keeps_the_shapes_built_onto_a_block()
    {
        var (_, _, store, pack) = Copy(8);
        var p = store.Pack(pack.Id)!;
        var inf = store.Def("inf")!;
        Assert.Equal("flat", inf.Kind);
        Assert.Equal("ad01", inf.AddOnMesh);
        Assert.Equal("ad02", inf.AddOnTexture);
        Assert.Equal("assets/ad01.obj", p.Assets["ad01"].File);
    }

    static void Ok(List<Outgoing> res)
    {
        var err = res.Select(o => o.Message).OfType<ErrorMessage>().FirstOrDefault();
        Assert.True(err == null, err?.Text);
    }
}
