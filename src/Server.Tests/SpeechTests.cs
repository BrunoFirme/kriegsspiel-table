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
}
