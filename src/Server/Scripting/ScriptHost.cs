using System.Diagnostics;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

public sealed partial class ScriptHost
{
    public const double CallBudgetMs = 100;
    public const double LoadBudgetMs = 10_000;
    public const long YieldEvery = 100_000;
    const int MaxDepth = 8;
    public const int MaxNesting = 50;
    public const int MaxCoroutinesPerCall = 64;
    public const int MaxRepLength = 16 * 1024 * 1024;
    public const long MaxCallMemory = 256L * 1024 * 1024;

    sealed class Reaction
    {
        public long Actor;
        public bool Action;
        public readonly List<Command> Items = new();
        public readonly List<Command?> Inverses = new();
        public readonly HashSet<string> Spawned = new();
        public readonly HashSet<string> WidgetsDirty = new();
        public readonly HashSet<string> MenusDirty = new();
        public bool HotkeysDirty, GlobalMenuDirty;
        public readonly HashSet<PieceScript> Ran = new();
        public bool GlobalRan;
    }

    readonly GameServer _s;
    readonly Script _vm;
    readonly Dictionary<string, PieceScript> _pieces = new();
    PieceScript? _global;
    readonly PieceHandles _handles;
    readonly PieceDrawing _drawing;
    readonly SaveRounds _saves;
    readonly TableControls _controls = new();
    readonly ScriptTimers _timers = new();
    ScriptGuards _guards = null!;
    CompiledScripts? _compiledScripts;
    CompiledScripts Compiled => _compiledScripts ??= new CompiledScripts(_vm);
    Reaction? _reaction;
    PieceScript? _current;
    int _depth;

    readonly ScriptFeed _feed = new();
    public List<string> Console => _feed.Text;
    public IReadOnlyList<ConsoleLine> Feed => _feed.Lines;
    public const int FeedKeep = ScriptFeed.Keep;
    PieceScript? _snippetPs;

    public List<ConsoleLine> TakeUnsent() => _feed.TakeUnsent();
    public int Loaded => _pieces.Count + (_global != null ? 1 : 0);
    public bool GlobalDefines(string name) => _global != null && _global.Enabled && _global.Env.Get(name).Type == DataType.Function;
    public string GlobalStatus => _global == null ? (_s.State.Script == null ? "none" : "not loaded") : _global.Enabled ? "running" : "stopped";

    internal GameServer Server => _s;
    internal Script Vm => _vm;
    internal PieceScript? Global => _global;
    internal IReadOnlyDictionary<string, PieceScript> PieceScripts => _pieces;

    internal ScriptHost(GameServer server)
    {
        _s = server;
        _handles = new PieceHandles(this);
        _drawing = new PieceDrawing(Find);
        _saves = new SaveRounds(this);
        var modules = CoreModules.Preset_SoftSandbox & ~(CoreModules.LoadMethods | CoreModules.OS_System | CoreModules.IO | CoreModules.Debug | CoreModules.Dynamic);
        _vm = new Script(modules);
        if (Repeatable.On) _vm.Call(_vm.Globals.Get("math").Table.Get("randomseed"), Repeatable.NewRandom().Next());
        InstallGuards();
        InstallApi();
        InstallUiApi();
        InstallCompat();
    }

    void InstallGuards() => _guards = new ScriptGuards(_vm);

    Table? _compatBooks, _compatProps;

    void InstallCompat()
    {
        using var stream = typeof(ScriptHost).Assembly.GetManifestResourceStream("compat.lua");
        if (stream == null) return;
        using var reader = new StreamReader(stream);
        var compat = _vm.DoString(reader.ReadToEnd(), null, "compat.lua");
        _vm.Globals["compat"] = compat;
        if (compat.Type == DataType.Table)
        {
            _compatBooks = compat.Table.Get("__books").Type == DataType.Table ? compat.Table.Get("__books").Table : null;
            _compatProps = compat.Table.Get("__props").Type == DataType.Table ? compat.Table.Get("__props").Table : null;
            foreach (var name in new[] { "Global", "Player", "Turns", "Notes", "Lighting", "Grid", "Hands", "MusicPlayer", "Backgrounds", "Time", "Timer", "Vector", "Color" })
                { var v = compat.Table.Get(name); if (!v.IsNil()) _vm.Globals[name] = v; }
        }
    }

    public List<Outgoing> Load() => Run(0, false, LoadAll);

    void LoadAll()
    {
        UnloadAll();
        var pieces = _s.State.Layer(Layers.Umpire).ToList();
        Volatile.Write(ref _loadTotal, pieces.Count);
        Volatile.Write(ref _loadDone, 0);
        bool mine = BeginRedraw(pieces, clearUndrawn: false);
        try
        {
            if (_s.State.Script is { } g) Guard("", () => LoadGlobal(g));
            for (int i = 0; i < pieces.Count; i++)
            {
                if (LoadCancel.IsCancellationRequested) break;
                if (Find(pieces[i].Id) is { } still) Guard(still.Id, () => LoadPiece(still));
                Volatile.Write(ref _loadDone, i + 1);
            }
        }
        finally { if (mine) EndRedraw(); }
    }

    public int LoadDone => Volatile.Read(ref _loadDone);
    public int LoadTotal => Volatile.Read(ref _loadTotal);
    int _loadDone, _loadTotal;
    public CancellationToken LoadCancel { get; set; }

    sealed class Redraw
    {
        public readonly Dictionary<string, (List<Widget> widgets, List<MenuItemDef> menus)> Saved = new();
        public bool ForGlobal;
    }
    Redraw? _redraw;

    internal void Drawn(string id, bool menu)
    {
        var r = _reaction!;
        (menu ? r.MenusDirty : r.WidgetsDirty).Add(id);
        if (_current == null) return;
        _drawing.DrawnBy(id, _current);
    }

    bool BeginRedraw(IEnumerable<PieceInstance> pieces, bool clearUndrawn)
    {
        bool mine = _redraw == null;
        _redraw ??= new Redraw();
        foreach (var p in pieces)
        {
            if (_redraw.Saved.ContainsKey(p.Id)) continue;
            _redraw.Saved[p.Id] = (p.Widgets.Select(w => w.Clone()).ToList(), p.MenuItems.Select(m => m.Clone()).ToList());
            _drawing.Blank(p.Id);
            _compatBooks?.Set(p.Id, DynValue.Nil);
            if (clearUndrawn && _reaction != null)
            {
                if (p.Widgets.Count > 0) _reaction.WidgetsDirty.Add(p.Id);
                if (p.MenuItems.Count > 0) _reaction.MenusDirty.Add(p.Id);
            }
        }
        return mine;
    }

    void EndRedraw()
    {
        var rd = _redraw;
        _redraw = null;
        if (rd == null || _reaction is not { } r) return;
        bool globalOk = !rd.ForGlobal || (_global != null ? ScriptOk(_global) : _s.State.Script == null);
        foreach (var (id, saved) in rd.Saved)
        {
            if (Find(id) is not { } p) continue;
            bool ownOk = string.IsNullOrWhiteSpace(CodeFor(p)) || (_pieces.TryGetValue(id, out var own) && ScriptOk(own));
            bool drawersOk = !_drawing.TryDrawers(id, out var who) || who.All(ScriptOk);
            if (globalOk && ownOk && drawersOk) continue;
            if (saved.widgets.Count > 0) { _drawing.SetWidgets(id, saved.widgets); r.WidgetsDirty.Remove(id); }
            if (saved.menus.Count > 0) { _drawing.SetMenus(id, saved.menus); r.MenusDirty.Remove(id); }
        }
    }

    bool ScriptOk(PieceScript ps) => ps.Enabled && !ps.Faulted && IsCurrent(ps);

    void ReloadGlobal(bool askOwn = true)
    {
        var old = _global;
        var pieces = new List<PieceInstance>();
        var ownToo = new List<string>();
        if (old != null)
            foreach (var (id, who) in _drawing.AllDrawers())
            {
                if (!who.Contains(old) || Find(id) is not { } p) continue;
                var others = who.Where(d => d != old && IsCurrent(d)).ToList();
                if (others.Any(d => d.Id != id)) continue;
                pieces.Add(p);
                if (others.Count > 0) ownToo.Add(id);
            }
        bool mine = BeginRedraw(pieces, clearUndrawn: true);
        if (mine) _redraw!.ForGlobal = true;
        try
        {
            if (old != null) ForgetOwner(old);
            _global = null;
            if (_s.State.Script is { } g) LoadGlobal(g);
            foreach (var id in ownToo)
                Guard(id, () =>
                {
                    if (Find(id) is not { } p) return;
                    if (askOwn && _pieces.TryGetValue(id, out var own) && own.Enabled && Call(own, "on_save") is { Type: DataType.String } s && s.String != p.ScriptState)
                        Emit(new SetScriptState { Layer = Layers.Umpire, Id = id, State = s.String });
                    LoadPiece(p);
                });
        }
        finally { if (mine) EndRedraw(); }
    }

    public List<Outgoing> Save() => SaveRound(sliced: true);

    public List<Outgoing> SaveAll() => SaveRound(sliced: false);

    List<Outgoing> SaveRound(bool sliced) => Run(0, false, () => UnderLoadBudget(() => _saves.Round(sliced)));

    public int AskedLast => _saves.AskedLast;
    public int WithUpdate => _saves.WithUpdate;

    public const int RoundBudgetMs = 4;

    public bool AnyScriptRan => (_global?.Ran ?? false) || _saves.Backlog > 0;

    public int Backlog => _saves.Backlog;

    void MarkRan(PieceScript ps)
    {
        if (_saves.MarkRan(ps)) _reaction?.Ran.Add(ps);
        else if (ps == _global && _reaction != null) _reaction.GlobalRan = true;
    }

    public List<Outgoing> Tick(double nowSeconds)
    {
        var due = _timers.Advance(nowSeconds);
        var resumes = _timers.TakeResumes();
        var updating = _pieces.Values.Where(ps => ps.HasUpdate && ps.Enabled).ToList();
        if (_global is { HasUpdate: true, Enabled: true }) updating.Add(_global);
        if (due.Count == 0 && resumes.Count == 0 && updating.Count == 0 && _fetched.IsEmpty) return new();
        return Run(0, false, () =>
        {
            DeliverPictures();
            foreach (var ps in updating) Guard(ps.Id, () => Call(ps, "on_update"));
            foreach (var t in due)
                Guard(t.Owner.Id, () =>
                {
                    if (t.Gone) return;
                    if (!t.Owner.Enabled || !IsCurrent(t.Owner)) { _timers.Remove(t); return; }
                    if (t.Predicate.Type == DataType.Function)
                    {
                        var ok = Invoke(t.Owner, t.Predicate);
                        if (ok == null) { _timers.Remove(t); return; }
                        bool fire = ok is { Type: DataType.Boolean, Boolean: true };
                        bool timedOut = !double.IsNaN(t.Timeout) && _timers.Now >= t.Timeout;
                        if (!fire && !timedOut) return;
                        _timers.Remove(t);
                        if (fire) Invoke(t.Owner, t.Fn);
                        else if (timedOut && t.OnTimeout.Type == DataType.Function) Invoke(t.Owner, t.OnTimeout);
                        return;
                    }
                    _timers.Remove(t);
                    Invoke(t.Owner, t.Fn);
                });
            foreach (var (owner, co) in resumes) Guard(owner.Id, () => { if (owner.Enabled && IsCurrent(owner)) Resume(owner, co); });
        });
    }

    bool IsCurrent(PieceScript ps) => ps.Id.Length == 0 ? ps == _global : _pieces.TryGetValue(ps.Id, out var current) && current == ps;

    internal void Guard(string pieceId, Action work)
    {
        try { work(); }
        catch (Exception e) { Error(pieceId, ScriptGuards.Describe(e)); }
    }

    void UnloadAll()
    {
        _timers.Clear();
        _pieces.Clear();
        _global = null;
        _handles.Clear();
        _feed.ForgetAll();
        _saves.Clear();
        _drawing.Clear();
        if (_compatBooks != null) foreach (var k in _compatBooks.Keys.ToList()) _compatBooks.Set(k, DynValue.Nil);
        if (_controls.Clear() && _reaction != null) { _reaction.HotkeysDirty = true; _reaction.GlobalMenuDirty = true; }
    }

    void Unload(string id, bool pieceGone)
    {
        UnloadScript(id);
        _drawing.Forget(id, pieceGone);
        _compatBooks?.Set(id, DynValue.Nil);
        if (pieceGone)
        {
            _handles.Forget(id);
            _uiTables.Remove(id);
            _compatProps?.Set(id, DynValue.Nil);
        }
    }

    void UnloadScript(string id)
    {
        if (_pieces.Remove(id, out var ps))
        {
            ForgetOwner(ps);
            _saves.Asked(ps);
            _timers.DropResumes(ps);
        }
        _timers.RemoveWhere(t => t.Owner.Id == id);
    }

    void ForgetOwner(PieceScript ps)
    {
        var (hotkeys, menu) = _controls.Forget(ps);
        if (hotkeys && _reaction != null) _reaction.HotkeysDirty = true;
        if (menu && _reaction != null) _reaction.GlobalMenuDirty = true;
    }

    string? CodeFor(PieceInstance p) => p.Script ?? _s.Store?.Def(p.Def)?.Script;

    void LoadPiece(PieceInstance p)
    {
        bool redrawn = _redraw?.Saved.ContainsKey(p.Id) == true;
        _feed.Forget(p.Id);
        if (redrawn) UnloadScript(p.Id);
        else Unload(p.Id, pieceGone: false);
        var code = CodeFor(p);
        if (string.IsNullOrWhiteSpace(code)) return;
        var compiled = Compiled.For(code);
        if (compiled.Verdict.Hostile) { Error(p.Id, "script refused: " + string.Join("; ", compiled.Verdict.Reasons)); return; }
        var ps = new PieceScript { Id = p.Id, Env = NewEnv(), Source = compiled.Verdict.Script };
        ps.Env["self"] = Handle(p.Id);
        UnderLoadBudget(() =>
        {
            if (!Compile(ps, compiled)) { _saves.Asked(ps); return; }
            ps.HasUpdate = ps.Env.Get("on_update").Type == DataType.Function;
            _pieces[p.Id] = ps;
            if (!redrawn) _drawing.Blank(p.Id);
            _drawing.FromLoad(p.Id);
            Call(ps, "on_load", DynValue.NewString(p.ScriptState));
        });
    }

    void UnderLoadBudget(Action body)
    {
        var saved = _guards.BudgetMs;
        _guards.BudgetMs = LoadBudgetMs;
        try { body(); } finally { _guards.BudgetMs = saved; }
    }

    void LoadGlobal(string code)
    {
        if (_global != null) ForgetOwner(_global);
        _global = null;
        _feed.Forget("");
        var verdict = ScriptSafety.Inspect(code);
        if (verdict.Hostile) { Error("", "script refused: " + string.Join("; ", verdict.Reasons)); return; }
        var ps = new PieceScript { Id = "", Env = NewEnv(), Source = verdict.Script };
        ps.Env["self"] = DynValue.Nil;
        UnderLoadBudget(() =>
        {
            if (!Compile(ps, null)) return;
            ps.HasUpdate = ps.Env.Get("on_update").Type == DataType.Function;
            _global = ps;
            Call(ps, "on_load", DynValue.NewString(_s.State.ScriptState));
            InstallScriptingButtons(ps);
        });
    }

    void InstallScriptingButtons(PieceScript ps)
    {
        if (ps.Env.Get("on_scripting_button_down").Type != DataType.Function && ps.Env.Get("on_scripting_button_up").Type != DataType.Function) return;
        try
        {
            var maker = _vm.Call(_vm.LoadString("return function(i) return function(who, piece, pos, up) local f = up and on_scripting_button_up or on_scripting_button_down if f then return f(i, who) end end end", ps.Env, "scripting buttons"));
            for (int i = 1; i <= 10; i++) _controls.AddHotkey(ps, $"Scripting button {i}", _vm.Call(maker, DynValue.NewNumber(i)), true);
            if (_reaction != null) _reaction.HotkeysDirty = true;
        }
        catch (InterpreterException e) { Error(ps.Id, e.DecoratedMessage ?? e.Message); }
    }

    Table NewEnv()
    {
        var env = new Table(_vm);
        var meta = new Table(_vm);
        meta["__index"] = _vm.Globals;
        env.MetaTable = meta;
        env["_G"] = env;
        return env;
    }

    bool Compile(PieceScript ps, CompiledScripts.Compiled? compiled)
    {
        try
        {
            if (compiled != null)
            {
                if (compiled.Chunk == null) { Error(ps.Id, compiled.Failure ?? "the script does not compile"); return false; }
                return Invoke(ps, compiled.Chunk, DynValue.NewTable(ps.Env)) != null || ps.Enabled;
            }
            var chunk = _vm.LoadString(ps.Source, ps.Env, ps.Name);
            return Invoke(ps, chunk) != null || ps.Enabled;
        }
        catch (SyntaxErrorException e) { Error(ps.Id, e.DecoratedMessage ?? e.Message); return false; }
        catch (InterpreterException e) { Error(ps.Id, e.DecoratedMessage ?? e.Message); return false; }
    }

    internal DynValue? Call(PieceScript ps, string fn, params DynValue[] args)
    {
        if (!ps.Enabled) return null;
        if (fn.StartsWith("global:"))
        {
            if (_global == null) return null;
            ps = _global;
            fn = fn[7..];
        }
        var f = ps.Env.Get(fn);
        return f.Type == DataType.Function ? Invoke(ps, f, args) : null;
    }

    internal DynValue? Invoke(PieceScript ps, DynValue fn, params DynValue[] args)
    {
        if (!ps.Enabled || fn.Type != DataType.Function) return null;
        if (_guards.Nesting >= MaxNesting) throw new ScriptRuntimeException($"calls inside calls more than {MaxNesting} deep (a script calling itself through game.call or obj.call?)");
        MarkRan(ps);
        var saved = _current;
        _current = ps;
        try
        {
            var co = _guards.Worker(fn, args);
            return Drive(ps, co);
        }
        finally { _current = saved; }
    }

    DynValue? Resume(PieceScript ps, DynValue co)
    {
        MarkRan(ps);
        var saved = _current;
        _current = ps;
        try { return Drive(ps, co); }
        finally { _current = saved; }
    }

    DynValue? Drive(PieceScript ps, DynValue co)
    {
        var g = _guards;
        if (g.Nesting == 0) g.BeginCall();
        var outer = g.Deadline;
        g.Deadline = Math.Min(outer, g.Elapsed + g.BudgetMs);
        g.Nesting++;
        var outerRunning = g.Enter(co.Coroutine);
        try
        {
            var r = co.Coroutine.Resume();
            while (r.Type == DataType.YieldRequest)
            {
                var over = g.Elapsed > g.Deadline ? $"more than {g.BudgetMs:0} ms" : g.OverMemory() ? ScriptGuards.MemoryOverrun : null;
                if (over != null)
                {
                    ps.Enabled = false;
                    ps.Faulted = true;
                    if (ps == _snippetPs) Error(ps.Id, $"stopped: {over}", console: true);
                    else Error(ps.Id, $"stopped: {over} in one call; the script is off until it is reloaded");
                    return null;
                }
                g.Enter(co.Coroutine);
                r = co.Coroutine.Resume();
            }
            if (co.Coroutine.State == CoroutineState.Suspended)
            {
                if (g.Finished(r, out var result)) { g.Release(co); return result; }
                _timers.ResumeNextFrame(ps, co);
                return null;
            }
            return r;
        }
        catch (InterpreterException e)
        {
            ps.Faulted = true;
            Error(ps.Id, e.DecoratedMessage ?? e.Message, console: ps == _snippetPs);
            return null;
        }
        catch (Exception e)
        {
            ps.Faulted = true;
            ps.Enabled = false;
            if (ps == _snippetPs) Error(ps.Id, $"stopped: {ScriptGuards.Describe(e)}", console: true);
            else Error(ps.Id, $"stopped: {ScriptGuards.Describe(e)}; the script is off until it is reloaded");
            return null;
        }
        finally
        {
            g.Nesting--;
            g.Deadline = outer;
            g.Leave(outerRunning);
        }
    }

    void Error(string pieceId, string message, bool console = false)
    {
        var name = pieceId.Length == 0 ? "the global script" : (_s.State.Find(Layers.Umpire, pieceId) is { } p ? (p.Label ?? _s.Store?.Def(p.Def)?.Name ?? pieceId) + $" ({pieceId})" : pieceId);
        if (console) { Print(message, "error", pieceId); return; }
        var line = $"Script error in {name}: {message}";
        Print(line, "error", pieceId, _feed.ErrorRaised(pieceId, message));
        if (_reaction != null)
            BestEffort.Try(() => Emit(new Say { Text = line, To = "u:", Color = "#f87" }));
    }

    void Print(string text, string kind = "print", string? source = null, int line = 0)
    {
        var src = source ?? _current?.Id ?? "";
        _feed.Add(text, kind, src, src.Length == 0 ? null : NameOf(src), line);
    }

    string NameOf(string id) => Find(id) is { } p ? p.Label ?? _s.Store?.Def(p.Def)?.Name ?? id : id;

    public List<ScriptInfo> Describe()
    {
        var timers = _timers.CountByOwner();
        ScriptInfo Info(string id, PieceScript? ps)
        {
            var info = new ScriptInfo { Id = id, Status = ps == null ? "not loaded" : !ps.Enabled ? "stopped" : _feed.LastError(id, out _, out _) ? "error" : "running" };
            if (_feed.LastError(id, out var message, out var line)) { info.LastError = message; info.ErrorLine = line; }
            if (ps != null) info.Timers = timers.GetValueOrDefault(ps);
            return info;
        }
        var list = new List<ScriptInfo>();
        if (_s.State.Script != null || _global != null)
        {
            var g = Info("", _global);
            g.Name = "Global script";
            list.Add(g);
        }
        foreach (var p in _s.State.Layer(Layers.Umpire))
        {
            if (string.IsNullOrWhiteSpace(CodeFor(p))) continue;
            var i = Info(p.Id, _pieces.GetValueOrDefault(p.Id));
            i.Name = NameOf(p.Id);
            i.Guid = p.Guid;
            i.Def = p.Def;
            i.Own = p.Script != null;
            list.Add(i);
        }
        return list;
    }

    public List<Outgoing> RunSnippet(long actor, string target, string code)
    {
        string? pieceId = null;
        if (target.Length > 0)
        {
            var p = Find(target) ?? _s.State.Layer(Layers.Umpire).FirstOrDefault(x => x.Guid == target);
            if (p == null)
            {
                Print(code, "input", target);
                Print($"no piece {target} on the umpire board", "error", target);
                return new();
            }
            pieceId = p.Id;
        }
        var src = pieceId ?? "";
        Print(code, "input", src);
        var verdict = ScriptSafety.Inspect(code);
        if (verdict.Hostile) { Print("refused: " + string.Join("; ", verdict.Reasons), "error", src); return new(); }
        var real = pieceId != null ? _pieces.GetValueOrDefault(pieceId) : _global;
        if (real != null && !real.Enabled) real = null;
        var env = real?.Env ?? NewEnv();
        if (real == null) env["self"] = pieceId != null ? Handle(pieceId) : DynValue.Nil;
        var temp = new PieceScript { Id = src, Env = env, Source = verdict.Script };
        var outer = _snippetPs;
        _snippetPs = temp;
        try
        {
            return Run(actor, true, () =>
            {
                DynValue chunk;
                try { chunk = _vm.LoadString("return " + verdict.Script, env, "console"); }
                catch (SyntaxErrorException)
                {
                    try { chunk = _vm.LoadString(verdict.Script, env, "console"); }
                    catch (SyntaxErrorException e) { Print(e.DecoratedMessage ?? e.Message, "error", src, ScriptFeed.LineOf(e.DecoratedMessage ?? e.Message)); return; }
                }
                var r = Invoke(temp, chunk);
                Adopt(temp, real);
                if (real != null) MarkRan(real);
                if (r == null) return;
                var shown = r.Type == DataType.Tuple ? string.Join(", ", r.Tuple.Select(v => Inspect(v))) : r.IsNil() || r.Type == DataType.Void ? null : Inspect(r);
                if (shown != null) Print(shown, "result", src);
            });
        }
        finally { _snippetPs = outer; }
    }

    void Adopt(PieceScript temp, PieceScript? real)
    {
        _timers.AdoptTimers(temp, real);
        var (hotkeys, menu) = _controls.Adopt(temp, real);
        if (hotkeys && _reaction != null) _reaction.HotkeysDirty = true;
        if (menu && _reaction != null) _reaction.GlobalMenuDirty = true;
        _timers.AdoptResumes(temp, real);
        foreach (var (token, d) in _dialogs.ToList()) if (d.owner == temp) { if (real != null) _dialogs[token] = d with { owner = real }; else _dialogs.Remove(token); }
        _drawing.AdoptDrawer(temp, real);
        _saves.Drop(temp);
    }

    string Inspect(DynValue v) => LuaText.Inspect(v, NameOf);

    List<Outgoing> Run(long actor, bool action, Action body)
    {
        if (_reaction != null) { body(); return new(); }
        _reaction = new Reaction { Actor = actor, Action = action };
        Reaction r;
        try
        {
            Guard("", body);
            Guard("", SaveReacted);
            FlushStaging();
        }
        finally { r = _reaction; _reaction = null; }
        return Close(r);
    }

    const int ReactionSaves = 8;

    void SaveReacted()
    {
        var r = _reaction!;
        bool logs = r.Items.Count > 0 || r.WidgetsDirty.Count > 0 || r.MenusDirty.Count > 0;
        if (r.Actor == 0 || !logs) return;
        if (r.GlobalRan && _global is { Enabled: true } g)
            Guard("", () =>
            {
                if (Call(g, "on_save") is { Type: DataType.String } gs && gs.String != _s.State.ScriptState)
                    Emit(new SetGlobalScriptState { State = gs.String });
            });
        if (r.Ran.Count == 0 || r.Ran.Count > ReactionSaves) return;
        foreach (var ps in r.Ran.ToList())
        {
            if (!ps.Enabled || !_pieces.TryGetValue(ps.Id, out var current) || current != ps || Find(ps.Id) is not { } p) continue;
            Guard(ps.Id, () =>
            {
                var saved = Call(ps, "on_save");
                if (saved is { Type: DataType.String } s && s.String != p.ScriptState)
                    Emit(new SetScriptState { Layer = Layers.Umpire, Id = ps.Id, State = s.String });
            });
        }
    }

    List<Outgoing> Close(Reaction r)
    {
        if (r.Items.Count == 0) return new();
        if (r.Items.Count == 1) return _s.Commit(r.Items[0], r.Inverses[0]);
        var inverses = r.Inverses.Where(i => i != null).Select(i => i!).Reverse().ToList();
        var batch = new Batch { Items = r.Items, Actor = r.Actor };
        return _s.Commit(batch, inverses.Count > 0 ? new Batch { Items = inverses } : null);
    }

    internal void Emit(Command c)
    {
        var r = _reaction ?? throw new ScriptRuntimeException("not inside a script call");
        if (c is DeletePiece d && Find(d.Id) != null && _depth < MaxDepth)
        {
            _depth++;
            try { Before(c); } finally { _depth--; }
        }
        var (err, inverse) = _s.ApplyEmitted(c, r.Actor);
        if (err != null) throw new ScriptRuntimeException(err);
        r.Items.Add(c);
        r.Inverses.Add(inverse);
        if (c is SpawnPiece sp && sp.Id != null) r.Spawned.Add(sp.Id);
        if (_depth < MaxDepth && c is not Say)
        {
            _depth++;
            try { React(c, r.Actor); } finally { _depth--; }
        }
    }

    void FlushStaging()
    {
        var r = _reaction!;
        foreach (var id in r.WidgetsDirty.ToList())
            if (_drawing.TryWidgets(id, out var ws) && _s.State.Find(Layers.Umpire, id) != null)
                Guard(id, () => Emit(new SetWidgets { Layer = Layers.Umpire, Id = id, Widgets = ws.Select(w => w.Clone()).ToList(), OnState = _drawing.IsFromLoad(id) ? 0 : -1 }));
        foreach (var id in r.MenusDirty.ToList())
            if (_drawing.TryMenus(id, out var ms) && _s.State.Find(Layers.Umpire, id) != null)
                Guard(id, () => Emit(new SetMenuItems { Layer = Layers.Umpire, Id = id, Items = ms.Select(m => m.Clone()).ToList(), OnState = _drawing.IsFromLoad(id) ? 0 : -1 }));
        r.WidgetsDirty.Clear();
        r.MenusDirty.Clear();
        _drawing.ClearFromLoad();
        if (r.HotkeysDirty) { r.HotkeysDirty = false; Guard("", () => Emit(new SetHotkeys { Items = _controls.HotkeyDefs() })); }
        if (r.GlobalMenuDirty) { r.GlobalMenuDirty = false; Guard("", () => Emit(new SetGlobalMenuItems { Items = _controls.MenuDefs() })); }
    }

    DynValue PosTable(Vec2? p)
    {
        var t = new Table(_vm);
        if (p is { } v) { t["x"] = DynValue.NewNumber(v.X); t["z"] = DynValue.NewNumber(v.Z); }
        return DynValue.NewTable(t);
    }

    public List<Outgoing> BeforeApply(Command c, long actor) => Run(actor, false, () => Before(c));

    void Before(Command c)
    {
        switch (c)
        {
            case Batch b:
                foreach (var i in b.Items) Before(i);
                break;
            case DeletePiece d:
                if (_pieces.TryGetValue(d.Id, out var ps)) Call(ps, "on_destroy");
                if (_global != null) Call(_global, "on_destroy", Handle(d.Id));
                break;
        }
    }

    public List<Outgoing> AfterApplied(Command c, long actor, bool undo) => Run(actor, IsAction(c), () =>
    {
        if (undo) { ReloadTouched(c); return; }
        React(c, actor);
    });

    static bool IsAction(Command c) => c is ClickWidget or EditWidget or PickMenuItem or UiEvent or DialogResult or Hotkey or PickGlobalMenuItem || (c is Batch b && b.Items.Any(IsAction));

    void ReactToMove(MovePiece m, DynValue who)
    {
        var old = _s.LastInverse is MovePiece inv && inv.Id == m.Id ? inv : null;
        bool turnedOnly = old != null && Math.Abs(old.RotDeg - m.RotDeg) > 0.01 && (old.Pos - m.Pos).Length < 0.001;
        if (_pieces.TryGetValue(m.Id, out var pm))
        {
            if (turnedOnly) Call(pm, "on_rotate", DynValue.NewNumber(m.RotDeg), DynValue.NewNumber(old!.RotDeg), who);
            else Call(pm, "on_drop", who);
        }
        if (_global == null) return;
        if (turnedOnly) Call(_global, "on_rotate", Handle(m.Id), DynValue.NewNumber(m.RotDeg), DynValue.NewNumber(old!.RotDeg), who);
        else Call(_global, "on_drop", Handle(m.Id), who);
    }

    void React(Command c, long actor)
    {
        var who = Who(actor);
        switch (c)
        {
            case Batch b:
                foreach (var i in b.Items) React(i, actor);
                break;
            case RestoreTurn or LoadSave:
                LoadAll();
                break;
            case SetGlobalScript:
                ReloadGlobal();
                break;
            case SetScript ss:
                if (Find(ss.Id) is { } p1) LoadPiece(p1);
                else Unload(ss.Id, pieceGone: true);
                break;
            case SetDef sd when Find(sd.Id) is { Script: null } pd:
                LoadPiece(pd);
                break;
            case SpawnPiece sp when sp.Id != null:
                if (Find(sp.Id) is { } p2) { LoadPiece(p2); if (_global != null) Call(_global, "on_spawn", Handle(sp.Id)); }
                break;
            case RestorePiece rp:
                if (Find(rp.Piece.Id) is { } p3) { LoadPiece(p3); if (_global != null) Call(_global, "on_spawn", Handle(rp.Piece.Id)); }
                break;
            case DeletePiece d:
                Unload(d.Id, pieceGone: true);
                break;
            case MovePiece m:
                ReactToMove(m, who);
                break;
            case HoldPiece { Held: true } h:
                if (_pieces.TryGetValue(h.Id, out var ph)) Call(ph, "on_pick_up", who);
                if (_global != null) Call(_global, "on_pick_up", Handle(h.Id), who);
                break;
            case SetState st:
                if (_pieces.TryGetValue(st.Id, out var pst)) Call(pst, "on_state", DynValue.NewNumber(OldState(st) + 1), DynValue.NewNumber(st.State + 1));
                break;
            case FlipPiece fp:
                if (_pieces.TryGetValue(fp.Id, out var pfp)) Call(pfp, "on_flip", DynValue.NewBoolean(fp.FaceDown));
                break;
            case AdvanceClock or SetClock:
            {
                var turn = DynValue.NewNumber(_s.State.Clock.Turn);
                if (_global != null) Call(_global, "on_turn", turn);
                foreach (var ps in _pieces.Values.ToList()) Call(ps, "on_turn", turn);
                break;
            }
            case ClickWidget cw:
                if (Find(cw.Id) is { } pc && cw.Index >= 1 && cw.Index <= pc.Widgets.Count && pc.Widgets[cw.Index - 1].OnClick is { } fn && HandlerOwner(cw.Id, fn) is ({ } pcw, var cfn))
                    Call(pcw, cfn, who, DynValue.NewBoolean(cw.Alt), DynValue.NewNumber(cw.Index), Handle(cw.Id));
                break;
            case EditWidget ew:
                if (Find(ew.Id) is { } pe && ew.Index >= 1 && ew.Index <= pe.Widgets.Count)
                {
                    var staged = Staging(ew.Id);
                    if (ew.Index <= staged.Count) staged[ew.Index - 1].Value = ew.Value;
                    if (pe.Widgets[ew.Index - 1].OnChange is { } fn2 && HandlerOwner(ew.Id, fn2) is ({ } pew, var efn)) Call(pew, efn, who, DynValue.NewString(ew.Value), DynValue.NewNumber(ew.Index),
                        Handle(ew.Id));
                }
                break;
            case UiEvent ue:
                UiEventReact(ue, who);
                break;
            case DialogResult dr:
                DialogReact(dr, who);
                break;
            case PickMenuItem pm2:
                if (Find(pm2.Id) is { } pp && pm2.Index >= 1 && pm2.Index <= pp.MenuItems.Count && pp.MenuItems[pm2.Index - 1].OnSelect is { } fn3 && HandlerOwner(pm2.Id, fn3) is ({ } ppm, var mfn3))
                    Call(ppm, mfn3, who, Handle(pm2.Id));
                break;
            case NumberTyped nt:
                ReactToNumber(nt, who);
                break;
            case Say sy when actor != 0 && sy.To == null && _global != null:
                Call(_global, "on_chat", DynValue.NewString(sy.Text), who);
                break;
            case Hotkey hk:
                ReactToHotkey(hk, actor, who);
                break;
            case PickGlobalMenuItem pgm:
                if (_controls.MenuRow(pgm.Index) is { } row) Invoke(row.owner, row.fn, who, PosTable(pgm.Pos));
                break;
        }
    }

    void ReactToNumber(NumberTyped nt, DynValue who)
    {
        if (Find(nt.Id) == null) return;
        var keep = _global != null && _global.Env.Get("on_number_typed").Type == DataType.Function
            && Call(_global, "on_number_typed", Handle(nt.Id), who, DynValue.NewNumber(nt.Number)) is { Type: DataType.Boolean, Boolean: true };
        if (keep || Find(nt.Id) is not { } still) return;
        var ndef = _s.Store?.Def(still.Def);
        if (ndef != null && nt.Number >= 1 && nt.Number <= ndef.States.Count && ndef.States.Count > 1 && still.State != nt.Number - 1)
            try { Emit(new SetState { Layer = Layers.Umpire, Id = nt.Id, State = nt.Number - 1 }); } catch (ScriptRuntimeException e) { Error("", e.Message); }
    }

    void ReactToHotkey(Hotkey hk, long actor, DynValue who)
    {
        _controls.Pointed(actor, hk.Selected.ToList(), hk.Piece);
        if (_controls.Hotkey(hk.Index) is not { } hot) return;
        var (owner, hfn, onUp) = hot;
        if (!hk.Up || onUp) Invoke(owner, hfn, who, hk.Piece != null && Find(hk.Piece) != null ? Handle(hk.Piece) : DynValue.Nil, PosTable(hk.Pos), DynValue.NewBoolean(hk.Up));
    }

    (PieceScript? ps, string fn) HandlerOwner(string id, string fn)
    {
        if (fn.StartsWith("global:")) return (_global, fn[7..]);
        if (fn.Contains('/')) return ResolveHandler(fn);
        if (_pieces.TryGetValue(id, out var ps) && ps.Env.Get(fn).Type == DataType.Function) return (ps, fn);
        return (_global != null && _global.Env.Get(fn).Type == DataType.Function ? _global : null, fn);
    }

    void ReloadTouched(Command c)
    {
        var ids = c.ScriptsToReload().Distinct().ToList();
        if (ids.Remove(Command.GlobalScript)) ReloadGlobal(askOwn: false);
        foreach (var id in ids)
        {
            if (Find(id) is not { } p) { Unload(id, pieceGone: true); continue; }
            if (_pieces.ContainsKey(id) || !string.IsNullOrWhiteSpace(CodeFor(p))) LoadPiece(p);
        }
    }

    int OldState(SetState st)
    {
        return _s.LastInverse is SetState inv && inv.Id == st.Id ? inv.State : st.State;
    }

    internal PieceInstance? Find(string id) => _s.State.Find(Layers.Umpire, id);

    internal List<Widget> Staging(string id) => _drawing.Widgets(id);
    internal List<MenuItemDef> MenuStaging(string id) => _drawing.Menus(id);

    internal bool MaySetScript(string id)
    {
        var r = _reaction!;
        return r.Action || r.Spawned.Contains(id);
    }
}
