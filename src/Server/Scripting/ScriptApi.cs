using System.Globalization;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

public sealed partial class ScriptHost
{
    DynValue T() => DynValue.NewTable(_vm);

    void InstallApi()
    {
        var g = _vm.Globals;
        g["print"] = Fn(a =>
        {
            var text = string.Join("\t", Enumerable.Range(0, a.Count).Select(i => a[i].ToPrintString()));
            Print(text, text.StartsWith("Module call not supported here: ") ? "todo" : "print");
            return DynValue.Nil;
        });
        g["log"] = Fn(a =>
        {
            var label = Arg(a, 1).CastToString();
            Print((label is { Length: > 0 } ? label + ": " : "") + Inspect(Arg(a, 0)));
            return DynValue.Nil;
        });

        var json = T();
        json.Table["encode"] = Fn(a => S(LuaJson.Encode(Arg(a, 0), Arg(a, 1).CastToBool())));
        json.Table["decode"] = Fn(a => Arg(a, 0).Type == DataType.String ? LuaJson.Decode(_vm, Arg(a, 0).String)
            : throw new ScriptRuntimeException("Expected string argument to JSON.decode(), got " + Arg(a, 0).Type.ToLuaTypeString()));
        g["json"] = json;

        var wait = T();
        wait.Table["seconds"] = Fn(a => AddTimer(new ScriptTimer { Due = _timers.Now + Arg(a, 0).CastToNumber() ?? 0, Fn = Arg(a, 1) }));
        wait.Table["frames"] = Fn(a => AddTimer(new ScriptTimer { DueFrame = _timers.Frame + (long)(Arg(a, 0).CastToNumber() ?? 1), Fn = Arg(a, 1) }));
        wait.Table["condition"] =
            Fn(a => AddTimer(new ScriptTimer { Predicate = Arg(a, 0), Fn = Arg(a, 1), Timeout = Arg(a, 2).Type == DataType.Number ? _timers.Now + Arg(a, 2).Number : double.NaN,
                OnTimeout = Arg(a, 3) }));
        wait.Table["stop"] = Fn(a =>
        {
            var h = (int)(Arg(a, 0).CastToNumber() ?? 0);
            return B(_timers.RemoveWhere(t => t.Handle == h) > 0);
        });
        wait.Table["now"] = Fn(_ => N(_timers.Now));
        wait.Table["frame"] = Fn(_ => N(_timers.Frame));
        wait.Table["delta"] = Fn(_ => N(_timers.Delta));
        g["wait"] = wait;

        var board = T();
        board.Table["find"] = Fn(a => FindHandle(Arg(a, 0).CastToString() ?? ""));
        board.Table["pieces"] = Fn(a => Pieces(Arg(a, 0)));
        board.Table["spawn"] = Fn(a => Spawn(Arg(a, 0)));
        board.Table["kinds"] = Fn(a => _handles.Kinds(Arg(a, 0)));
        board.Table["show_cast"] = Fn(a => _handles.ShowCastOf(Arg(a, 0)));
        board.Table["distance"] = Fn(a => Distance(Arg(a, 0), Arg(a, 1)));
        board.Table["zones"] = Fn(_ => Zones());
        board.Table["draw"] = Fn(a => _handles.DrawStroke(Arg(a, 0)));
        board.Table["erase"] = Fn(a => { Emit(new EraseStroke { Layer = Layers.Umpire, StrokeId = Arg(a, 0).CastToString() ?? "" }); return DynValue.Nil; });
        board.Table["strokes"] = Fn(_ => _handles.Strokes());
        g["board"] = board;

        InstallGame(g);
    }

    void InstallGame(Table g)
    {
        var game = T();
        var gmeta = new Table(_vm);
        gmeta["__index"] = DynValue.NewCallback((_, a) => GameFields.Of(_s.State, Arg(a, 1).CastToString() ?? ""));
        game.Table.MetaTable = gmeta;
        game.Table["advance_clock"] = Fn(_ => { Emit(new AdvanceClock()); return DynValue.Nil; });
        game.Table["hotkey"] = Fn(a =>
        {
            var owner = _current ?? _global ?? throw new ScriptRuntimeException("game.hotkey: not inside a script");
            if (Arg(a, 1).Type != DataType.Function) throw new ScriptRuntimeException("game.hotkey(label, fn, on_up): fn must be a function");
            var n = _controls.AddHotkey(owner, Arg(a, 0).CastToString() ?? "", Arg(a, 1), Arg(a, 2).Type == DataType.Boolean && Arg(a, 2).Boolean);
            if (_reaction != null) _reaction.HotkeysDirty = true;
            return N(n);
        });
        game.Table["hotkeys_clear"] = Fn(_ =>
        {
            var owner = _current ?? _global;
            if (_controls.ClearHotkeys(owner) && _reaction != null) _reaction.HotkeysDirty = true;
            return DynValue.Nil;
        });
        game.Table["menu_add"] = Fn(a =>
        {
            var owner = _current ?? _global ?? throw new ScriptRuntimeException("game.menu_add: not inside a script");
            if (Arg(a, 1).Type != DataType.Function) throw new ScriptRuntimeException("game.menu_add(label, fn): fn must be a function");
            var n = _controls.AddMenuRow(owner, Arg(a, 0).CastToString() ?? "", Arg(a, 1));
            if (_reaction != null) _reaction.GlobalMenuDirty = true;
            return N(n);
        });
        game.Table["menu_clear"] = Fn(_ =>
        {
            var owner = _current ?? _global;
            if (_controls.ClearMenu(owner) && _reaction != null) _reaction.GlobalMenuDirty = true;
            return DynValue.Nil;
        });
        game.Table["select"] = Fn(a =>
        {
            var peer = PeerOf(Arg(a, 0));
            var id = IdOf(Arg(a, 1));
            bool on = Arg(a, 2).Type != DataType.Boolean || Arg(a, 2).Boolean;
            if (peer < 0 || Find(id) == null) return DynValue.Nil;
            Emit(new SelectPiece { Layer = Layers.Umpire, Id = id, To = $"p:{peer}", Selected = on });
            _controls.Select(peer, id, on);
            return DynValue.Nil;
        });
        game.Table["selected"] = Fn(a =>
        {
            var t = T();
            foreach (var id in _controls.SelectedBy(PeerOf(Arg(a, 0)))) if (Find(id) != null) t.Table.Append(Handle(id));
            return t;
        });
        game.Table["hovered"] = Fn(a => _controls.HoveredBy(PeerOf(Arg(a, 0))) is { } hovered && Find(hovered) != null ? Handle(hovered) : DynValue.Nil);
        game.Table["call"] = Fn(a =>
        {
            if (_global == null) return DynValue.Nil;
            var f = _global.Env.Get(Arg(a, 0).CastToString() ?? "");
            if (f.Type != DataType.Function) return DynValue.Nil;
            var rest = Enumerable.Range(1, Math.Max(0, a.Count - 1)).Select(i => a[i]).ToArray();
            return Invoke(_global, f, rest) ?? DynValue.Nil;
        });
        game.Table["get_var"] = Fn(a => _global?.Env.Get(Arg(a, 0).CastToString() ?? "") ?? DynValue.Nil);
        game.Table["set_var"] = Fn(a => { _global?.Env.Set(Arg(a, 0).CastToString() ?? "", Arg(a, 1)); return DynValue.Nil; });
        game.Table["umpires"] = Fn(_ =>
        {
            var t = T();
            foreach (var p in _s.Peers.Where(p => Visibility.IsUmpire(p.Role))) t.Table.Append(WhoTable(p));
            return t;
        });
        game.Table["say"] =
            Fn(a => { Emit(new Say { Text = Arg(a, 0).CastToString() ?? "", To = SayTarget(Arg(a, 1)),
                Color = Arg(a, 1).Type == DataType.Table ? ColorOf(Arg(a, 1).Table.Get("color")) : null }); return DynValue.Nil; });
        game.Table["roll"] = Fn(a =>
        {
            var rd = new RollDice { Sides = (int)(Arg(a, 0).CastToNumber() ?? 6), Count = (int)(Arg(a, 1).CastToNumber() ?? 1), Label = Arg(a, 2).CastToString() ?? "" };
            Emit(rd);
            var t = T();
            foreach (var r in rd.Results) t.Table.Append(N(r));
            return t;
        });
        g["game"] = game;
    }

    string? SayTarget(DynValue opts)
    {
        if (opts.Type != DataType.Table) return null;
        var to = opts.Table.Get("to");
        if (to.Type == DataType.Table && to.Table.Get("id").Type == DataType.Number) return $"p:{(long)to.Table.Get("id").Number}";
        if (to.Type == DataType.String) return to.String.StartsWith("p:") || to.String == "u:" ? to.String : null;
        return null;
    }

    DynValue AddTimer(ScriptTimer t)
    {
        var owner = _current ?? throw new ScriptRuntimeException("wait: not inside a script call");
        return N(_timers.Add(t, owner));
    }

    DynValue Who(long actor) => _s.Peer(actor) is { } p ? WhoTable(p) : DynValue.Nil;

    DynValue WhoTable(PeerInfo p)
    {
        var t = T();
        t.Table["id"] = N(p.PeerId);
        t.Table["name"] = S(p.Name);
        t.Table["role"] = S(p.Role switch { Role.HostUmpire => "host", Role.LeadUmpire => "lead", Role.Umpire => "umpire", _ => "observer" });
        return t;
    }

    DynValue FindHandle(string idOrGuid)
    {
        if (idOrGuid.Length == 0) return DynValue.Nil;
        var p = _s.State.Find(Layers.Umpire, idOrGuid) ?? _s.State.FindByGuid(Layers.Umpire, idOrGuid);
        return p == null ? DynValue.Nil : Handle(p.Id);
    }

    DynValue Pieces(DynValue filter)
    {
        string? def = null, tag = null, name = null;
        if (filter.Type == DataType.Table)
        {
            def = filter.Table.Get("def").CastToString();
            tag = filter.Table.Get("tag").CastToString();
            name = filter.Table.Get("name").CastToString();
        }
        var t = T();
        foreach (var p in _s.State.Layer(Layers.Umpire))
        {
            if (def != null && p.Def != def) continue;
            if (name != null && !string.Equals(p.Label, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (tag != null && !TagsOf(p).Contains(tag)) continue;
            t.Table.Append(Handle(p.Id));
        }
        return t;
    }

    DynValue Spawn(DynValue spec)
    {
        if (spec.Type != DataType.Table) throw new ScriptRuntimeException("board.spawn needs a table: { def = ..., x = ..., z = ... }");
        var s = spec.Table;
        var def = s.Get("def").CastToString() ?? throw new ScriptRuntimeException("board.spawn: def is required");
        var sp = new SpawnPiece
        {
            Layer = Layers.Umpire, Def = def,
            Pos = new Vec2(s.Get("x").CastToNumber() ?? 0, s.Get("z").CastToNumber() ?? 0),
            RotDeg = s.Get("rot").CastToNumber() ?? 0, Label = s.Get("label").CastToString(),
            Scale = s.Get("scale").CastToNumber() ?? 1, Y = s.Get("y").CastToNumber() ?? 0, Guid = s.Get("guid").CastToString(),
            ScriptState = s.Get("script_state").CastToString(),
            Tags = s.Get("tags").Type == DataType.Table ? Strings(s.Get("tags")) : null, Memo = s.Get("memo").CastToString(),
        };
        var taken = _s.State.Layer(Layers.Umpire).Select(p => p.Guid).OfType<string>().ToHashSet();
        if (sp.Guid == null || taken.Contains(sp.Guid)) sp.Guid = PlayerActions.FreshGuid(taken);
        Emit(sp);
        var id = sp.Id!;
        if (s.Get("state").Type == DataType.Number) Emit(new SetState { Layer = Layers.Umpire, Id = id, State = (int)s.Get("state").Number - 1 });
        if (s.Get("script").Type == DataType.String && s.Get("script").String != (_s.Store?.Def(def)?.Script ?? "")) Emit(new SetScript { Layer = Layers.Umpire, Id = id, Text = s.Get("script").String });
        if (s.Get("note").Type == DataType.String) Emit(new SetNote { Layer = Layers.Umpire, Id = id, Value = s.Get("note").String });
        return Handle(id);
    }

    internal List<string> TagsOf(PieceInstance p) => p.Tags ?? _s.Store?.Def(p.Def)?.Tags ?? new();

    internal Vec2? PosOf(DynValue v)
    {
        if (v.Type != DataType.Table) return null;
        var id = v.Table.Get("id");
        if (id.Type == DataType.String) return Find(id.String)?.Pos;
        var x = v.Table.Get("x");
        var z = v.Table.Get("z");
        return x.Type == DataType.Number && z.Type == DataType.Number ? new Vec2(x.Number, z.Number) : null;
    }

    DynValue Distance(DynValue a, DynValue b)
    {
        var pa = PosOf(a);
        var pb = PosOf(b);
        if (pa == null || pb == null) return DynValue.Nil;
        var d = (pa.Value - pb.Value).Length;
        return N(_s.State.Calibration.UnitsPerMeter is { } u and > 0 ? d / u : d);
    }

    DynValue Zones()
    {
        var t = T();
        foreach (var z in _s.State.Zones)
        {
            var e = T();
            e.Table["id"] = S(z.Id);
            e.Table["name"] = S(z.Name);
            e.Table["owner"] = S(z.Owner);
            var zone = z;
            e.Table["contains"] = Fn(a => { var p = PosOf(Arg(a, 0)); return B(p != null && zone.Contains(p.Value)); });
            t.Table.Append(e);
        }
        return t;
    }

    DynValue Handle(string id) => _handles.Handle(id);
}

internal sealed partial class PieceHandles(ScriptHost host)
{
    readonly Dictionary<string, DynValue> _handles = new();

    Script Vm => host.Vm;
    DynValue T() => DynValue.NewTable(Vm);

    public DynValue Handle(string id)
    {
        if (_handles.TryGetValue(id, out var h)) return h;
        var t = new Table(Vm);
        t["id"] = S(id);
        var meta = new Table(Vm);
        var pid = id;
        meta["__index"] = DynValue.NewCallback((_, a) => Index(pid, Arg(a, 1).CastToString() ?? ""));
        meta["__newindex"] = DynValue.NewCallback((_, _) => throw new ScriptRuntimeException("a piece's fields are read-only: use its set_ calls"));
        meta["__tostring"] = DynValue.NewCallback((_, _) => S("piece " + pid));
        t.MetaTable = meta;
        h = DynValue.NewTable(t);
        _handles[id] = h;
        return h;
    }

    public void Forget(string id) => _handles.Remove(id);

    public void Clear() => _handles.Clear();

    DynValue Index(string id, string key)
    {
        var p = host.Find(id);
        if (key == "exists") return B(p != null);
        p ??= host.FindStowed(id);
        if (p == null)
        {
            if (key.StartsWith("set_") || key is "delete" or "button" or "input" or "edit" or "flip" or "move" or "attach" or "detach" or "stow" or "unstow" or "release" or "recall" or "call")
                return Fn(_ => throw new ScriptRuntimeException($"piece {id} is gone"));
            return DynValue.Nil;
        }
        var def = host.Server.Store?.Def(p.Def);
        var found = Field(id, p, def, key) ?? DieField(p, def, key) ?? Setter(id, p, def, key) ?? MarkSetter(id, p, def, key) ?? ReadySetter(id, p, key);
        return found ?? DieSetter(id, p, def, key) ?? SmokeSetter(id, key) ?? Drawing(id, key) ?? Scripted(id, p, key) ?? DynValue.Nil;
    }

    DynValue? Field(string id, PieceInstance p, PieceDef? def, string key)
    {
        switch (key)
        {
            case "guid": return S(p.Guid ?? p.Id);
            case "memo": return S(p.Memo);
            case "gm_notes": return S(p.GmNotes ?? "");
            case "script_state": return S(p.ScriptState);
            case "def": return S(p.Def);
            case "pack": return host.Server.Store?.PackOfDef(p.Def)?.Id is { } pk ? S(pk) : DynValue.Nil;
            case "name": return p.Label == null ? DynValue.Nil : S(p.Label);
            case "note": return p.Note == null ? DynValue.Nil : S(p.Note);
            case "state": return N(p.State + 1);
            case "states": return ListTable((def?.States ?? new()).Select(st => S(st.Name)));
            case "pos": return XzTable(p.Pos.X, p.Pos.Z);
            case "rot": return N(p.RotDeg);
            case "y": return N(p.Y);
            case "scale": return N(p.Scale);
            case "size":
            {
                var sz = def?.Size ?? new Size2(1, 1);
                if (def != null && p.State >= 0 && p.State < def.States.Count && def.States[p.State].Size is { } ss) sz = ss;
                var t = T();
                t.Table["w"] = N(sz.W * p.Scale);
                t.Table["h"] = N(sz.H * p.Scale);
                return t;
            }
            case "middle":
            {
                var m = host.Server.Store?.MiddleOnMap(p) ?? p.Pos;
                return XzTable(m.X, m.Z);
            }
            case "picture_size": return PictureSize(p, def);
            case "stowed": return StowedList(p);
            case "ready": return p.ReadyNow is { } r && ReadyPiece.Of(p, x => host.Find(x)) is Readiness.In or Readiness.Out ? Handle(r.Id) : DynValue.Nil;
            case "local_unit":
            {
                if (def?.LocalUnit is not { } lu) return DynValue.Nil;
                var t = T();
                t.Table["x"] = N(lu.W);
                t.Table["z"] = N(lu.H);
                return t;
            }
            case "shape":
            {
                var sh = def != null && p.State > 0 && p.State < def.States.Count ? def.States[p.State].Shape : null;
                return S(sh ?? def?.Shape ?? "box");
            }
            case "face_down": return B(p.FaceDown);
            case "tilt_x": return N(p.TiltX);
            case "tilt_z": return N(p.TiltZ);
            case "model": return B(def?.Kind == "model");
            case "holds": return B(p.HoldsPieces(def) || def?.Tags.Contains("bag") == true);
            case "locked": return B(p.Locked);
            case "sticky": return B(!p.NotSticky);
            case "box_selected": return B(!p.NotBoxSelected);
            case "measures_moves": return B(p.MeasuresMoves);
            case "interactable": return B(!p.NotInteractable);
            case "source": return def?.ModuleType != null ? SourceTable(def, p) : DynValue.Nil;
            case "held_by": return host.Every.HeldBy.TryGetValue(id, out var holder) ? holder : DynValue.Nil;
            case "highlight": return p.Highlight is { } hl ? S(hl) : DynValue.Nil;
            case "book_highlight": return p.BookHighlight is { Length: SetBookHighlight.Corners } bx ? BoxTable(bx) : DynValue.Nil;
            case "effect_on": return B(!p.EffectOff);
            case "effect_smoke": return def?.Effect is { } fx ? N(p.Smoke ?? fx.Density) : DynValue.Nil;
            case "tint": return p.TintOverride is { } ti ? S(ti) : def != null ? S(def.Tint) : DynValue.Nil;
            case "counters":
            {
                var t = T();
                foreach (var c in def?.Counters ?? new()) t.Table[c.Name] = N(p.Counters.TryGetValue(c.Name, out var v) ? v : c.Default);
                foreach (var (k, v) in p.Counters) t.Table[k] = N(v);
                return t;
            }
            case "text":
            {
                var t = T();
                foreach (var (k, v) in p.Text) t.Table[k] = S(v);
                return t;
            }
            case "attached": return ListTable(p.Attached.Select(a => Handle(a)));
            case "tags": return ListTable(host.TagsOf(p).Select(tg => S(tg)));
            case "script": return (def?.ScriptFor(p) ?? p.Script) is { } sc ? S(sc) : DynValue.Nil;
            case "ui": return host.UiTableFor(id);
            case "lines": return ListTable(p.Lines.Select(ln => LineTable(ln)));
            case "decals": return ListTable(DecalDef.ShownOn(p, def).Select(d => DecalTable(d)));
            case "to_world": return Fn(a => XzTable(ToWorld(p, Arg(a, 0).CastToNumber() ?? 0, Arg(a, 1).CastToNumber() ?? 0)));
            case "to_local": return Fn(a => XzTable(ToLocal(p, Arg(a, 0).CastToNumber() ?? 0, Arg(a, 1).CastToNumber() ?? 0)));
            default: return null;
        }
    }

    DynValue PictureSize(PieceInstance p, PieceDef? def)
    {
        var store = host.Server.Store;
        var shown = p.Picture is { } other ? store?.Def(other) : def;
        if (shown == null || store?.PackOfDef(shown.Id) is not { } pack) return DynValue.Nil;
        var picture = (p.State > 0 && p.State < shown.States.Count ? shown.States[p.State].Image : null) ?? shown.Image;
        if (picture == null || !pack.Assets.TryGetValue(picture, out var a) || a.W <= 0 || a.H <= 0) return DynValue.Nil;
        var t = T();
        t.Table["w"] = N(a.W);
        t.Table["h"] = N(a.H);
        return t;
    }

    DynValue ListTable(IEnumerable<DynValue> items)
    {
        var t = T();
        foreach (var item in items) t.Table.Append(item);
        return t;
    }

    DynValue XzTable(Vec2 at) => XzTable(at.X, at.Z);

    DynValue XzTable(double x, double z)
    {
        var t = T();
        t.Table["x"] = N(x);
        t.Table["z"] = N(z);
        return t;
    }

    DynValue? Setter(string id, PieceInstance p, PieceDef? def, string key)
    {
        const string L = Layers.Umpire;
        switch (key)
        {
            case "set_lines": return Fn(a =>
            {
                var lines = new List<LineDef>();
                if (Arg(a, 0).Type == DataType.Table)
                    foreach (var v in Arg(a, 0).Table.Values) if (v.Type == DataType.Table && ParseLine(v.Table) is { } ln) lines.Add(ln);
                host.Emit(new SetLines { Layer = L, Id = id, Lines = lines });
                return DynValue.Nil;
            });
            case "set_decals": return Fn(a =>
            {
                var decals = new List<DecalDef>();
                if (Arg(a, 0).Type == DataType.Table)
                    foreach (var v in Arg(a, 0).Table.Values) if (v.Type == DataType.Table && ParseDecal(v.Table) is { } d) decals.Add(d);
                if (decals.Count > DecalDef.MostOnAPiece) throw new ScriptRuntimeException($"set_decals: a piece carries at most {DecalDef.MostOnAPiece} decals");
                if (decals.FirstOrDefault(d => !PictureInGame(d.Picture)) is { } unknown) throw new ScriptRuntimeException($"set_decals: no picture '{unknown.Picture}' in this game's packs");
                bool looksCarryDecals = def != null && def.States.Any(s => s.Decals is { Count: > 0 });
                if (looksCarryDecals && host.Loading) return DynValue.Nil;
                host.Emit(new SetDecals { Layer = L, Id = id, Decals = decals, OnState = looksCarryDecals ? p.State : -1 });
                return DynValue.Nil;
            });
            case "set_picture": return Fn(a =>
            {
                var d = Arg(a, 0).IsNil() ? null : Arg(a, 0).CastToString();
                if (!string.IsNullOrEmpty(d) && host.Server.Store != null && host.Server.Store.Def(d) == null) throw new ScriptRuntimeException($"set_picture: no piece definition '{d}' in this game's packs");
                host.Emit(new SetPicture { Layer = L, Id = id, Picture = d });
                return DynValue.Nil;
            });

            case "set_def": return Fn(a =>
            {
                var d = Arg(a, 0).CastToString() ?? "";
                if (host.Server.Store != null && host.Server.Store.Def(d) == null) throw new ScriptRuntimeException($"set_def: no piece definition '{d}' in this game's packs");
                host.Emit(new SetDef { Layer = L, Id = id, Def = d, SwapsScript = Arg(a, 1).CastToBool(), Script = Arg(a, 1).CastToBool() ? host.CodeFor(p) : null });
                return DynValue.Nil;
            });

            case "set_name": return Fn(a => { host.Emit(new SetLabel { Layer = L, Id = id, Value = Arg(a, 0).IsNil() ? null : Arg(a, 0).CastToString() }); return DynValue.Nil; });
            case "set_note": return Fn(a => { host.Emit(new SetNote { Layer = L, Id = id, Value = Arg(a, 0).IsNil() ? null : Arg(a, 0).CastToString() }); return DynValue.Nil; });
            case "set_tags": return Fn(a => { host.Emit(new SetTags { Layer = L, Id = id, Tags = Arg(a, 0).IsNil() ? null : Strings(Arg(a, 0)) }); return DynValue.Nil; });
            case "add_tag": return Fn(a =>
            {
                var t = host.TagsOf(p).ToList();
                var tag = Arg(a, 0).CastToString() ?? "";
                if (tag.Length > 0 && !t.Contains(tag)) { t.Add(tag); host.Emit(new SetTags { Layer = L, Id = id, Tags = t }); }
                return DynValue.Nil;
            });
            case "remove_tag": return Fn(a =>
            {
                var t = host.TagsOf(p).ToList();
                if (t.Remove(Arg(a, 0).CastToString() ?? "")) host.Emit(new SetTags { Layer = L, Id = id, Tags = t });
                return DynValue.Nil;
            });
            case "set_memo": return Fn(a => { host.Emit(new SetMemo { Layer = L, Id = id, Memo = Arg(a, 0).IsNil() ? "" : Arg(a, 0).CastToString() ?? "" }); return DynValue.Nil; });
            case "set_script_state": return Fn(a => { host.Emit(new SetScriptState { Layer = L, Id = id, State = Arg(a, 0).IsNil() ? "" : Arg(a, 0).CastToString() ?? "" }); return DynValue.Nil; });
            case "set_state": return Fn(a =>
            {
                var v = Arg(a, 0);
                int n = v.Type == DataType.String ? (def?.States.FindIndex(st => string.Equals(st.Name, v.String, StringComparison.OrdinalIgnoreCase)) ?? -1) : (int)(v.CastToNumber() ?? 1) - 1;
                if (n < 0) throw new ScriptRuntimeException($"set_state: no state '{v.CastToString()}'");
                host.Emit(new SetState { Layer = L, Id = id, State = n });
                return DynValue.Nil;
            });
            case "set_pos": return Fn(a => { host.Emit(new MovePiece { Layer = L, Id = id, Pos = Place(a, "set_pos", p.Pos).at, RotDeg = p.RotDeg, Stack = p.Stack, Y = p.Y }); return DynValue.Nil; });
            case "set_rot": return Fn(a => { host.Emit(new MovePiece { Layer = L, Id = id, Pos = p.Pos, RotDeg = Arg(a, 0).CastToNumber() ?? p.RotDeg, Stack = p.Stack, Y = p.Y }); return DynValue.Nil; });
            case "move": return Fn(a =>
            {
                var (at, next) = Place(a, "move", p.Pos);
                host.Emit(new MovePiece { Layer = L, Id = id, Pos = at, RotDeg = Number(Arg(a, next), "move", p.RotDeg), Stack = p.Stack, Y = p.Y });
                return DynValue.Nil;
            });
            case "set_scale": return Fn(a => { host.Emit(new SetScale { Layer = L, Id = id, Scale = Arg(a, 0).CastToNumber() ?? 1 }); return DynValue.Nil; });
            case "set_tilt": return Fn(a => { host.Emit(new TiltPiece { Layer = L, Id = id, X = Number(Arg(a, 0), "set_tilt", 0), Z = Number(Arg(a, 1), "set_tilt", 0) }); return DynValue.Nil; });
            case "flip": return Fn(a => { host.Emit(new FlipPiece { Layer = L, Id = id, FaceDown = Arg(a, 0).IsNil() ? !p.FaceDown : Arg(a, 0).CastToBool() }); return DynValue.Nil; });
            case "set_tint": return Fn(a => { host.Emit(new SetTint { Layer = L, Id = id, Value = Arg(a, 0).IsNil() ? null : ColorOf(Arg(a, 0)) }); return DynValue.Nil; });
            case "set_locked": return Fn(a => { host.Emit(new SetLocked { Layer = L, Id = id, Value = Arg(a, 0).IsNil() || Arg(a, 0).CastToBool() }); return DynValue.Nil; });
            case "set_box_selected": return Option(id, PieceOptions.NotBoxSelected, inverted: true);
            case "set_measures_moves": return Option(id, PieceOptions.MeasuresMoves, inverted: false);
            case "set_interactable": return Option(id, PieceOptions.NotInteractable, inverted: true);
            case "set_effect_on": return Option(id, PieceOptions.EffectOff, inverted: true);
            case "set_sticky": return Fn(a =>
            {
                host.Emit(new SetPieceOption { Layer = L, Id = id, Name = PieceOptions.NotSticky, Value = !(Arg(a, 0).IsNil() || Arg(a, 0).CastToBool()) });
                return DynValue.Nil;
            });
            case "set_counter" : return Fn(a => { host.Emit(new SetCounter { Layer = L, Id = id, Name = Arg(a, 0).CastToString()
                ?? "", Value = (int)Math.Round(Arg(a, 1).CastToNumber() ?? 0) }); return DynValue.Nil; });
            case "set_text": return Fn(a => { host.Emit(new SetText { Layer = L, Id = id, Name = Arg(a, 0).CastToString() ?? "", Value = Arg(a, 1).CastToString() ?? "" }); return DynValue.Nil; });
            case "attach": return Fn(a => { host.Emit(new Attach { Layer = L, Parent = id, Child = IdOf(Arg(a, 0)) }); return DynValue.Nil; });
            case "detach": return Fn(_ => { host.Emit(new Detach { Layer = L, Child = id }); return DynValue.Nil; });
            case "stow": return Fn(a => { host.Emit(new Stow { Layer = L, Holder = id, Id = IdOf(Arg(a, 0)) }); return DynValue.Nil; });
            case "unstow": return Fn(a => TakeOut(id, p, Arg(a, 0), Arg(a, 1), Arg(a, 2)));
            case "delete": return Fn(_ => { host.Emit(new DeletePiece { Layer = L, Id = id }); return DynValue.Nil; });
            default: return null;
        }
    }

    static readonly Dictionary<string, string> LinkNames = new(StringComparer.Ordinal)
    {
        ["ImageURL"] = "image", ["ImageSecondaryURL"] = "image_secondary", ["MeshURL"] = "mesh", ["DiffuseURL"] = "diffuse", ["NormalURL"] = "normal", ["AssetbundleURL"] = "assetbundle",
    };

    DynValue SourceTable(PieceDef def, PieceInstance? p = null)
    {
        var t = T();
        t.Table["type"] = S(def.ModuleType!);
        if (def.ModuleTop is { } top) t.Table["top"] = N(top);
        if (def.ModuleOrigin is { } origin) t.Table["origin"] = N(origin);
        if (def.ModuleRise is { } rise) t.Table["rise"] = N(rise);
        if (def.ModuleObjectTag is { } tag) t.Table["tag"] = S(tag);
        t.Table["tags"] = ListTable((def.ModuleTags ?? new()).Select(tg => S(tg)));
        var links = T();
        foreach (var (k, v) in def.Sources ?? new()) links.Table[LinkNames.GetValueOrDefault(k, k)] = S(v);
        t.Table["links"] = links;
        if (def.States.Count > 1) t.Table["states"] = StateSources(def, p);
        return t;
    }

    DynValue Highlight(string id, DynValue color)
    {
        host.Emit(new SetHighlight { Layer = Layers.Umpire, Id = id, Color = color.IsNil() ? null : ColorOf(color) });
        return DynValue.Nil;
    }

    DynValue? ReadySetter(string id, PieceInstance p, string key)
    {
        const string L = Layers.Umpire;
        return key switch
        {
            "set_ready" => Fn(a =>
            {
                var which = Arg(a, 0).IsNil() ? null : IdOf(Arg(a, 0));
                var held = which != null && p.Stowed?.Any(s => s.Id == which) == true;
                host.Emit(which == null ? new SetReady { Layer = L, Holder = id } : new SetReady { Layer = L, Holder = id, Ready = new ReadyPiece { Id = which }, Move = held ? which : null });
                return DynValue.Nil;
            }),
            "release" => Fn(_ => Ready(id, p, Readiness.In, new ReleaseReady { Layer = L, Holder = id })),
            "recall" => Fn(_ => Ready(id, p, Readiness.Out, new RecallReady { Layer = L, Holder = id })),
            _ => null,
        };
    }

    DynValue Ready(string id, PieceInstance holder, Readiness needed, Command c)
    {
        if (ReadyPiece.Of(holder, x => host.Find(x)) != needed) return B(false);
        host.Emit(c);
        return B(true);
    }

    DynValue StowedList(PieceInstance holder)
    {
        var list = T();
        int n = 0;
        foreach (var s in holder.Loose())
        {
            var e = T();
            e.Table["id"] = S(s.Id);
            e.Table["name"] = S(s.Label ?? host.Server.Store?.Def(s.Def)?.Name ?? "");
            e.Table["guid"] = S(s.Guid ?? s.Id);
            e.Table["def"] = S(s.Def);
            e.Table["held"] = N(s.Stowed?.Count ?? 0);
            (e.Table["note"], e.Table["gm_notes"], e.Table["memo"], e.Table["script_state"]) = (S(s.Note ?? ""), S(s.GmNotes ?? ""), S(s.Memo), S(s.ScriptState));
            e.Table["tags"] = ListTable((s.Tags ?? []).Select(tg => S(tg)));
            e.Table["script"] = S(host.CodeFor(s) ?? "");
            list.Table[++n] = e;
        }
        return list;
    }

    DynValue? SmokeSetter(string id, string key) => key != "set_effect_smoke" ? null : Fn(a =>
    {
        host.Emit(new SetSmoke { Layer = Layers.Umpire, Id = id, Value = Arg(a, 0).CastToNumber() is { } v ? Math.Clamp(v, SmokeDensity.VeryFine, SmokeDensity.VeryDense) : null });
        return DynValue.Nil;
    });

    DynValue Option(string id, string option, bool inverted) => Fn(a =>
    {
        bool on = Arg(a, 0).IsNil() || Arg(a, 0).CastToBool();
        host.Emit(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = option, Value = inverted ? !on : on });
        return DynValue.Nil;
    });

    DynValue TakeOut(string id, PieceInstance holder, DynValue which, DynValue place, DynValue rot)
    {
        const string L = Layers.Umpire;
        var stowed = holder.Stowed ?? [];
        var target = which.Type switch
        {
            DataType.Number => holder.Loose().ElementAtOrDefault((int)which.Number - 1),
            DataType.String => stowed.FirstOrDefault(s => s.Id == which.String || s.Guid == which.String),
            DataType.Table => stowed.FirstOrDefault(s => s.Id == IdOf(which)),
            _ => holder.NextOut(Random.Shared),
        };
        if (target == null) return DynValue.Nil;
        var cmd = new Unstow { Layer = L, Holder = id, Id = target.Id };
        if (place.Type == DataType.Table)
        {
            var at = place.Table.Get("id").Type == DataType.String ? host.Find(IdOf(place))?.Pos : null;
            cmd.Pos = at ?? new Vec2(place.Table.Get("x").CastToNumber() ?? 0, place.Table.Get("z").CastToNumber() ?? 0);
        }
        if (rot.Type == DataType.Number) cmd.RotDeg = rot.Number;
        host.Emit(cmd);
        return Handle(target.Id);
    }

    DynValue? Drawing(string id, string key)
    {
        switch (key)
        {
            case "button": return Fn(a => AddWidget(id, "button", Arg(a, 0)));
            case "input": return Fn(a => AddWidget(id, "input", Arg(a, 0)));
            case "edit": return Fn(a =>
            {
                var ws = host.Staging(id);
                int i = (int)(Arg(a, 0).CastToNumber() ?? 0);
                if (i < 1 || i > ws.Count) throw new ScriptRuntimeException($"edit: no widget {i}");
                if (Arg(a, 1).Type == DataType.Table) Fill(ws[i - 1], Arg(a, 1).Table);
                host.Drawn(id, menu: false);
                return DynValue.Nil;
            });
            case "remove_widget": return Fn(a =>
            {
                var ws = host.Staging(id);
                int i = (int)(Arg(a, 0).CastToNumber() ?? 0);
                if (i >= 1 && i <= ws.Count) { ws.RemoveAt(i - 1); host.Drawn(id, menu: false); }
                return DynValue.Nil;
            });
            case "clear_widgets": return Fn(_ =>
            {
                host.Staging(id).Clear();
                host.Drawn(id, menu: false);
                return DynValue.Nil;
            });
            case "widgets": return Fn(_ =>
            {
                var t = T();
                foreach (var w in host.Staging(id)) t.Table.Append(WidgetTable(w));
                return t;
            });
            case "menu_add": return Fn(a =>
            {
                host.MenuStaging(id).Add(new MenuItemDef { Label = Arg(a, 0).CastToString() ?? "", OnSelect = Arg(a, 1).CastToString() });
                host.Drawn(id, menu: true);
                return DynValue.Nil;
            });
            case "menu_clear": return Fn(_ =>
            {
                host.MenuStaging(id).Clear();
                host.Drawn(id, menu: true);
                return DynValue.Nil;
            });
            default: return null;
        }
    }

    DynValue? Scripted(string id, PieceInstance p, string key)
    {
        const string L = Layers.Umpire;
        switch (key)
        {
            case "reload": return Fn(_ => { host.Respawn(id, () => host.Emit(new SetScript { Layer = L, Id = id, Text = p.Script })); return DynValue.Nil; });
            case "set_script": return Fn(a =>
            {
                if (!host.MaySetScript(id)) throw new ScriptRuntimeException("set_script: only from a click or menu handler, or on a piece this call spawned");
                var text = Arg(a, 0).IsNil() ? null : Arg(a, 0).CastToString();
                if (text != null && ScriptSafety.Inspect(text) is { Hostile: true } v) throw new ScriptRuntimeException("set_script refused: " + string.Join("; ", v.Reasons));
                host.Emit(new SetScript { Layer = L, Id = id, Text = text });
                return DynValue.Nil;
            });
            case "call": return Fn(a =>
            {
                if (!host.PieceScripts.TryGetValue(id, out var ps)) return DynValue.Nil;
                var f = ps.Env.Get(Arg(a, 0).CastToString() ?? "");
                if (f.Type != DataType.Function) return DynValue.Nil;
                var rest = Enumerable.Range(1, Math.Max(0, a.Count - 1)).Select(i => a[i]).ToArray();
                return host.Invoke(ps, f, rest) ?? DynValue.Nil;
            });
            case "get_var": return Fn(a => host.PieceScripts.TryGetValue(id, out var ps) ? ps.Env.Get(Arg(a, 0).CastToString() ?? "") : DynValue.Nil);
            case "set_var": return Fn(a => { if (host.PieceScripts.TryGetValue(id, out var ps)) ps.Env.Set(Arg(a, 0).CastToString() ?? "", Arg(a, 1)); return DynValue.Nil; });
            default: return null;
        }
    }

    (Vec2 at, int next) Place(CallbackArguments a, string fn, Vec2 keep)
    {
        var first = Arg(a, 0);
        if (first.Type == DataType.Table) return (host.PosOf(first) ?? throw new ScriptRuntimeException($"{fn}: a place is {{ x = …, z = … }} or a piece"), 1);
        return (new Vec2(Number(first, fn, keep.X), Number(Arg(a, 1), fn, keep.Z)), 2);
    }

    static double Number(DynValue v, string fn, double keep) =>
        v.IsNil() ? keep : v.CastToNumber() is double n && double.IsFinite(n) ? n : throw new ScriptRuntimeException($"{fn}: {v.Type.ToLuaTypeString()} is not a number");

    static Vec2 ToWorld(PieceInstance p, double lx, double lz)
    {
        double a = -(180 - p.RotDeg) * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        double wx = lx * c - lz * s, wz = lx * s + lz * c;
        return new Vec2(p.Pos.X + wx, p.Pos.Z - wz);
    }

    static Vec2 ToLocal(PieceInstance p, double mx, double mz)
    {
        double wx = mx - p.Pos.X, wz = -(mz - p.Pos.Z);
        double a = -(180 - p.RotDeg) * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new Vec2(wx * c + wz * s, -wx * s + wz * c);
    }

    static LineDef? ParseLine(Table t)
    {
        var ln = new LineDef();
        var pts = t.Get("points");
        if (pts.Type != DataType.Table) return null;
        foreach (var pv in pts.Table.Values)
        {
            if (pv.Type != DataType.Table) continue;
            var x = pv.Table.Get("x");
            var z = pv.Table.Get("z");
            if (x.Type != DataType.Number) x = pv.Table.Get(1);
            if (z.Type != DataType.Number) z = pv.Table.Get(2);
            if (x.Type == DataType.Number && z.Type == DataType.Number) ln.Points.Add(new Vec2(x.Number, z.Number));
        }
        if (ln.Points.Count < 2) return null;
        ln.Color = ColorOf(t.Get("color")) ?? "#FFFFFF";
        ln.Width = t.Get("width").Type == DataType.Number ? t.Get("width").Number : 0.1;
        ln.Loop = t.Get("loop").Type == DataType.Boolean && t.Get("loop").Boolean;
        return ln;
    }

    internal DynValue DrawStroke(DynValue v)
    {
        if (v.Type != DataType.Table || ParseLine(v.Table) is not { } ln || ln.Points.Count < 2) return DynValue.Nil;
        var points = new List<Vec2>(ln.Points);
        if (ln.Loop) points.Add(points[0]);
        var dr = new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = points, Color = ln.Color, Width = ln.Width } };
        host.Emit(dr);
        return S(dr.Stroke.Id);
    }

    internal DynValue Strokes() => ListTable(host.Server.State.DrawLayer(Layers.Umpire).Select(StrokeTable));

    DynValue StrokeTable(Stroke s)
    {
        var t = T();
        t.Table["id"] = S(s.Id);
        t.Table["points"] = ListTable(s.Points.Select(p => XzTable(p.X, p.Z)));
        t.Table["color"] = S(s.Color);
        t.Table["width"] = N(s.Width);
        return t;
    }

    bool PictureInGame(string picture) =>
        host.Server.Store is not { } store
        || (UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, picture) ? File.Exists(Path.Combine(store.PackageDir(UploadAsset.ReliefPackage), picture))
            : host.Server.State.Packs.Any(id => store.Pack(id)?.Assets.ContainsKey(picture) == true));

    static DecalDef? ParseDecal(Table t)
    {
        double Num(string key, double fallback) => t.Get(key).Type == DataType.Number && double.IsFinite(t.Get(key).Number) ? t.Get(key).Number : fallback;
        if (t.Get("picture").Type != DataType.String || t.Get("picture").String.Length == 0) return null;
        var d = new DecalDef
        {
            Picture = t.Get("picture").String, Name = t.Get("name").Type == DataType.String ? t.Get("name").String : "",
            Face = t.Get("face").Type == DataType.String && t.Get("face").String == "back" ? "back" : "front",
            X = Num("x", 0), Z = Num("z", 0), W = Num("w", 0.2), H = Num("h", 0.2), Rot = Num("rot", 0),
        };
        return d.W > 0 && d.H > 0 ? d : null;
    }

    DynValue DecalTable(DecalDef d)
    {
        var t = T();
        t.Table["picture"] = S(d.Picture);
        t.Table["name"] = S(d.Name);
        t.Table["face"] = S(d.Face);
        t.Table["x"] = N(d.X);
        t.Table["z"] = N(d.Z);
        t.Table["w"] = N(d.W);
        t.Table["h"] = N(d.H);
        t.Table["rot"] = N(d.Rot);
        return t;
    }

    DynValue LineTable(LineDef ln)
    {
        var t = T();
        var pts = T();
        foreach (var p in ln.Points)
        {
            var pt = T();
            pt.Table["x"] = N(p.X);
            pt.Table["z"] = N(p.Z);
            pts.Table.Append(pt);
        }
        t.Table["points"] = pts;
        t.Table["color"] = S(ln.Color);
        t.Table["width"] = N(ln.Width);
        t.Table["loop"] = B(ln.Loop);
        return t;
    }

    DynValue AddWidget(string id, string kind, DynValue spec)
    {
        var w = new Widget { Kind = kind };
        if (spec.Type == DataType.Table) Fill(w, spec.Table);
        var ws = host.Staging(id);
        ws.Add(w);
        host.Drawn(id, menu: false);
        return N(ws.Count);
    }

    static double Finite(DynValue v, string field, double fallback)
    {
        var n = v.CastToNumber() ?? fallback;
        if (!double.IsFinite(n)) throw new ScriptRuntimeException($"widget {field}: {n.ToString(CultureInfo.InvariantCulture)} is not a number a widget can have");
        return n;
    }

    static void Fill(Widget w, Table t)
    {
        foreach (var pair in t.Pairs)
        {
            if (pair.Key.Type != DataType.String) continue;
            var v = pair.Value;
            switch (pair.Key.String)
            {
                case "face":
                    w.Face = v.CastToString() == "back" ? "back" : "front";
                    break;
                case "x":
                    w.X = Finite(v, "x", 0);
                    break;
                case "z":
                    w.Z = Finite(v, "z", 0);
                    break;
                case "w":
                    w.W = Finite(v, "w", 0);
                    break;
                case "h":
                    w.H = Finite(v, "h", 0);
                    break;
                case "y":
                    w.Y = v.IsNil() ? null : Finite(v, "y", 0);
                    break;
                case "rot":
                    w.Rot = Finite(v, "rot", 0);
                    break;
                case "label":
                    w.Label = v.CastToString() ?? "";
                    break;
                case "value":
                    w.Value = v.CastToString() ?? "";
                    break;
                case "placeholder":
                    w.Placeholder = v.CastToString() ?? "";
                    break;
                case "font":
                    w.Font = Finite(v, "font", w.Font);
                    break;
                case "color":
                    w.Color = ColorOf(v);
                    break;
                case "text_color":
                    w.TextColor = ColorOf(v);
                    break;
                case "align":
                    w.Align = v.CastToString() is "left" or "right" ? v.CastToString()! : "center";
                    break;
                case "lines":
                    w.Lines = Math.Max(1, (int)(v.CastToNumber() ?? 1));
                    break;
                case "tooltip":
                    w.Tooltip = v.CastToString();
                    break;
                case "on_click":
                    w.OnClick = v.CastToString();
                    break;
                case "on_change":
                    w.OnChange = v.CastToString();
                    break;
                case "hover_color":
                    w.HoverColor = ColorOf(v);
                    break;
                case "press_color":
                    w.PressColor = ColorOf(v);
                    break;
                case "validation":
                    w.Validation = v.CastToString() is "integer" or "float" or "alphanumeric" or "username" or "name" ? v.String : null;
                    break;
                case "tab":
                    w.Tab = v.CastToString() is "next" or "spaces" or "none" ? v.String : null;
                    break;
                case "kind" when v.CastToString() is "button" or "input" or "label":
                    w.Kind = v.String;
                    break;
            }
        }
    }

    DynValue WidgetTable(Widget w)
    {
        var t = T();
        t.Table["kind"] = S(w.Kind);
        t.Table["face"] = S(w.Face);
        t.Table["x"] = N(w.X);
        t.Table["z"] = N(w.Z);
        t.Table["w"] = N(w.W);
        t.Table["h"] = N(w.H);
        t.Table["rot"] = N(w.Rot);
        if (w.Y is { } height) t.Table["y"] = N(height);
        t.Table["label"] = S(w.Label);
        t.Table["value"] = S(w.Value);
        t.Table["placeholder"] = S(w.Placeholder);
        t.Table["font"] = N(w.Font);
        if (w.Color != null) t.Table["color"] = S(w.Color);
        if (w.TextColor != null) t.Table["text_color"] = S(w.TextColor);
        t.Table["align"] = S(w.Align);
        t.Table["lines"] = N(w.Lines);
        if (w.Tooltip != null) t.Table["tooltip"] = S(w.Tooltip);
        if (w.OnClick != null) t.Table["on_click"] = S(w.OnClick);
        if (w.OnChange != null) t.Table["on_change"] = S(w.OnChange);
        return t;
    }
}
