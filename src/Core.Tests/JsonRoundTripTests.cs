using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class JsonRoundTripTests
{
    [Fact]
    public void Scenario_card_reads_what_a_full_read_does_of_the_head()
    {
        var sc = CoreSamples.Scenario();
        var path = Path.Combine(Path.GetTempPath(), $"ktable-card-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, Json.Serialize(sc, indented: true));
            var (head, pieces) = Json.ScenarioCard(path);
            Assert.Equal(sc.Id, head.Id);
            Assert.Equal(sc.Name, head.Name);
            Assert.Equal(sc.Packs, head.Packs);
            Assert.Equal(Json.Serialize(sc.Sheets), Json.Serialize(head.Sheets));
            Assert.Equal(sc.UmpireBoard.Count, pieces);
            File.WriteAllText(path, Json.Serialize(sc)[..^40]);
            Assert.ThrowsAny<Exception>(() => Json.ScenarioCard(path));
            File.WriteAllText(path, "{\"id\":\"x\",\"umpireBoard\":null}");
            Assert.Equal(-1, Json.ScenarioCard(path).pieces);
        }
        finally { File.Delete(path); }
    }

    static void AssertRoundTrip<T>(T value)
    {
        var json = Json.Serialize(value);
        var back = Json.Deserialize<T>(json);
        Assert.NotNull(back);
        Assert.Equal(value!.GetType(), back!.GetType());
        Assert.Equal(json, Json.Serialize(back));
        Assert.Equal(json, Json.Serialize(Json.Deserialize<T>(Json.SerializeBytes(value))));
        Assert.Equal(json, Json.Serialize(Json.Deserialize<T>(Json.Serialize(value, indented: true))));
    }

    public static TheoryData<string> CommandTypes()
    {
        var d = new TheoryData<string>();
        foreach (var c in CoreSamples.AllCommands()) d.Add(c.GetType().Name);
        return d;
    }

    [Theory]
    [MemberData(nameof(CommandTypes))]
    public void Command_round_trips_polymorphically(string typeName)
    {
        var c = CoreSamples.AllCommands().WithHeader().Single(x => x.GetType().Name == typeName);
        var json = Json.Serialize<Command>(c);
        Assert.StartsWith($"{{\"type\":\"{typeName}\"", json);
        var back = Json.Deserialize<Command>(json);
        Assert.IsType(c.GetType(), back);
        Assert.Equal(c.Seq, back.Seq);
        Assert.Equal(c.Actor, back.Actor);
        Assert.Equal(c.At, back.At);
        Assert.Equal(json, Json.Serialize<Command>(back));
    }

    [Fact]
    public void Every_concrete_command_type_is_covered()
    {
        var all = typeof(Command).Assembly.GetTypes()
            .Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract).Select(t => t.Name).OrderBy(x => x);
        var covered = CoreSamples.AllCommands().Select(c => c.GetType().Name).OrderBy(x => x);
        Assert.Equal(all, covered);
    }

    [Fact]
    public void Command_list_round_trips()
    {
        var list = CoreSamples.AllCommands().WithHeader().ToList();
        var json = Json.Serialize(list);
        var back = Json.Deserialize<List<Command>>(json);
        Assert.Equal(list.Select(c => c.GetType()), back.Select(c => c.GetType()));
        Assert.Equal(json, Json.Serialize(back));
    }

    [Fact]
    public void Batch_items_keep_their_types()
    {
        var b = (Batch)Json.Deserialize<Command>(Json.Serialize<Command>(CoreSamples.AllCommands().OfType<Batch>().Single()));
        Assert.IsType<MovePiece>(b.Items[0]);
        var inner = Assert.IsType<Batch>(b.Items[1]);
        Assert.IsType<SetState>(inner.Items[0]);
    }

    public static IEnumerable<object[]> AllMessages()
    {
        yield return new object[] { new JoinRequest { Name = "Ann", Password = "abc", Token = "ABCD2345ABCD2345", AppVersion = "1.4.0", Protocol = ProtocolVersion.Current } };
        yield return new object[] { new JoinResult
        {
            Ok = true, Error = "", PeerId = 7, Role = Role.LeadUmpire, View = CoreSamples.State(), Token = "ABCD2345ABCD2345",
            Access = Access(), Created = "2026-10-29T10:00:00Z",
        } };
        yield return new object[] { new JoinResult { Ok = false, Waiting = true, PeerId = 7 } };
        yield return new object[] { new JoinResult { Ok = false, Error = "wrong password" } };
        yield return new object[] { new Knock { PeerId = 7, Name = "Ann" } };
        yield return new object[] { new Admit { PeerId = 7, Role = Role.Umpire } };
        yield return new object[] { new Refuse { PeerId = 7 } };
        yield return new object[] { new SetRole { PeerId = 7, Role = Role.Observer } };
        yield return new object[] { new Revoke { PeerId = 7 } };
        yield return new object[] { new SetPassword { Password = "new one" } };
        yield return new object[] { new RoleChanged { Role = Role.Observer, View = CoreSamples.State() } };
        yield return new object[] { new AccessChanged { Access = Access() } };
        yield return new object[] { new CommandMessage { Command = new MovePiece { Seq = 3, Actor = 2, At = 9, Layer = "umpire", Id = "p-1", Pos = new(1, 2), RotDeg = 3, Stack = 1 } } };
        yield return new object[] { new CommandMessage { Command = CoreSamples.AllCommands().OfType<Batch>().Single() } };
        yield return new object[] { new Snapshot { View = CoreSamples.State() } };
        yield return new object[] { new ErrorMessage { Text = "nope" } };
        yield return new object[] { new NeedAsset { Package = "pack-x", Path = "img/a.png", Offset = 131072 } };
        yield return new object[] { new AssetChunk { Package = "pack-x", Path = "img/a.png", Offset = 65536, Total = 100000 } };
        yield return new object[] { new PeerList { Peers = new() { new PeerInfo { PeerId = 1, Name = "Host", Role = Role.HostUmpire }, new PeerInfo { PeerId = 2, Name = "O", Role = Role.Observer } }, Waiting = new() { new PeerInfo { PeerId = 3, Name = "W" } } } };
        yield return new object[] { new UploadAsset { Package = "user.mine", Path = "pack.json", Offset = 0, Total = 3 } };
        yield return new object[] { new ChunkAck { Package = "user.mine", Path = "pack.json", Offset = 65536 } };
        yield return new object[] { new GetTurns() };
        yield return new object[] { new GetSnapshot() };
        yield return new object[] { new ScriptConsole { Backlog = true, Lines = { new ConsoleLine { Seq = 7, At = "2026-10-31T12:00:00.0000000Z", Kind = "error", Source = "p-000002", Name = "Infantry", Line = 3, Text = "Script error in Infantry (p-000002): attempt to index a nil value" } } } };
        yield return new object[] { new RunLua { Target = "a1b2c3", Code = "self.set_name('x')" } };
        yield return new object[] { new GetScripts() };
        yield return new object[] { new ScriptList { Scripts = { new ScriptInfo { Id = "p-000002", Name = "Infantry", Guid = "a1b2c3", Def = "inf", Own = true, Status = "error", LastError = "boom", ErrorLine = 3, Timers = 1 } } } };
        yield return new object[] { new TurnList { Turns = new() { new TurnInfo { Index = 0, Turn = 0, GameTime = "1812-07-22T06:00", EndedAt = "2026-10-28T18:00:00Z", Pieces = 12 } } } };
        yield return new object[] { new GetTurn { Index = 2 } };
        yield return new object[] { new TurnState { Index = 2, Info = new TurnInfo { Index = 2, Turn = 2, GameTime = "1812-07-22T06:30" }, State = CoreSamples.State() } };
    }

    static GameAccess Access() => new()
    {
        Password = "abc",
        Approved = new() { new Approved { TokenHash = new string('a', 64), Name = "Ann", Role = Role.Umpire }, new Approved { TokenHash = new string('b', 64), Name = "Bob", Role = Role.Observer } },
    };

    [Fact]
    public void Access_round_trips_and_hashes_tokens_one_way()
    {
        AssertRoundTrip(Access());
        var tok = GameAccess.NewToken();
        Assert.Matches("^[A-HJ-NP-Z2-9]{16}$", tok);
        Assert.Equal(GameAccess.Hash(tok), GameAccess.Hash(" " + tok.ToLowerInvariant() + " "));
        Assert.NotEqual(GameAccess.Hash(tok), GameAccess.Hash(GameAccess.NewToken()));
        var a = new GameAccess { Approved = { new Approved { TokenHash = GameAccess.Hash(tok), Name = "x", Role = Role.Umpire } } };
        Assert.NotNull(a.Find(tok));
        Assert.Null(a.Find(null));
        Assert.Null(a.Find("other"));
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Message_round_trips_polymorphically(Message m)
    {
        var json = Json.Serialize<Message>(m);
        Assert.StartsWith("{\"msg\":", json);
        var back = Json.Deserialize<Message>(json);
        Assert.IsType(m.GetType(), back);
        Assert.Equal(json, Json.Serialize<Message>(back));
    }

    [Fact]
    public void Every_concrete_message_type_is_covered()
    {
        var all = typeof(Message).Assembly.GetTypes()
            .Where(t => typeof(Message).IsAssignableFrom(t) && !t.IsAbstract).Select(t => t.Name).Distinct().OrderBy(x => x);
        var covered = AllMessages().Select(o => o[0].GetType().Name).Distinct().OrderBy(x => x);
        Assert.Equal(all, covered);
    }

    [Fact]
    public void A_file_piece_s_bytes_travel_outside_its_json()
    {
        Assert.DoesNotContain("Data", Json.Serialize<Message>(new AssetChunk { Data = new byte[] { 9, 8, 7 } }));
        var m = (AssetChunk)BulkFrame.Decode(BulkFrame.Encode(new AssetChunk { Package = "p", Path = "a.png", Offset = 3, Total = 6, Data = new byte[] { 9, 8, 7 } }));
        Assert.Equal(("p", "a.png", 3L, 6L), (m.Package, m.Path, m.Offset, m.Total));
        Assert.Equal(new byte[] { 9, 8, 7 }, m.Data);
    }

    [Fact]
    public void Scenario_round_trips() => AssertRoundTrip(CoreSamples.Scenario());

    [Fact]
    public void PackManifest_round_trips()
    {
        var p = CoreSamples.Pack();
        AssertRoundTrip(p);
        var back = Json.Deserialize<PackManifest>(Json.Serialize(p));
        Assert.Equal(new Size2(2, 2), back.Pieces[0].States[0].Size);
        Assert.Null(back.Pieces[0].States[1].Size);
        Assert.Equal(new Vec2(0.5, 0), back.Pieces[0].Attachments[0].Offset);
    }

    [Fact]
    public void A_counter_that_placed_a_marker_is_one_of_the_piece_s_markers_and_becomes_one_when_the_studio_asks()
    {
        const string json = """
            {"id":"p","name":"P","pieces":[
              {"id":"gun","name":"Gun","size":{"w":2,"h":1},"counters":[{"name":"Shots","spawnsMarker":"smoke"},{"name":"Dead","spawnsMarker":"wreck"}],
               "attachments":[{"piece":"wreck","offset":{"x":0,"z":1}}]},
              {"id":"smoke","name":"Smoke","size":{"w":1,"h":1}}],
             "assets":{}}
            """;
        var pack = Json.Deserialize<PackManifest>(json);
        var gun = pack.Pieces[0];
        var expected = new[] { ("wreck", new Vec2(0, 1)), ("smoke", new Vec2(2 * PackManifest.MarkerBeside, 0)) };
        Assert.Equal(expected, gun.Markers().Select(a => (a.Piece, a.Offset)).ToArray());
        Assert.Equal("smoke", gun.Counters[0].SpawnsMarker);
        Assert.True(pack.MoveCounterMarkers());
        Assert.All(gun.Counters, c => Assert.Null(c.SpawnsMarker));
        Assert.Equal(expected, gun.Attachments.Select(a => (a.Piece, a.Offset)).ToArray());
        Assert.False(pack.MoveCounterMarkers());
    }

    [Fact]
    public void GameState_round_trips()
    {
        var g = CoreSamples.State();
        AssertRoundTrip(g);
        var back = Json.Deserialize<GameState>(Json.Serialize(g));
        Assert.Equal("Ump2", back.Zones[0].Owner);
        Assert.Equal(4, back.Layers[Layers.Umpire][0].Counters["str"]);
        Assert.Equal(0.01, back.Calibration.UnitsPerMeter);
    }

    [Fact]
    public void Empty_GameState_round_trips() => AssertRoundTrip(new GameState());

    [Fact]
    public void SaveFile_round_trips()
    {
        var s = new SaveFile
        {
            ScenarioId = "scn-1", Created = "2026-10-28T10:00:00Z", Saved = "2026-10-28T11:00:00Z",
            Access = Access(),
            Snapshot = CoreSamples.State(), Commands = CoreSamples.AllCommands().WithHeader().ToList(),
        };
        AssertRoundTrip(s);
        var back = Json.Deserialize<SaveFile>(Json.Serialize(s));
        Assert.Equal(s.Commands.Select(c => c.GetType()), back.Commands.Select(c => c.GetType()));
        Assert.Equal(2, back.Access.Approved.Count);
        Assert.Equal(SaveFile.CurrentFormat, back.Format);
    }

    [Fact]
    public void A_format_1_save_with_keys_and_sightings_still_reads()
    {
        const string old = @"{""format"":1,""scenarioId"":""scn"",""created"":""c"",""saved"":""s"",
            ""keys"":{""umpire"":""AAAAAAAA"",""observer"":""BBBBBBBB"",""sides"":{""red"":""CCCCCCCC""}},
            ""snapshot"":{""scenarioId"":""scn"",""layers"":{""umpire"":[{""id"":""p-000001"",""def"":""x""}]},""sightings"":[{""id"":""v-000002"",""to"":""side:red"",""pieces"":[{""def"":""x""}],""mask"":[{""center"":{""x"":1,""z"":2},""radius"":0.5}]}],""observersSeeBoard"":false,""nextId"":3,""lastSeq"":2},
            ""commands"":[{""type"":""MovePiece"",""seq"":1,""layer"":""umpire"",""id"":""p-000001"",""pos"":{""x"":1,""z"":1}},{""type"":""PublishSighting"",""seq"":2,""actor"":1,""sighting"":{""id"":""v-000002"",""to"":""side:red"",""note"":""cavalry""}}]}";
        var back = Json.Deserialize<SaveFile>(old);
        Assert.Equal(1, back.Format);
        Assert.Equal("", back.Access.Password);
        Assert.Empty(back.Access.Approved);
        Assert.Single(back.Snapshot.Layer(Layers.Umpire));
        var ps = Assert.IsType<PublishSighting>(back.Commands[1]);
        Assert.Equal(2, ps.Seq);
        Assert.NotNull(ps.Old);
        Assert.Null(StateMachine.Apply(back.Snapshot, ps, out var inv));
        Assert.Null(inv);
        Assert.Contains("cavalry", Json.Serialize<Command>(ps));
    }

    [Fact]
    public void TileIndex_round_trips() => AssertRoundTrip(new TileIndex
    {
        SourceW = 1000, SourceH = 600, Ext = "png",
        Levels = new() { new TileLevel { Level = 0, W = 1000, H = 600, Cols = 4, Rows = 3 }, new TileLevel { Level = 1, W = 500, H = 300, Cols = 2, Rows = 2 } },
    });

    [Fact]
    public void Save_and_Load_file_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "sub", "scenario.json");
            var s = CoreSamples.Scenario();
            Json.Save(path, s);
            Assert.Equal(Json.Serialize(s), Json.Serialize(Json.Load<Scenario>(path)));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class UserPackRulesTests
{
    [Theory]
    [InlineData("user.my-pieces", true)]
    [InlineData("user.", false)]
    [InlineData("iks.scripted-war-game-pieces", false)]
    [InlineData("user.../etc", false)]
    [InlineData("user.UPPER", false)]
    public void UserPackIds(string id, bool ok) => Assert.Equal(ok, KTable.Core.Model.PackManifest.IsUserPackId(id));

    [Theory]
    [InlineData("user.mine", "pack.json", true)]
    [InlineData("user.mine", "assets/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("user.mine", "assets/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.obj", true)]
    [InlineData("user.mine", "assets/../../evil.obj", false)]
    [InlineData("user.mine", "assets/../../evil.png", false)]
    [InlineData("user.mine", "assets/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.exe", false)]
    [InlineData("iks.scripted-war-game-pieces", "pack.json", false)]
    public void UploadPaths(string pkg, string path, bool ok) => Assert.Equal(ok, KTable.Core.Protocol.UploadAsset.PathAllowed(pkg, path));
}
