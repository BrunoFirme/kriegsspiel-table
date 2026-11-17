namespace KTable.Core.Model;

public static class PlaceClocks
{
    public static List<MapPlace> Apart(GameState g, Func<string, PieceDef?> def)
    {
        if (g.Clock.Apart is not { Count: > 0 } apart) return new();
        return PlaceTree.All(PlaceTree.Of(g, def)).Where(p => p.Kind != PlaceKind.Pin && apart.ContainsKey(p.Id)).ToList();
    }

    public static MapPlace? At(IEnumerable<MapPlace> apart, Vec2 point) => apart.Where(p => p.Contains(point)).MinBy(p => p.Span);

    public static string? At(GameState g, Func<string, PieceDef?> def, Vec2 point) => At(Apart(g, def), point)?.Id;

    public static bool MayKeep(MapPlace place) => place.Kind != PlaceKind.Pin;

    public static ClockTime Started(GameState g, Func<string, PieceDef?> def, MapPlace place) => new() { Seconds = g.Clock.Of(At(g, def, place.At)).Now };
}
