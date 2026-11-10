using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class ApprovalTests
{
    const string U = Layers.Umpire;

    static JoinResult Answer(List<Outgoing> outs, long peer) => Assert.IsType<JoinResult>(outs.First(o => o.PeerId == peer && o.Message is JoinResult).Message);

    static Challenge Asked(List<Outgoing> outs, long peer) => Assert.IsType<Challenge>(Assert.Single(outs, o => o.PeerId == peer).Message);

    static string HashOf(Table t, string name) => t.Server.Access.Approved.Single(a => a.Name == name).TokenHash;

    static JoinRequest Knock(string name, JoinKey key, string password = Table.Password) =>
        new() { Name = name, Password = password, AppVersion = "test", Protocol = ProtocolVersion.Current, Key = key.Public };

    static JoinRequest Proved(JoinRequest knock, string proof) =>
        new() { Name = knock.Name, Password = knock.Password, Token = knock.Token, AppVersion = knock.AppVersion, Protocol = knock.Protocol, Key = knock.Key, Proof = proof };

    static List<Outgoing> KnockWithKey(Table t, long peer, JoinRequest knock, JoinKey key)
    {
        var ch = Asked(t.Server.Join(peer, knock), peer);
        return t.Server.Join(peer, Proved(knock, key.Sign(ch.Nonce)));
    }

    [Fact]
    public void A_forgotten_approval_is_refused_at_the_door_whatever_it_brings()
    {
        using var t = new Table();
        t.Server.Leave(Table.Ump3);
        t.Door(Table.Lead, new Forget { TokenHash = HashOf(t, "Umpire Three") });
        Assert.DoesNotContain(t.Server.Access.Approved, a => a.Name == "Umpire Three");
        var wrong = Answer(t.Knock(40, "Umpire Three", password: "wrong", token: t.Tokens[Table.Ump3]), 40);
        Assert.False(wrong.Ok);
        Assert.Equal("wrong password", wrong.Error);
        Assert.True(Answer(t.Knock(41, "Umpire Three", token: t.Tokens[Table.Ump3]), 41).Waiting);
        Assert.DoesNotContain(t.Server.Peers, p => p.PeerId is 40 or 41);
    }

    [Fact]
    public void Only_someone_away_is_forgotten_and_a_lead_only_by_the_host()
    {
        using var t = new Table();
        Assert.Contains("at the table now", t.DoorError(Table.Lead, new Forget { TokenHash = HashOf(t, "Umpire Three") }));
        t.LetIn(40, "Second lead", Role.LeadUmpire);
        t.Server.Leave(40);
        var lead = HashOf(t, "Second lead");
        Assert.Contains("only the host", t.DoorError(Table.Lead, new Forget { TokenHash = lead }));
        Assert.Contains("only the host or a lead umpire", t.DoorError(Table.Ump3, new Forget { TokenHash = lead }));
        Assert.Contains("nobody", t.DoorError(Table.Host, new Forget { TokenHash = "nope" }));
        t.Door(Table.Host, new Forget { TokenHash = lead });
        Assert.DoesNotContain(t.Server.Access.Approved, a => a.Name == "Second lead");
        Assert.True(t.Server.Dirty);
    }

    [Fact]
    public void A_person_let_back_in_as_who_they_were_keeps_their_name_role_and_zone()
    {
        using var t = new Table();
        t.Server.State.Zones.Add(new Zone { Id = "z-1", Name = "Home", Owner = "Umpire Three", Polygon = { new(-5, -5), new(5, -5), new(5, 5), new(-5, 5) } });
        var hash = HashOf(t, "Umpire Three");
        var approvals = t.Server.Access.Approved.Count;
        t.Server.Leave(Table.Ump3);
        Assert.True(Answer(t.Knock(40, "Umpire 3"), 40).Waiting);
        var jr = Table.JoinResultOf(t.Door(Table.Lead, new Admit { PeerId = 40, Role = Role.Observer, Returning = hash }), 40, ok: true);
        Assert.Equal("Umpire Three", jr.Name);
        Assert.Equal(Role.Umpire, jr.Role);
        Assert.Equal("Umpire Three", t.Server.Peer(40)!.Name);
        Assert.Equal(approvals, t.Server.Access.Approved.Count);
        t.Ok(40, new MovePiece { Layer = U, Id = "p-000002", Pos = new(1, 1) });
        Assert.Equal("wrong password", Answer(t.Knock(41, "x", password: "wrong", token: t.Tokens[Table.Ump3]), 41).Error);
        Assert.Equal("Umpire Three", t.Rejoin(42, jr.Token!).Name);
    }

    [Fact]
    public void Letting_someone_back_in_as_another_is_refused_when_that_one_is_seated_or_gone_or_a_lead()
    {
        using var t = new Table();
        t.LetIn(40, "Second lead", Role.LeadUmpire);
        t.Server.Leave(40);
        Assert.True(Answer(t.Knock(41, "Who"), 41).Waiting);
        Assert.Contains("at the table now", t.DoorError(Table.Lead, new Admit { PeerId = 41, Returning = HashOf(t, "Umpire Three") }));
        Assert.Contains("only the host", t.DoorError(Table.Lead, new Admit { PeerId = 41, Returning = HashOf(t, "Second lead") }));
        Assert.Contains("no longer", t.DoorError(Table.Lead, new Admit { PeerId = 41, Returning = "gone" }));
        Assert.Contains(t.Server.Waiting, w => w.PeerId == 41);
        Assert.Equal("Second lead", Table.JoinResultOf(t.Door(Table.Host, new Admit { PeerId = 41, Returning = HashOf(t, "Second lead") }), 41, ok: true).Name);
    }

    [Fact]
    public void Forgetting_someone_leaves_every_other_colour_and_a_newcomer_takes_the_free_one()
    {
        using var t = new Table();
        int Slot(string name) => t.Server.Access.Approved.Single(a => a.Name == name).ColorSlot;
        Assert.Equal(new[] { 1, 2, 3 }, new[] { Slot("Umpire Two"), Slot("Umpire Three"), Slot("Watcher") });
        t.Server.Leave(Table.Ump3);
        t.Door(Table.Lead, new Forget { TokenHash = HashOf(t, "Umpire Three") });
        Assert.Equal(new[] { 1, 3 }, new[] { Slot("Umpire Two"), Slot("Watcher") });
        t.LetIn(40, "Newcomer", Role.Umpire);
        Assert.Equal(2, Slot("Newcomer"));
    }

    [Fact]
    public void Approvals_saved_without_colours_take_the_free_ones_in_list_order()
    {
        using var t = new Table(join: false);
        var srv = new GameServer(GameState.FromScenario(t.Scenario), t.Store, new GameAccess
        {
            Approved = { new Approved { Name = "First" }, new Approved { Name = "Second", ColorSlot = 1 }, new Approved { Name = "Third" } },
        });
        Assert.Equal(new[] { 2, 1, 3 }, srv.Access.Approved.Select(a => a.ColorSlot));
    }

    [Fact]
    public void A_pc_whose_key_was_let_in_comes_straight_back_at_any_address_after_proving_it()
    {
        using var t = new Table();
        using var key = JoinKey.New();
        Assert.True(Answer(KnockWithKey(t, 40, Knock("Kim", key), key), 40).Waiting);
        t.Door(Table.Host, new Admit { PeerId = 40, Role = Role.Umpire });
        Assert.Equal(key.Public, t.Server.Access.Approved.Single(a => a.Name == "Kim").Key);
        t.Server.Leave(40);
        var jr = Answer(KnockWithKey(t, 41, Knock("Someone else", key, password: ""), key), 41);
        Assert.True(jr.Ok, jr.Error);
        Assert.Equal("Kim", jr.Name);
        Assert.Equal(Role.Umpire, jr.Role);
    }

    [Fact]
    public void A_key_without_its_proof_gets_nobody_in()
    {
        using var t = new Table();
        using var kim = JoinKey.New();
        using var eve = JoinKey.New();
        KnockWithKey(t, 40, Knock("Kim", kim), kim);
        t.Door(Table.Host, new Admit { PeerId = 40, Role = Role.Umpire });
        t.Server.Leave(40);
        var forged = Knock("Eve", kim);
        var ch = Asked(t.Server.Join(50, forged), 50);
        Assert.Contains("prove", Answer(t.Server.Join(50, Proved(forged, eve.Sign(ch.Nonce))), 50).Error);
        Assert.Contains("prove", Answer(t.Server.Join(51, Proved(forged, kim.Sign(ch.Nonce))), 51).Error);
        Assert.Contains("prove", Answer(t.Server.Join(52, Proved(forged, "not a signature")), 52).Error);
        Assert.Contains("read", Answer(t.Server.Join(53, new JoinRequest { Protocol = ProtocolVersion.Current, Key = new string('A', 1000), Proof = "x" }), 53).Error);
        Assert.DoesNotContain(t.Server.Peers, p => p.PeerId >= 50);
        Assert.Empty(t.Server.Waiting);
    }

    [Fact]
    public void Coming_back_by_token_teaches_the_host_this_pcs_key()
    {
        using var t = new Table();
        using var key = JoinKey.New();
        t.Server.Leave(Table.Ump3);
        var back = Answer(KnockWithKey(t, 40, new JoinRequest { Token = t.Tokens[Table.Ump3], Protocol = ProtocolVersion.Current, Key = key.Public }, key), 40);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(key.Public, t.Server.Access.Approved.Single(a => a.Name == "Umpire Three").Key);
        t.Server.Leave(40);
        Assert.Equal("Umpire Three", Answer(KnockWithKey(t, 41, Knock("U3", key, password: ""), key), 41).Name);
    }
}
