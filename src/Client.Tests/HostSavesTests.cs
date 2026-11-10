using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class HostSavesTests
{
    readonly string _dir = TestFolders.New();
    readonly ClientSession _session;
    readonly FakeFolders _folders = new();
    readonly List<string> _errors = new(), _notes = new();
    readonly List<string?> _problems = new();
    readonly List<string> _posted = new();
    readonly Queue<Action> _mainThread = new();

    void RunPosted()
    {
        while (true)
        {
            Action a;
            lock (_mainThread) { if (_mainThread.Count == 0) return; a = _mainThread.Dequeue(); } a();
        }
    }

    string? Load(LoadFrom from)
    {
        string? why = "never answered";
        bool answered = false;
        _saves.Load(from, w => { why = w; answered = true; });
        Assert.True(SpinWait.SpinUntil(() => { RunPosted(); return answered; }, 10000));
        return why;
    }

    GameServer HostRun(string run, int turns)
    {
        _store.MakeRun("feud", run, "Run 1", null, DateTime.UtcNow);
        var server = Host(_store.LivePath("feud", run));
        server.State.ScenarioId = "feud";
        server.Handle(1, new SpawnPiece { Layer = Layers.Umpire, Def = "d", Pos = new Vec2(1, 1) });
        for (int i = 0; i < turns; i++) server.Handle(1, new AdvanceClock());
        return server;
    }

    void TickUntil(Func<bool> done)
    {
        Assert.True(SpinWait.SpinUntil(() =>
        {
            _now += 1;
            _saves.Tick(1);
            _saves.Harvest();
            RunPosted();
            return done();
        }, 10000));
    }
    GameServer? _server;
    double _now;
    readonly HostSaves _saves;
    readonly SaveStore _store;

    public HostSavesTests()
    {
        _session = new ClientSession(new PackageStore(Path.Combine(_dir, "packages")));
        _store = new SaveStore(Path.Combine(_dir, "_saves", "live"), Path.Combine(_dir, "_saves", "history"), Path.Combine(_dir, "_saves", "trash"));
        _saves = new HostSaves(() => _session, () => _server, _folders, _store, new SaveServices
        {
            Dispatch = _ => { }, PostWriteBackDone = e => { lock (_posted) _posted.Add(e); }, Post = a => { lock (_mainThread) _mainThread.Enqueue(a); },
            Clock = () => _now,
            Log = new ClientLog { Error = _errors.Add },
            MirrorOf = () => "the host at 10.0.0.1",
        });
        _saves.SaveProblem += _problems.Add;
        _saves.SaveNote += _notes.Add;
    }

    GameServer Host(string savePath)
    {
        _server = new GameServer(new GameState { Name = "Feud" }, _session.Store);
        _session.SavePath = savePath;
        _saves.BeginHosting(savePath, _server.Turns.Count);
        _server.Join(1, new JoinRequest { Name = "Host" });
        return _server;
    }

    [Fact]
    public void Nothing_is_saved_without_a_table_or_a_save_path()
    {
        _saves.SaveNow(closing: true);
        _saves.Tick(1);
        Assert.Equal("this table is not one of a scenario's games", Load(new LoadFrom("Turn 1", TurnIndex: 0)));
        Assert.Equal("no log", _saves.SaveLogState);
        Assert.Empty(_problems);
    }

    [Fact]
    public void A_closing_save_writes_the_table_before_it_returns()
    {
        var path = Path.Combine(_dir, "live", "game.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var server = Host(path);
        server.Dirty = true;
        _saves.SaveNow(closing: true);
        Assert.True(File.Exists(path));
        Assert.Equal("Feud", Json.Load<SaveFile>(path).Snapshot.Name);
        Assert.Null(_saves.LastSaveProblem);
        Assert.Empty(_problems);
    }

    [Fact]
    public void A_closing_save_that_fails_is_shown_at_once_and_the_table_stays_unsaved()
    {
        var blocker = Path.Combine(_dir, "a file");
        File.WriteAllText(blocker, "");
        var path = Path.Combine(blocker, "game.json");
        var server = Host(path);
        server.Dirty = false;
        _saves.SaveNow(closing: true);
        Assert.StartsWith("the game could not be written to " + path, _saves.LastSaveProblem);
        Assert.Equal(_saves.LastSaveProblem, Assert.Single(_problems));
        Assert.True(server.Dirty);
        Assert.Contains(_errors, e => e.StartsWith("save failed: the game could not be written"));
    }

    [Fact]
    public void A_failing_save_is_quiet_for_ten_seconds_then_shown_and_cleared_once_it_saves_again()
    {
        var blocker = Path.Combine(_dir, "a file");
        File.WriteAllText(blocker, "");
        var bad = Path.Combine(blocker, "game.json");
        Host(bad);
        _now = 100;
        _saves.SaveNow();
        Assert.True(SpinWait.SpinUntil(() => { _saves.Harvest(); return _saves.LastSaveProblem != null; }, 10000));
        Assert.Empty(_problems);
        Assert.Null(_saves.Warning);
        _now = 110;
        _saves.SaveNow();
        Assert.True(SpinWait.SpinUntil(() => { _saves.Harvest(); return _problems.Count > 0; }, 10000));
        Assert.Single(_problems);
        Assert.Equal(_problems[0], _saves.Warning);
        var good = Path.Combine(_dir, "game.json");
        _session.SavePath = good;
        _saves.SaveNow(closing: true);
        Assert.Equal(new string?[] { _problems[0], null }, _problems);
        Assert.Null(_saves.LastSaveProblem);
        Assert.Null(_saves.Warning);
    }

    [Fact]
    public void Loading_a_turn_writes_the_game_first_and_plays_on_in_a_new_folder_leaving_the_old_play_whole()
    {
        var server = HostRun("r1", turns: 3);
        var old = _session.SavePath!;
        Assert.Null(Load(new LoadFrom("Turn 1", TurnIndex: 1)));
        Assert.Equal(3, Json.Load<SaveFile>(old).Turns.Count());
        Assert.NotEqual(old, _session.SavePath);
        var (dir, run) = _store.RunOf(_session.SavePath!)!.Value;
        Assert.Equal("feud", dir);
        Assert.Equal("Run 2 · from Turn 1", Path.GetFileName(_store.RunFolderOf(dir, run)));
        Assert.Single(server.Turns);
        Assert.Equal(1, server.State.Clock.Turn);
        Assert.True(_saves.WaitForDisk());
        _saves.Harvest();
        Assert.Equal(1, Json.Load<SaveFile>(_session.SavePath!).Snapshot.Clock.Turn);
        Assert.Equal(run, _store.CurrentRun("feud"));
    }

    [Fact]
    public void Loading_the_latest_of_another_play_carries_on_in_its_folder()
    {
        var server = HostRun("r1", turns: 2);
        Assert.Null(Load(new LoadFrom("Turn 0", TurnIndex: 0)));
        Assert.True(_saves.WaitForDisk());
        _saves.Harvest();
        Assert.Null(Load(new LoadFrom("Run 1", Run: "r1")));
        Assert.Equal(_store.LivePath("feud", "r1"), _session.SavePath);
        Assert.Equal(2, server.Turns.Count);
        Assert.Equal(2, server.State.Clock.Turn);
        Assert.True(_saves.WaitForDisk());
        _saves.Harvest();
        Assert.Equal("r1", _store.CurrentRun("feud"));
    }

    [Fact]
    public void Every_ended_turn_is_saved_into_the_play_s_folder_and_each_opens_with_the_turns_before_it()
    {
        var server = HostRun("r1", turns: 0);
        server.Handle(1, new AdvanceClock());
        server.Handle(1, new AdvanceClock());
        var folder = _store.RunFolderOf("feud", "r1")!;
        TickUntil(() => Directory.GetFiles(folder, "*" + SaveStore.Ext).Length == 2);
        var saves = Directory.GetFiles(folder, "*" + SaveStore.Ext).Select(f => Json.Load<SaveFile>(f)).OrderBy(s => s.Snapshot.Clock.Turn).ToList();
        Assert.Equal(new[] { 0, 1 }, saves.Select(s => s.Snapshot.Clock.Turn));
        Assert.Equal(new[] { 0, 1 }, saves.Select(s => s.Turns.Count));
        Assert.All(saves, s => Assert.Single(s.Snapshot.Layer(Layers.Umpire)));
        Assert.True(Json.IsPacked(File.ReadAllBytes(Directory.GetFiles(folder, "*" + SaveStore.Ext)[0])));
        server.Handle(1, new AdvanceClock());
        TickUntil(() => Directory.GetFiles(folder, "*" + SaveStore.Ext).Length == 3);
    }

    [Fact]
    public void A_save_made_by_hand_goes_into_the_folder_chosen_and_a_name_taken_is_refused_unless_replaced()
    {
        HostRun("r1", turns: 1);
        var root = _store.SavesRoot("feud");
        string? Save(bool replace)
        {
            string? why = "never answered";
            bool answered = false;
            _saves.SaveAs(root, "Before the attack", replace, (_, w) => { why = w; answered = true; });
            Assert.True(SpinWait.SpinUntil(() => { RunPosted(); return answered; }, 10000));
            return why;
        }
        Assert.Null(Save(false));
        Assert.Single(Json.Load<SaveFile>(Path.Combine(root, "Before the attack" + SaveStore.Ext)).Turns);
        Assert.NotNull(Save(false));
        Assert.Null(Save(true));
        Assert.Contains(_store.TrashEntries(), e => e.Kind == TrashKind.Saves);
        Assert.Single(Directory.GetFiles(root, "*" + SaveStore.Ext));
    }

    [Fact]
    public void A_new_play_is_made_beside_the_others_where_asked_and_the_table_stays_as_it_is()
    {
        var server = HostRun("r1", turns: 2);
        server.Dirty = true;
        _saves.SaveNow(closing: true);
        var group = _store.MakeFolder("feud", _store.SavesRoot("feud"), "Second group");
        var start = new GameState { ScenarioId = "feud", Name = "Feud" };
        string? made = null, why = "never answered";
        bool answered = false;
        _saves.MakePlay(new LoadFrom("the scenario's start", Start: () => (start, new List<TurnRecord>()), PlayName: "Group B", PlaceIn: group), (f, w) =>
        {
            made = f;
            why = w;
            answered = true;
        });
        Assert.True(SpinWait.SpinUntil(() => { RunPosted(); return answered; }, 10000));
        Assert.Null(why);
        Assert.Equal(Path.Combine(group, "Group B"), made);
        var run = _store.Runs("feud").Single(r => r.Folder == made).Id;
        Assert.Equal(0, Json.Load<SaveFile>(_store.LivePath("feud", run)).Snapshot.Clock.Turn);
        Assert.Equal(2, server.State.Clock.Turn);
        Assert.Equal("r1", _store.RunOf(_session.SavePath!)!.Value.Run);
        Assert.Equal("r1", _store.CurrentRun("feud"));
    }

    [Fact]
    public void While_the_game_is_written_before_a_load_no_other_save_runs_beside_it()
    {
        var server = HostRun("r1", turns: 1);
        using var slow = new ManualResetEventSlim(false);
        int landings = 0;
        _saves.BeforeLanding = _ => { Interlocked.Increment(ref landings); slow.Wait(TimeSpan.FromSeconds(20)); };
        string? why = "never answered";
        bool answered = false;
        _saves.Load(new LoadFrom("Turn 0", TurnIndex: 0), w => { why = w; answered = true; });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref landings) == 1, 10000));
        server.Dirty = true;
        _saves.SaveNow();
        Thread.Sleep(100);
        Assert.Equal(1, Volatile.Read(ref landings));
        slow.Set();
        Assert.True(SpinWait.SpinUntil(() => { RunPosted(); return answered; }, 10000));
        Assert.Null(why);
    }

    [Fact]
    public void The_first_tick_opens_the_log_and_writes_the_whole_table()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = Host(path);
        server.Dirty = true;
        _saves.Tick(0.5);
        Assert.StartsWith("0 command(s) since the table was last written whole", _saves.SaveLogState);
        Assert.False(File.Exists(path));
        _saves.Tick(0.6);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(path), 10000));
        _saves.CloseLog();
        Assert.Equal("no log", _saves.SaveLogState);
        Assert.Null(server.Applied);
    }

    GameServer HostWithLog(string path)
    {
        var server = Host(path);
        server.State.ScenarioId = "local.feud";
        server.SaveTo(path);
        _saves.Tick(0.5);
        Assert.StartsWith("0 command(s)", _saves.SaveLogState);
        return server;
    }

    static void Roll(GameServer server) =>
        Assert.DoesNotContain(server.Handle(1, new RollDice { Sides = 6, Count = 1 }), o => o.Message is ErrorMessage);

    void WaitForSaveOutcome(Func<bool> done) => Assert.True(SpinWait.SpinUntil(() => { _saves.Harvest(); return done(); }, 10000));

    [Fact]
    public void A_failed_write_and_the_save_after_it_lose_no_command_between_them()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = HostWithLog(path);
        Roll(server);
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _saves.SaveNow();
        WaitForSaveOutcome(() => _errors.Count(e => e.StartsWith("save failed")) == 1);
        Roll(server);
        _saves.SaveNow();
        WaitForSaveOutcome(() => _errors.Count(e => e.StartsWith("save failed")) == 2);
        Roll(server);
        _saves.CloseLog();
        var back = SaveLoader.Load(path, _session.Store);
        Assert.Equal(3, back.Loaded.ReplayedFromLog);
        Assert.Equal(server.State.Dice.Count, back.State.Dice.Count);

        held.Dispose();
        _saves.SaveNow(closing: true);
        Assert.Null(_saves.LastSaveProblem);
        Assert.False(File.Exists(SaveLog.OldPathFor(path)));
        back = SaveLoader.Load(path, _session.Store);
        Assert.Equal(0, back.Loaded.ReplayedFromLog);
        Assert.Equal(server.State.Dice.Count, back.State.Dice.Count);
    }

    string StopTheMachine(string path, string name)
    {
        var to = Directory.CreateDirectory(Path.Combine(_dir, "stopped-" + name)).FullName;
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!))
            using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(Path.Combine(to, Path.GetFileName(f))))
                src.CopyTo(dst);
        return Path.Combine(to, Path.GetFileName(path));
    }

    static int LinesOf(string path)
    {
        using var r = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        int n = 0;
        while (r.ReadLine() != null) n++;
        return n;
    }

    [Fact]
    public void Play_reaches_the_log_on_the_disk_while_a_slow_whole_save_is_still_being_written()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = HostWithLog(path);
        using var slow = new ManualResetEventSlim();
        _saves.BeforeLanding = _ => slow.Wait(TimeSpan.FromSeconds(20));
        Roll(server);
        _saves.SaveNow();
        Roll(server);
        Roll(server);
        _saves.Tick(1.1);
        Assert.True(_saves.WaitForLog());
        Assert.Equal(3, LinesOf(SaveLog.PathFor(path)));
        Roll(server);
        _saves.CloseLog();
        Assert.Equal(4, LinesOf(SaveLog.PathFor(path)));
        slow.Set();
        Assert.True(_saves.WaitForDisk());
    }

    [Fact]
    public void The_machine_stopping_at_any_point_of_saving_loses_no_change_that_reached_the_disk()
    {
        var path = Path.Combine(_dir, "live", "game.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var server = HostWithLog(path);
        var stopped = new List<(string name, string copy, int dice)>();
        _saves.BeforeLanding = _ => stopped.Add(("mid-write", StopTheMachine(path, "mid-write"), server.State.Dice.Count));
        _saves.AfterLanding = _ => stopped.Add(("landed", StopTheMachine(path, "landed"), server.State.Dice.Count));
        Roll(server);
        Roll(server);
        _saves.Tick(0.6);
        WaitForSaveOutcome(() => stopped.Count == 2 && File.Exists(path) && _saves.SaveLogState.StartsWith("0 command(s)"));
        _saves.BeforeLanding = null;
        _saves.AfterLanding = null;
        stopped.Add(("written", StopTheMachine(path, "written"), server.State.Dice.Count));
        Roll(server);
        _saves.Tick(1.0);
        Assert.True(_saves.WaitForDisk());
        stopped.Add(("logged", StopTheMachine(path, "logged"), server.State.Dice.Count));
        foreach (var (name, copy, dice) in stopped)
        {
            var back = SaveLoader.Open(_store, copy, _session.Store, out _);
            Assert.True(dice == back.State.Dice.Count, $"{name}: {back.State.Dice.Count} of {dice} rolls came back");
        }
        Assert.Equal(new[] { 2, 2, 2, 3 }, stopped.Select(x => x.dice));
    }

    [Fact]
    public void A_write_that_lands_after_the_table_closed_does_not_take_the_closing_log_with_it()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = HostWithLog(path);
        Roll(server);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _saves.SaveNow();
            Roll(server);
            server.State.Layer(Layers.Umpire).Add(new PieceInstance { Id = "p-000099", Def = "x", Pos = new(double.NaN, 0) });
            _saves.SaveNow(closing: true);
            Assert.NotNull(_saves.LastSaveProblem);
        }
        WaitForSaveOutcome(() => _saves.LastSaveProblem == null);
        Assert.True(File.Exists(SaveLog.OldPathFor(path)));
        _saves.CloseLog();
        var back = SaveLoader.Load(path, _session.Store);
        Assert.Equal(server.State.Dice.Count, back.State.Dice.Count);
        Assert.Equal(server.State.LastSeq, back.State.LastSeq);
    }

    [Fact]
    public void Each_write_has_a_temp_file_of_its_own_so_one_left_running_by_an_earlier_table_is_never_written_into()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = Host(path);
        Directory.CreateDirectory(path + ".tmp");
        server.Dirty = true;
        _saves.SaveNow(closing: true);
        Assert.Null(_saves.LastSaveProblem);
        Assert.Equal("Feud", Json.Load<SaveFile>(path).Snapshot.Name);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            server.Dirty = true;
            _saves.SaveNow(closing: true);
            Assert.NotNull(_saves.LastSaveProblem);
        }
        Assert.Equal(new[] { path + ".tmp" }, Directory.GetFileSystemEntries(_dir, "game.json.*tmp"));
    }

    [Fact]
    public void A_closing_save_that_finishes_after_a_newer_save_landed_is_dropped_and_the_newer_table_stays()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = Host(path);
        using var slow = new ManualResetEventSlim(false);
        _saves.BeforeLanding = n => { if (n == 1) slow.Wait(TimeSpan.FromSeconds(20)); };
        _saves.CloseWait = TimeSpan.FromMilliseconds(200);
        Roll(server);
        _saves.SaveNow(closing: true);
        Assert.Equal("the game was still being written when the table closed", _saves.LastSaveProblem);
        Assert.Contains(_errors, e => e.StartsWith("the closing save of " + path));
        Roll(server);
        _saves.SaveNow(closing: true);
        Assert.Null(_saves.LastSaveProblem);
        Assert.Equal(2, Json.Load<SaveFile>(path).Snapshot.Dice.Count);
        slow.Set();
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(path + ".1.tmp"), 10000));
        Assert.Equal(2, Json.Load<SaveFile>(path).Snapshot.Dice.Count);
        Assert.Empty(Directory.GetFiles(_dir, "game.json.*tmp"));
    }

    [Fact]
    public void A_save_that_does_not_finish_is_shown_as_failing_and_cleared_once_it_ends()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = Host(path);
        using var slow = new ManualResetEventSlim(false);
        _saves.BeforeLanding = n => { if (n == 1) slow.Wait(TimeSpan.FromSeconds(20)); };
        server.Dirty = true;
        _saves.SaveNow();
        _now = 5;
        _saves.SaveNow();
        Assert.Null(_saves.LastSaveProblem);
        _now = 12;
        _saves.SaveNow();
        Assert.StartsWith($"the last save of {path} has not finished after 12 seconds", _saves.LastSaveProblem);
        Assert.Empty(_problems);
        _now = 23;
        _saves.SaveNow();
        Assert.Equal(_saves.LastSaveProblem, Assert.Single(_problems));
        slow.Set();
        WaitForSaveOutcome(() => _problems.Count == 2);
        Assert.Null(_problems[1]);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void A_closing_save_still_writing_after_the_table_is_left_keeps_the_folder_held_until_it_ends()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = Host(path);
        _saves.HeldFolder = "C:/scenarios/feud";
        using var slow = new ManualResetEventSlim(false);
        _saves.BeforeLanding = n => { if (n == 1) slow.Wait(TimeSpan.FromSeconds(20)); };
        _saves.CloseWait = TimeSpan.FromMilliseconds(200);
        server.Dirty = true;
        _saves.SaveNow(closing: true);
        Assert.Equal("the game was still being written when the table closed", _saves.LastSaveProblem);
        _saves.Stop();
        Assert.Null(_saves.HeldFolder);
        Assert.DoesNotContain("release C:/scenarios/feud", _folders.Calls);
        slow.Set();
        Assert.True(SpinWait.SpinUntil(() => _folders.Calls.Contains("release C:/scenarios/feud"), 10000));
    }

    [Fact]
    public void A_log_that_stops_is_said_once_and_the_table_is_then_written_whole()
    {
        var path = Path.Combine(_dir, "game.json");
        var server = Host(path);
        Directory.CreateDirectory(SaveLog.PathFor(path));
        server.Dirty = true;
        _saves.Tick(0.5);
        Assert.True(_saves.WaitForDisk());
        _saves.Tick(0.6);
        Assert.Contains("(NOT WORKING)", _saves.SaveLogState);
        Assert.StartsWith("The log of each change between whole saves stopped: it could not be opened (", Assert.Single(_notes));
        Assert.Contains(_errors, e => e.StartsWith("save log stopped: "));
        Assert.True(SpinWait.SpinUntil(() => File.Exists(path), 10000));
        server.Dirty = true;
        _saves.Tick(1.1);
        Assert.Single(_notes, n => n.StartsWith("The log of each change"));
    }

    [Fact]
    public void The_log_is_not_opened_again_for_the_same_save_named_in_another_case()
    {
        var path = Path.Combine(_dir, "Game.json");
        var server = Host(path);
        var keeper = new SaveLogKeeper(new SaveLogGenerations(), () => new ClientLog(), _ => { });
        Assert.True(keeper.Ensure(server, path));
        Assert.False(keeper.Ensure(server, path.ToUpperInvariant()));
        Assert.False(keeper.SetAside(path.ToLowerInvariant(), 0).LiveLogIdle);
        keeper.Close(server);
    }

    [Fact]
    public void The_scenario_folder_is_prepared_with_the_first_save_and_written_with_it()
    {
        var path = Path.Combine(_dir, "game.json");
        Host(path);
        _session.ScenarioDir = Path.Combine(_dir, "scenario");
        Directory.CreateDirectory(_session.ScenarioDir);
        _saves.SaveNow(closing: true);
        Assert.Equal(new[] { "pending", "prepare Feud" }, _folders.Calls);
        Assert.True(File.Exists(Path.Combine(_session.ScenarioDir, "scenario.json")));
    }

    [Fact]
    public void A_map_s_tiles_are_copied_in_the_background_and_said_so()
    {
        var path = Path.Combine(_dir, "game.json");
        Host(path);
        _session.ScenarioDir = Path.Combine(_dir, "scenario");
        _folders.Pending = true;
        var done = _posted;
        _saves.SaveNow(closing: true);
        Assert.True(SpinWait.SpinUntil(() => { lock (done) return done.Count > 0; }, 10000));
        Assert.Equal("", done[0]);
        Assert.StartsWith("Copying the map's tiles", _notes[0]);
        _folders.Pending = false;
        _saves.WriteBackDone("");
        Assert.Equal("The map's tiles are in the scenario folder now.", _notes[^1]);
        WaitForSaveOutcome(() => File.Exists(Path.Combine(_session.ScenarioDir, "scenario.json")));
        Assert.Equal(new[] { "pending", "save Feud", "pending", "prepare Feud" }, _folders.Calls);
    }

    [Fact]
    public void Leaving_the_table_lets_the_held_folder_go()
    {
        _saves.HeldFolder = "C:/scenarios/feud";
        _saves.Stop();
        Assert.Null(_saves.HeldFolder);
        Assert.Equal(new[] { "release C:/scenarios/feud" }, _folders.Calls);
    }

    [Fact]
    public void A_co_umpire_keeps_a_mirror_until_it_is_no_longer_an_umpire()
    {
        _session.OnJoined(new JoinResult { Ok = true, PeerId = 2, Role = Role.Umpire, View = new GameState { Name = "Mirror test " + Guid.NewGuid().ToString("N") }, Created = "c1" });
        _saves.StartMirror(new JoinResult { Created = "c1" });
        var path = _saves.MirrorPath;
        Assert.NotNull(path);
        Assert.Equal(_store.MirrorPath(_session.Live.Name), path);
        _saves.StopMirror();
        Assert.Null(_saves.MirrorPath);
        var mirror = Json.Load<SaveFile>(path!);
        Assert.Equal("c1", mirror.Created);
        Assert.False(_session.Dirty);
    }
}
