using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static class GroundLayerEdits
{
    public const int LongestName = 60, MostLayers = 32;
    public const string LastStays = "the last ground layer stays";

    public static string? Apply(GameState g, Command c, ref Command? inverse) => c switch
    {
        AddGroundLayer a => Add(g, a, ref inverse),
        RenameGroundLayer n => Rename(g, n, ref inverse),
        LockGroundLayer l => Lock(g, l, ref inverse),
        RemoveGroundLayer r => Remove(g, r, ref inverse),
        MoveGroundLayer m => Move(g, m, ref inverse),
        _ => "not a ground layer",
    };

    static string Named(string? name, int count) => (name ?? "").Trim() is { Length: > 0 } kept ? kept[..Math.Min(kept.Length, LongestName)] : $"Ground {count + 1}";

    static string? Add(GameState g, AddGroundLayer a, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(a.Id)) a.Id = g.NewId("g");
        if (!GroundLayerDef.FitId(a.Id)) return "a ground layer's id is letters, digits and dashes";
        if (g.GroundLayers.Any(l => l.Id == a.Id)) return "a ground layer of that id is already there";
        if (g.GroundLayers.Count >= MostLayers) return $"a table holds at most {MostLayers} ground layers";
        StateMachine.PutBack(g.GroundLayers, new GroundLayerDef { Id = a.Id, Name = Named(a.Name, g.GroundLayers.Count), Locked = a.Locked }, a.Index);
        StateMachine.BumpId(g, a.Id);
        inverse = new RemoveGroundLayer { Id = a.Id };
        return null;
    }

    static string? Rename(GameState g, RenameGroundLayer n, ref Command? inverse)
    {
        if (g.GroundLayers.FirstOrDefault(l => l.Id == n.Id) is not { } layer) return "no such ground layer";
        inverse = new RenameGroundLayer { Id = layer.Id, Name = layer.Name };
        layer.Name = Named(n.Name, g.GroundLayers.IndexOf(layer));
        return null;
    }

    static string? Lock(GameState g, LockGroundLayer l, ref Command? inverse)
    {
        if (g.GroundLayers.FirstOrDefault(x => x.Id == l.Id) is not { } layer) return "no such ground layer";
        inverse = new LockGroundLayer { Id = layer.Id, Locked = layer.Locked };
        layer.Locked = l.Locked;
        return null;
    }

    static string? Remove(GameState g, RemoveGroundLayer r, ref Command? inverse)
    {
        if (g.GroundLayers.FirstOrDefault(x => x.Id == r.Id) is not { } layer) return "no such ground layer";
        if (g.GroundLayers.Count == 1) return LastStays;
        int at = g.GroundLayers.IndexOf(layer);
        g.GroundLayers.RemoveAt(at);
        inverse = new AddGroundLayer { Id = layer.Id, Name = layer.Name, Index = at, Locked = layer.Locked };
        return null;
    }

    static string? Move(GameState g, MoveGroundLayer m, ref Command? inverse)
    {
        if (g.GroundLayers.FirstOrDefault(x => x.Id == m.Id) is not { } layer) return "no such ground layer";
        if (m.Index < 0 || m.Index >= g.GroundLayers.Count) return "no such place among the ground layers";
        int at = g.GroundLayers.IndexOf(layer);
        g.GroundLayers.RemoveAt(at);
        g.GroundLayers.Insert(m.Index, layer);
        inverse = new MoveGroundLayer { Id = layer.Id, Index = at };
        return null;
    }
}
