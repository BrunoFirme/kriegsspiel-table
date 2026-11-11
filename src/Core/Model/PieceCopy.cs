using System;
using System.Collections.Generic;
using System.Linq;

namespace KTable.Core.Model;

public static class PieceCopy
{
    public static List<PieceInstance> WithContents(IEnumerable<PieceInstance> picked, Func<string, PieceInstance?> onTable)
    {
        var taken = new List<PieceInstance>();
        var seen = new HashSet<string>();
        void Take(PieceInstance p)
        {
            if (!seen.Add(p.Id)) return;
            taken.Add(p.Clone());
            var outReady = ReadyPiece.Of(p, onTable) == Readiness.Out ? p.Ready!.Id : null;
            foreach (var id in (p.Attached ?? []).Append(outReady).OfType<string>())
                if (onTable(id) is { } more) Take(more);
        }
        foreach (var p in picked) Take(p);
        return taken;
    }

    public static void FreshHeld(PieceInstance holder, Func<string> freshId) => FreshHeld(holder, freshId, nested: false);

    static void FreshHeld(PieceInstance holder, Func<string> freshId, bool nested)
    {
        var held = holder.Stowed ?? [];
        var renamed = new Dictionary<string, string>();
        foreach (var s in held)
        {
            var id = freshId();
            renamed.TryAdd(s.Id, id);
            s.Id = id;
        }
        foreach (var s in held)
        {
            s.Attached = (s.Attached ?? []).Select(a => renamed.GetValueOrDefault(a, a)).ToList();
            FreshHeld(s, freshId, nested: true);
        }
        if (holder.Ready is not { } r) return;
        holder.Ready = renamed.TryGetValue(r.Id, out var now) ? new ReadyPiece { Id = now, Offset = r.Offset, Turn = r.Turn }
            : nested ? null
            : r;
    }
}
