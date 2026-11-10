using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class TakeOut
{
    public const double From = 2, Apart = 1.5;

    public static Command Taking(ClientSession s, string layer, PieceInstance holder, List<Command> unstows)
    {
        if (holder.Holds == null && s.Store.Def(holder.Def)?.Holds != true)
            unstows.Add(new SetPieceOption { Layer = layer, Id = holder.Id, Name = PieceOptions.Holds, Value = true });
        return unstows.Count == 1 ? unstows[0] : new Batch { Items = unstows };
    }

    public static Command? All(ClientSession s, string layer, PieceInstance holder, int most = -1)
    {
        var top = holder.Loose();
        if (most >= 0 && top.Count > most) top = top.Take(most).ToList();
        if (top.Count == 0) return null;
        return Taking(s, layer, holder,
            top.Select((p, i) => (Command)new Unstow { Layer = layer, Holder = holder.Id, Id = p.Id, Pos = new Vec2(holder.Pos.X + From + i * Apart, holder.Pos.Z) }).ToList());
    }
}
