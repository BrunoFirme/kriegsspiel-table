using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class RoleAndJoinTests
{
    const string U = Layers.Umpire;

    static IEnumerable<Command> AllCommands(string layer)
    {
        yield return new SpawnPiece { Layer = layer, Def = "inf", Pos = new(9, 9) };
        yield return new RestorePiece { Layer = layer, Piece = new PieceInstance { Id = "p-000900", Def = "inf" } };
        yield return new MovePiece { Layer = layer, Id = "p-000004", Pos = new(4, 4) };
        yield return new DeletePiece { Layer = layer, Id = "p-000004" };
        yield return new SetState { Layer = layer, Id = "p-000004", State = 1 };
        yield return new TurnDie { Layer = layer, Id = "p-000004", Face = 1 };
        yield return new SetWorth { Layer = layer, Id = "p-000004", Worth = 5 };
        yield return new SetFacePicture { Layer = layer, Id = "p-000004", Picture = "pictures/face.png" };
        yield return new SetLettering { Layer = layer, Id = "p-000004", Lettering = new Lettering { Text = "HQ" } };
        yield return new SetDieFaces { Layer = layer, Id = "p-000004", Faces = [new DieFace { Value = "1" }] };
        yield return new SetCounter { Layer = layer, Id = "p-000004", Name = "str", Value = 3 };
        yield return new SetText { Layer = layer, Id = "p-000004", Name = "name", Value = "x" };
        yield return new SetLabel { Layer = layer, Id = "p-000004", Value = "x" };
        yield return new SetNote { Layer = layer, Id = "p-000004", Value = "notes" };
        yield return new RestoreTurn { Index = 0 };
        yield return new LoadSave { Name = "Turn 0" };
        yield return new SetTint { Layer = layer, Id = "p-000004", Value = "#123456" };
        yield return new SetLocked { Layer = layer, Id = "p-000004", Value = true };
        yield return new SetHighlight { Layer = layer, Id = "p-000004", Color = "#FF0000" };
        yield return new SetBookHighlight { Layer = layer, Id = "p-000004", Box = [0.1, 0.2, 0.5, 0.6] };
        yield return new PlayEffect { Layer = layer, Id = "p-000004", Index = 1 };
        yield return new GlidePiece { Layer = layer, Id = "p-000004", To = new Vec2(2, 3), Rot = 90 };
        yield return new ShowCast { Shape = "box", From = [0, 1, 0], To = [3, 1, 0], Size = [1, 1, 1], Seconds = 0.5 };
        yield return new ReportSelection { Selected = { "p-000004" } };
        yield return new SetPieceOption { Layer = layer, Id = "p-000004", Name = PieceOptions.NoCollision, Value = true };
        yield return new FlipPiece { Layer = layer, Id = "p-000004", FaceDown = true };
        yield return new SetScale { Layer = layer, Id = "p-000004", Scale = 1.25 };
        yield return new SetZone { Zone = new Zone { Name = "North", Owner = "u2", Polygon = { new(0, 0), new(10, 0), new(10, 10) } } };
        yield return new RemoveZone { Id = "z-000099" };
        yield return new AddPack { Id = "user.bobs-pieces" };
        yield return new Attach { Layer = layer, Parent = "p-000004", Child = "p-000002" };
        yield return new Detach { Layer = layer, Child = "p-000002" };
        yield return new Stow { Layer = layer, Holder = "p-000004", Id = "p-000002" };
        yield return new Unstow { Layer = layer, Holder = "p-000004", Id = "p-000002", Pos = new(1, 1) };
        yield return new Draw { Layer = layer, Stroke = new Stroke { Points = { new(0, 0) } } };
        yield return new EraseStroke { Layer = layer, StrokeId = "d-000005" };
        yield return new StampDecal { Layer = layer, Decal = new DecalDef { Picture = "blood", W = 1, H = 1 } };
        yield return new RemoveDecal { Layer = layer, DecalId = "dc-000005" };
        yield return new SetDecalPalette { Entries = new() { new PaletteDecal { Name = "Guts", Picture = "guts", Size = 0.5 } } };
        yield return new AddSheet { Sheet = new SheetDef { Name = "new" } };
        yield return new MoveSheet { Id = "s-000001", Pos = new(1, 1) };
        yield return new LockSheet { Id = "s-000001", Locked = false };
        yield return new RenameSheet { Id = "s-000001", Name = "renamed" };
        yield return new RemoveSheet { Id = "s-000001" };
        yield return new CropSheet { Id = "s-000001", Crop = new FaceBox { X = 0.1, Y = 0.1, W = 0.8, H = 0.8 } };
        yield return new SetSheetRelief { Id = "s-000001", Relief = new string('e', 64), Height = 2 };
        yield return new SetSheetPicture { Id = "s-000001", Image = "other", Package = "sheets" };
        yield return new SetLighting { Lighting = new Lighting { Warmth = 0.5 } };
        yield return new SetWind { Wind = new Wind { From = 90, Strength = 2 } };
        yield return new SetVisionRadius { Radius = 30 };
        yield return new SetPointerRule { AlwaysShown = true };
        yield return new SetBriefing { Text = "18:00. Plancenoit holds." };
        yield return new AddBoard { Name = "Plan" };
        yield return new RenameBoard { Id = "b-000001", Name = "Ground" };
        yield return new RemoveBoard { Id = "b-000001" };
        yield return new RollDice { Sides = 6, Count = 2 };
        yield return new SetClock { Turn = 4, GameTime = "1809-05-21T08:00", IncrementMinutes = 15 };
        yield return new AdvanceClock();
        yield return new SetCalibration { UnitsPerMeter = 0.5 };
        yield return new SetGrid { Grid = new GridDef { Kind = "square", Pitch = 10 } };
        yield return new SetPicture { Layer = layer, Id = "p-000004", Picture = "testpack.marker" };
        yield return new PublishSighting();
        yield return new Undo { TargetSeq = 1 };
        yield return new Batch { Items = { new MovePiece { Layer = layer, Id = "p-000004", Pos = new(5, 5) } } };
        yield return new SetScript { Layer = layer, Id = "p-000004", Text = "function on_load(s) end" };
        yield return new SetGlobalScript { Text = "function on_turn(t) end" };
        yield return new SetScriptState { Layer = layer, Id = "p-000004", State = "{}" };
        yield return new SetTags { Layer = layer, Id = "p-000004", Tags = new() { "Source" } };
        yield return new SetMemo { Layer = layer, Id = "p-000004", Memo = "1" };
        yield return new SetGlobalScriptState { State = "{}" };
        yield return new SetWidgets { Layer = layer, Id = "p-000004", Widgets = { new Widget { Label = "x" } } };
        yield return new SetMenuItems { Layer = layer, Id = "p-000004", Items = { new MenuItemDef { Label = "x", OnSelect = "f" } } };
        yield return new ClickWidget { Layer = layer, Id = "p-000004", Index = 1 };
        yield return new SetHotkeys { Items = { new HotkeyDef { Label = "x" } } };
        yield return new SetGlobalMenuItems { Items = { new MenuItemDef { Label = "x" } } };
        yield return new Hotkey { Index = 1, Layer = layer, Piece = "p-000004" };
        yield return new PickGlobalMenuItem { Index = 1 };
        yield return new NumberTyped { Layer = layer, Id = "p-000004", Number = 2 };
        yield return new SelectPiece { Layer = layer, Id = "p-000004", To = "p:1" };
        yield return new ShowContents { Layer = layer, Id = "p-000004", To = "p:1", Most = 3 };
        yield return new EditWidget { Layer = layer, Id = "p-000004", Index = 1, Value = "v" };
        yield return new PickMenuItem { Layer = layer, Id = "p-000004", Index = 1 };
        yield return new HoldPiece { Layer = layer, Id = "p-000004", Held = true };
        yield return new Say { Text = "hello" };
        yield return new SetDef { Layer = layer, Id = "p-000004", Def = "marker" };
        yield return new SetLines { Layer = layer, Id = "p-000004", Lines = { new LineDef { Points = { new(0, 0), new(1, 1) } } } };
        yield return new SetDecals { Layer = layer, Id = "p-000004", Decals = { new DecalDef { Picture = "aa11" } } };
        yield return new SetUi { Tree = { new UiNode { Tag = "Text", Attributes = { ["id"] = "t", ["text"] = "x" } } } };
        yield return new SetUiAttributes { Id = "t", Attributes = { ["text"] = "y" } };
        yield return new SetUiPictures { Pictures = { ["crest"] = "pic-crest" } };
        yield return new UiEvent { Id = "t", Event = "click", Value = "-1" };
        yield return new ShowDialog { Kind = "confirm", Title = "?", Text = "sure?", Token = "d-1" };
        yield return new DialogResult { Token = "d-1", Value = "True" };
    }

    [Fact]
    public void Command_list_covers_every_command_type()
    {
        var all = typeof(Command).Assembly.GetTypes().Where(t => typeof(Command).IsAssignableFrom(t) && !t.IsAbstract).Select(t => t.Name).OrderBy(x => x);
        Assert.Equal(all, AllCommands(U).Select(c => c.GetType().Name).OrderBy(x => x));
    }

    static bool LeadsOnly(Command c) => c is SetZone or RemoveZone or SetGlobalScript or SetPointerRule or SetBriefing;
    static bool RuntimeOnly(Command c) =>
        c is RestoreTurn or LoadSave or SetScriptState or SetGlobalScriptState or SetWidgets or SetMenuItems or SetUi or SetUiAttributes or SetUiPictures or ShowDialog
            or SetMemo or SetHotkeys
            or SetGlobalMenuItems or SelectPiece or ShowContents or GlidePiece or ShowCast;

    public static TheoryData<string, string> Cases()
    {
        var d = new TheoryData<string, string>();
        foreach (var layer in new[] { U, "scratch:red", "bogus" })
            foreach (var c in AllCommands(layer))
                if (c is LayerCommand || c is Batch || layer == U) d.Add(c.GetType().Name, layer);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Role_enforcement(string type, string layer)
    {
        Command Make() => AllCommands(layer).Single(c => c.GetType().Name == type);

        using var t = new Table();
        var peers = new[] { (Table.Host, Role.HostUmpire), (Table.Lead, Role.LeadUmpire), (Table.Ump3, Role.Umpire), (Table.Obs, Role.Observer) };
        foreach (var (id, role) in peers)
        {
            var peer = t.Server.Peer(id)!;
            Assert.Equal(role, peer.Role);
            bool expect = !RuntimeOnly(Make()) && role switch
            {
                Role.HostUmpire or Role.LeadUmpire => true,
                Role.Umpire => !LeadsOnly(Make()),
                _ => false,
            };
            Assert.Equal(expect, GameServer.Allowed(peer, Make()));

            var before = t.StateJson;
            var logCount = t.Server.Log.Count;
            var res = t.Server.Handle(id, Make());
            if (!expect)
            {
                var o = Assert.Single(res);
                Assert.Equal(id, o.PeerId);
                Assert.Contains("not allowed", Assert.IsType<ErrorMessage>(o.Message).Text);
                Assert.Equal(before, t.StateJson);
                Assert.Equal(logCount, t.Server.Log.Count);
            }
            else
            {
                Assert.DoesNotContain(res, o => o.Message is ErrorMessage e && e.Text.Contains("not allowed"));
            }
        }
    }

    [Fact]
    public void Observers_send_nothing_at_all()
    {
        var obs = new PeerInfo { PeerId = 1, Role = Role.Observer };
        Assert.False(GameServer.Allowed(obs, new Batch()));
        Assert.False(GameServer.Allowed(obs, new Batch { Items = { new RollDice() } }));
        Assert.False(GameServer.Allowed(obs, new MovePiece { Layer = U }));
        Assert.False(GameServer.Allowed(obs, new Draw { Layer = U }));
        Assert.False(GameServer.Allowed(obs, new AddPack { Id = "user.bobs-pieces" }));
        Assert.False(GameServer.Allowed(obs, new RollDice { Layer = U, PieceIds = { "p-000002" } }));
        Assert.False(GameServer.Allowed(obs, new RollDice { Sides = 6, Count = 2 }));
    }

    [Fact]
    public void Commands_reach_only_those_who_may_see_them()
    {
        using var t = new Table();
        var res = t.Ok(Table.Ump3, new RollDice { Sides = 6, Count = 1 });
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3 }, res.Select(o => o.PeerId).OrderBy(x => x));
        Assert.All(res, o => Assert.IsType<CommandMessage>(o.Message));

        var res2 = t.Ok(Table.Ump, new MovePiece { Layer = U, Id = "p-000002", Pos = new(7, 7) });
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3, Table.Obs }, res2.Select(o => o.PeerId).OrderBy(x => x));

        var res3 = t.Ok(Table.Ump, new AdvanceClock());
        Assert.Equal(4, res3.Count);

        var res4 = t.Ok(Table.Lead, new SetZone { Zone = new Zone { Name = "N", Owner = "Umpire Three", Polygon = { new(0, 0), new(1, 0), new(1, 1) } } });
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3 }, res4.Select(o => o.PeerId).OrderBy(x => x));
    }

    [Fact]
    public void Server_overwrites_actor_and_timestamps()
    {
        using var t = new Table();
        var c = new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 2), Actor = Table.Ump, Seq = 999, At = 5 };
        t.Ok(Table.Ump2, c);
        var logged = t.Server.Log[^1];
        Assert.Equal(Table.Ump2, logged.Actor);
        Assert.Equal(1, logged.Seq);
        Assert.NotEqual(5, logged.At);
    }

    [Fact]
    public void The_host_seats_itself_without_a_password()
    {
        using var t = new Table(join: false);
        var jr = t.HostJoins("  Ann  ");
        Assert.Equal(Role.HostUmpire, jr.Role);
        Assert.Equal("Ann", t.Server.Peer(1)!.Name);
        Assert.NotNull(jr.Access);
        Assert.Equal(Table.Password, jr.Access!.Password);
        Assert.Null(jr.Token);
        Assert.Contains(jr.View!.Layer(U), p => p.Id == "p-000002");
    }

    [Fact]
    public void A_wrong_password_is_turned_away_and_the_right_one_waits_for_the_umpires()
    {
        using var t = new Table();
        var bad = t.Knock(40, "Eve", "nope");
        var br = Table.JoinResultOf(bad, 40, ok: false);
        Assert.Equal("wrong password", br.Error);
        Assert.False(br.Waiting);
        Assert.Single(bad);
        Assert.Null(t.Server.Peer(40));
        Assert.Equal("not joined", Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Handle(40, new RollDice())).Message).Text);

        var outs = t.Knock(41, "Fay", " abc ");
        var wr = Table.JoinResultOf(outs, 41, ok: false);
        Assert.True(wr.Waiting);
        Assert.Null(wr.View);
        Assert.Null(wr.Access);
        Assert.Equal(new long[] { Table.Host, Table.Lead }, outs.Where(o => o.Message is Knock).Select(o => o.PeerId).OrderBy(x => x));
        Assert.Equal("Fay", ((Knock)outs.First(o => o.Message is Knock).Message).Name);
        var lists = outs.Where(o => o.Message is PeerList).ToDictionary(o => o.PeerId, o => (PeerList)o.Message);
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3 }, lists.Keys.OrderBy(x => x));
        Assert.Single(lists[Table.Host].Waiting);
        Assert.Single(lists[Table.Lead].Waiting);
        Assert.Empty(lists[Table.Ump3].Waiting);
        Assert.Null(t.Server.Peer(41));
        Assert.Equal("not joined", Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Handle(41, new RollDice())).Message).Text);
        Assert.Single(t.Server.Waiting);
    }

    [Fact]
    public void An_open_door_lets_anyone_knock_but_still_waits()
    {
        using var t = new Table(join: false);
        t.HostJoins("H");
        t.Door(Table.Host, new SetPassword { Password = "" });
        Assert.Equal("", t.Server.Access.Password);
        var wr = Table.JoinResultOf(t.Knock(5, "Anyone", "whatever"), 5, ok: false);
        Assert.True(wr.Waiting);
        var jr = Table.JoinResultOf(t.Door(Table.Host, new Admit { PeerId = 5, Role = Role.Observer }), 5, ok: true);
        Assert.Equal(Role.Observer, jr.Role);
    }

    [Fact]
    public void Admitting_gives_the_role_a_token_and_the_view_for_that_role()
    {
        using var t = new Table(join: false);
        t.HostJoins("H");
        var obs = t.LetIn(10, "Cy", Role.Observer);
        Assert.Null(obs.Access);
        Assert.Null(obs.Created);
        Assert.Contains(obs.View!.Layer(U), p => p.Id == "p-000002");
        Assert.Empty(obs.View.Zones);
        var json = Json.Serialize<Message>(obs);
        Assert.DoesNotContain(Table.Password, json);
        Assert.DoesNotContain("tokenHash", json);

        var ump = t.LetIn(11, "Dee", Role.Umpire);
        Assert.NotNull(ump.Access);
        Assert.Equal(Table.Password, ump.Access!.Password);
        Assert.Equal(2, ump.Access.Approved.Count);
        Assert.All(ump.Access.Approved, a => Assert.Matches("^[0-9a-f]{64}$", a.TokenHash));
        Assert.DoesNotContain(ump.Token!, Json.Serialize(ump.Access));
        Assert.NotNull(ump.Created);

        var lead = t.LetIn(12, "Eli", Role.LeadUmpire);
        Assert.Equal(Role.LeadUmpire, lead.Role);
        Assert.Equal(new[] { Role.Observer, Role.Umpire, Role.LeadUmpire }, t.Server.Access.Approved.Select(a => a.Role));
        Assert.Equal(new[] { "Cy", "Dee", "Eli" }, t.Server.Access.Approved.Select(a => a.Name));
        Assert.Empty(t.Server.Waiting);
    }

    [Fact]
    public void Admitting_tells_the_umpires_the_access_list_changed_and_names_are_settled_once()
    {
        using var t = new Table();
        t.Knock(50, "Umpire Three");
        var outs = t.Door(Table.Lead, new Admit { PeerId = 50, Role = Role.Observer });
        Assert.Equal("Umpire Three (2)", t.Server.Peer(50)!.Name);
        Assert.Equal(new long[] { Table.Ump, Table.Ump2, Table.Ump3 }, outs.Where(o => o.Message is AccessChanged).Select(o => o.PeerId).OrderBy(x => x));
        Assert.DoesNotContain(outs, o => o.PeerId == Table.Obs && o.Message is not CommandMessage);
    }

    [Fact]
    public void Refusing_sends_the_person_away()
    {
        using var t = new Table();
        t.Knock(50, "Nope");
        var outs = t.Door(Table.Lead, new Refuse { PeerId = 50 });
        var jr = Table.JoinResultOf(outs, 50, ok: false);
        Assert.False(jr.Waiting);
        Assert.Equal("not let in", jr.Error);
        Assert.Empty(t.Server.Waiting);
        Assert.Contains("waiting", t.DoorError(Table.Host, new Admit { PeerId = 50, Role = Role.Observer }));
    }

    [Fact]
    public void A_role_that_does_not_exist_is_refused_at_the_door_and_after()
    {
        using var t = new Table();
        t.Knock(50, "Odd");
        Assert.Equal("no such role", t.DoorError(Table.Host, new Admit { PeerId = 50, Role = (Role)99 }));
        Assert.Contains(t.Server.Waiting, w => w.PeerId == 50);
        Assert.Equal("no such role", t.DoorError(Table.Host, new SetRole { PeerId = Table.Ump3, Role = (Role)(-1) }));
        Assert.Equal("no such role", t.DoorError(Table.Lead, new SetRole { PeerId = Table.Obs, Role = (Role)4 }));
        Assert.Equal(Role.Umpire, t.Server.Peer(Table.Ump3)!.Role);
        Assert.Equal(Role.Observer, t.Server.Peer(Table.Obs)!.Role);
    }

    [Fact]
    public void Only_the_host_and_the_leads_decide_and_only_the_host_makes_leads()
    {
        using var t = new Table();
        t.Knock(50, "Guest");
        Assert.Contains("host or a lead", t.DoorError(Table.Ump3, new Admit { PeerId = 50, Role = Role.Observer }));
        Assert.Contains("host or a lead", t.DoorError(Table.Obs, new Admit { PeerId = 50, Role = Role.Observer }));
        Assert.Contains("only the host makes lead", t.DoorError(Table.Lead, new Admit { PeerId = 50, Role = Role.LeadUmpire }));
        Assert.Single(t.Server.Waiting);
        var jr = Table.JoinResultOf(t.Door(Table.Host, new Admit { PeerId = 50, Role = Role.HostUmpire }), 50, ok: true);
        Assert.Equal(Role.LeadUmpire, jr.Role);
    }

    [Fact]
    public void A_token_brings_the_same_person_straight_back_with_name_and_role()
    {
        using var t = new Table();
        var token = t.Tokens[Table.Ump3];
        t.Server.Leave(Table.Ump3);
        var again = t.Rejoin(77, token, "someone else");
        Assert.Equal(Role.Umpire, again.Role);
        Assert.Equal("Umpire Three", t.Server.Peer(77)!.Name);
        Assert.Null(again.Token);
        Assert.NotNull(again.Access);
        Assert.Empty(t.Server.Waiting);
        t.Door(Table.Host, new SetPassword { Password = "changed" });
        t.Server.Leave(77);
        Assert.True(Table.JoinResultOf(t.Knock(78, "x", "old", token.ToLowerInvariant()), 78, ok: true).Ok);
        var outs = t.Knock(79, "x", null, token);
        Table.JoinResultOf(outs, 79, ok: true);
        var dropped = Table.JoinResultOf(outs, 78, ok: false);
        Assert.Contains("elsewhere", dropped.Error);
        Assert.Null(t.Server.Peer(78));
        Assert.NotNull(t.Server.Peer(79));
        Assert.True(Table.JoinResultOf(t.Knock(80, "Stranger", "changed", "NOTATOKEN2345678"), 80, ok: false).Waiting);
    }

    [Fact]
    public void Roles_are_changed_within_each_ranks_say()
    {
        using var t = new Table();
        var outs = t.Door(Table.Lead, new SetRole { PeerId = Table.Ump3, Role = Role.Observer });
        var rc = Assert.IsType<RoleChanged>(outs.Single(o => o.PeerId == Table.Ump3 && o.Message is RoleChanged).Message);
        Assert.Equal(Role.Observer, rc.Role);
        Assert.Empty(rc.View.Zones);
        Assert.Equal(Role.Observer, t.Server.Peer(Table.Ump3)!.Role);
        Assert.Equal(Role.Observer, t.Server.Access.Find(t.Tokens[Table.Ump3])!.Role);
        Assert.Contains("not allowed", t.Error(Table.Ump3, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) }));
        t.Door(Table.Lead, new SetRole { PeerId = Table.Obs, Role = Role.Umpire });
        Assert.Equal(Role.Umpire, t.Server.Peer(Table.Obs)!.Role);
        t.Ok(Table.Obs, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) });
        Assert.Contains("only the host makes lead", t.DoorError(Table.Lead, new SetRole { PeerId = Table.Obs, Role = Role.LeadUmpire }));
        Assert.Contains("the host", t.DoorError(Table.Lead, new SetRole { PeerId = Table.Host, Role = Role.Observer }));
        Assert.Contains("own role", t.DoorError(Table.Lead, new SetRole { PeerId = Table.Lead, Role = Role.Observer }));
        t.Door(Table.Host, new SetRole { PeerId = Table.Obs, Role = Role.LeadUmpire });
        Assert.Equal(Role.LeadUmpire, t.Server.Peer(Table.Obs)!.Role);
        Assert.Contains("only the host changes a lead", t.DoorError(Table.Lead, new SetRole { PeerId = Table.Obs, Role = Role.Umpire }));
        t.Door(Table.Host, new SetRole { PeerId = Table.Lead, Role = Role.Umpire });
        Assert.Equal(Role.Umpire, t.Server.Peer(Table.Lead)!.Role);
        Assert.Contains("host or a lead", t.DoorError(Table.Lead, new SetRole { PeerId = Table.Ump3, Role = Role.Umpire }));
        Assert.Contains("own role", t.DoorError(Table.Host, new SetRole { PeerId = Table.Host, Role = Role.Observer }));
        Assert.Contains("one host", t.DoorError(Table.Host, new SetRole { PeerId = Table.Obs, Role = Role.HostUmpire }));
        t.Server.Leave(Table.Lead);
        Assert.Equal(Role.Umpire, t.Rejoin(90, t.Tokens[Table.Lead]).Role);
    }

    [Fact]
    public void Revoking_puts_the_person_out_and_forgets_the_token()
    {
        using var t = new Table();
        var token = t.Tokens[Table.Ump3];
        var outs = t.Door(Table.Lead, new Revoke { PeerId = Table.Ump3 });
        var jr = Table.JoinResultOf(outs, Table.Ump3, ok: false);
        Assert.Contains("put out", jr.Error);
        Assert.Null(t.Server.Peer(Table.Ump3));
        Assert.Null(t.Server.Access.Find(token));
        Assert.True(Table.JoinResultOf(t.Knock(91, "back?", Table.Password, token), 91, ok: false).Waiting);
        Assert.Contains("own role", t.DoorError(Table.Lead, new Revoke { PeerId = Table.Lead }));
        Assert.Contains("the host", t.DoorError(Table.Lead, new Revoke { PeerId = Table.Host }));
    }

    [Fact]
    public void Peer_lists_go_to_umpires_and_leaving_is_noticed()
    {
        using var t = new Table(join: false);
        t.HostJoins("U");
        var res = t.Knock(2, "P");
        var lists = res.Where(o => o.Message is PeerList).ToList();
        Assert.Equal(new long[] { 1 }, lists.Select(o => o.PeerId));
        Assert.Single(((PeerList)lists[0].Message).Peers);
        Assert.Single(((PeerList)lists[0].Message).Waiting);
        var leave = t.Server.Leave(2);
        Assert.Empty(((PeerList)Assert.Single(leave).Message).Waiting);
        Assert.Empty(t.Server.Waiting);
        var obs = t.LetIn(3, "O", Role.Observer);
        var lists2 = t.Server.Leave(3);
        Assert.Equal(new long[] { 1 }, lists2.Select(o => o.PeerId));
        Assert.Single(((PeerList)lists2[0].Message).Peers);
    }

    [Fact]
    public void Limits_eight_umpires_and_thirty_two_observers()
    {
        using var t = new Table(join: false);
        t.HostJoins("H");
        for (int i = 0; i < 7; i++) t.LetIn(100 + i, "u" + i, i == 0 ? Role.LeadUmpire : Role.Umpire);
        t.Knock(200, "one too many");
        Assert.Contains("umpire limit (8)", t.DoorError(Table.Host, new Admit { PeerId = 200, Role = Role.Umpire }));
        Assert.Single(t.Server.Waiting);
        t.Door(Table.Host, new Admit { PeerId = 200, Role = Role.Observer });
        for (int i = 1; i < 32; i++) t.LetIn(300 + i, "o" + i, Role.Observer);
        t.Knock(400, "too many");
        Assert.Contains("observer limit (32)", t.DoorError(Table.Host, new Admit { PeerId = 400, Role = Role.Observer }));
        Assert.Contains("umpire limit", t.DoorError(Table.Host, new SetRole { PeerId = 200, Role = Role.Umpire }));
        var tok = t.Tokens[101];
        t.Server.Leave(101);
        t.LetIn(500, "fills the seat", Role.Umpire);
        Assert.Contains("umpire limit", Table.JoinResultOf(t.Knock(501, "", null, tok), 501, ok: false).Error);
    }
}

public class NameSpoofingTests
{
    [Fact]
    public void A_name_already_at_the_table_or_on_the_list_is_numbered()
    {
        using var t = new Table(join: false);
        t.HostJoins("Bob");
        t.LetIn(2, "bob", Role.Observer);
        Assert.Equal("bob (2)", t.Server.Peer(2)!.Name);
        t.Server.Leave(2);
        t.LetIn(3, "BOB", Role.Observer);
        Assert.Equal("BOB (3)", t.Server.Peer(3)!.Name);
        var back = t.Rejoin(4, t.Tokens[2], "Bob");
        Assert.Equal("bob (2)", t.Server.Peer(4)!.Name);
    }
}
