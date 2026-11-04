using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class ClientSessionTests
{
    readonly ClientSession _s = new(new PackageStore(TestFolders.New()));
    readonly List<Command> _sent = new();

    public ClientSessionTests() { _s.Send = _sent.Add; }

    void JoinAs(Role role, long peer = 2) => _s.OnJoined(new JoinResult { Ok = true, PeerId = peer, Role = role, View = new GameState(), Created = "2026-01-01T00:00:00.0000000Z" });

    static SpawnPiece Spawn(long seq, long actor, string id, long undoOf = 0) => new() { Seq = seq, Actor = actor, Id = id, Def = "d", UndoOf = undoOf };

    [Fact]
    public void Joining_sets_who_this_is_and_shows_the_table()
    {
        int resets = 0;
        _s.Reset += () => resets++;
        _s.SetName("Ann");
        JoinAs(Role.Umpire);
        Assert.Equal((2L, "Ann"), (_s.Me.PeerId, _s.Me.Name));
        Assert.True(_s.IsUmpire);
        Assert.False(_s.IsHost);
        Assert.True(_s.Dirty);
        Assert.Equal("2026-01-01T00:00:00.0000000Z", _s.Created);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void The_session_is_caught_up_once_the_host_has_answered_every_change_it_sent()
    {
        JoinAs(Role.Umpire);
        Assert.True(_s.CaughtUp);
        _s.Do(new SpawnPiece { Id = "a", Def = "d" });
        _s.Do(new SpawnPiece { Id = "b", Def = "d" });
        Assert.Equal(2, _s.Unanswered);
        _s.OnCommand(Spawn(1, 3, "x"));
        Assert.Equal(2, _s.Unanswered);
        _s.OnCommand(Spawn(2, 2, "a"));
        Assert.Equal(1, _s.Unanswered);
        _s.OnError("refused");
        Assert.True(_s.CaughtUp);
        _s.OnError("a script's own error");
        _s.OnCommand(Spawn(3, 2, "y"));
        Assert.Equal(0, _s.Unanswered);
        _s.Do(new SpawnPiece { Id = "c", Def = "d" });
        _s.SetView(new GameState());
        Assert.True(_s.CaughtUp);
    }

    [Fact]
    public void My_own_commands_go_on_the_undo_stack_and_ctrl_z_sends_the_newest_back()
    {
        JoinAs(Role.Umpire);
        _s.OnCommand(Spawn(1, 2, "a"));
        _s.OnCommand(Spawn(2, 3, "b"));
        _s.OnCommand(Spawn(3, 2, "c", undoOf: 9));
        Assert.Equal(new long[] { 1 }, _s.MyUndoable);
        Assert.Equal(3, _s.Live.LastSeq);
        _s.UndoLast();
        Assert.Equal(1, Assert.IsType<Undo>(Assert.Single(_sent)).TargetSeq);
        _s.UndoLast();
        Assert.Single(_sent);
    }

    [Fact]
    public void Ctrl_y_takes_back_my_undo_and_ctrl_z_then_undoes_the_change_again()
    {
        JoinAs(Role.Umpire);
        _s.OnCommand(Spawn(1, 2, "a"));
        _s.UndoLast();
        _s.OnCommand(Spawn(2, 2, "b", undoOf: 1));
        Assert.Empty(_s.MyUndoable);
        Assert.Equal(new long[] { 2 }, _s.MyRedoable);

        _s.RedoLast();
        Assert.Equal(2, Assert.IsType<Undo>(_sent[^1]).TargetSeq);
        _s.OnCommand(Spawn(3, 2, "c", undoOf: 2));
        Assert.Equal(new long[] { 3 }, _s.MyUndoable);
        Assert.Empty(_s.MyRedoable);

        _s.UndoLast();
        Assert.Equal(3, Assert.IsType<Undo>(_sent[^1]).TargetSeq);
        _s.OnCommand(Spawn(4, 2, "d", undoOf: 3));
        Assert.Equal(new long[] { 4 }, _s.MyRedoable);
        _s.RedoLast();
        _s.RedoLast();
        Assert.Equal(4, _sent.Count);
    }

    [Fact]
    public void A_new_change_of_mine_ends_what_can_be_redone_but_another_umpires_does_not()
    {
        JoinAs(Role.Umpire);
        _s.OnCommand(Spawn(1, 2, "a"));
        _s.UndoLast();
        _s.OnCommand(Spawn(2, 2, "b", undoOf: 1));
        _s.OnCommand(Spawn(3, 3, "c"));
        Assert.Equal(new long[] { 2 }, _s.MyRedoable);
        _s.OnCommand(Spawn(4, 2, "d"));
        Assert.Empty(_s.MyRedoable);
        Assert.Equal(new long[] { 4 }, _s.MyUndoable);
    }

    [Fact]
    public void An_observer_has_nothing_to_undo()
    {
        JoinAs(Role.Observer);
        _s.MyUndoable.Push(5);
        _s.UndoLast();
        Assert.Empty(_sent);
        Assert.True(_s.SeesBoard);
        Assert.False(_s.CanEdit(Layers.Umpire));
    }

    [Fact]
    public void While_reviewing_a_past_turn_nothing_is_sent_and_the_view_is_the_past()
    {
        JoinAs(Role.Umpire);
        var past = new GameState();
        var errors = new List<string>();
        _s.Error += errors.Add;
        _s.Review(past, new TurnInfo());
        Assert.Same(past, _s.View);
        Assert.False(_s.CanEdit(Layers.Umpire));
        _s.Do(Spawn(0, 2, "x"));
        Assert.Empty(_sent);
        Assert.Equal("You are looking at a past turn; press Back to now to change the table.", Assert.Single(errors));
        _s.Review(null, null);
        Assert.Same(_s.Live, _s.View);
        Assert.True(_s.CanEdit(Layers.Umpire));
        _s.Do(Spawn(0, 2, "x"));
        Assert.Single(_sent);
    }

    [Fact]
    public void A_change_that_will_not_apply_says_so_and_asks_for_the_table()
    {
        JoinAs(Role.Umpire);
        var errors = new List<string>();
        int desynced = 0, applied = 0;
        _s.Error += errors.Add;
        _s.Desynced += () => desynced++;
        _s.Applied += _ => applied++;
        _s.OnCommand(new RenameBoard { Seq = 4, Actor = 2, Id = "no such sheet", Name = "x" });
        Assert.Equal(1, desynced);
        Assert.Equal(0, applied);
        Assert.StartsWith("This table no longer agrees with the host's (RenameBoard: ", Assert.Single(errors));
        Assert.Equal(4, _s.Live.LastSeq);
        Assert.Empty(_s.MyUndoable);
    }

    [Fact]
    public void A_role_change_clears_the_undo_stack_and_any_review()
    {
        JoinAs(Role.LeadUmpire);
        _s.OnAccess(new GameAccess());
        _s.OnCommand(Spawn(1, 2, "a"));
        _s.Review(new GameState(), new TurnInfo());
        int roleChanged = 0;
        _s.RoleChanged += () => roleChanged++;
        _s.OnRoleChanged(new RoleChanged { Role = Role.Observer, View = new GameState() });
        Assert.Empty(_s.MyUndoable);
        Assert.Empty(_s.MyRedoable);
        Assert.False(_s.Reviewing);
        Assert.Null(_s.Access);
        Assert.Equal(1, roleChanged);
    }

    [Fact]
    public void The_console_keeps_each_line_once_and_a_backlog_replaces_it()
    {
        var changes = new List<int?>();
        _s.ConsoleChanged += l => changes.Add(l?.Count);
        ConsoleLine L(long seq) => new() { Seq = seq, Text = "line " + seq };
        _s.OnScriptMessage(new ScriptConsole { Lines = { L(1), L(2) } });
        _s.OnScriptMessage(new ScriptConsole { Lines = { L(2), L(3) } });
        _s.OnScriptMessage(new ScriptConsole { Lines = { L(1) } });
        Assert.Equal(new long[] { 1, 2, 3 }, _s.ConsoleLines.Select(l => l.Seq));
        _s.OnScriptMessage(new ScriptConsole { Backlog = true, Lines = { L(7) } });
        Assert.Equal(new long[] { 7 }, _s.ConsoleLines.Select(l => l.Seq));
        _s.OnScriptMessage(new ScriptConsole { Lines = { L(5) } });
        Assert.Equal(new int?[] { 2, 1, null }, changes);
    }

    [Fact]
    public void The_console_keeps_the_newest_thousand_lines()
    {
        _s.OnScriptMessage(new ScriptConsole { Lines = Enumerable.Range(1, ClientSession.ConsoleKeep + 5).Select(i => new ConsoleLine { Seq = i }).ToList() });
        Assert.Equal(ClientSession.ConsoleKeep, _s.ConsoleLines.Count);
        Assert.Equal(6, _s.ConsoleLines[0].Seq);
    }
}
