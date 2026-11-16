using KTable.Core.Model;

namespace KTable.Core.Protocol;

static class BoundEdits
{
    public static string? Apply(GameState g, Command c, ref Command? inverse) => c switch
    {
        SetBoundLayer sl => Set(g, sl, ref inverse),
        RemoveBoundLayer rl => Remove(g, rl, ref inverse),
        SetBoundRule sr => Rule(g, sr, ref inverse),
        _ => "not a bound edit",
    };

    static string? Set(GameState g, SetBoundLayer sl, ref Command? inverse)
    {
        if (Unsound(sl.Bound) is { } wrong) return wrong;
        var layers = g.BoundLayers ??= new();
        var layer = sl.Bound.Clone();
        if (layer.Id.Length == 0) layer.Id = g.NewId("b");
        foreach (var shape in layer.Shapes.Where(s => s.Id.Length == 0)) shape.Id = g.NewId("bs");
        var old = layers.FirstOrDefault(l => l.Id == layer.Id);
        if (old == null && layers.Count >= Bounds.MostLayers) return "too many bound layers";
        inverse = old != null ? new SetBoundLayer { Bound = old.Clone() } : new RemoveBoundLayer { Id = layer.Id };
        if (old != null) layers[layers.IndexOf(old)] = layer;
        else if (sl.Index is int at && at >= 0 && at <= layers.Count) layers.Insert(at, layer);
        else layers.Add(layer);
        sl.Bound = layer.Clone();
        StateMachine.BumpId(g, layer.Id);
        foreach (var shape in layer.Shapes) StateMachine.BumpId(g, shape.Id);
        return null;
    }

    static string? Remove(GameState g, RemoveBoundLayer rl, ref Command? inverse)
    {
        if (g.BoundLayers?.FirstOrDefault(l => l.Id == rl.Id) is not { } layer) return "no such bound layer";
        int at = g.BoundLayers.IndexOf(layer);
        g.BoundLayers.RemoveAt(at);
        if (g.BoundLayers.Count == 0) g.BoundLayers = null;
        inverse = new SetBoundLayer { Bound = layer, Index = at };
        return null;
    }

    static string? Rule(GameState g, SetBoundRule sr, ref Command? inverse)
    {
        if (sr.Player is { } who && (who.Trim().Length == 0 || who.Length > TableRules.LongestName)) return "bad player name";
        if (g.BoundLayers?.Any(l => l.Id == sr.LayerId) != true) return "no such bound layer";
        if (!Enum.IsDefined(sr.Rule)) return "bad bound rule";
        inverse = new SetBoundRule { Player = sr.Player, LayerId = sr.LayerId, Rule = Bounds.SetFor(g, sr.Player, sr.LayerId) };
        if (sr.Player == null)
        {
            var everyone = g.EveryoneBounds ??= new();
            if (sr.Rule is BoundRule.Off or BoundRule.Same) everyone.Remove(sr.LayerId);
            else everyone[sr.LayerId] = sr.Rule;
            if (everyone.Count == 0) g.EveryoneBounds = null;
            return null;
        }
        var players = g.PlayerBounds ??= new(StringComparer.OrdinalIgnoreCase);
        var key = players.Keys.FirstOrDefault(k => string.Equals(k, sr.Player, StringComparison.OrdinalIgnoreCase)) ?? sr.Player;
        if (!players.ContainsKey(key) && players.Count >= TableRules.MostPlayers) return "too many players with their own bounds";
        var own = players.TryGetValue(key, out var had) ? had : players[key] = new();
        if (sr.Rule == BoundRule.Same) own.Remove(sr.LayerId);
        else own[sr.LayerId] = sr.Rule;
        if (own.Count == 0) players.Remove(key);
        if (players.Count == 0) g.PlayerBounds = null;
        return null;
    }

    static string? Unsound(BoundLayer layer)
    {
        if (layer.Name.Length > Bounds.LongestName || layer.Shapes.Count > Bounds.MostShapes) return "bound layer too large";
        foreach (var s in layer.Shapes)
        {
            if (s.Polygon.Count is < 3 or > Bounds.MostCorners) return "a bound needs 3 to 256 corners";
            if (s.Polygon.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Z)) || !double.IsFinite(s.Floor) || !double.IsFinite(s.Ceiling)) return "bad bound corner";
            if (s.Ceiling <= s.Floor) return "a bound's ceiling must be above its floor";
            if (s.On is { Length: > 128 }) return "bad bound piece";
        }
        return null;
    }
}
