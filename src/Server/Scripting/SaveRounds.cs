using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class SaveRounds(ScriptHost host)
{
    readonly HashSet<PieceScript> _ran = new();

    public int Backlog => _ran.Count;

    public int AskedLast { get; private set; }
    public int WithUpdate { get; private set; }

    public bool MarkRan(PieceScript ps)
    {
        ps.Ran = true;
        if (ps.Id.Length == 0) return false;
        _ran.Add(ps);
        return true;
    }

    public void Asked(PieceScript ps) { ps.Ran = false; _ran.Remove(ps); }

    public void Drop(PieceScript ps) => _ran.Remove(ps);

    public void Clear() => _ran.Clear();

    public void Round(bool sliced)
    {
        if (host.Global is { Ran: true } gps)
            host.Guard("", () =>
            {
                var gs = host.Call(gps, "on_save");
                gps.Ran = false;
                if (gs is { Type: DataType.String } g2 && g2.String != host.Server.State.ScriptState) host.Emit(new SetGlobalScriptState { State = g2.String });
            });
        var board = new Dictionary<string, PieceInstance>();
        foreach (var p in host.Server.State.Layer(Layers.Umpire)) board.TryAdd(p.Id, p);
        AskedLast = 0; WithUpdate = host.PieceScripts.Values.Count(x => x.HasUpdate);
        var round = System.Diagnostics.Stopwatch.StartNew();
        foreach (var ps in _ran.ToList())
        {
            if (sliced && round.ElapsedMilliseconds > ScriptHost.RoundBudgetMs) break;
            if (!host.PieceScripts.TryGetValue(ps.Id, out var current) || current != ps || !board.TryGetValue(ps.Id, out var p)) { Asked(ps); continue; }
            host.Guard(ps.Id, () =>
            {
                var r = host.Call(ps, "on_save");
                Asked(ps);
                AskedLast++;
                if (r is { Type: DataType.String } r2 && r2.String != p.ScriptState)
                    host.Emit(new SetScriptState { Layer = Layers.Umpire, Id = ps.Id, State = r2.String });
            });
            Asked(ps);
        }
    }
}
