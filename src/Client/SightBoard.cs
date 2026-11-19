using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class SightBoard
{
    public const string Named = "Sight";

    public static IEnumerable<Stroke> Kept(GameState g, IReadOnlySet<string> hidden) =>
        g.DrawLayer(Layers.Umpire).Where(s => s.Sight != null && s.Points.Count >= 2 && !hidden.Contains(s.Board));

    public static SightLine LineOf(Stroke s) => new(SightLine.KindNamed(s.Sight), s.Points, s.Blur ?? 0);

    public static AddBoard Made(string id) => new() { Id = id, Name = Named, Sight = true, Index = 0 };

    public static Command Keep(GameState g, SightLine line, string newBoard, double width)
    {
        var board = g.Boards.FirstOrDefault(b => b.Sight)?.Id;
        var stroke = new Stroke { Points = line.Points.ToList(), Board = board ?? newBoard, Width = width };
        Kind(stroke, line.Kind, line.Blur);
        var draw = new Draw { Layer = Layers.Umpire, Stroke = stroke };
        return board != null ? draw : new Batch { Items = new List<Command> { Made(newBoard), draw } };
    }

    public static void Kind(Stroke s, string kind, double blur)
    {
        s.Sight = SightLine.KindNamed(kind);
        s.Blur = s.Sight == SightLine.Blurs ? blur : null;
        s.Color = SightLine.InkOf(s.Sight);
    }

    public static double? BlurOf(GameState g, IReadOnlySet<string> hidden) => Kept(g, hidden).FirstOrDefault(s => s.Sight == SightLine.Blurs)?.Blur;

    public static Command? Blurred(GameState g, double blur)
    {
        var items = g.DrawLayer(Layers.Umpire).Where(s => s.Sight == SightLine.Blurs && s.Blur != blur).Select(s => s.Id).ToList()
            .Select(id => Changed(g, id, s => s.Blur = blur)).OfType<Command>().ToList();
        return items.Count == 0 ? null : new Batch { Items = items };
    }

    public static Command? Changed(GameState g, string strokeId, Action<Stroke> change)
    {
        var layer = g.DrawLayer(Layers.Umpire);
        int at = layer.FindIndex(s => s.Id == strokeId);
        if (at < 0) return null;
        var now = Json.Clone(layer[at]);
        change(now);
        return new Batch { Items = new List<Command> { new EraseStroke { Layer = Layers.Umpire, StrokeId = strokeId }, new Draw { Layer = Layers.Umpire, Stroke = now, Index = at } } };
    }
}
