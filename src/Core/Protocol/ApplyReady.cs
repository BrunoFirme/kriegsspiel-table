using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static partial class StateMachine
{
    const int MostReadyStates = 1000;

    static string? ApplyReadyCommand(GameState g, Command c, ref Command? inverse) => c switch
    {
        SetReady sr => ApplySetReady(g, sr, ref inverse),
        ReleaseReady rr => ApplyRelease(g, rr, ref inverse),
        RecallReady rc => ApplyRecall(g, rc, ref inverse),
        _ => "unknown command",
    };

    static string? ApplySetReady(GameState g, SetReady s, ref Command? inverse)
    {
        var holder = g.Find(s.Layer, s.Holder);
        if (holder == null) return "no such piece";
        if (s.ForState is < 0 or >= MostReadyStates) return "no such state";
        if (s.Ready is { } r)
        {
            if (r.Offset is { } o && !(double.IsFinite(o.X) && double.IsFinite(o.Z))) return "bad offset";
            if (!double.IsFinite(r.Turn)) return "bad turn";
            if (holder.Stowed?.Any(x => x.Id == r.Id) != true && g.Find(s.Layer, r.Id) == null) return "not held there";
        }
        var stowed = holder.Stowed;
        var moved = s.Move == null ? null : stowed?.FirstOrDefault(x => x.Id == s.Move);
        if (s.Move != null && moved == null) return "not held there";
        var was = s.ForState is int look ? holder.ReadyIn?.GetValueOrDefault(look) : holder.Ready;
        inverse = new SetReady { Layer = s.Layer, Holder = s.Holder, ForState = s.ForState, Ready = was?.Clone(), Move = s.Move, Slot = moved == null ? 0 : stowed!.IndexOf(moved) };
        if (s.ForState is not int state) holder.Ready = s.Ready?.Clone();
        else if (s.Ready is { } mark) (holder.ReadyIn ??= new())[state] = mark.Clone();
        else if (holder.ReadyIn?.Remove(state) == true && holder.ReadyIn.Count == 0) holder.ReadyIn = null;
        if (moved == null) return null;
        stowed!.Remove(moved);
        stowed.Insert(Math.Clamp(s.Slot, 0, stowed.Count), moved);
        return null;
    }

    static string? ApplyRelease(GameState g, ReleaseReady r, ref Command? inverse)
    {
        var holder = g.Find(r.Layer, r.Holder);
        if (holder?.ReadyNow is not { } ready) return "nothing ready there";
        var state = ReadyPiece.Of(holder, id => g.Find(r.Layer, id));
        if (state != Readiness.In) return state == Readiness.Out ? "the ready piece is out" : "the ready piece is gone";
        var (pos, rot) = ready.OutBeside(holder);
        return ApplyUnstow(g, new Unstow { Layer = r.Layer, Holder = r.Holder, Id = ready.Id, Pos = pos, RotDeg = rot }, ref inverse);
    }

    static string? ApplyRecall(GameState g, RecallReady r, ref Command? inverse)
    {
        var holder = g.Find(r.Layer, r.Holder);
        if (holder?.ReadyNow is not { } ready) return "nothing ready there";
        var p = g.Find(r.Layer, ready.Id);
        if (p == null) return ReadyPiece.Of(holder, _ => null) == Readiness.In ? "the ready piece is in" : "the ready piece is gone";
        var kept = new ReadyPiece { Id = ready.Id, Offset = ReadyPiece.Turned(p.Pos - holder.Pos, -holder.RotDeg), Turn = Norm(p.RotDeg - holder.RotDeg) };
        Command? back = null;
        var err = ApplyStow(g, new Stow { Layer = r.Layer, Holder = r.Holder, Id = ready.Id, Slot = 0 }, ref back);
        if (err != null) return err;
        ReadyPiece.KeepNow(holder, kept);
        int? look = holder.ReadyIn?.ContainsKey(holder.State) == true ? holder.State : null;
        inverse = new Batch { Items = new() { back!, new SetReady { Layer = r.Layer, Holder = r.Holder, ForState = look, Ready = ready } } };
        return null;
    }

    static Action LeavingLook(GameState g, string layer, PieceInstance holder, int to)
    {
        var now = holder.ReadyNow;
        if (ReferenceEquals(now, holder.ReadyFor(to))) return () => { };
        Readiness Now() => ReadyPiece.Of(holder, id => g.Find(layer, id));
        Command? ignored = null;
        if (now != null && Now() == Readiness.Out && ApplyRecall(g, new RecallReady { Layer = layer, Holder = holder.Id }, ref ignored) == null) holder.ReadyNow!.WasOut = true;
        else if (now != null) now.WasOut = false;
        return () =>
        {
            if (holder.ReadyNow is not { WasOut: true } next) { HookIfOut(g, layer, holder); return; }
            next.WasOut = false;
            if (Now() == Readiness.In) ApplyRelease(g, new ReleaseReady { Layer = layer, Holder = holder.Id }, ref ignored);
            HookIfOut(g, layer, holder);
        };
    }

    static void HookIfOut(GameState g, string layer, PieceInstance holder)
    {
        if (holder.ReadyNow is { } r && g.Find(layer, r.Id) is { } p) HookReady(g, layer, holder, p);
    }

    const string RidesItsHolder = "a ready piece rides its holder";

    static PieceInstance? ReadyHolderOf(GameState g, string layer, string id) =>
        g.Layer(layer).FirstOrDefault(h => h.ReadyNow?.Id == id && h.Stowed?.Any(s => s.Id == id) != true);

    static void HookReady(GameState g, string layer, PieceInstance holder, PieceInstance piece)
    {
        if (holder.ReadyNow?.Id != piece.Id || Carries(g, layer, piece, holder.Id)) return;
        foreach (var o in g.Layer(layer)) o.Attached?.Remove(piece.Id);
        (holder.Attached ??= new()).Add(piece.Id);
    }

    public static void HookReadyPieces(GameState g)
    {
        foreach (var (name, layer) in g.Layers)
            foreach (var h in layer.ToList())
                if (h.ReadyNow is { } r && h.Attached?.Contains(r.Id) != true && g.Find(name, r.Id) is { } p) HookReady(g, name, h, p);
    }
}
