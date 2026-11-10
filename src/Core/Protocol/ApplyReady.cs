using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static partial class StateMachine
{
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
        if (s.Ready is { } r)
        {
            if (r.Offset is { } o && !(double.IsFinite(o.X) && double.IsFinite(o.Z))) return "bad offset";
            if (!double.IsFinite(r.Turn)) return "bad turn";
            if (holder.Stowed?.Any(x => x.Id == r.Id) != true && g.Find(s.Layer, r.Id) == null) return "not held there";
        }
        var stowed = holder.Stowed;
        var moved = s.Move == null ? null : stowed?.FirstOrDefault(x => x.Id == s.Move);
        if (s.Move != null && moved == null) return "not held there";
        inverse = new SetReady { Layer = s.Layer, Holder = s.Holder, Ready = holder.Ready?.Clone(), Move = s.Move, Slot = moved == null ? 0 : stowed!.IndexOf(moved) };
        holder.Ready = s.Ready?.Clone();
        if (moved == null) return null;
        stowed!.Remove(moved);
        stowed.Insert(Math.Clamp(s.Slot, 0, stowed.Count), moved);
        return null;
    }

    static string? ApplyRelease(GameState g, ReleaseReady r, ref Command? inverse)
    {
        var holder = g.Find(r.Layer, r.Holder);
        if (holder?.Ready is not { } ready) return "nothing ready there";
        var state = ReadyPiece.Of(holder, id => g.Find(r.Layer, id));
        if (state != Readiness.In) return state == Readiness.Out ? "the ready piece is out" : "the ready piece is gone";
        var (pos, rot) = ready.OutBeside(holder);
        return ApplyUnstow(g, new Unstow { Layer = r.Layer, Holder = r.Holder, Id = ready.Id, Pos = pos, RotDeg = rot }, ref inverse);
    }

    static string? ApplyRecall(GameState g, RecallReady r, ref Command? inverse)
    {
        var holder = g.Find(r.Layer, r.Holder);
        if (holder?.Ready is not { } ready) return "nothing ready there";
        var p = g.Find(r.Layer, ready.Id);
        if (p == null) return ReadyPiece.Of(holder, _ => null) == Readiness.In ? "the ready piece is in" : "the ready piece is gone";
        var kept = new ReadyPiece { Id = ready.Id, Offset = ReadyPiece.Turned(p.Pos - holder.Pos, -holder.RotDeg), Turn = Norm(p.RotDeg - holder.RotDeg) };
        Command? back = null;
        var err = ApplyStow(g, new Stow { Layer = r.Layer, Holder = r.Holder, Id = ready.Id, Slot = 0 }, ref back);
        if (err != null) return err;
        holder.Ready = kept;
        inverse = new Batch { Items = new() { back!, new SetReady { Layer = r.Layer, Holder = r.Holder, Ready = ready } } };
        return null;
    }
}
