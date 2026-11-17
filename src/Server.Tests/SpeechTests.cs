using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class SpeechTests
{
    const long Ann = 40, Bob = 41, Cid = 42;
    const string AnnBody = "p-000002", BobBody = "p-000003", CidBody = "p-000090", Sentry = "p-000091";
    const string U = Layers.Umpire;

    static Table Seated()
    {
        var t = new Table();
        t.LetIn(Ann, "Ann", Role.Player);
        t.LetIn(Bob, "Bob", Role.Player);
        t.LetIn(Cid, "Cid", Role.Player);
        t.Ok(Table.Host, new SetCalibration { UnitsPerMeter = 1 });
        t.Ok(Table.Host, new SpawnPiece { Layer = U, Id = CidBody, Def = "marker", Pos = new(40, 0) });
        t.Ok(Table.Host, new SpawnPiece { Layer = U, Id = Sentry, Def = "inf", Pos = new(0, 10) });
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = BobBody, Pos = new(1, 0) });
        (t.Server.Bodies[AnnBody], t.Server.Bodies[BobBody], t.Server.Bodies[CidBody]) = ("Ann", "Bob", "Cid");
        return t;
    }

    static Dictionary<long, Speak> Sent(List<Outgoing> res) =>
        res.Where(o => o.Message is CommandMessage { Command: Speak }).ToDictionary(o => o.PeerId, o => (Speak)((CommandMessage)o.Message).Command);

    static string Refused(List<Outgoing> res) => Assert.IsType<ErrorMessage>(Assert.Single(res).Message).Text;

    [Fact]
    public void A_whisper_a_say_and_a_shout_reach_the_pieces_within_two_fifteen_and_sixty_metres_and_only_the_players_whose_bodies_heard()
    {
        using var t = Seated();
        var whisper = Sent(t.Server.Handle(Ann, new Speak { Id = AnnBody, Text = "Psst", Loud = Earshot.Whisper }));
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Ann, Bob }, whisper.Keys.OrderBy(k => k));
        Assert.Equal(new[] { BobBody }, whisper[Table.Ump].Heard);
        t.Pass(GameServer.LineEveryMs);
        var say = Sent(t.Server.Handle(Ann, new Speak { Id = AnnBody, Text = "Who goes there?" }));
        Assert.Equal(new[] { BobBody, Sentry }, say[Table.Ump].Heard);
        Assert.Equal(new[] { "Bob" }, say[Table.Ump].Hearers);
        Assert.DoesNotContain(Cid, say.Keys);
        t.Pass(GameServer.LineEveryMs);
        var shout = Sent(t.Server.Handle(Ann, new Speak { Id = AnnBody, Text = "To arms!", Loud = Earshot.Shout }));
        Assert.Contains(Cid, shout.Keys);
        Assert.Equal((null, null, "Ann", "To arms!", "Infantry"), (shout[Cid].Heard, shout[Cid].Hearers, shout[Table.Ump].By, shout[Cid].Text, shout[Cid].Name));
        Assert.DoesNotContain(Table.Obs, shout.Keys);
        t.Ok(Table.Host, new SetCalibration { UnitsPerMeter = 0.1 });
        t.Pass(GameServer.LineEveryMs);
        Assert.DoesNotContain(Cid, Sent(t.Server.Handle(Ann, new Speak { Id = AnnBody, Text = "To arms!", Loud = Earshot.Shout })).Keys);
    }

    [Fact]
    public void A_player_speaks_only_as_the_body_they_wear_and_only_with_the_right_and_an_umpire_as_any_piece()
    {
        using var t = Seated();
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Speak { Id = Sentry, Text = "Halt" })));
        Assert.Contains("too long", Refused(t.Server.Handle(Ann, new Speak { Id = AnnBody, Text = new string('x', Earshot.MostLength + 1) })));
        t.Ok(Table.Host, new SetPlayerRights { Rights = new() { ["Ann"] = new PlayerRights { Speak = false } } });
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Speak { Id = AnnBody, Text = "Hello" })));
        var halt = Sent(t.Server.Handle(Table.Ump3, new Speak { Id = Sentry, Text = "Halt" }));
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Ann, Bob }, halt.Keys.OrderBy(k => k));
        Assert.Contains("not allowed", Refused(t.Server.Handle(Table.Obs, new Speak { Id = Sentry, Text = "Halt" })));
    }

    [Fact]
    public void What_was_said_is_kept_with_where_and_who_heard_and_a_player_is_given_back_only_what_their_body_heard()
    {
        using var t = Seated();
        t.Ok(Ann, new Speak { Id = AnnBody, Text = "Who goes there?" });
        var kept = t.Server.Record.Single(e => e.Kind == RecordEntry.Speech);
        Assert.Equal(("Ann", AnnBody, "Infantry", new Vec2(0, 0), Earshot.Say), (kept.Who, kept.Piece, kept.PieceName, kept.Pos!.Value, kept.Loud));
        Assert.Equal(new[] { "Bob" }, kept.Hearers);
        List<string> For(long peer) => t.Server.RecordFor(peer).Select(o => o.Message).OfType<RecordList>().Single().Entries.Where(e => e.Kind == RecordEntry.Speech).Select(e => e.Text).ToList();
        Assert.Single(For(Ann));
        Assert.Single(For(Bob));
        Assert.Empty(For(Cid));
        Assert.Single(For(Table.Ump3));
    }

    [Fact]
    public void A_hotline_line_reaches_the_one_who_wears_the_piece_and_its_sender_alone_and_the_leads_read_it_afterwards()
    {
        using var t = Seated();
        var res = t.Server.Handle(Table.Ump3, new Hotline { Id = BobBody, Text = "Your man is drunk: play it." });
        var readers = res.Where(o => o.Message is CommandMessage { Command: Hotline }).Select(o => o.PeerId).OrderBy(id => id).ToList();
        Assert.Equal(new[] { Table.Ump3, Bob }, readers);
        bool Reads(long peer) => t.Server.RecordFor(peer).Select(o => o.Message).OfType<RecordList>().Single().Entries.Any(e => e.Kind == RecordEntry.Direct);
        Assert.True(Reads(Bob) && Reads(Table.Ump3) && Reads(Table.Host) && Reads(Table.Lead));
        Assert.False(Reads(Ann) || Reads(Table.Obs));
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Hotline { Id = BobBody, Text = "x" })));
        var toNobody = t.Server.Handle(Table.Host, new Hotline { Id = Sentry, Text = "Hold the gate" });
        Assert.Equal(new[] { Table.Host }, toNobody.Where(o => o.Message is CommandMessage { Command: Hotline }).Select(o => o.PeerId));
    }

    [Fact]
    public void A_note_is_pinned_to_its_place_for_the_umpires_alone()
    {
        using var t = Seated();
        var res = t.Server.Handle(Table.Ump3, new Note { Text = "The mill burns", Pos = new(12, 7) });
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3 }, res.Where(o => o.Message is CommandMessage { Command: Note }).Select(o => o.PeerId).OrderBy(id => id));
        var kept = t.Server.Record.Single(e => e.Kind == RecordEntry.Noted);
        Assert.Equal(("Umpire Three", "The mill burns", new Vec2(12, 7)), (kept.Who, kept.Text, kept.Pos!.Value));
        Assert.DoesNotContain(t.Server.RecordFor(Ann).Select(o => o.Message).OfType<RecordList>().Single().Entries, e => e.Kind == RecordEntry.Noted);
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Note { Text = "x" })));
    }

    [Fact]
    public void A_hotline_line_also_reaches_the_umpires_marked_as_directing_the_piece_and_players_are_never_told_who_directs()
    {
        using var t = Seated();
        Assert.Contains("not allowed", Refused(t.Server.Handle(Table.Ump3, new SetDirectors { Id = Sentry, Names = new() { "Umpire Three" } })));
        var sent = t.Server.Handle(Table.Lead, new SetDirectors { Id = Sentry, Names = new() { " Umpire Three ", "umpire three", "" } });
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3 }, sent.Where(o => o.Message is CommandMessage).Select(o => o.PeerId).OrderBy(id => id));
        Assert.Equal(new[] { "Umpire Three" }, t.Server.State.Directors![Sentry]);
        Assert.Null(Visibility.Filter(t.Server.State, t.Server.Peers.First(p => p.PeerId == Ann)).Directors);
        var line = t.Server.Handle(Table.Host, new Hotline { Id = Sentry, Text = "Hold the gate" });
        Assert.Equal(new[] { Table.Host, Table.Ump3 }, line.Where(o => o.Message is CommandMessage { Command: Hotline }).Select(o => o.PeerId).OrderBy(id => id));
        t.Ok(Table.Lead, new Undo { TargetSeq = t.Server.Log.Last(c => c is SetDirectors).Seq });
        Assert.Null(t.Server.State.Directors);
    }

    [Fact]
    public void Lines_of_who_changed_what_are_kept_for_the_umpires_and_dropped_three_turns_on()
    {
        using var t = Seated();
        t.Server.Changed("Umpire One moved the bridge");
        var kept = t.Server.Record.Single(e => e.Kind == RecordEntry.Table);
        Assert.Equal(("Umpire One moved the bridge", RecordEntry.ToUmpires, 0), (kept.Text, kept.Reach, kept.Turn));
        Assert.DoesNotContain(t.Server.RecordFor(Ann).Select(o => o.Message).OfType<RecordList>().Single().Entries, e => e.Kind == RecordEntry.Table);
        Assert.Contains(t.Server.RecordFor(Table.Ump3).Select(o => o.Message).OfType<RecordList>().Single().Entries, e => e.Kind == RecordEntry.Table);
        for (int i = 0; i < RecordEntry.TableTurnsKept; i++) t.Ok(Table.Host, new AdvanceClock());
        Assert.Contains(t.Server.Record, e => e.Kind == RecordEntry.Table);
        t.Ok(Table.Host, new AdvanceClock());
        Assert.DoesNotContain(t.Server.Record, e => e.Kind == RecordEntry.Table);
    }

    [Fact]
    public void A_piece_speaking_or_told_on_the_hotline_does_not_stop_another_umpire_taking_back_its_move()
    {
        using var t = Seated();
        t.Ok(Table.Ump3, new MovePiece { Layer = U, Id = Sentry, Pos = new(3, 3) });
        long moved = t.Server.Log[^1].Seq;
        t.Ok(Table.Host, new Speak { Id = Sentry, Text = "Halt" });
        t.Ok(Table.Host, new Hotline { Id = Sentry, Text = "Hold the gate" });
        t.Ok(Table.Ump3, new Undo { TargetSeq = moved });
        Assert.Equal(new Vec2(0, 10), t.Server.State.Find(U, Sentry)!.Pos);
    }

    const string House = "p-000200", Hall = "p-000201", Beside = "p-000202", Upstairs = "p-000203", AtTheDoor = "p-000204", Behind = "p-000205", Yard = "p-000206", Clerk = "p-000207";

    static Table Housed()
    {
        var t = new Table();
        var pack = new PackManifest { Id = "testpack" };
        t.Store.RegisterDef(pack, new PieceDef { Id = "front", Name = "Front door", Tags = { Doors.Tag } });
        PieceStateDef Door(bool open) => new() { Parts = new() { new PartDef { Def = "front", Z = 10, Rot = open ? 90 : 0 } } };
        t.Store.RegisterDef(pack, new PieceDef { Id = "house", Name = "House", Size = new(20, 20), Tags = { Doors.BuildingTag }, States = { Door(false), Door(true) } });
        t.Ok(Table.Host, new SetCalibration { UnitsPerMeter = 1 });
        t.Ok(Table.Host, new SpawnPiece { Layer = U, Id = House, Def = "house", Pos = new(100, 0) });
        void At(string id, string label, Vec2 pos, double y = 0) => t.Ok(Table.Host, new SpawnPiece { Layer = U, Id = id, Def = "marker", Label = label, Pos = pos, Y = y });
        At(Hall, "Hall", new(100, 0));
        At(Beside, "Beside", new(101, 0));
        At(Clerk, "Clerk", new(99, 0));
        At(Upstairs, "Upstairs", new(100, 1), Footings.StoreyM);
        At(AtTheDoor, "At the door", new(100, 12));
        At(Behind, "Behind", new(100, -13));
        At(Yard, "Yard", new(100, 45));
        return t;
    }

    static List<string> Heard(Table t, string speaker, int loud, string? to = null)
    {
        t.Pass(GameServer.LineEveryMs);
        return Sent(t.Server.Handle(Table.Host, new Speak { Id = speaker, Text = "Hark", Loud = loud, To = to }))[Table.Host].Heard!.Where(id => id != House).Order().ToList();
    }

    static List<string> Ids(params string[] ids) => ids.Order().ToList();

    static void OpenTheDoor(Table t) => t.Ok(Table.Host, new SetState { Layer = U, Id = House, State = 1 });

    [Fact]
    public void A_whisper_inside_a_building_reaches_its_own_storey_and_never_passes_a_wall_or_a_floor()
    {
        using var t = Housed();
        Assert.Equal(Ids(Beside, Clerk), Heard(t, Hall, Earshot.Whisper));
        OpenTheDoor(t);
        Assert.Equal(Ids(Beside, Clerk), Heard(t, Hall, Earshot.Whisper));
        Assert.Empty(Heard(t, Upstairs, Earshot.Whisper));
        Assert.Empty(Heard(t, AtTheDoor, Earshot.Whisper).Intersect(new[] { Hall, Beside, Clerk, Upstairs }));
    }

    [Fact]
    public void A_say_inside_a_building_keeps_to_its_storey_and_goes_out_and_comes_in_only_through_an_open_door_within_its_reach()
    {
        using var t = Housed();
        Assert.Equal(Ids(Beside, Clerk), Heard(t, Hall, Earshot.Say));
        Assert.DoesNotContain(Hall, Heard(t, AtTheDoor, Earshot.Say));
        OpenTheDoor(t);
        Assert.Equal(Ids(AtTheDoor, Beside, Clerk), Heard(t, Hall, Earshot.Say));
        Assert.Contains(Hall, Heard(t, AtTheDoor, Earshot.Say));
        Assert.DoesNotContain(Upstairs, Heard(t, AtTheDoor, Earshot.Say));
    }

    [Fact]
    public void A_shout_passes_walls_and_floors_at_half_its_reach_and_an_open_door_at_its_whole_reach()
    {
        using var t = Housed();
        Assert.Equal(Ids(AtTheDoor, Behind, Beside, Clerk, Upstairs), Heard(t, Hall, Earshot.Shout));
        OpenTheDoor(t);
        Assert.Equal(Ids(AtTheDoor, Behind, Beside, Clerk, Upstairs, Yard), Heard(t, Hall, Earshot.Shout));
    }

    [Fact]
    public void A_whisper_said_to_one_piece_is_heard_by_it_alone_within_reach_and_the_record_names_it()
    {
        using var t = Housed();
        Assert.Equal(Ids(Beside), Heard(t, Hall, Earshot.Whisper, Beside));
        var kept = t.Server.Record.Last(e => e.Kind == RecordEntry.Speech);
        Assert.Equal(("Hall", "Beside", Earshot.Whisper), (kept.PieceName, kept.ToPieceName, kept.Loud));
        Assert.Equal("Beside", Assert.IsType<Speak>(t.Server.Log[^1]).ToName);
        Assert.Empty(Heard(t, Hall, Earshot.Whisper, Upstairs));
        Assert.Empty(Heard(t, Hall, Earshot.Whisper, Yard));
        Assert.Equal(Ids(Beside, Clerk), Heard(t, Hall, Earshot.Say, Beside));
        Assert.Null(t.Server.Record.Last(e => e.Kind == RecordEntry.Speech).ToPieceName);
    }

    [Fact]
    public void A_script_whispers_to_one_piece_named_by_its_handle_or_its_id()
    {
        using var t = Seated();
        t.Ok(Table.Host, new SetScript { Layer = U, Id = AnnBody, Text = "function tell(to) self.speak('Psst', 'whisper', to) end" });
        t.Server.Request(Table.Host, new RunLua { Target = AnnBody, Code = "tell(board.find('" + BobBody + "'))" });
        Assert.True(t.Server.Log.OfType<Speak>().Last() is Speak { Loud: Earshot.Whisper, To: BobBody, ToName: "Marker" } said && said.Heard!.SequenceEqual(new[] { BobBody }));
        t.Pass(GameServer.LineEveryMs);
        t.Server.Request(Table.Host, new RunLua { Target = AnnBody, Code = "tell('" + Sentry + "')" });
        Assert.True(t.Server.Log.OfType<Speak>().Last() is Speak { To: Sentry } far && far.Heard!.Count == 0);
    }

    [Fact]
    public void A_whisper_and_whom_it_is_said_to_survive_the_wire()
    {
        var sent = Assert.IsType<Speak>(Json.Clone<Command>(new Speak { Id = AnnBody, Text = "Psst", Loud = Earshot.Whisper, To = BobBody }));
        Assert.Equal((Earshot.Whisper, BobBody), (sent.Loud, sent.To));
        Assert.Equal(Earshot.Say, Json.Deserialize<Command>("""{"type":"Speak","id":"p-1","text":"Hello"}""") is Speak { Loud: var loud } ? loud : -1);
    }
}
