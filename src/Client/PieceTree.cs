using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class PieceTree
{
    public static Dictionary<string, PieceInstance> Parents(IEnumerable<PieceInstance> layer)
    {
        var pieces = layer.ToList();
        var here = pieces.Select(p => p.Id).ToHashSet();
        var parents = new Dictionary<string, PieceInstance>();
        foreach (var p in pieces)
            foreach (var child in p.Attached ?? [])
                if (child != null && child != p.Id && here.Contains(child)) parents.TryAdd(child, p);
        foreach (var id in parents.Keys.ToList())
            if (Within(parents, id, id)) parents.Remove(id);
        return parents;
    }

    public static bool Within(Dictionary<string, PieceInstance> parents, string id, string ancestor)
    {
        var seen = new HashSet<string>();
        for (var at = id; parents.TryGetValue(at, out var up) && seen.Add(at); at = up.Id)
            if (up.Id == ancestor) return true;
        return false;
    }

    public static int Depth(Dictionary<string, PieceInstance> parents, string id)
    {
        int depth = 0;
        var seen = new HashSet<string>();
        for (var at = id; parents.TryGetValue(at, out var up) && seen.Add(at); at = up.Id) depth++;
        return depth;
    }

    public static List<string> Tops(Dictionary<string, PieceInstance> parents, IEnumerable<string> ids)
    {
        var all = ids.Distinct().ToList();
        return all.Where(id => !all.Any(other => other != id && Within(parents, id, other))).ToList();
    }

    public static List<Command> Nest(GameState view, string layer, IEnumerable<string> dragged, string? into)
    {
        var parents = Parents(view.Layer(layer));
        if (into != null && view.Find(layer, into) == null) return new();
        var cmds = new List<Command>();
        var readyOut = ReadyPiece.OutHolders(view.Layer(layer));
        foreach (var id in Tops(parents, dragged))
        {
            if (view.Find(layer, id) == null || id == into || readyOut.ContainsKey(id)) continue;
            var now = parents.GetValueOrDefault(id)?.Id;
            if (now == into) continue;
            if (into == null) cmds.Add(new Detach { Layer = layer, Child = id });
            else if (!Within(parents, into, id)) cmds.Add(new Attach { Layer = layer, Parent = into, Child = id });
        }
        return cmds;
    }
}
