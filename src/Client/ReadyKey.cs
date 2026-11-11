using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class ReadyKey
{
    public static Command? For(GameState view, string layer, IEnumerable<string> picked)
    {
        var ids = picked.ToList();
        Readiness State(PieceInstance h) => ReadyPiece.Of(h, id => view.Find(layer, id));
        var holders = ids.Select(id => view.Find(layer, id)).OfType<PieceInstance>().Where(h => h.ReadyNow != null).ToList();
        var outs = holders.Where(h => State(h) == Readiness.Out)
            .Concat(ids.Select(id => HolderOf(view, layer, id)).OfType<PieceInstance>())
            .DistinctBy(h => h.Id).ToList();
        var items = outs.Count > 0
            ? outs.Select(h => (Command)new RecallReady { Layer = layer, Holder = h.Id }).ToList()
            : holders.Where(h => State(h) == Readiness.In).Select(h => (Command)new ReleaseReady { Layer = layer, Holder = h.Id }).ToList();
        return items.Count switch { 0 => null, 1 => items[0], _ => new Batch { Items = items } };
    }

    public static List<Command> Stowing(GameState view, string layer, PieceInstance holder, List<Command> stows)
    {
        if (holder.Stowed is { Count: > 0 } || stows.OfType<Stow>().FirstOrDefault() is not { } first
            || ReadyPiece.Of(holder, id => view.Find(layer, id)) is Readiness.In or Readiness.Out) return stows;
        return [.. stows, new SetReady { Layer = layer, Holder = holder.Id, Ready = new ReadyPiece { Id = first.Id } }];
    }

    public static PieceInstance? HolderOf(GameState view, string layer, string id) =>
        view.Layer(layer).FirstOrDefault(h => h.ReadyNow?.Id == id && ReadyPiece.Of(h, x => view.Find(layer, x)) == Readiness.Out);
}
