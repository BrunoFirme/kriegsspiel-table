using KTable.Core.Model;

namespace KTable.Core.Protocol;

public static class BoardEdits
{
    public static string? Apply(GameState g, Command c, ref Command? inverse) => c switch
    {
        AddBoard ab => ApplyAddBoard(g, ab, ref inverse),
        RenameBoard nb => ApplyRenameBoard(g, nb, ref inverse),
        RemoveBoard rb => ApplyRemoveBoard(g, rb, ref inverse),
        MoveBoard mb => ApplyMoveBoard(g, mb, ref inverse),
        _ => "not a board",
    };

    public const string SightStays = "the Sight layer stays on top";

    public static int Pinned(GameState g) => g.Boards.Count(b => b.Sight);

    static string? ApplyAddBoard(GameState g, AddBoard ab, ref Command? inverse)
    {
        if (string.IsNullOrEmpty(ab.Id)) ab.Id = g.NewId("b");
        if (g.Boards.Any(b => b.Id == ab.Id)) return "a board of that id is already there";
        int? at = ab.Sight ? 0 : ab.Index is int i ? Math.Max(i, Pinned(g)) : null;
        StateMachine.PutBack(g.Boards, new BoardDef { Id = ab.Id, Name = ab.Name.Length > 0 ? ab.Name : $"Layer {g.Boards.Count + 1}", Sight = ab.Sight }, at);
        StateMachine.BumpId(g, ab.Id);
        inverse = new RemoveBoard { Id = ab.Id };
        return null;
    }

    static string? ApplyRenameBoard(GameState g, RenameBoard nb, ref Command? inverse)
    {
        var b = g.Boards.FirstOrDefault(x => x.Id == nb.Id);
        if (b == null) return "no such board";
        if (string.IsNullOrWhiteSpace(nb.Name)) return "a layer needs a name";
        inverse = new RenameBoard { Id = b.Id, Name = b.Name };
        b.Name = nb.Name;
        return null;
    }

    static string? ApplyRemoveBoard(GameState g, RemoveBoard rb, ref Command? inverse)
    {
        var b = g.Boards.FirstOrDefault(x => x.Id == rb.Id);
        if (b == null) return "no such board";
        var drawn = new List<Command>();
        foreach (var (layer, l) in g.Drawings)
            if (l != null)
                for (int i = 0; i < l.Count; i++)
                    if (l[i]?.Board == rb.Id) drawn.Add(new Draw { Layer = layer, Stroke = l[i], Index = i });
        foreach (var l in g.Drawings.Values) l?.RemoveAll(s => s?.Board == rb.Id);
        Command Back(Command board) => drawn.Count == 0 ? board : new Batch { Items = new List<Command> { board }.Concat(drawn).ToList() };
        if (g.Boards.Count == 1)
        {
            inverse = Back(new RenameBoard { Id = b.Id, Name = b.Name });
            b.Name = "Layer 1";
            return null;
        }
        int at = g.Boards.IndexOf(b);
        g.Boards.RemoveAt(at);
        inverse = Back(new AddBoard { Id = b.Id, Name = b.Name, Index = at, Sight = b.Sight });
        return null;
    }

    static string? ApplyMoveBoard(GameState g, MoveBoard mb, ref Command? inverse)
    {
        var b = g.Boards.FirstOrDefault(x => x.Id == mb.Id);
        if (b == null) return "no such board";
        if (mb.Index < 0 || mb.Index >= g.Boards.Count) return "no such place among the boards";
        if (b.Sight || mb.Index < Pinned(g)) return SightStays;
        int at = g.Boards.IndexOf(b);
        g.Boards.RemoveAt(at);
        g.Boards.Insert(mb.Index, b);
        inverse = new MoveBoard { Id = b.Id, Index = at };
        return null;
    }
}
