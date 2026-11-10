using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static partial class StateMachine
{
    public static string? Apply(GameState g, Command c, out Command? inverse)
    {
        inverse = null;
        switch (c)
        {
            case SpawnPiece s: return ApplySpawn(g, s, ref inverse);
            case RestorePiece r: return ApplyRestore(g, r, ref inverse);
            case MovePiece m: return ApplyMove(g, m, ref inverse);
            case DeletePiece d: return ApplyDelete(g, d, ref inverse);
            case SetState st: return ApplySetState(g, st, ref inverse);
            case TurnDie or SetDieFaces or SetWorth or SetFacePicture or SetLettering: return ApplyDie(g, c, ref inverse);
            case SetCounter sc: return ApplySetCounter(g, sc, ref inverse);
            case SetText t: return ApplySetText(g, t, ref inverse);
            case SetLabel l: return ApplySetLabel(g, l, ref inverse);
            case SetNote nt: return ApplySetNote(g, nt, ref inverse);
            case RestoreTurn rt: return ApplyRestoreTurn(g, rt);
            case LoadSave ls: return ApplyLoadSave(g, ls);
            case SetTint ti: return ApplySetTint(g, ti, ref inverse);
            case SetLocked lk: return ApplySetLocked(g, lk, ref inverse);
            case SetHighlight hl: return ApplySetHighlight(g, hl, ref inverse);
            case SetBookHighlight bh: return ApplySetBookHighlight(g, bh, ref inverse);
            case SetWebModel wm: return ApplySetWebModel(g, wm, ref inverse);
            case SetPieceOption po: return ApplySetPieceOption(g, po, ref inverse);
            case FlipPiece fp: return ApplyFlip(g, fp, ref inverse);
            case TiltPiece tp: return ApplyTilt(g, tp, ref inverse);
            case SetScale sz: return ApplySetScale(g, sz, ref inverse);
            case Attach or Detach or Stow or Unstow: return ApplyCarrying(g, c, ref inverse);
            case SetReady or ReleaseReady or RecallReady: return ApplyReadyCommand(g, c, ref inverse);
            case AddSheet ash: return ApplyAddSheet(g, ash, ref inverse);
            case MoveSheet ms: return ApplyMoveSheet(g, ms, ref inverse);
            case SetVisionRadius sv: return ApplySetVisionRadius(g, sv, ref inverse);
            case SetBriefing sb:
                inverse = new SetBriefing { Text = g.Briefing };
                g.Briefing = string.IsNullOrWhiteSpace(sb.Text) ? null : sb.Text;
                return null;
            case SetPointerRule pr:
                inverse = new SetPointerRule { AlwaysShown = g.PointersAlwaysShown };
                g.PointersAlwaysShown = pr.AlwaysShown;
                return null;
            case SetLighting sl: return ApplySetLighting(g, sl, ref inverse);
            case SetWind sw: return ApplySetWind(g, sw, ref inverse);
            case CropSheet cs: return ApplyCropSheet(g, cs, ref inverse);
            case SetSheetRelief sr: return ApplySetSheetRelief(g, sr, ref inverse);
            case SetSheetOpacity so: return ApplySetSheetOpacity(g, so, ref inverse);
            case SetSheetPicture sp: return ApplySetSheetPicture(g, sp, ref inverse);
            case LockSheet ls: return ApplyLockSheet(g, ls, ref inverse);
            case RenameSheet rs: return ApplyRenameSheet(g, rs, ref inverse);
            case RemoveSheet rs: return ApplyRemoveSheet(g, rs, ref inverse);
            case Draw dr: return ApplyDraw(g, dr, ref inverse);
            case EraseStroke er: return ApplyEraseStroke(g, er, ref inverse);
            case StampDecal or RemoveDecal or SetDecalPalette: return ApplyDecals(g, c, ref inverse);
            case AddBoard ab: return ApplyAddBoard(g, ab, ref inverse);
            case RenameBoard nb: return ApplyRenameBoard(g, nb, ref inverse);
            case RemoveBoard rb: return ApplyRemoveBoard(g, rb, ref inverse);
            case RollDice rd: return ApplyRollDice(g, rd);
            case SetClock sc2: return ApplySetClock(g, sc2, ref inverse);
            case AdvanceClock: return ApplyAdvanceClock(g, ref inverse);
            case SetPicture sp2: return ApplySetPicture(g, sp2, ref inverse);
            case SetGrid sg: return ApplySetGrid(g, sg, ref inverse);
            case SetCalibration cal: return ApplySetCalibration(g, cal, ref inverse);
            case PublishSighting: return null;
            case AddPack ap: return ApplyAddPack(g, ap);
            case SetZone sz: return ApplySetZone(g, sz, ref inverse);
            case RemoveZone rz: return ApplyRemoveZone(g, rz, ref inverse);
            case Batch b: return ApplyBatch(g, b, ref inverse);
            case Undo:
                return "undo is resolved by the server";
            case SetScript ss: return ApplySetScript(g, ss, ref inverse);
            case SetGlobalScript gs: return ApplySetGlobalScript(g, gs, ref inverse);
            case SetScriptState st2: return ApplySetScriptState(g, st2, ref inverse);
            case SetTags tg: return ApplySetTags(g, tg, ref inverse);
            case SetMemo mm: return ApplySetMemo(g, mm, ref inverse);
            case SetGlobalScriptState gst: return ApplySetGlobalScriptState(g, gst, ref inverse);
            case SetWidgets sw: return ApplySetWidgets(g, sw, ref inverse);
            case SetMenuItems sm: return ApplySetMenuItems(g, sm, ref inverse);
            case EditWidget ew: return ApplyEditWidget(g, ew, ref inverse);
            case ClickWidget cw: return CheckClick(g, cw);
            case SetHotkeys sh: return ApplySetHotkeys(g, sh, ref inverse);
            case SetGlobalMenuItems sgm: return ApplySetGlobalMenuItems(g, sgm, ref inverse);
            case Hotkey hk:
                return hk.Index >= 1 && hk.Index <= g.Hotkeys.Count ? null : "no such hotkey";
            case PickGlobalMenuItem pgm:
                return pgm.Index >= 1 && pgm.Index <= g.MenuItems.Count ? null : "no such menu row";
            case NumberTyped nt: return Present(g, nt.Layer, nt.Id);
            case SelectPiece: return null;
            case ShowContents or PlayEffect or GlidePiece or ShowCast or ReportSelection: return CheckShown(g, c);
            case PickMenuItem pm: return Present(g, pm.Layer, pm.Id);
            case HoldPiece hp: return Present(g, hp.Layer, hp.Id);
            case Say: return null;
            case SetUi or SetUiAttributes or SetUiPictures: return ApplyUi(g, c, ref inverse);
            case UiEvent ue: return ApplyUiEvent(g, ue);
            case ShowDialog or DialogResult:
                return null;
            case SetLines sl2: return ApplySetLines(g, sl2, ref inverse);
            case SetDecals sd2: return ApplySetDecals(g, sd2, ref inverse);
            case SetDef sd: return ApplySetDef(g, sd, ref inverse);
            default:
                return "unknown command";
        }
    }

    static string? Present(GameState g, string layer, string id) => g.Find(layer, id) == null ? "no such piece" : null;

    static string? CheckShown(GameState g, Command c) => c switch
    {
        ShowContents sc => Present(g, sc.Layer, sc.Id),
        PlayEffect pe => Present(g, pe.Layer, pe.Id),
        GlidePiece gp => Present(g, gp.Layer, gp.Id),
        ShowCast cast => cast.Problem(),
        ReportSelection rs => rs.Selected is { Count: <= ReportSelection.MostPieces } ? null : "too many pieces in one selection",
        _ => null,
    };

    static string? ApplySpawn(GameState g, SpawnPiece s, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(s.Id)) s.Id = g.NewId();
        var layer = g.Layer(s.Layer);
        int stack = 0;
        foreach (var p in layer)
        {
            if (p.Id == s.Id) return "duplicate id";
            if ((p.Pos - s.Pos).Length < 0.01) stack++;
        }
        layer.Add(new PieceInstance
        {
            Id = s.Id, Def = s.Def, Pos = s.Pos, RotDeg = s.RotDeg, Label = s.Label, Scale = s.Scale, Y = Math.Max(0, s.Y),
            Stack = stack, Guid = s.Guid, ScriptState = s.ScriptState ?? "",
            Tags = CleanTags(s.Tags), Memo = s.Memo ?? "",
        });
        BumpId(g, s.Id);
        inverse = new DeletePiece { Layer = s.Layer, Id = s.Id };
        return null;
    }

    static string? ApplyRestore(GameState g, RestorePiece r, ref Command? inverse)
    {
        var layer = g.Layer(r.Layer);
        if (string.IsNullOrEmpty(r.Piece.Id)) return "no piece id";
        if (layer.Any(p => p.Id == r.Piece.Id)) return "duplicate id";
        PutBack(layer, r.Piece.Clone(), r.Index);
        BumpId(g, r.Piece.Id);
        inverse = new DeletePiece { Layer = r.Layer, Id = r.Piece.Id };
        return null;
    }

    static string? ApplyMove(GameState g, MovePiece m, ref Command? inverse)
    {
        var p = g.Find(m.Layer, m.Id);
        if (p == null) return "no such piece";
        var back = new MovePiece { Layer = m.Layer, Id = m.Id, Pos = p.Pos, RotDeg = p.RotDeg, Stack = p.Stack, Y = p.Y };
        inverse = back;
        var delta = m.Pos - p.Pos;
        double drot = m.RotDeg - p.RotDeg;
        double dy = Math.Max(0, m.Y) - p.Y;
        p.Pos = m.Pos;
        p.RotDeg = Norm(m.RotDeg);
        p.Stack = m.Stack;
        p.Y = Math.Max(0, m.Y);
        foreach (var cid in p.Attached ?? [])
        {
            var ch = cid == null ? null : g.Find(m.Layer, cid);
            if (ch == null) continue;
            var off = ch.Pos - (p.Pos - delta);
            var rad = -drot * Math.PI / 180.0;
            var rot = new Vec2(off.X * Math.Cos(rad) - off.Z * Math.Sin(rad), off.X * Math.Sin(rad) + off.Z * Math.Cos(rad));
            if (ch.Y + dy < 0)
            {
                var undo = inverse as Batch ?? new Batch { Items = new() { back } };
                var own = (ch.Attached ?? []).Where(id => id != null && g.Find(m.Layer, id) != null).ToList();
                foreach (var id in own) undo.Items.Add(new Detach { Layer = m.Layer, Child = id });
                undo.Items.Add(new MovePiece { Layer = m.Layer, Id = ch.Id, Pos = ch.Pos, RotDeg = ch.RotDeg, Stack = ch.Stack, Y = ch.Y });
                foreach (var id in own) undo.Items.Add(new Attach { Layer = m.Layer, Parent = ch.Id, Child = id });
                inverse = undo;
            }
            ch.Pos = p.Pos + rot;
            ch.RotDeg = Norm(ch.RotDeg + drot);
            ch.Y = Math.Max(0, ch.Y + dy);
        }
        return null;
    }

    static string? ApplyDelete(GameState g, DeletePiece d, ref Command? inverse)
    {
        var layer = g.Layer(d.Layer);
        int at = layer.FindIndex(x => x.Id == d.Id);
        if (at < 0) return "no such piece";
        var p = layer[at];
        var restore = new RestorePiece { Layer = d.Layer, Piece = p.Clone(), Index = at };
        PieceInstance? holder = null;
        foreach (var x in layer)
        {
            bool holds = x == p ? x.Attached?.Contains(d.Id) == true : x.Attached?.Remove(d.Id) == true;
            if (holds) holder ??= x;
        }
        layer.RemoveAt(at);
        inverse = holder == null ? restore
            : new Batch { Items = new() { restore, new Attach { Layer = d.Layer, Parent = holder.Id, Child = d.Id } } };
        return null;
    }

    static string? ApplySetState(GameState g, SetState st, ref Command? inverse)
    {
        var p = g.Find(st.Layer, st.Id);
        if (p == null) return "no such piece";
        inverse = new SetState { Layer = st.Layer, Id = st.Id, State = p.State };
        int to = Math.Max(0, st.State);
        if (to != p.State) p.BookHighlight = null;
        if (!PieceLook.Swap(p, to, st.Leaving)) p.State = to;
        return null;
    }

    static string? ApplyTurnDie(GameState g, TurnDie td, ref Command? inverse)
    {
        var p = g.Find(td.Layer, td.Id);
        if (p == null) return "no such piece";
        if (td.Face < 0) return "no such face";
        inverse = new TurnDie { Layer = td.Layer, Id = td.Id, Face = p.ShownFace };
        p.Face = td.Face;
        return null;
    }

    static string? ApplySetDieFaces(GameState g, SetDieFaces sdf, ref Command? inverse)
    {
        var p = g.Find(sdf.Layer, sdf.Id);
        if (p == null) return "no such piece";
        if (sdf.Faces != null && sdf.Faces.Any(f => f == null || f.Up is not { Length: 3 } || f.Up.Any(x => !double.IsFinite(x)))) return "a face without a direction";
        inverse = new SetDieFaces { Layer = sdf.Layer, Id = sdf.Id, Faces = p.Faces?.Select(f => f.Clone()).ToList() };
        p.Faces = sdf.Faces?.Select(f => f.Clone()).ToList();
        return null;
    }

    static string? ApplySetCounter(GameState g, SetCounter sc, ref Command? inverse)
    {
        var p = g.Find(sc.Layer, sc.Id);
        if (p == null) return "no such piece";
        inverse = p.Counters.TryGetValue(sc.Name, out var was)
            ? new SetCounter { Layer = sc.Layer, Id = sc.Id, Name = sc.Name, Value = was }
            : new SetCounter { Layer = sc.Layer, Id = sc.Id, Name = sc.Name, Clear = true };
        if (sc.Clear) p.Counters.Remove(sc.Name);
        else p.Counters[sc.Name] = sc.Value;
        return null;
    }

    static string? ApplySetText(GameState g, SetText t, ref Command? inverse)
    {
        var p = g.Find(t.Layer, t.Id);
        if (p == null) return "no such piece";
        inverse = p.Text.TryGetValue(t.Name, out var was)
            ? new SetText { Layer = t.Layer, Id = t.Id, Name = t.Name, Value = was ?? "" }
            : new SetText { Layer = t.Layer, Id = t.Id, Name = t.Name, Clear = true };
        if (t.Clear) p.Text.Remove(t.Name);
        else p.Text[t.Name] = t.Value;
        return null;
    }

    static string? ApplySetLabel(GameState g, SetLabel l, ref Command? inverse)
    {
        var p = g.Find(l.Layer, l.Id);
        if (p == null) return "no such piece";
        inverse = new SetLabel { Layer = l.Layer, Id = l.Id, Value = p.Label };
        p.Label = l.Value;
        return null;
    }

    static string? ApplySetNote(GameState g, SetNote nt, ref Command? inverse)
    {
        var p = g.Find(nt.Layer, nt.Id);
        if (p == null) return "no such piece";
        inverse = new SetNote { Layer = nt.Layer, Id = nt.Id, Value = p.Note };
        p.Note = string.IsNullOrWhiteSpace(nt.Value) ? null : nt.Value;
        return null;
    }

    static string? ApplyRestoreTurn(GameState g, RestoreTurn rt)
    {
        if (rt.State == null) return "nothing to restore";
        g.CopyFrom(rt.State);
        return null;
    }

    static string? ApplyLoadSave(GameState g, LoadSave ls)
    {
        if (ls.State == null) return "nothing to load";
        g.CopyFrom(ls.State);
        return null;
    }

    static string? ApplySetTint(GameState g, SetTint ti, ref Command? inverse)
    {
        var p = g.Find(ti.Layer, ti.Id);
        if (p == null) return "no such piece";
        inverse = new SetTint { Layer = ti.Layer, Id = ti.Id, Value = p.TintOverride };
        p.TintOverride = ti.Value;
        return null;
    }

    static string? ApplySetHighlight(GameState g, SetHighlight hl, ref Command? inverse)
    {
        var p = g.Find(hl.Layer, hl.Id);
        if (p == null) return "no such piece";
        inverse = new SetHighlight { Layer = hl.Layer, Id = hl.Id, Color = p.Highlight };
        p.Highlight = hl.Color;
        return null;
    }

    static string? ApplySetBookHighlight(GameState g, SetBookHighlight bh, ref Command? inverse)
    {
        var p = g.Find(bh.Layer, bh.Id);
        if (p == null) return "no such piece";
        if (bh.Box is { } box && (box.Length != SetBookHighlight.Corners || box.Any(v => !double.IsFinite(v)))) return "a highlight box is four numbers: left, bottom, right, top";
        inverse = new SetBookHighlight { Layer = bh.Layer, Id = bh.Id, Box = p.BookHighlight };
        p.BookHighlight = bh.Box?.ToArray();
        return null;
    }

    static string? ApplySetWebModel(GameState g, SetWebModel wm, ref Command? inverse)
    {
        var p = g.Find(wm.Layer, wm.Id);
        if (p == null) return "no such piece";
        if (wm.Mesh is { Length: > 0 } && !UploadAsset.IsWebModel(UploadAsset.ReliefPackage, wm.Mesh)) return "a model fetched from a link is kept under models/";
        if (wm.Texture != null && !UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, wm.Texture)) return "a picture fetched from a link is kept under pictures/";
        inverse = new SetWebModel { Layer = wm.Layer, Id = wm.Id, Mesh = p.WebMesh, Texture = p.WebTexture };
        (p.WebMesh, p.WebTexture) = (wm.Mesh, wm.Texture);
        return null;
    }

    static string? ApplySetLocked(GameState g, SetLocked lk, ref Command? inverse)
    {
        var p = g.Find(lk.Layer, lk.Id);
        if (p == null) return "no such piece";
        inverse = new SetLocked { Layer = lk.Layer, Id = lk.Id, Value = p.Locked };
        p.Locked = lk.Value;
        return null;
    }

    static string? ApplySetPieceOption(GameState g, SetPieceOption po, ref Command? inverse)
    {
        var p = g.Find(po.Layer, po.Id);
        if (p == null) return "no such piece";
        bool was;
        switch (po.Name)
        {
            case PieceOptions.NoCollision:
                was = p.NoCollision;
                p.NoCollision = po.Value;
                break;
            case PieceOptions.PhysicalRoll:
                was = p.PhysicalRoll;
                p.PhysicalRoll = po.Value;
                break;
            case PieceOptions.Holds:
                inverse = new SetPieceOption { Layer = po.Layer, Id = po.Id, Name = po.Name, Value = p.Holds ?? false, Inherit = p.Holds == null };
                p.Holds = po.Inherit ? null : po.Value;
                return null;
            case PieceOptions.NotSticky:
                was = p.NotSticky;
                p.NotSticky = po.Value;
                break;
            case PieceOptions.NotBoxSelected:
                was = p.NotBoxSelected;
                p.NotBoxSelected = po.Value;
                break;
            case PieceOptions.MeasuresMoves:
                was = p.MeasuresMoves;
                p.MeasuresMoves = po.Value;
                break;
            case PieceOptions.EffectOff:
                was = p.EffectOff;
                p.EffectOff = po.Value;
                break;
            case PieceOptions.NotInteractable:
                was = p.NotInteractable;
                p.NotInteractable = po.Value;
                break;
            default: return "no such option: " + po.Name;
        }
        inverse = new SetPieceOption { Layer = po.Layer, Id = po.Id, Name = po.Name, Value = was };
        return null;
    }

    static string? ApplyFlip(GameState g, FlipPiece fp, ref Command? inverse)
    {
        var p = g.Find(fp.Layer, fp.Id);
        if (p == null) return "no such piece";
        inverse = new FlipPiece { Layer = fp.Layer, Id = fp.Id, FaceDown = p.FaceDown };
        p.FaceDown = fp.FaceDown;
        return null;
    }

    static string? ApplyTilt(GameState g, TiltPiece tp, ref Command? inverse)
    {
        var p = g.Find(tp.Layer, tp.Id);
        if (p == null) return "no such piece";
        if (!double.IsFinite(tp.X) || !double.IsFinite(tp.Z)) return "bad tilt";
        inverse = new TiltPiece { Layer = tp.Layer, Id = tp.Id, X = p.TiltX, Z = p.TiltZ };
        (p.TiltX, p.TiltZ) = (Tilt.Signed(tp.X), Tilt.Signed(tp.Z));
        return null;
    }

    static string? ApplySetScale(GameState g, SetScale sz, ref Command? inverse)
    {
        var p = g.Find(sz.Layer, sz.Id);
        if (p == null) return "no such piece";
        if (!(sz.Scale > 0.01 && sz.Scale < 100)) return "bad scale";
        inverse = new SetScale { Layer = sz.Layer, Id = sz.Id, Scale = p.Scale };
        p.Scale = sz.Scale;
        return null;
    }

    static string? ApplyAttach(GameState g, Attach a, ref Command? inverse)
    {
        var parent = g.Find(a.Layer, a.Parent);
        var child = g.Find(a.Layer, a.Child);
        if (parent == null || child == null || a.Parent == a.Child) return "no such piece";
        if (Carries(g, a.Layer, child, a.Parent)) return "a piece cannot ride on one it carries";
        var previous = g.Layer(a.Layer).FirstOrDefault(p => p.Attached?.Contains(a.Child) == true);
        foreach (var p in g.Layer(a.Layer)) p.Attached?.Remove(a.Child);
        (parent.Attached ??= new()).Add(a.Child);
        inverse = previous == null ? new Detach { Layer = a.Layer, Child = a.Child }
            : new Attach { Layer = a.Layer, Parent = previous.Id, Child = a.Child };
        return null;
    }

    static bool Carries(GameState g, string layer, PieceInstance carrier, string id)
    {
        var seen = new HashSet<string>();
        var todo = new Stack<PieceInstance>();
        todo.Push(carrier);
        while (todo.Count > 0)
            foreach (var cid in todo.Pop().Attached ?? [])
            {
                if (cid == id) return true;
                if (cid != null && seen.Add(cid) && g.Find(layer, cid) is { } next) todo.Push(next);
            }
        return false;
    }

    static string? ApplyDetach(GameState g, Detach dt, ref Command? inverse)
    {
        var parent = g.Layer(dt.Layer).FirstOrDefault(p => p.Attached?.Contains(dt.Child) == true);
        if (parent == null) return "not attached";
        parent.Attached!.Remove(dt.Child);
        inverse = new Attach { Layer = dt.Layer, Parent = parent.Id, Child = dt.Child };
        return null;
    }

    static string? ApplyCarrying(GameState g, Command c, ref Command? inverse) => c switch
    {
        Attach a => ApplyAttach(g, a, ref inverse),
        Detach dt => ApplyDetach(g, dt, ref inverse),
        Stow sw => ApplyStow(g, sw, ref inverse),
        Unstow us => ApplyUnstow(g, us, ref inverse),
        _ => "unknown command",
    };

    const double TakenOutBeside = 2;

    static List<PieceInstance> Carried(GameState g, string layer, PieceInstance carrier)
    {
        var found = new List<PieceInstance>();
        var todo = new Stack<PieceInstance>();
        todo.Push(carrier);
        while (todo.Count > 0)
            foreach (var cid in todo.Pop().Attached ?? [])
                if (g.Find(layer, cid) is { } next && !found.Contains(next)) { found.Add(next); todo.Push(next); }
        return found;
    }

    static string? ApplyStow(GameState g, Stow s, ref Command? inverse)
    {
        var layer = g.Layer(s.Layer);
        var holder = g.Find(s.Layer, s.Holder);
        var p = g.Find(s.Layer, s.Id);
        if (holder == null || p == null || s.Holder == s.Id) return "no such piece";
        if (Carries(g, s.Layer, p, s.Holder)) return "a piece cannot hold one it rides on";
        int at = layer.IndexOf(p);
        var previous = layer.FirstOrDefault(o => o.Attached?.Contains(s.Id) == true);
        var carried = Carried(g, s.Layer, p);
        foreach (var o in layer) o.Attached?.Remove(s.Id);
        layer.Remove(p);
        foreach (var c in carried) layer.Remove(c);
        if (s.Guid != null) p.Guid = s.Guid;
        holder.Stowed ??= new();
        PutBack(holder.Stowed, p, s.Slot);
        holder.Stowed.AddRange(carried);
        var back = new Unstow { Layer = s.Layer, Holder = s.Holder, Id = s.Id, Pos = p.Pos, RotDeg = p.RotDeg, Index = at, KeepGuid = true };
        inverse = previous == null ? back : new Batch { Items = new() { back, new Attach { Layer = s.Layer, Parent = previous.Id, Child = s.Id } } };
        return null;
    }

    static string? ApplyUnstow(GameState g, Unstow u, ref Command? inverse)
    {
        var layer = g.Layer(u.Layer);
        var holder = g.Find(u.Layer, u.Holder);
        if (holder?.Stowed is not { Count: > 0 } stowed) return "nothing stowed there";
        var p = u.Id.Length == 0 ? stowed[0] : stowed.FirstOrDefault(x => x.Id == u.Id);
        if (p == null) return "not stowed there";
        if (layer.Any(x => x.Id == p.Id)) return "duplicate id";
        int slot = stowed.IndexOf(p);
        var carried = stowed.Where(x => x != p && CarriedWithin(p, x.Id, stowed)).ToList();
        var pos = u.Pos ?? new Vec2(holder.Pos.X + TakenOutBeside, holder.Pos.Z);
        var delta = new Vec2(pos.X - p.Pos.X, pos.Z - p.Pos.Z);
        stowed.Remove(p);
        foreach (var c in carried) stowed.Remove(c);
        if (stowed.Count == 0) holder.Stowed = null;
        p.Pos = pos;
        if (u.RotDeg is { } r) p.RotDeg = Norm(r);
        p.Y = 0;
        p.Stack = 0;
        var heldGuid = p.Guid;
        if (!u.KeepGuid && heldGuid != null && layer.Any(x => x.Guid == heldGuid)) p.Guid = FreshGuid(layer, p.Id);
        PutBack(layer, p, u.Index);
        BumpId(g, p.Id);
        foreach (var c in carried)
        {
            c.Pos = new Vec2(c.Pos.X + delta.X, c.Pos.Z + delta.Z);
            layer.Add(c);
            BumpId(g, c.Id);
        }
        inverse = new Stow { Layer = u.Layer, Holder = u.Holder, Id = p.Id, Slot = slot, Guid = p.Guid != heldGuid ? heldGuid : null };
        return null;
    }

    static bool CarriedWithin(PieceInstance carrier, string id, List<PieceInstance> among)
    {
        var seen = new HashSet<string>();
        var todo = new Stack<PieceInstance>();
        todo.Push(carrier);
        while (todo.Count > 0)
            foreach (var cid in todo.Pop().Attached ?? [])
            {
                if (cid == id) return true;
                if (seen.Add(cid) && among.FirstOrDefault(x => x.Id == cid) is { } next) todo.Push(next);
            }
        return false;
    }

    static string? ApplyAddSheet(GameState g, AddSheet ash, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(ash.Sheet.Id)) ash.Sheet.Id = g.NewId("s");
        if (g.Sheets.Any(s => s.Id == ash.Sheet.Id)) return "duplicate sheet";
        PutBack(g.Sheets, Json.Clone(ash.Sheet), ash.Index);
        BumpId(g, ash.Sheet.Id);
        inverse = new RemoveSheet { Id = ash.Sheet.Id };
        return null;
    }

    static string? ApplyMoveSheet(GameState g, MoveSheet ms, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == ms.Id);
        if (s == null) return "no such sheet";
        if (ms.Size is { } sz && (sz.W <= 0 || sz.H <= 0)) return "bad size";
        inverse = new MoveSheet { Id = s.Id, Pos = s.Pos, RotDeg = s.RotDeg, Z = s.Z, Size = s.Size };
        s.Pos = ms.Pos;
        s.RotDeg = Norm(ms.RotDeg);
        s.Z = ms.Z;
        if (ms.Size is { } ns) s.Size = ns;
        return null;
    }

    public const double MostVisionBlur = 0.5;

    static string? ApplySetVisionRadius(GameState g, SetVisionRadius sv, ref Command? inverse)
    {
        if (sv.Radius <= 0) return "radius must be positive";
        if (sv.Sharp is { } sh && !(sh >= 0 && sh <= 1)) return "sharp share out of range";
        if (sv.Blur is { } bl && !(bl >= 0 && bl <= MostVisionBlur)) return "blur out of range";
        inverse = new SetVisionRadius { Radius = g.VisionRadius, Sharp = sv.Sharp == null ? null : g.VisionSharp ?? 1, Blur = sv.Blur == null ? null : g.VisionBlur ?? 0 };
        g.VisionRadius = sv.Radius;
        if (sv.Sharp is { } kept) g.VisionSharp = kept >= 1 ? null : kept;
        if (sv.Blur is { } blur) g.VisionBlur = blur > 0 ? blur : null;
        return null;
    }

    static string? ApplySetLighting(GameState g, SetLighting sl, ref Command? inverse)
    {
        inverse = new SetLighting { Lighting = Json.Clone(g.Lighting) };
        g.Lighting = Json.Clone(sl.Lighting);
        return null;
    }

    static string? ApplySetWind(GameState g, SetWind sw, ref Command? inverse)
    {
        if (!double.IsFinite(sw.Wind.From) || !double.IsFinite(sw.Wind.Strength)) return "wind must be numbers";
        inverse = new SetWind { Wind = g.Wind.Clone() };
        g.Wind = new Wind { From = ((sw.Wind.From % 360) + 360) % 360, Strength = Math.Clamp(sw.Wind.Strength, 0, 5) };
        return null;
    }

    static string? ApplyCropSheet(GameState g, CropSheet cs, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == cs.Id);
        if (s == null) return "no such sheet";
        if (cs.Crop is { } nc && !SheetGeometry.Valid(nc)) return "bad crop";
        inverse = new CropSheet { Id = s.Id, Crop = s.Crop, Pos = s.Pos, Size = s.Size };
        if (cs.Pos is { } ep && cs.Size is { } es) { s.Pos = ep; s.Size = es; }
        else
        {
            var (centre, w, h) = SheetGeometry.Full(s);
            var (pos, size) = SheetGeometry.Cropped(centre, w, h, s.RotDeg, cs.Crop);
            s.Pos = pos;
            s.Size = size;
        }
        s.Crop = cs.Crop;
        return null;
    }

    public const double MostHeight = 20;

    static string? ApplySetSheetRelief(GameState g, SetSheetRelief sr, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == sr.Id);
        if (s == null) return "no such sheet";
        if (sr.Relief is { Length: > 0 } r && !System.Text.RegularExpressions.Regex.IsMatch(r, "^[0-9a-f]{64}$")) return "bad relief";
        if (!double.IsFinite(sr.Height) || sr.Height < 0 || sr.Height > MostHeight) return "height out of range";
        inverse = new SetSheetRelief { Id = s.Id, Relief = s.Relief, Height = s.Height };
        s.Relief = sr.Relief is { Length: > 0 } ? sr.Relief : null;
        s.Height = sr.Height;
        return null;
    }

    static string? ApplySetSheetOpacity(GameState g, SetSheetOpacity so, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == so.Id);
        if (s == null) return "no such sheet";
        if (!double.IsFinite(so.Opacity) || so.Opacity < 0 || so.Opacity > 1) return "opacity out of range";
        inverse = new SetSheetOpacity { Id = s.Id, Opacity = s.Seen };
        s.Opacity = so.Opacity >= 1 ? null : so.Opacity;
        return null;
    }

    static string? ApplySetSheetPicture(GameState g, SetSheetPicture sp, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == sp.Id);
        if (s == null) return "no such sheet";
        if (sp.Image.Length == 0 && s.Relief is not { Length: > 0 }) return "a sheet needs a picture or a relief";
        if (sp.Size is { } sz && (sz.W <= 0 || sz.H <= 0)) return "bad size";
        inverse = new SetSheetPicture { Id = s.Id, Image = s.Image, Package = s.Package, Size = s.Size, Crop = s.Crop };
        s.Image = sp.Image;
        if (sp.Package.Length > 0) s.Package = sp.Package;
        if (sp.Size is { } ns) s.Size = ns;
        s.Crop = sp.Crop;
        return null;
    }

    static string? ApplyLockSheet(GameState g, LockSheet ls, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == ls.Id);
        if (s == null) return "no such sheet";
        inverse = new LockSheet { Id = s.Id, Locked = s.Locked };
        s.Locked = ls.Locked;
        return null;
    }

    static string? ApplyRenameSheet(GameState g, RenameSheet rs, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == rs.Id);
        if (s == null) return "no such sheet";
        if (rs.Name == null) return "the sheet needs a name";
        inverse = new RenameSheet { Id = s.Id, Name = s.Name };
        s.Name = rs.Name;
        return null;
    }

    static string? ApplyRemoveSheet(GameState g, RemoveSheet rs, ref Command? inverse)
    {
        var s = g.Sheets.FirstOrDefault(x => x.Id == rs.Id);
        if (s == null) return "no such sheet";
        int at = g.Sheets.IndexOf(s);
        g.Sheets.RemoveAt(at);
        inverse = new AddSheet { Sheet = s, Index = at };
        return null;
    }

    static string? ApplyDraw(GameState g, Draw dr, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(dr.Stroke.Id)) dr.Stroke.Id = g.NewId("d");
        if (string.IsNullOrEmpty(dr.Stroke.Board) && g.Boards.Count > 0) dr.Stroke.Board = g.Boards[0].Id;
        PutBack(g.DrawLayer(dr.Layer), Json.Clone(dr.Stroke), dr.Index);
        BumpId(g, dr.Stroke.Id);
        inverse = new EraseStroke { Layer = dr.Layer, StrokeId = dr.Stroke.Id };
        return null;
    }

    static string? ApplyEraseStroke(GameState g, EraseStroke er, ref Command? inverse)
    {
        var l = g.DrawLayer(er.Layer);
        var s = l.FirstOrDefault(x => x.Id == er.StrokeId);
        if (s == null) return "no such stroke";
        int at = l.IndexOf(s);
        l.RemoveAt(at);
        inverse = new Draw { Layer = er.Layer, Stroke = s, Index = at };
        return null;
    }

    static string? ApplyDie(GameState g, Command c, ref Command? inverse) => c switch
    {
        TurnDie td => ApplyTurnDie(g, td, ref inverse),
        SetDieFaces sdf => ApplySetDieFaces(g, sdf, ref inverse),
        SetWorth sw => ApplySetWorth(g, sw, ref inverse),
        SetFacePicture sfp => ApplySetFacePicture(g, sfp, ref inverse),
        SetLettering sl => ApplySetLettering(g, sl, ref inverse),
        _ => "unknown command",
    };

    static string? ApplySetLettering(GameState g, SetLettering sl, ref Command? inverse)
    {
        var p = g.Find(sl.Layer, sl.Id);
        if (p == null) return "no such piece";
        if (sl.Lettering is { } l && (!double.IsFinite(l.Size) || l.Size <= 0 || !double.IsFinite(l.UnitsPerPixel) || l.UnitsPerPixel <= 0)) return "lettering of no size";
        inverse = new SetLettering { Layer = sl.Layer, Id = sl.Id, Lettering = p.Lettering?.Clone() };
        p.Lettering = sl.Lettering?.Clone();
        return null;
    }

    static string? ApplySetFacePicture(GameState g, SetFacePicture sfp, ref Command? inverse)
    {
        var p = g.Find(sfp.Layer, sfp.Id);
        if (p == null) return "no such piece";
        inverse = new SetFacePicture { Layer = sfp.Layer, Id = sfp.Id, Picture = p.FacePicture };
        p.FacePicture = string.IsNullOrWhiteSpace(sfp.Picture) ? null : sfp.Picture;
        return null;
    }

    static string? ApplySetWorth(GameState g, SetWorth sw, ref Command? inverse)
    {
        var p = g.Find(sw.Layer, sw.Id);
        if (p == null) return "no such piece";
        inverse = new SetWorth { Layer = sw.Layer, Id = sw.Id, Worth = p.Worth, Groups = p.WorthGroups };
        p.Worth = sw.Worth;
        p.WorthGroups = sw.Groups;
        return null;
    }

    static string? ApplyUi(GameState g, Command c, ref Command? inverse) => c switch
    {
        SetUi su => ApplySetUi(g, su, ref inverse),
        SetUiAttributes sua => ApplySetUiAttributes(g, sua, ref inverse),
        SetUiPictures sup => ApplySetUiPictures(g, sup, ref inverse),
        _ => "unknown command",
    };

    static string? ApplyDecals(GameState g, Command c, ref Command? inverse) => c switch
    {
        StampDecal st => ApplyStampDecal(g, st, ref inverse),
        RemoveDecal rd => ApplyRemoveDecal(g, rd, ref inverse),
        SetDecalPalette sp => ApplySetDecalPalette(g, sp, ref inverse),
        _ => "unknown command",
    };

    static string? ApplyStampDecal(GameState g, StampDecal st, ref Command? inverse)
    {
        var d = st.Decal;
        if (DecalProblem(d) is { } why) return why;
        var l = g.DecalLayer(st.Layer);
        if (l.Count >= PaletteDecal.MostOnTheTable) return $"the table carries at most {PaletteDecal.MostOnTheTable} decals";
        if (string.IsNullOrEmpty(d.Id)) d.Id = g.NewId("dc");
        if (l.Any(x => x.Id == d.Id)) return "a decal of that id is already there";
        var c = d.Clone();
        c.Name ??= "";
        PutBack(l, c, st.Index);
        BumpId(g, c.Id!);
        inverse = new RemoveDecal { Layer = st.Layer, DecalId = c.Id! };
        return null;
    }

    static string? ApplyRemoveDecal(GameState g, RemoveDecal rd, ref Command? inverse)
    {
        var l = g.DecalLayer(rd.Layer);
        var d = l.FirstOrDefault(x => x.Id == rd.DecalId);
        if (d == null) return "no such decal";
        int at = l.IndexOf(d);
        l.RemoveAt(at);
        inverse = new StampDecal { Layer = rd.Layer, Decal = d, Index = at };
        return null;
    }

    static string? ApplySetDecalPalette(GameState g, SetDecalPalette sp, ref Command? inverse)
    {
        if (sp.Entries == null || sp.Entries.Count > PaletteDecal.MostInAPalette) return $"a palette holds at most {PaletteDecal.MostInAPalette} decals";
        foreach (var e in sp.Entries)
            if (e == null || string.IsNullOrEmpty(e.Picture) || e.Picture.Length > LongestDecalText || (e.Name?.Length ?? 0) > LongestDecalText
                || !double.IsFinite(e.Size) || e.Size < PaletteDecal.SmallestSize || e.Size > PaletteDecal.LargestSize)
                return "a palette decal needs a picture and a size";
        inverse = new SetDecalPalette { Entries = g.DecalPalette.Select(e => e.Clone()).ToList() };
        g.DecalPalette = sp.Entries.Select(e => e.Clone()).ToList();
        foreach (var e in g.DecalPalette) e.Name ??= "";
        return null;
    }

    const int LongestDecalText = 256;

    static string? DecalProblem(DecalDef? d)
    {
        if (d == null || string.IsNullOrEmpty(d.Picture) || d.Picture.Length > LongestDecalText || (d.Name?.Length ?? 0) > LongestDecalText) return "a decal needs a picture";
        if (d.Face is not ("front" or "back")) return "a decal's face is front or back";
        if (!double.IsFinite(d.X) || !double.IsFinite(d.Z) || !double.IsFinite(d.Rot) || !(d.W > 0) || !(d.H > 0) || !double.IsFinite(d.W) || !double.IsFinite(d.H))
            return "a decal's place and size must be numbers, its size above 0";
        return null;
    }

    static string? ApplyAddBoard(GameState g, AddBoard ab, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(ab.Id)) ab.Id = g.NewId("b");
        if (g.Boards.Any(b => b.Id == ab.Id)) return "a board of that id is already there";
        PutBack(g.Boards, new BoardDef { Id = ab.Id, Name = ab.Name.Length > 0 ? ab.Name : $"Layer {g.Boards.Count + 1}" }, ab.Index);
        BumpId(g, ab.Id);
        inverse = new RemoveBoard { Id = ab.Id };
        return null;
    }

    static string? ApplyRenameBoard(GameState g, RenameBoard nb, ref Command? inverse)
    {
        var b = g.Boards.FirstOrDefault(x => x.Id == nb.Id);
        if (b == null) return "no such board";
        inverse = new RenameBoard { Id = b.Id, Name = b.Name };
        b.Name = nb.Name;
        return null;
    }

    static string? ApplyRemoveBoard(GameState g, RemoveBoard rb, ref Command? inverse)
    {
        var b = g.Boards.FirstOrDefault(x => x.Id == rb.Id);
        if (b == null) return "no such board";
        var drawn = new List<Command>();
        foreach (var (layer, l) in g.Drawings)
            if (l != null)
                for (int i = 0; i < l.Count; i++)
                    if (l[i]?.Board == rb.Id) drawn.Add(new Draw { Layer = layer, Stroke = l[i], Index = i });
        foreach (var l in g.Drawings.Values) l?.RemoveAll(s => s?.Board == rb.Id);
        Command Back(Command board) => drawn.Count == 0 ? board : new Batch { Items = new List<Command> { board }.Concat(drawn).ToList() };
        if (g.Boards.Count == 1)
        {
            inverse = Back(new RenameBoard { Id = b.Id, Name = b.Name });
            b.Name = "Layer 1";
            return null;
        }
        int at = g.Boards.IndexOf(b);
        g.Boards.RemoveAt(at);
        inverse = Back(new AddBoard { Id = b.Id, Name = b.Name, Index = at });
        return null;
    }

    static string? ApplyRollDice(GameState g, RollDice rd)
    {
        if (rd.Results == null || rd.Layer != null && (rd.PieceIds == null || rd.PieceIds.Any(id => id == null))) return "a roll without its results or its dice";
        g.Dice.Add(new DiceRoll { Seq = rd.Seq, Actor = rd.ActorName, Sides = rd.Sides, Results = rd.Results.ToList(), Label = rd.Label });
        if (g.Dice.Count > 500) g.Dice.RemoveAt(0);
        if (rd.Layer != null)
            for (int i = 0; i < rd.PieceIds.Count && i < rd.Results.Count; i++)
                if (g.Find(rd.Layer, rd.PieceIds[i]) is { } die) (die.Face, die.TiltX, die.TiltZ) = (Math.Max(0, rd.Results[i] - 1), 0, 0);
        return null;
    }

    static string? ApplySetClock(GameState g, SetClock sc2, ref Command? inverse)
    {
        inverse = new SetClock { Turn = g.Clock.Turn, GameTime = g.Clock.GameTime, IncrementMinutes = g.Clock.IncrementMinutes };
        g.Clock.Turn = sc2.Turn;
        g.Clock.GameTime = sc2.GameTime;
        g.Clock.IncrementMinutes = Math.Max(1, sc2.IncrementMinutes);
        return null;
    }

    static string? ApplyAdvanceClock(GameState g, ref Command? inverse)
    {
        if (!g.Clock.CanAdvance()) return "the clock cannot go on past the year 9999";
        inverse = new SetClock { Turn = g.Clock.Turn, GameTime = g.Clock.GameTime, IncrementMinutes = g.Clock.IncrementMinutes };
        g.Clock.Advance();
        return null;
    }

    static string? ApplySetPicture(GameState g, SetPicture sp2, ref Command? inverse)
    {
        var pic = g.Find(sp2.Layer, sp2.Id);
        if (pic == null) return "no such piece";
        inverse = new SetPicture { Layer = sp2.Layer, Id = sp2.Id, Picture = pic.Picture };
        pic.Picture = string.IsNullOrEmpty(sp2.Picture) ? null : sp2.Picture;
        return null;
    }

    static string? ApplySetGrid(GameState g, SetGrid sg, ref Command? inverse)
    {
        inverse = new SetGrid { Grid = Json.Clone(g.Grid) };
        g.Grid = Json.Clone(sg.Grid);
        return null;
    }

    static string? ApplySetCalibration(GameState g, SetCalibration cal, ref Command? inverse)
    {
        inverse = new SetCalibration { UnitsPerMeter = g.Calibration.UnitsPerMeter, Units = g.Calibration.Units };
        g.Calibration.UnitsPerMeter = cal.UnitsPerMeter;
        if (cal.Units != null) g.Calibration.Units = cal.Units;
        return null;
    }

    static string? ApplyAddPack(GameState g, AddPack ap)
    {
        if (string.IsNullOrWhiteSpace(ap.Id)) return "no pack id";
        if (!g.Packs.Contains(ap.Id)) g.Packs.Add(ap.Id);
        return null;
    }

    static string? ApplySetZone(GameState g, SetZone sz, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(sz.Zone.Id)) sz.Zone.Id = g.NewId("z");
        var old = g.Zones.FirstOrDefault(z => z.Id == sz.Zone.Id);
        var zone = Json.Clone(sz.Zone);
        inverse = old != null ? new SetZone { Zone = Json.Clone(old) } : new RemoveZone { Id = sz.Zone.Id };
        if (old != null) g.Zones[g.Zones.IndexOf(old)] = zone;
        else PutBack(g.Zones, zone, sz.Index);
        BumpId(g, sz.Zone.Id);
        return null;
    }

    static string? ApplyRemoveZone(GameState g, RemoveZone rz, ref Command? inverse)
    {
        var z = g.Zones.FirstOrDefault(x => x.Id == rz.Id);
        if (z == null) return "no such zone";
        int at = g.Zones.IndexOf(z);
        g.Zones.RemoveAt(at);
        inverse = new SetZone { Zone = z, Index = at };
        return null;
    }

    static string? ApplyBatch(GameState g, Batch b, ref Command? inverse)
    {
        var rollback = new List<Action>();
        var err = ApplyItems(g, b, rollback, out inverse);
        if (err != null) foreach (var back in rollback) back();
        return err;
    }

    static string? ApplyItems(GameState g, Batch b, List<Action> rollback, out Command? inverse)
    {
        inverse = null;
        var inverses = new List<Command>();
        foreach (var item in b.Items)
        {
            string? err;
            Command? inv = null;
            try
            {
                if (item is Batch inner) err = ApplyItems(g, inner, rollback, out inv);
                else
                {
                    var keep = Keep(g, item);
                    err = Apply(g, item, out inv);
                    if (err == null && (inv != null || keep != null))
                    {
                        var undo = inv;
                        rollback.Insert(0, () => { if (undo != null) Apply(g, undo, out _); keep?.Invoke(); });
                    }
                }
            }
            catch (Exception e) { err = $"{item?.GetType().Name} failed: {e.Message}"; inv = null; }
            if (err != null) return err;
            if (inv != null) inverses.Insert(0, inv);
        }
        inverse = inverses.Count > 0 ? new Batch { Items = inverses } : null;
        return null;
    }

    static Action? Keep(GameState g, Command? item)
    {
        switch (item)
        {
            case RollDice rd:
            {
                var history = g.Dice.ToList();
                var faces = rd.Layer == null ? new() : rd.PieceIds.Select(id => g.Find(rd.Layer, id)).Where(p => p != null).Select(p => (p!, p!.Face, p!.TiltX, p!.TiltZ)).ToList();
                return () =>
                {
                    g.Dice.Clear();
                    g.Dice.AddRange(history);
                    foreach (var (p, f, tx, tz) in faces) (p.Face, p.TiltX, p.TiltZ) = (f, tx, tz);
                };
            }
            default:
                return null;
        }
    }

    static string? ApplySetScript(GameState g, SetScript ss, ref Command? inverse)
    {
        var p = g.Find(ss.Layer, ss.Id);
        if (p == null) return "no such piece";
        inverse = new SetScript { Layer = ss.Layer, Id = ss.Id, Text = p.Script };
        p.Script = string.IsNullOrWhiteSpace(ss.Text) ? null : ss.Text;
        return null;
    }

    static string? ApplySetGlobalScript(GameState g, SetGlobalScript gs, ref Command? inverse)
    {
        inverse = new SetGlobalScript { Text = g.Script };
        g.Script = string.IsNullOrWhiteSpace(gs.Text) ? null : gs.Text;
        return null;
    }

    static string? ApplySetScriptState(GameState g, SetScriptState st2, ref Command? inverse)
    {
        var p = g.Find(st2.Layer, st2.Id);
        if (p == null) return "no such piece";
        inverse = new SetScriptState { Layer = st2.Layer, Id = st2.Id, State = p.ScriptState };
        p.ScriptState = st2.State ?? "";
        return null;
    }

    static string? ApplySetTags(GameState g, SetTags tg, ref Command? inverse)
    {
        var p = g.Find(tg.Layer, tg.Id);
        if (p == null) return "no such piece";
        inverse = new SetTags { Layer = tg.Layer, Id = tg.Id, Tags = p.Tags == null ? null : new(p.Tags) };
        p.Tags = CleanTags(tg.Tags);
        return null;
    }

    static string? ApplySetMemo(GameState g, SetMemo mm, ref Command? inverse)
    {
        var p = g.Find(mm.Layer, mm.Id);
        if (p == null) return "no such piece";
        inverse = new SetMemo { Layer = mm.Layer, Id = mm.Id, Memo = p.Memo };
        p.Memo = mm.Memo ?? "";
        return null;
    }

    static string? ApplySetGlobalScriptState(GameState g, SetGlobalScriptState gst, ref Command? inverse)
    {
        inverse = new SetGlobalScriptState { State = g.ScriptState };
        g.ScriptState = gst.State ?? "";
        return null;
    }

    static string? ApplySetWidgets(GameState g, SetWidgets sw, ref Command? inverse)
    {
        var p = g.Find(sw.Layer, sw.Id);
        if (p == null) return "no such piece";
        inverse = new SetWidgets { Layer = sw.Layer, Id = sw.Id, Widgets = p.Widgets.Select(w => w.Clone()).ToList(), OnState = Math.Max(0, p.DrawnOnState) };
        p.Widgets = sw.Widgets.Select(w => w.Clone()).ToList();
        p.DrawnOnState = sw.OnState >= 0 ? sw.OnState : p.State;
        return null;
    }

    static string? ApplySetMenuItems(GameState g, SetMenuItems sm, ref Command? inverse)
    {
        var p = g.Find(sm.Layer, sm.Id);
        if (p == null) return "no such piece";
        inverse = new SetMenuItems { Layer = sm.Layer, Id = sm.Id, Items = p.MenuItems.Select(m => m.Clone()).ToList(), OnState = Math.Max(0, p.DrawnOnState) };
        p.MenuItems = sm.Items.Select(m => m.Clone()).ToList();
        p.DrawnOnState = sm.OnState >= 0 ? sm.OnState : p.State;
        return null;
    }

    static string? CheckClick(GameState g, ClickWidget cw) =>
        g.Find(cw.Layer, cw.Id) is not { } p ? "no such piece"
        : cw.Index >= 1 && cw.Index <= p.Widgets.Count && p.Widgets[cw.Index - 1].Locked ? "that widget is locked" : null;

    static string? ApplyEditWidget(GameState g, EditWidget ew, ref Command? inverse)
    {
        var p = g.Find(ew.Layer, ew.Id);
        if (p == null) return "no such piece";
        if (ew.Index < 1 || ew.Index > p.Widgets.Count) return "no such widget";
        var w = p.Widgets[ew.Index - 1];
        if (w.Locked) return "that widget is locked";
        inverse = new EditWidget { Layer = ew.Layer, Id = ew.Id, Index = ew.Index, Value = w.Value };
        w.Value = ew.Value ?? "";
        return null;
    }

    static string? ApplySetHotkeys(GameState g, SetHotkeys sh, ref Command? inverse)
    {
        inverse = new SetHotkeys { Items = g.Hotkeys.Select(h => h.Clone()).ToList() };
        g.Hotkeys = sh.Items.Select(h => h.Clone()).ToList();
        return null;
    }

    static string? ApplySetGlobalMenuItems(GameState g, SetGlobalMenuItems sgm, ref Command? inverse)
    {
        inverse = new SetGlobalMenuItems { Items = g.MenuItems.Select(m => m.Clone()).ToList() };
        g.MenuItems = sgm.Items.Select(m => m.Clone()).ToList();
        return null;
    }

    static string? ApplySetLines(GameState g, SetLines sl2, ref Command? inverse)
    {
        var p = g.Find(sl2.Layer, sl2.Id);
        if (p == null) return "no such piece";
        inverse = new SetLines { Layer = sl2.Layer, Id = sl2.Id, Lines = p.Lines.Select(l => l.Clone()).ToList(), OnState = Math.Max(0, p.DrawnOnState) };
        p.Lines = sl2.Lines.Select(l => l.Clone()).ToList();
        p.DrawnOnState = sl2.OnState >= 0 ? sl2.OnState : p.State;
        return null;
    }

    static string? ApplySetDecals(GameState g, SetDecals sd, ref Command? inverse)
    {
        var p = g.Find(sd.Layer, sd.Id);
        if (p == null) return "no such piece";
        if (sd.Decals == null || sd.Decals.Count > DecalDef.MostOnAPiece) return $"a piece carries at most {DecalDef.MostOnAPiece} decals";
        foreach (var d in sd.Decals)
            if (DecalProblem(d) is { } why) return why;
        inverse = new SetDecals { Layer = sd.Layer, Id = sd.Id, Decals = p.Decals.Select(d => d.Clone()).ToList(), OnState = p.DecalsOnState ?? -1 };
        p.Decals = sd.Decals.Select(d =>
        {
            var c = d.Clone();
            c.Name ??= "";
            return c;
        }).ToList();
        p.DecalsOnState = sd.OnState >= 0 ? sd.OnState : null;
        return null;
    }

    static string? ApplySetDef(GameState g, SetDef sd, ref Command? inverse)
    {
        var p = g.Find(sd.Layer, sd.Id);
        if (p == null) return "no such piece";
        if (string.IsNullOrEmpty(sd.Def)) return "no definition";
        inverse = new SetDef { Layer = sd.Layer, Id = sd.Id, Def = p.Def, SwapsScript = sd.SwapsScript, Script = sd.SwapsScript ? p.Script : null };
        p.Def = sd.Def;
        if (sd.SwapsScript) p.Script = sd.Script;
        return null;
    }

    static List<string>? CleanTags(List<string>? tags) => tags?.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();

    static void PutBack<T>(List<T> list, T item, int? index)
    {
        if (index is int i && i >= 0 && i <= list.Count) list.Insert(i, item);
        else list.Add(item);
    }

    public static string FreshGuid(IEnumerable<PieceInstance> layer, string seed)
    {
        const uint Offset = 2166136261, Prime = 16777619, SixHex = 0x1000000;
        var taken = layer.Select(x => x.Guid).OfType<string>().ToHashSet();
        uint h = Offset;
        foreach (var ch in seed) h = (h ^ ch) * Prime;
        for (uint k = 0; ; k++)
        {
            var g = ((h + k) % SixHex).ToString("x6", System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(g)) return g;
        }
    }

    static void BumpId(GameState g, string id)
    {
        var dash = id.IndexOf('-');
        if (dash > 0 && long.TryParse(id[(dash + 1)..], out var n) && n >= g.NextId) g.NextId = n + 1;
    }

    static double Norm(double deg)
    {
        deg %= 360;
        if (deg < 0) deg += 360;
        return deg;
    }

    public static string? LayerOf(Command c) => c switch
    {
        LayerCommand l => l.Layer,
        Batch b => b.Items.Select(LayerOf).Distinct().Count() == 1 ? LayerOf(b.Items[0]) : "*mixed*",
        _ => null,
    };
}
