using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public class ClientSession
{
    public GameState Live { get; private set; } = new();
    GameState? _review;
    public GameState View => _review ?? Live;
    public bool Reviewing => _review != null;
    public TurnInfo? ReviewInfo { get; private set; }
    public List<TurnInfo> Turns { get; private set; } = new();
    public event Action? TurnsChanged;
    public PeerInfo Me { get; private set; } = new();
    public GameAccess? Access { get; private set; }
    public string? Token { get; private set; }
    public List<PeerInfo> Peers { get; private set; } = new();
    public List<PeerInfo> Waiting { get; private set; } = new();
    public PackageStore Store { get; }
    public string? SavePath { get; set; }
    public string? ScenarioDir { get; set; }
    public int Unanswered { get; private set; }
    public bool CaughtUp => Unanswered == 0;

    public event Action? Reset;
    public event Action<Command>? Applied;
    public event Action<string>? Error;
    public event Action? PeersChanged;
    public event Action<Knock>? Knocked;
    public event Action? Desynced;
    public event Action? RoleChanged;
    public event Action? AccessChanged;
    public event Action<string, string>? PackAdded;

    public Action<Command> Send = _ => { };
    public Action<Message> Request = _ => { };

    public readonly List<ConsoleLine> ConsoleLines = new();
    public const int ConsoleKeep = 1000;
    long _consoleSeq;
    public event Action<List<ConsoleLine>?>? ConsoleChanged;
    public List<ScriptInfo> Scripts { get; private set; } = new();
    public event Action? ScriptsChanged;

    public void OnScriptMessage(Message m)
    {
        switch (m)
        {
            case ScriptConsole sc:
                if (sc.Backlog)
                {
                    ConsoleLines.Clear();
                    ConsoleLines.AddRange(sc.Lines);
                    _consoleSeq = sc.Lines.Count > 0 ? sc.Lines[^1].Seq : _consoleSeq;
                    ConsoleChanged?.Invoke(null);
                    break;
                }
                var fresh = sc.Lines.Where(l => l.Seq > _consoleSeq).ToList();
                if (fresh.Count == 0) break;
                _consoleSeq = fresh[^1].Seq;
                ConsoleLines.AddRange(fresh);
                if (ConsoleLines.Count > ConsoleKeep) ConsoleLines.RemoveRange(0, ConsoleLines.Count - ConsoleKeep);
                ConsoleChanged?.Invoke(fresh);
                break;
            case ScriptList sl:
                Scripts = sl.Scripts;
                ScriptsChanged?.Invoke();
                break;
        }
    }

    public readonly Stack<long> MyUndoable = new();
    public readonly Stack<long> MyRedoable = new();
    readonly HashSet<long> _redoing = new();
    readonly Dictionary<long, long> _scriptFrom = new();
    long _openScript;

    public readonly List<TurnRecord> MirrorTurns = new();
    public string Created { get; private set; } = "";
    public bool Dirty { get; set; }
    bool Mirroring => IsUmpire && !IsHost;

    public SaveFile ToMirrorSave(string mirrorOf) => SaveFile.Mirror(Live, Access, Created, MirrorTurns, mirrorOf);

    public ClientSession(PackageStore store) { Store = store; }

    public bool IsHost => Me.Role == Role.HostUmpire;
    public bool IsLead => Visibility.IsLead(Me.Role);
    public bool IsUmpire => Visibility.IsUmpire(Me.Role);
    public bool SeesBoard => IsUmpire || Me.Role == Role.Observer;

    public void OnJoined(JoinResult r)
    {
        Me = new PeerInfo { PeerId = r.PeerId, Role = r.Role, Name = Me.Name };
        Access = r.Access;
        if (r.Token != null) Token = r.Token;
        Created = r.Created ?? Repeatable.UtcNow.ToString("o");
        MirrorTurns.Clear();
        SetView(r.View ?? new GameState());
    }

    public void OnRoleChanged(RoleChanged rc)
    {
        Me.Role = rc.Role;
        if (!IsUmpire) { Access = null; MirrorTurns.Clear(); }
        ForgetUndo();
        _review = null;
        ReviewInfo = null;
        SetView(rc.View);
        RoleChanged?.Invoke();
    }

    public void OnAccess(GameAccess a) { Access = a; AccessChanged?.Invoke(); }
    public void OnKnock(Knock k) => Knocked?.Invoke(k);

    public void SetName(string name) => Me.Name = name;

    public void SetView(GameState v)
    {
        Live = v;
        Unanswered = 0;
        Dirty = true;
        Store.LoadPacks(v.Packs);
        if (_review == null) Reset?.Invoke();
    }

    public void Review(GameState? past, TurnInfo? info)
    {
        _review = past;
        ReviewInfo = info;
        Reset?.Invoke();
    }

    public void OnTurns(TurnList list) { Turns = list.Turns; TurnsChanged?.Invoke(); }

    public void OnCommand(Command c)
    {
        if (c.Actor == Me.PeerId) Answered();
        if (Mirroring && SaveFile.EndsTurn(Live, c)) MirrorTurns.Add(SaveFile.RecordOf(Live, Repeatable.UtcNow));
        var err = StateMachine.Apply(Live, c, out _);
        if (c.Seq > Live.LastSeq) Live.LastSeq = c.Seq;
        if (err != null)
        {
            Error?.Invoke($"This table no longer agrees with the host's ({c.GetType().Name}: {err}); fetching the host's table again.");
            Desynced?.Invoke();
            return;
        }
        if (c is RestoreTurn rt)
        {
            if (rt.State != null) MirrorTurns.RemoveAll(r => r.Turn >= rt.State.Clock.Turn);
            ForgetUndo();
        }
        if (c is LoadSave)
        {
            MirrorTurns.Clear();
            ForgetUndo();
        }
        Dirty = true;
        if (c.Actor == Me.PeerId && c.OnUndoStack()) Track(c);
        else if (c.Actor == 0 && IsLead && c.MakesUndoStep()) TrackScript(c);
        if (c.UndoFrom > 0 && c.Actor != Me.PeerId) DropScriptSteps(c.UndoFrom, c.UndoOf);
        if (c is AddPack ap) PackAdded?.Invoke(ap.Id, ap.Hash);
        Applied?.Invoke(c);
        if (c is RestoreTurn or LoadSave)
        {
            _review = null;
            ReviewInfo = null;
            Reset?.Invoke();
        }
    }

    public void OnPeers(PeerList p)
    {
        Peers = p.Peers;
        Waiting = p.Waiting;
        PeersChanged?.Invoke();
    }

    public void OnError(string e)
    {
        Answered();
        Error?.Invoke(e);
    }

    public void Answered()
    {
        if (Unanswered > 0) Unanswered--;
    }

    void Sent(Command c)
    {
        Unanswered++;
        Send(c);
    }

    public Func<Command, Command>? Outgoing;

    public void Do(Command c)
    {
        if (Reviewing) { Error?.Invoke("You are looking at a past turn; press Back to now to change the table."); return; }
        Sent(Outgoing?.Invoke(c) ?? c);
    }

    public void UndoLast()
    {
        if (!IsUmpire || MyUndoable.Count == 0) return;
        var seq = MyUndoable.Pop();
        _openScript = 0;
        Sent(_scriptFrom.Remove(seq, out var from) ? new Undo { TargetSeq = seq, FromSeq = from } : new Undo { TargetSeq = seq });
    }

    public void RedoLast()
    {
        if (!IsUmpire || MyRedoable.Count == 0) return;
        var seq = MyRedoable.Pop();
        _redoing.Add(seq);
        Sent(new Undo { TargetSeq = seq });
    }

    void Track(Command c)
    {
        _openScript = 0;
        if (c.UndoOf == 0)
        {
            MyUndoable.Push(c.Seq);
            MyRedoable.Clear();
            _redoing.Clear();
        }
        else if (_redoing.Remove(c.UndoOf)) MyUndoable.Push(c.Seq);
        else MyRedoable.Push(c.Seq);
    }

    void TrackScript(Command c)
    {
        var from = c.Seq;
        if (_openScript != 0 && MyUndoable.TryPeek(out var top) && top == _openScript)
        {
            MyUndoable.Pop();
            _scriptFrom.Remove(top, out from);
        }
        _scriptFrom[c.Seq] = from;
        MyUndoable.Push(c.Seq);
        _openScript = c.Seq;
    }

    void DropScriptSteps(long from, long to)
    {
        var kept = MyUndoable.Reverse().Where(seq => !(_scriptFrom.TryGetValue(seq, out var start) && start >= from && seq <= to)).ToList();
        if (kept.Count == MyUndoable.Count) return;
        foreach (var seq in MyUndoable.Except(kept)) _scriptFrom.Remove(seq);
        MyUndoable.Clear();
        foreach (var seq in kept) MyUndoable.Push(seq);
        if (!MyUndoable.TryPeek(out var top) || top != _openScript) _openScript = 0;
    }

    void ForgetUndo()
    {
        MyUndoable.Clear();
        MyRedoable.Clear();
        _redoing.Clear();
        _scriptFrom.Clear();
        _openScript = 0;
    }

    public PieceDef? Def(string id) => Store.Def(id);

    public IEnumerable<string> VisibleLayers => View.Layers.Keys;

    public bool CanEdit(string layer) => !Reviewing && IsUmpire && layer == Layers.Umpire;

    public bool CanTouch(string layer, Vec2 at) => CanEdit(layer) && ZoneRule.Allows(View, Me.Role, Me.Name, layer, at);

    public bool CanTouch(string layer, PieceInstance piece) => CanTouch(layer, piece.Pos);
}
