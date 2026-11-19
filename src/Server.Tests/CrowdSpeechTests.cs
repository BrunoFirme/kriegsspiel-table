using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class CrowdSpeechTests
{
    const long Ann = 40, Bob = 41;
    const string AnnBody = "p-000002", BobBody = "p-000003", Sentry = "p-000091", Town = "w-000001", Vagrants = "m-000002";
    const string Hans = Town + ":3", Greta = Town + ":4", Ulrich = Vagrants + ":0";
    const string U = Layers.Umpire;

    sealed class Watch : ICrowdWatch
    {
        readonly List<CrowdSeen> _people =
        [
            new(Hans, "Hans", "baker", CrowdEntry.Man, CrowdEntry.Adult, CrowdEntry.Middle, Town, "market", new(0, 0), 0, true),
            new(Greta, "Greta", "spinner", CrowdEntry.Woman, CrowdEntry.Elder, CrowdEntry.Poor, Town, null, new(30, 0), 0, false),
            new(Ulrich, "Ulrich", "beggar", CrowdEntry.Man, CrowdEntry.Youth, CrowdEntry.Poor, Vagrants, "square", new(100, 0), 0, true),
        ];

        public IReadOnlyList<CrowdOwner> Owners() => [new(Town, "Lindau", false, 2, 2), new(Vagrants, "Vagrants", true, 1, 1)];
        public IReadOnlyList<CrowdSeen> OutNow(string? owner) => _people.Where(p => owner == null || p.Owner == owner).ToList();
        public CrowdSeen? Find(string id) => _people.FirstOrDefault(p => p.Id == id);
    }

    static Table Market()
    {
        var t = new Table();
        t.LetIn(Ann, "Ann", Role.Player);
        t.LetIn(Bob, "Bob", Role.Player);
        t.Ok(Table.Host, new SetCalibration { UnitsPerMeter = 1 });
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = AnnBody, Pos = new(5, 0) });
        t.Ok(Table.Host, new MovePiece { Layer = U, Id = BobBody, Pos = new(40, 0) });
        t.Ok(Table.Host, new SpawnPiece { Layer = U, Id = Sentry, Def = "inf", Pos = new(0, 10) });
        (t.Server.Bodies[AnnBody], t.Server.Bodies[BobBody]) = ("Ann", "Bob");
        t.Server.Crowds = new Watch();
        return t;
    }

    static Dictionary<long, Speak> Sent(List<Outgoing> res) =>
        res.Where(o => o.Message is CommandMessage { Command: Speak }).ToDictionary(o => o.PeerId, o => (Speak)((CommandMessage)o.Message).Command);

    static string Lua(Table t, string target, string code) =>
        t.Server.Request(Table.Host, new RunLua { Target = target, Code = code }).Where(o => o.PeerId == Table.Host && o.Message is ScriptConsole)
            .SelectMany(o => ((ScriptConsole)o.Message).Lines).LastOrDefault(l => l.Kind is "result" or "error")?.Text ?? "";

    [Fact]
    public void An_umpire_speaks_as_a_crowd_person_where_they_stand_and_the_pieces_and_players_within_earshot_hear()
    {
        using var t = Market();
        var said = Sent(t.Server.Handle(Table.Ump, new Speak { Layer = CrowdPiece.Layer, Id = Hans, Text = "Fresh bread!" }));
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Ann }, said.Keys.OrderBy(k => k));
        Assert.Equal(new[] { AnnBody, Sentry }, said[Table.Ump].Heard);
        Assert.Equal(("Hans", new Vec2(0, 0)), (said[Ann].Name, said[Ann].Pos!.Value));
        var kept = t.Server.Record.Single(e => e.Kind == RecordEntry.Speech);
        Assert.Equal(((string?)null, "Hans"), (kept.Piece, kept.PieceName));
        Assert.Equal(new[] { "Ann" }, kept.Hearers);
        Assert.Single(t.Server.RecordFor(Ann).Select(o => o.Message).OfType<RecordList>().Single().Entries, e => e.Kind == RecordEntry.Speech);
        Assert.DoesNotContain(t.Server.RecordFor(Bob).Select(o => o.Message).OfType<RecordList>().Single().Entries, e => e.Kind == RecordEntry.Speech);
    }

    [Fact]
    public void A_player_cannot_speak_as_a_crowd_person_and_with_no_crowds_on_the_host_nobody_hears()
    {
        using var t = Market();
        Assert.Contains("not allowed", Assert.IsType<ErrorMessage>(Assert.Single(t.Server.Handle(Ann, new Speak { Layer = CrowdPiece.Layer, Id = Hans, Text = "Hi" })).Message).Text);
        t.Server.Crowds = null;
        t.Ok(Table.Ump, new Speak { Layer = CrowdPiece.Layer, Id = Hans, Text = "Anyone?" });
        Assert.True(t.Server.Log.OfType<Speak>().Last() is { Pos: null } quiet && quiet.Heard!.Count == 0);
    }

    [Fact]
    public void A_script_lists_the_people_out_by_place_town_reach_and_kind_and_makes_one_speak()
    {
        using var t = Market();
        Assert.Equal("1", Lua(t, "", "return #crowds.people{ place = 'market' }"));
        Assert.Equal("Ulrich", Lua(t, "", "return crowds.people{ town = '" + Vagrants + "' }[1].name"));
        Assert.Equal("2", Lua(t, "", "return #crowds.people{ near = { x = 0, z = 0 }, within = 40 }"));
        Assert.Equal("Greta", Lua(t, "", "return crowds.people{ age = 'elder', staying = false }[1].name"));
        Assert.Equal("1 1 2", Lua(t, "", "return #crowds.towns() .. ' ' .. #crowds.made() .. ' ' .. #crowds.towns()[1].people()"));
        Lua(t, "", "crowds.find('" + Hans + "').speak('Who wants bread?', 'shout')");
        var shout = t.Server.Log.OfType<Speak>().Last();
        Assert.Equal((CrowdPiece.Layer, Hans, Earshot.Shout), (shout.Layer, shout.Id, shout.Loud));
        Assert.Contains(BobBody, shout.Heard!);
        Assert.Equal("nil", Lua(t, "", "return tostring(crowds.find('nobody'))"));
    }

    [Fact]
    public void A_piece_script_hears_a_crowd_person_and_learns_who_spoke()
    {
        using var t = Market();
        t.Ok(Table.Host, new SetScript { Layer = U, Id = Sentry, Text = "function on_heard(who, text) heard = who.name .. ' at the ' .. who.place .. ': ' .. text end" });
        t.Ok(Table.Ump, new Speak { Layer = CrowdPiece.Layer, Id = Hans, Text = "Fresh bread!" });
        Assert.Equal("Hans at the market: Fresh bread!", Lua(t, Sentry, "return heard"));
        Assert.Equal("15", Lua(t, Sentry, "return tostring(board.distance(self, crowds.find('" + Hans + "')) + 5)"));
    }
}
