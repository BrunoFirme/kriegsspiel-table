using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class DoorTests
{
    static JoinResult Answer(List<Outgoing> outs, long peer) => Assert.IsType<JoinResult>(outs.First(o => o.PeerId == peer && o.Message is JoinResult).Message);

    [Fact]
    public void A_knock_from_an_app_on_another_protocol_is_refused_naming_both_versions()
    {
        using var t = new Table();
        t.Server.AppVersion = "1.4.0";
        var newer = Answer(t.Server.Join(50, new JoinRequest { Name = "Newer", Password = Table.Password, AppVersion = "1.5.0", Protocol = ProtocolVersion.Current + 1 }), 50);
        Assert.False(newer.Ok);
        Assert.False(newer.Waiting);
        Assert.Contains("1.5.0", newer.Error);
        Assert.Contains("1.4.0", newer.Error);
        Assert.Contains($"protocol {ProtocolVersion.Current + 1}", newer.Error);
        Assert.Contains($"protocol {ProtocolVersion.Current}", newer.Error);
        Assert.Empty(t.Server.Waiting);
    }

    [Fact]
    public void A_knock_that_names_no_version_is_from_an_older_app_and_is_refused_the_same_way()
    {
        using var t = new Table();
        t.Server.AppVersion = "1.4.0";
        var old = Answer(t.Server.Join(51, Json.Deserialize<JoinRequest>("""{"name":"Old","password":"abc"}""")), 51);
        Assert.False(old.Ok);
        Assert.False(old.Waiting);
        Assert.Contains("1.4.0", old.Error);
        Assert.Contains("older", old.Error);
        var back = Answer(t.Server.Join(52, new JoinRequest { Token = t.Tokens[Table.Ump3] }), 52);
        Assert.False(back.Ok);
        Assert.Empty(t.Server.Waiting);
        Assert.DoesNotContain(t.Server.Peers, p => p.PeerId is 51 or 52);
    }

    [Fact]
    public void Wrong_passwords_from_one_place_are_slowed_down_and_others_still_get_in()
    {
        long now = 1_000_000;
        var t = new Table();
        using var _ = t;
        var srv = new GameServer(GameState.FromScenario(t.Scenario), t.Store, new GameAccess { Password = "abc" }, clock: () => now);
        srv.Join(1, new JoinRequest { Name = "Host" });
        JoinResult Knock(long peer, string password, string from) =>
            Answer(srv.Join(peer, new JoinRequest { Name = "Guess", Password = password, AppVersion = "x", Protocol = ProtocolVersion.Current }, from), peer);

        for (int i = 0; i < 3; i++) Assert.Equal("wrong password", Knock(60, "nope" + i, "10.0.0.9").Error);
        var slowed = Knock(61, "abc", "10.0.0.9");
        Assert.False(slowed.Waiting);
        Assert.Contains("too many wrong passwords", slowed.Error);
        Assert.True(Knock(62, "abc", "10.0.0.7").Waiting);
        now += 1_001;
        Assert.Equal("wrong password", Knock(63, "nope", "10.0.0.9").Error);
        now += 1_001;
        Assert.Contains("too many wrong passwords", Knock(64, "abc", "10.0.0.9").Error);
        now += 1_000;
        Assert.True(Knock(65, "abc", "10.0.0.9").Waiting);
        Assert.Equal("wrong password", Knock(66, "nope", "10.0.0.9").Error);
        Assert.Equal("wrong password", Knock(67, "nope", "10.0.0.9").Error);
        Assert.True(Knock(68, "abc", "10.0.0.9").Waiting);
    }

    [Fact]
    public void Without_an_address_the_peer_is_the_one_slowed_down()
    {
        using var t = new Table();
        for (int i = 0; i < 3; i++) t.Knock(70, "Guess", "nope");
        Assert.Contains("too many wrong passwords", Answer(t.Knock(70, "Guess"), 70).Error);
        Assert.True(Answer(t.Knock(71, "Other"), 71).Waiting);
    }

    [Fact]
    public void The_waiting_list_has_room_for_so_many_and_turns_the_next_away_in_words()
    {
        using var t = new Table();
        for (int i = 0; i < GameServer.MaxWaiting; i++) Assert.True(Answer(t.Knock(100 + i, "k" + i), 100 + i).Waiting);
        var turned = Answer(t.Knock(500, "late"), 500);
        Assert.False(turned.Waiting);
        Assert.Contains("waiting", turned.Error);
        Assert.Equal(GameServer.MaxWaiting, t.Server.Waiting.Count);
        Assert.True(Answer(t.Knock(100, "k0"), 100).Waiting);
        t.Door(Table.Host, new Admit { PeerId = 101, Role = Role.Observer });
        Assert.True(Answer(t.Knock(501, "next"), 501).Waiting);
    }

    [Fact]
    public void A_knock_full_of_nulls_is_answered_and_never_throws()
    {
        using var t = new Table();
        Assert.False(Answer(t.Server.Join(40, null!), 40).Ok);
        var nulls = Answer(t.Server.Join(41, new JoinRequest { Name = null!, Password = null!, Token = null, AppVersion = null, Protocol = ProtocolVersion.Current }), 41);
        Assert.Equal("wrong password", nulls.Error);
        t.Door(Table.Host, new SetPassword { Password = "" });
        Assert.True(Answer(t.Server.Join(42, new JoinRequest { Name = null!, Password = null!, Protocol = ProtocolVersion.Current }), 42).Waiting);
    }
}
