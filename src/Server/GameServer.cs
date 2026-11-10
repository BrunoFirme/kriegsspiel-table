using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server;

public readonly record struct Outgoing(long PeerId, Message Message);

public sealed partial class GameServer
{
    public GameState State { get; private set; }
    public GameAccess Access { get; }
    public PackageStore? Store { get; }
    public string Created { get; }
    public bool Dirty { get; set; }
    public byte[]? Picture { get; set; }

    public const int MaxSeats = 40, MaxUmpires = 32;
    public const int MaxWaiting = 32;
    public string AppVersion { get; set; } = "";

    readonly Dictionary<long, PeerInfo> _peers = new();
    readonly Dictionary<long, string> _tokens = new();
    readonly Dictionary<long, PeerInfo> _waiting = new();
    readonly Dictionary<string, (int fails, long until)> _guesses = new();
    readonly List<Command> _log = new();
    readonly Dictionary<long, Undoable> _inverses = new();
    readonly HashSet<long> _undone = new();
    readonly Dictionary<long, Queue<long>> _undoableBy = new();
    long _scriptsForgottenTo;
    readonly Random _rng = Repeatable.NewRandom();
    readonly Func<long> _clock;
    public Scripting.ScriptHost Scripts { get; }
    internal Command? LastInverse { get; private set; }
    static readonly PeerInfo ScriptPeer = new() { PeerId = 0, Name = "script", Role = Role.HostUmpire };

    public IReadOnlyCollection<PeerInfo> Peers => _peers.Values;
    public IReadOnlyCollection<PeerInfo> Waiting => _waiting.Values;
    public IReadOnlyList<Command> Log => _log;
    public List<TurnRecord> Turns { get; } = new();

    public GameServer(GameState state, PackageStore? store = null, GameAccess? access = null, Func<long>? clock = null, string? created = null)
    {
        State = state;
        State.EnsureBoard();
        Store = store;
        Access = access ?? new GameAccess();
        _clock = clock ?? (() => new DateTimeOffset(Repeatable.UtcNow).ToUnixTimeMilliseconds());
        Scripts = new Scripting.ScriptHost(this);
        Created = created ?? Repeatable.UtcNow.ToString("o");
        OpenPacks(state);
    }

    void OpenPacks(GameState state)
    {
        DieColors.Recolor(state);
        if (Store == null) return;
        Store.LoadPacks(state.Packs);
        BringPacksOfUnknownPieces(state);
        var unknownFrom = state.Layers.Values.SelectMany(l => l).Where(p => Store.Def(p.Def) == null).Select(p => state.PackNamedBy(p.Def)).ToHashSet();
        if (!unknownFrom.Contains(null)) state.Packs.RemoveAll(p => Store.Pack(p) == null && !unknownFrom.Contains(p));
        MissingPacks = state.Packs.Where(p => Store.Pack(p) == null).ToList();
    }

    void BringPacksOfUnknownPieces(GameState state)
    {
        var unknown = state.Layers.Values.SelectMany(l => l).Select(p => p.Def).Where(d => Store!.Def(d) == null).ToHashSet();
        if (unknown.Count == 0) return;
        foreach (var id in Store!.ListPackIds().Where(id => !state.Packs.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList())
        {
            var manifest = BestEffort.Read(() => Json.Load<PackManifest>(Path.Combine(Store.PackageDir(id), "pack.json")), (PackManifest?)null);
            if (manifest == null || !manifest.Pieces.Any(d => unknown.Contains(d.Id)) || Store.Pack(id) == null) continue;
            state.Packs.Add(id);
            unknown.RemoveWhere(d => Store.Def(d) != null);
            if (unknown.Count == 0) return;
        }
    }

    public IReadOnlyList<string> MissingPacks { get; private set; } = Array.Empty<string>();

    public static GameServer FromScenario(Scenario s, PackageStore? store = null) => new(GameState.FromScenario(s), store);

    public List<Outgoing> Join(long peerId, JoinRequest req, string? from = null)
    {
        var out_ = new List<Outgoing>();
        if (req == null) { out_.Add(new(peerId, new JoinResult { Ok = false, Error = "the knock could not be read" })); return out_; }
        var name = string.IsNullOrWhiteSpace(req.Name) ? $"peer{peerId}" : req.Name.Trim();
        if (peerId == 1)
        {
            Seat(peerId, new PeerInfo { PeerId = 1, Name = UniqueName(name, null), Role = Role.HostUmpire }, null, out_, token: null);
            return out_;
        }
        if (OtherApp(req) is { } other) { out_.Add(new(peerId, new JoinResult { Ok = false, Error = other })); return out_; }
        if (Access.Find(req.Token) is { } known)
        {
            foreach (var (oldId, hash) in _tokens.Where(t => t.Value == known.TokenHash).ToList())
            {
                _peers.Remove(oldId);
                _tokens.Remove(oldId);
                out_.Add(new(oldId, new JoinResult { Ok = false, Error = "you connected from elsewhere" }));
            }
            if (Full(known.Role) is { } full) { out_.Add(new(peerId, new JoinResult { Ok = false, Error = full })); return out_; }
            Seat(peerId, new PeerInfo { PeerId = peerId, Name = known.Name, Role = known.Role }, known.TokenHash, out_, token: null);
            return out_;
        }
        if (Access.Password.Length > 0)
        {
            var place = from ?? $"peer {peerId}";
            if (Guessing(place) is { } wait) { out_.Add(new(peerId, new JoinResult { Ok = false, Error = wait })); return out_; }
            if (!string.Equals(Access.Password, (req.Password ?? "").Trim(), StringComparison.Ordinal))
            {
                WrongGuess(place);
                out_.Add(new(peerId, new JoinResult { Ok = false, Error = "wrong password" }));
                return out_;
            }
            _guesses.Remove(place);
        }
        if (!_waiting.ContainsKey(peerId) && _waiting.Count >= MaxWaiting)
        {
            out_.Add(new(peerId, new JoinResult { Ok = false, Error = $"{MaxWaiting} people are already waiting to be let in: try again once the umpires have seen to them" }));
            return out_;
        }
        _waiting[peerId] = new PeerInfo { PeerId = peerId, Name = name, Role = Role.Observer };
        out_.Add(new(peerId, new JoinResult { Ok = false, Waiting = true, PeerId = peerId }));
        foreach (var lead in Leads()) out_.Add(new(lead.PeerId, new Knock { PeerId = peerId, Name = name }));
        out_.AddRange(BroadcastPeers());
        return out_;
    }

    string? OtherApp(JoinRequest req)
    {
        if (req.Protocol == ProtocolVersion.Current) return null;
        var here = $"version {(AppVersion.Length > 0 ? AppVersion : "unknown")}, protocol {ProtocolVersion.Current}";
        var there = req.Protocol <= 0 && string.IsNullOrWhiteSpace(req.AppVersion)
            ? "an older version that does not say which"
            : $"version {(string.IsNullOrWhiteSpace(req.AppVersion) ? "unknown" : req.AppVersion.Trim())}, protocol {req.Protocol}";
        return $"this table needs the same version of the app as the host: the host has {here}, and you have {there}. Install the same version as the host and knock again.";
    }

    string? Guessing(string place)
    {
        if (!_guesses.TryGetValue(place, out var g) || g.until == 0) return null;
        var left = g.until - _clock();
        if (left <= 0) return null;
        return $"too many wrong passwords: try again in {Math.Max(1, (left + 999) / 1000)} s";
    }

    void WrongGuess(string place)
    {
        var now = _clock();
        if (_guesses.Count >= 1024)
            foreach (var k in _guesses.Where(e => e.Value.until <= now).Select(e => e.Key).ToList()) _guesses.Remove(k);
        int fails = _guesses.GetValueOrDefault(place).fails + 1;
        _guesses[place] = (fails, fails >= 3 ? now + (1000L << Math.Min(fails - 3, 8)) : 0);
    }

    public List<Outgoing> Control(long byId, Message m)
    {
        var res = new List<Outgoing>();
        if (m == null) { res.Add(new(byId, new ErrorMessage { Text = "the door message could not be read" })); return res; }
        if (!_peers.TryGetValue(byId, out var by) || !Visibility.IsLead(by.Role))
        {
            res.Add(new(byId, new ErrorMessage { Text = "only the host or a lead umpire decides who is at the table" }));
            return res;
        }
        bool host = by.Role == Role.HostUmpire;
        switch (m)
        {
            case Admit a:
            {
                if (!_waiting.Remove(a.PeerId, out var w)) { res.Add(new(byId, new ErrorMessage { Text = "nobody is waiting under that id" })); return res; }
                if (!Enum.IsDefined(a.Role))
                {
                    _waiting[a.PeerId] = w;
                    res.Add(new(byId, new ErrorMessage { Text = "no such role" }));
                    return res;
                }
                var role = a.Role;
                if (role == Role.HostUmpire) role = Role.LeadUmpire;
                if (role == Role.LeadUmpire && !host)
                {
                    _waiting[a.PeerId] = w;
                    res.Add(new(byId, new ErrorMessage { Text = "only the host makes lead umpires" }));
                    return res;
                }
                if (Full(role) is { } full)
                {
                    _waiting[a.PeerId] = w;
                    res.Add(new(byId, new ErrorMessage { Text = full }));
                    return res;
                }
                var token = GameAccess.NewToken();
                var entry = new Approved { TokenHash = GameAccess.Hash(token), Name = UniqueName(w.Name, null), Role = role };
                Access.Approved.Add(entry);
                Dirty = true;
                Seat(a.PeerId, new PeerInfo { PeerId = a.PeerId, Name = entry.Name, Role = role }, entry.TokenHash, res, token);
                res.AddRange(AccessToUmpires());
                return res;
            }
            case Refuse r:
            {
                if (!_waiting.Remove(r.PeerId)) { res.Add(new(byId, new ErrorMessage { Text = "nobody is waiting under that id" })); return res; }
                res.Add(new(r.PeerId, new JoinResult { Ok = false, Error = "not let in" }));
                res.AddRange(BroadcastPeers());
                return res;
            }
            case SetRole sr:
            {
                if (!_peers.TryGetValue(sr.PeerId, out var target)) { res.Add(new(byId, new ErrorMessage { Text = "nobody is connected under that id" })); return res; }
                if (May(by, target, sr.Role) is { } why) { res.Add(new(byId, new ErrorMessage { Text = why })); return res; }
                if (target.Role == sr.Role) return res;
                if (Visibility.IsUmpire(sr.Role) != Visibility.IsUmpire(target.Role) && Full(sr.Role, seated: true) is { } full) { res.Add(new(byId, new ErrorMessage { Text = full })); return res; }
                target.Role = sr.Role;
                if (_tokens.TryGetValue(target.PeerId, out var hash) && Access.FindHash(hash) is { } e) { e.Role = sr.Role; Dirty = true; }
                res.Add(new(target.PeerId, new RoleChanged { Role = target.Role, View = Visibility.Filter(State, target) }));
                if (Visibility.IsUmpire(target.Role)) res.Add(new(target.PeerId, ConsoleBacklog()));
                res.AddRange(BroadcastPeers());
                res.AddRange(AccessToUmpires());
                return res;
            }
            case Revoke rv:
            {
                if (!_peers.TryGetValue(rv.PeerId, out var target)) { res.Add(new(byId, new ErrorMessage { Text = "nobody is connected under that id" })); return res; }
                if (May(by, target, Role.Observer) is { } why) { res.Add(new(byId, new ErrorMessage { Text = why })); return res; }
                _peers.Remove(target.PeerId);
                if (_tokens.Remove(target.PeerId, out var hash)) { Access.Approved.RemoveAll(x => x.TokenHash == hash); Dirty = true; }
                res.Add(new(target.PeerId, new JoinResult { Ok = false, Error = "put out by the umpire" }));
                res.AddRange(BroadcastPeers());
                res.AddRange(AccessToUmpires());
                return res;
            }
            case SetPassword { Password: null }:
                res.Add(new(byId, new ErrorMessage { Text = "the new password is missing (an empty one opens the door to anyone)" }));
                return res;
            case SetPassword sp:
                Access.Password = sp.Password.Trim();
                Dirty = true;
                res.AddRange(AccessToUmpires());
                return res;
            default:
                res.Add(new(byId, new ErrorMessage { Text = $"{m.GetType().Name} is not a door message" }));
                return res;
        }
    }

    static string? May(PeerInfo by, PeerInfo target, Role to)
    {
        if (!Enum.IsDefined(to)) return "no such role";
        if (target.PeerId == by.PeerId) return "not your own role";
        if (target.Role == Role.HostUmpire) return "the host's role is the host's";
        if (by.Role != Role.HostUmpire)
        {
            if (target.Role == Role.LeadUmpire) return "only the host changes a lead umpire";
            if (Visibility.IsLead(to)) return "only the host makes lead umpires";
        }
        if (to == Role.HostUmpire) return "there is one host: this PC";
        return null;
    }

    string? Full(Role role, bool seated = false) =>
        Visibility.IsUmpire(role) && _peers.Values.Count(p => Visibility.IsUmpire(p.Role)) >= MaxUmpires ? $"umpire limit ({MaxUmpires}) reached"
        : !seated && _peers.Count >= MaxSeats ? $"the table is full ({MaxSeats} seats)"
        : null;

    string UniqueName(string name, string? ownHash)
    {
        bool Taken(string n) =>
            _peers.Values.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase) && (!_tokens.TryGetValue(p.PeerId, out var h) || h != ownHash)) ||
            Access.Approved.Any(a => string.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase) && a.TokenHash != ownHash);
        var candidate = name;
        for (int n = 2; Taken(candidate); n++) candidate = $"{name} ({n})";
        return candidate;
    }

    void Seat(long peerId, PeerInfo peer, string? tokenHash, List<Outgoing> out_, string? token)
    {
        _peers[peerId] = peer;
        if (tokenHash != null) _tokens[peerId] = tokenHash;
        bool ump = Visibility.IsUmpire(peer.Role);
        out_.Add(new(peerId, new JoinResult
        {
            Ok = true, PeerId = peerId, Role = peer.Role, Token = token,
            View = Visibility.Filter(State, peer),
            Access = ump ? Access.Clone() : null,
            Created = ump ? Created : null,
        }));
        if (ump) out_.Add(new(peerId, ConsoleBacklog()));
        out_.AddRange(BroadcastPeers());
    }

    ScriptConsole ConsoleBacklog() => new() { Backlog = true, Lines = Scripts.Feed.ToList() };

    public List<Outgoing> ConsoleOut()
    {
        var lines = Scripts.TakeUnsent();
        if (lines.Count == 0) return new();
        return _peers.Values.Where(p => Visibility.IsUmpire(p.Role)).Select(p => new Outgoing(p.PeerId, new ScriptConsole { Lines = lines })).ToList();
    }

    public List<Outgoing> Request(long peerId, Message m)
    {
        var res = new List<Outgoing>();
        if (m == null) { res.Add(new(peerId, new ErrorMessage { Text = "the request could not be read" })); return res; }
        if (!_peers.TryGetValue(peerId, out var peer) || !Visibility.IsUmpire(peer.Role))
        {
            res.Add(new(peerId, new ErrorMessage { Text = "only umpires use the script tools" }));
            return res;
        }
        switch (m)
        {
            case RunLua rl:
                if (!Visibility.IsLead(peer.Role)) { res.Add(new(peerId, new ErrorMessage { Text = "only the host or a lead umpire runs code in the scripts" })); break; }
                try { res.AddRange(Scripts.RunSnippet(peerId, rl.Target ?? "", rl.Code ?? "")); }
                catch (Exception e) { res.Add(new(peerId, new ErrorMessage { Text = "the code could not be run: " + e.Message })); }
                res.AddRange(ConsoleOut());
                res.Add(new(peerId, new ScriptList { Scripts = Scripts.Describe() }));
                break;
            case GetScripts:
                res.Add(new(peerId, new ScriptList { Scripts = Scripts.Describe() }));
                break;
            default:
                res.Add(new(peerId, new ErrorMessage { Text = $"{m.GetType().Name} is not a script request" }));
                break;
        }
        return res;
    }

    IEnumerable<PeerInfo> Leads() => _peers.Values.Where(p => Visibility.IsLead(p.Role));

    public List<Outgoing> Leave(long peerId)
    {
        _peers.Remove(peerId);
        _tokens.Remove(peerId);
        _waiting.Remove(peerId);
        ForgetCarrier(peerId);
        return BroadcastPeers();
    }

    List<Outgoing> BroadcastPeers()
    {
        var peers = _peers.Values.Select(p => new PeerInfo { PeerId = p.PeerId, Name = p.Name, Role = p.Role }).ToList();
        var waiting = _waiting.Values.Select(p => new PeerInfo { PeerId = p.PeerId, Name = p.Name, Role = p.Role }).ToList();
        return _peers.Values.Where(p => Visibility.IsUmpire(p.Role))
            .Select(p => new Outgoing(p.PeerId, new PeerList { Peers = peers, Waiting = Visibility.IsLead(p.Role) ? waiting : new() })).ToList();
    }

    List<Outgoing> AccessToUmpires() =>
        _peers.Values.Where(p => Visibility.IsUmpire(p.Role)).Select(p => new Outgoing(p.PeerId, new AccessChanged { Access = Access.Clone() })).ToList();

    public PeerInfo? Peer(long id) => _peers.GetValueOrDefault(id);

    public static bool Allowed(PeerInfo peer, Command c) => c switch
    {
        Batch b => Visibility.IsUmpire(peer.Role) && b.Items.All(i => i is null || (i.SentBy() is not (Sender.UmpireAlone or Sender.LeadAlone) && Allowed(peer, i))),
        _ => (c?.SentBy() ?? Sender.Umpire) switch
        {
            Sender.Lead or Sender.LeadAlone => Visibility.IsLead(peer.Role),
            Sender.Runtime => false,
            _ => Visibility.IsUmpire(peer.Role),
        },
    };

    public string? ZoneDenies(PeerInfo peer, Command c) => ZoneDenies(State, peer, c);

    public static string? ZoneDenies(GameState state, PeerInfo peer, Command c)
    {
        if (c == null || !ZoneRule.Binds(state, peer.Role)) return null;
        bool In(string layer, Vec2 p) => ZoneRule.Allows(state, peer.Role, peer.Name, layer, p);
        bool ok = c.ZonePieces().All(x => state.Find(x.Layer, x.Id) is not { } p || In(x.Layer, p.Pos))
            && c.ZonePoints().All(x => In(x.Layer, x.Pos));
        return ok ? null : ZoneRule.HasZone(state, peer.Name) ? "outside your umpire zone" : "no zone on the umpire board has been assigned to you";
    }

    public List<Outgoing> Handle(long peerId, Command c)
    {
        var res = new List<Outgoing>();
        if (!_peers.TryGetValue(peerId, out var peer))
        {
            res.Add(new(peerId, new ErrorMessage { Text = "not joined" }));
            return res;
        }
        var nextId = State.NextId;
        var seqBefore = State.LastSeq;
        List<long>? undoTargets;
        Command? inverse;
        TurnRecord? ending;
        try
        {
            if (MissingFields.Of(c) is { } missing) return Refuse(res, peerId, missing);
            if (Admission(peer, c) is { } refused) return Refuse(res, peerId, refused);
            if (TakeBack(peer, ref c, out undoTargets) is { } noUndo) return Refuse(res, peerId, noUndo);
            if (Readiness(c) is { } notReady) return Refuse(res, peerId, notReady);
            if (Scripts.ScriptVeto(c, peerId, res) is { } vetoed) return Refuse(res, peerId, vetoed);

            c.Actor = peerId;
            c.At = _clock();
            Prepare(c, peer, undoTargets != null);

            if (c is Batch tb && Deletes(tb) && StateMachine.Apply(State.DeepClone(), Json.Clone(c), out _) is { } terr)
            {
                GiveBackIds(nextId, seqBefore);
                return Refuse(res, peerId, terr);
            }

            BeforeApplying(res, peerId, c);
            c.Seq = State.LastSeq + 1;
            ending = SaveFile.EndsTurn(State, c) ? SaveFile.RecordOf(State, Repeatable.UtcNow) : null;
            var err = StateMachine.Apply(State, c, out inverse);
            if (err != null)
            {
                GiveBackIds(nextId, seqBefore);
                res.Add(new(peerId, new ErrorMessage { Text = err }));
                return res;
            }
        }
        catch (Exception e)
        {
            GiveBackIds(nextId, seqBefore);
            return Refuse(res, peerId, $"{c?.GetType().Name ?? "a command"} could not be applied: {e.Message}");
        }
        if (ending != null) Turns.Add(ending);
        if (c is RestoreTurn restored)
        {
            Turns.RemoveRange(restored.Index, Turns.Count - restored.Index);
            ForgetUndo();
        }
        foreach (var ut in undoTargets ?? [])
        {
            _undone.Add(ut);
            if (Turns.Count > 0 && Turns[^1].LastSeq == ut - 1) Turns.RemoveAt(Turns.Count - 1);
        }
        State.LastSeq = c.Seq;
        Remember(c, inverse);
        LastInverse = inverse;
        Dirty = true;

        res.AddRange(ToPeers(c));
        try { res.AddRange(Scripts.AfterApplied(c, peerId, undoTargets != null)); }
        catch (Exception e) { res.Add(new(peerId, new ErrorMessage { Text = "a script failed after the change: " + e.Message })); }
        return res;
    }

    void GiveBackIds(long nextId, long seqBefore)
    {
        State.NextId = nextId;
        if (State.LastSeq != seqBefore) State.EnsureNextId();
    }

    static List<Outgoing> Refuse(List<Outgoing> res, long peerId, string text)
    {
        res.Add(new(peerId, new ErrorMessage { Text = text }));
        return res;
    }

    string? Admission(PeerInfo peer, Command c)
    {
        if (!Allowed(peer, c)) return $"{c.GetType().Name} not allowed for {peer.Role}";
        if (c is AddPack apk && Store != null)
        {
            Store.Forget(apk.Id);
            if (Store.Pack(apk.Id) == null) return $"pack {apk.Id} is not on the host";
        }
        if (ZoneDenies(peer, c) is { } zoneErr) return zoneErr;
        var scriptText = c switch { SetScript ss => ss.Text, SetGlobalScript gs => gs.Text, _ => null };
        if (scriptText != null && ScriptSafety.Inspect(scriptText) is { Hostile: true } verdict)
            return "script refused: " + string.Join("; ", verdict.Reasons);
        return null;
    }

    string? TakeBack(PeerInfo peer, ref Command c, out List<long>? undoTargets)
    {
        undoTargets = null;
        if (c is not Undo u) return null;
        if (u.FromSeq > 0 && u.FromSeq <= _scriptsForgottenTo)
            return $"the scripts made more than {RememberCommands:N0} changes since then, too many to take back";
        var targets = u.FromSeq > 0 ? ScriptStepsBetween(u.FromSeq, u.TargetSeq) : _inverses.ContainsKey(u.TargetSeq) && !_undone.Contains(u.TargetSeq) ? [u.TargetSeq] : [];
        if (targets.Count == 0) return "nothing to undo";
        foreach (var seq in targets)
            if (_inverses[seq].By != peer.Name && !(_inverses[seq].By.Length == 0 && Visibility.IsLead(peer.Role)))
                return "that change is not yours to take back";
        var inverses = targets.Select(seq => Json.Clone(_inverses[seq].Inverse)).ToList();
        Command inv = u.FromSeq == 0 ? inverses[0] : new Batch { Items = inverses, UndoFrom = u.FromSeq };
        inv.UndoOf = u.TargetSeq;
        if (ZoneDenies(peer, inv) is { } undoZone) return "that change cannot be taken back: " + undoZone;
        undoTargets = targets;
        c = inv;
        return null;
    }

    List<long> ScriptStepsBetween(long from, long to) =>
        _undoableBy.TryGetValue(0, out var scripts)
            ? scripts.Where(seq => seq >= from && seq <= to && !_undone.Contains(seq) && _inverses[seq].Step).Reverse().ToList()
            : [];

    readonly record struct Undoable(Command Inverse, string By, bool Step);

    string? Readiness(Command c)
    {
        if (c is RestoreTurn rt)
        {
            if (rt.Index < 0 || rt.Index >= Turns.Count) return "no such turn";
            rt.State = Turns[rt.Index].Unpack();
        }
        if (NotWritable(c) is { } badNumber) return badNumber;
        if (c is DeletePiece gone && State.Find(gone.Layer, gone.Id) == null) return "no such piece";
        return null;
    }

    void BeforeApplying(List<Outgoing> res, long peerId, Command c)
    {
        try { res.AddRange(Scripts.BeforeApply(c, peerId)); }
        catch (Exception e) { res.Add(new(peerId, new ErrorMessage { Text = "a script failed before the change: " + e.Message })); }
        if (c is Batch db) db.Items.RemoveAll(i => i is DeletePiece d && State.Find(d.Layer, d.Id) == null);
        if (SaveFile.EndsTurn(State, c))
            try { res.AddRange(Scripts.SaveAll()); }
            catch (Exception e) { res.Add(new(peerId, new ErrorMessage { Text = "a script failed to give its state as the turn ended: " + e.Message })); }
    }

    static bool Deletes(Batch b) => b.Items.Any(i => i is DeletePiece || i is Batch inner && Deletes(inner));

    internal static string? NotWritable(Command c)
    {
        try { Json.SerializeBytes(c); return null; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException)
        {
            return $"{c.GetType().Name} refused: it carries a number that is not a number or is infinite ({e.Message})";
        }
    }

    public const int RememberCommands = 10_000;

    public Action<Command>? Applied;

    void Remember(Command c, Command? inverse, bool toSaveLog = true)
    {
        if (toSaveLog) Applied?.Invoke(c);
        _log.Add(Json.Clone(c));
        if (_log.Count > RememberCommands + RememberCommands / 10) _log.RemoveRange(0, _log.Count - RememberCommands);
        if (inverse == null) return;
        _inverses[c.Seq] = new Undoable(inverse, c.Actor == 0 ? "" : _peers.GetValueOrDefault(c.Actor)?.Name ?? $"#{c.Actor}", c.MakesUndoStep());
        if (!_undoableBy.TryGetValue(c.Actor, out var mine)) _undoableBy[c.Actor] = mine = new();
        mine.Enqueue(c.Seq);
        if (mine.Count <= RememberCommands) return;
        var oldest = mine.Dequeue();
        if (c.Actor == 0) _scriptsForgottenTo = oldest;
        _inverses.Remove(oldest);
        _undone.Remove(oldest);
    }

    void ForgetUndo()
    {
        _inverses.Clear();
        _undone.Clear();
        _undoableBy.Clear();
        _scriptsForgottenTo = 0;
    }

    internal (string? err, Command? inverse) ApplyEmitted(Command c, long actor)
    {
        if (NotWritable(c) is { } badNumber) return (badNumber, null);
        var nextId = State.NextId;
        var seqBefore = State.LastSeq;
        TurnRecord? ending;
        string? err;
        Command? inverse;
        try
        {
            c.Actor = actor;
            c.At = _clock();
            Prepare(c, _peers.GetValueOrDefault(actor) ?? ScriptPeer);
            ending = SaveFile.EndsTurn(State, c) ? SaveFile.RecordOf(State, Repeatable.UtcNow) : null;
            err = StateMachine.Apply(State, c, out inverse);
        }
        catch (Exception e)
        {
            err = $"{c?.GetType().Name ?? "a command"} could not be applied: {e.Message}";
            ending = null;
            inverse = null;
        }
        if (err != null) { GiveBackIds(nextId, seqBefore); return (err, null); }
        if (ending != null) Turns.Add(ending);
        LastInverse = inverse;
        return (null, inverse);
    }

    internal List<Outgoing> Commit(Command c, Command? inverse)
    {
        c.Seq = State.LastSeq + 1;
        if (c.At == 0) c.At = _clock();
        State.LastSeq = c.Seq;
        Remember(c, inverse);
        Dirty = true;
        return ToPeers(c);
    }

    List<Outgoing> ToPeers(Command c)
    {
        var res = new List<Outgoing>();
        bool sameForEveryObserver = c.ShownToObservers() == Shown.WholeTable;
        Command? forObservers = null;
        bool filtered = false;
        foreach (var p in _peers.Values)
        {
            Command? copy;
            if (sameForEveryObserver && p.Role == Role.Observer)
            {
                if (!filtered) { forObservers = Visibility.ForPeer(c, p, State); filtered = true; }
                copy = forObservers;
            }
            else copy = Visibility.ForPeer(c, p, State);
            if (copy != null) res.Add(new(p.PeerId, new CommandMessage { Command = copy }));
        }
        return res;
    }

    void Prepare(Command c, PeerInfo peer, bool isUndo = false)
    {
        switch (c)
        {
            case SpawnPiece s:
                if (string.IsNullOrEmpty(s.Id) || State.Find(s.Layer, s.Id) != null) s.Id = State.NewId();
                if (s.ScriptState == null && Store?.Def(s.Def)?.ScriptState is { } st0) s.ScriptState = st0;
                break;
            case RestorePiece r when string.IsNullOrEmpty(r.Piece.Id) || State.Find(r.Layer, r.Piece.Id) != null:
                r.Piece.Id = State.NewId();
                break;
            case RenameSheet rn when !isUndo && rn.Name != null:
                rn.Name = rn.Name.Trim();
                break;
            case RollDice d:
                if (d.Layer != null && d.PieceIds.Count > 0)
                {
                    var landed = new Dictionary<string, int>();
                    if (d.Physical && d.Results.Count == d.PieceIds.Count)
                        for (int i = 0; i < d.PieceIds.Count; i++) landed.TryAdd(d.PieceIds[i], d.Results[i]);
                    d.PieceIds = d.PieceIds.Where(id => State.Find(d.Layer, id) != null).Distinct().Take(50).ToList();
                    int Faces(string id) => State.Find(d.Layer, id) is { } die ? Math.Max(2, PieceInstance.FacesOf(Store?.Def(die.Def), die) is { Count: > 0 } f ? f.Count : d.Sides) : d.Sides;
                    int Result(string id) => State.Find(d.Layer, id)!.PhysicalRoll && landed.TryGetValue(id, out var r) && r >= 1 && r <= Faces(id) ? r : _rng.Next(1, Faces(id) + 1);
                    d.Results = d.PieceIds.Select(Result).ToList();
                    d.Count = d.PieceIds.Count;
                    if (d.PieceIds.Count > 0) d.Sides = Faces(d.PieceIds[0]);
                }
                else
                {
                    d.Layer = null;
                    d.Sides = Math.Clamp(d.Sides, 2, 1000);
                    d.Count = Math.Clamp(d.Count, 1, 50);
                    d.Results = Enumerable.Range(0, d.Count).Select(_ => _rng.Next(1, d.Sides + 1)).ToList();
                }
                d.ActorName = peer.Name;
                break;
            case SetCounter sc:
            {
                var p = State.Find(sc.Layer, sc.Id);
                var def = p != null ? Store?.Def(p.Def) : null;
                var cd = def?.Counters.FirstOrDefault(x => x.Name == sc.Name);
                if (cd != null) sc.Value = Math.Clamp(sc.Value, cd.Min, cd.Max);
                break;
            }
            case Draw dr when !isUndo || string.IsNullOrEmpty(dr.Stroke.Id):
                dr.Stroke.Id = State.NewId("d");
                break;
            case AddSheet a when string.IsNullOrEmpty(a.Sheet.Id):
                a.Sheet.Id = State.NewId("s");
                break;
            case AddBoard ab when !isUndo || string.IsNullOrEmpty(ab.Id):
                ab.Id = State.NewId("b");
                break;
            case Batch b:
                foreach (var i in b.Items) Prepare(i, peer, isUndo);
                break;
            case AddPack ap when Store != null:
                Store.Forget(ap.Id);
                break;
        }
    }

    public TurnInfo Info(int index) => new()
    {
        Index = index, Turn = Turns[index].Turn, GameTime = Turns[index].GameTime, EndedAt = Turns[index].EndedAt,
        Pieces = Turns[index].Pieces,
    };

    public List<Outgoing> Resync(long peerId)
    {
        var res = new List<Outgoing>();
        if (_peers.TryGetValue(peerId, out var peer)) res.Add(new(peerId, new Snapshot { View = Visibility.Filter(State, peer) }));
        return res;
    }

    public List<Outgoing> TurnHistory(long peerId)
    {
        var res = new List<Outgoing>();
        if (!_peers.TryGetValue(peerId, out var peer) || !Visibility.IsUmpire(peer.Role)) { res.Add(new(peerId, new ErrorMessage { Text = "turn history is for umpires" })); return res; }
        res.Add(new(peerId, new TurnList { Turns = Enumerable.Range(0, Turns.Count).Select(Info).ToList() }));
        return res;
    }

    public List<Outgoing> TurnSnapshot(long peerId, int index)
    {
        var res = new List<Outgoing>();
        if (!_peers.TryGetValue(peerId, out var peer) || !Visibility.IsUmpire(peer.Role)) { res.Add(new(peerId, new ErrorMessage { Text = "turn history is for umpires" })); return res; }
        if (index < 0 || index >= Turns.Count) { res.Add(new(peerId, new ErrorMessage { Text = "no such turn" })); return res; }
        res.Add(new(peerId, new TurnState { Index = index, Info = Info(index), State = Turns[index].Unpack() }));
        return res;
    }

    public SaveFile ToSave(bool copy = true) => new()
    {
        ScenarioId = State.ScenarioId, Created = Created, Saved = Repeatable.UtcNow.ToString("o"), Preview = SavePreview.Of(State, Picture),
        Access = Access.Clone(), Snapshot = copy ? State.DeepClone() : State, Turns = Turns.ToList(),
    };

    public List<Outgoing> LoadSave(GameState state, IEnumerable<TurnRecord> turns, string name)
    {
        OpenPacks(state);
        var c = new LoadSave { Name = name, State = state, Actor = 1, At = _clock(), Seq = State.LastSeq + 1 };
        if (StateMachine.Apply(State, c, out _) is { } err) throw new InvalidDataException(err);
        Turns.Clear();
        Turns.AddRange(turns);
        ForgetUndo();
        State.LastSeq = c.Seq;
        Remember(c, null, toSaveLog: false);
        LastInverse = null;
        Dirty = true;
        var res = ToPeers(c);
        try { res.AddRange(Scripts.AfterApplied(c, 1, false)); }
        catch (Exception e) { res.Add(new(1, new ErrorMessage { Text = "a script failed after the save was loaded: " + e.Message })); }
        return res;
    }

    public byte[] SaveBytes()
    {
        var bytes = Json.BigBytes(ToSave(copy: false));
        Dirty = false;
        return bytes;
    }

    public void SaveTo(string path)
    {
        Json.SaveBig(path, ToSave(copy: false));
        Dirty = false;
    }

    public LoadReport Loaded { get; internal set; } = new();
}
