using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public enum BoundRule { Same, In, Out, Off }

public sealed class BoundShape
{
    public string Id { get; set; } = "";
    public List<Vec2> Polygon { get; set; } = new();
    public double Floor { get; set; }
    public double Ceiling { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? On { get; set; }

    public BoundShape Clone() => new() { Id = Id, Polygon = new(Polygon), Floor = Floor, Ceiling = Ceiling, On = On };
}

public sealed class BoundLayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<BoundShape> Shapes { get; set; } = new();

    public BoundLayer Clone() => new() { Id = Id, Name = Name, Shapes = Shapes.Select(s => s.Clone()).ToList() };
}

public static class Bounds
{
    public const int MostLayers = 64, MostShapes = 128, MostCorners = 256, LongestName = 64;

    public static BoundRule RuleOf(GameState g, string? player, string layer)
    {
        if (player != null && Own(g, player)?.GetValueOrDefault(layer) is { } own and not BoundRule.Same) return own;
        return g.EveryoneBounds?.GetValueOrDefault(layer) ?? BoundRule.Off;
    }

    public static BoundRule SetFor(GameState g, string? player, string layer) =>
        player == null ? g.EveryoneBounds?.GetValueOrDefault(layer) ?? BoundRule.Off : Own(g, player)?.GetValueOrDefault(layer) ?? BoundRule.Same;

    public static bool Binds(GameState g, string player) =>
        g.BoundLayers?.Any(l => l.Shapes.Count > 0 && RuleOf(g, player, l.Id) is BoundRule.In or BoundRule.Out) == true;

    public static IEnumerable<(BoundLayer Layer, BoundRule Rule)> Active(GameState g, string player) =>
        (g.BoundLayers ?? []).Select(l => (l, RuleOf(g, player, l.Id))).Where(x => x.Item2 is BoundRule.In or BoundRule.Out);

    static Dictionary<string, BoundRule>? Own(GameState g, string player) =>
        g.PlayerBounds?.FirstOrDefault(p => string.Equals(p.Key, player, StringComparison.OrdinalIgnoreCase)).Value;
}
