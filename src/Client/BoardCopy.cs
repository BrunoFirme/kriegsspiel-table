using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class BoardCopy
{
    public static Batch? Of(GameState g, string boardId, string newId)
    {
        int at = g.Boards.FindIndex(b => b.Id == boardId);
        if (at < 0) return null;
        var items = new List<Command> { new AddBoard { Id = newId, Name = Named(g, g.Boards[at].Name), Index = at + 1 } };
        foreach (var (layer, strokes) in g.Drawings)
            foreach (var stroke in strokes.Where(s => s.Board == boardId))
            {
                var copy = Json.Clone(stroke);
                (copy.Id, copy.Board) = ("", newId);
                items.Add(new Draw { Layer = layer, Stroke = copy });
            }
        return new Batch { Items = items };
    }

    static string Named(GameState g, string name)
    {
        var taken = g.Boards.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string first = $"{name} copy";
        if (!taken.Contains(first)) return first;
        for (int n = 2; ; n++)
            if (!taken.Contains($"{first} {n}")) return $"{first} {n}";
    }
}
