using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class NumberKey
{
    public const int Most = 10;

    public static bool IsDie(PieceDef? def, PieceInstance p) => def is { Kind: "model" } && PieceInstance.FacesOf(def, p).Count > 1;

    public static bool ToScript(ClientSession s, string layer, PieceInstance p) =>
        s.View.Script != null || (layer == Layers.Umpire && !string.IsNullOrWhiteSpace(p.Script ?? s.Store.Def(p.Def)?.ScriptFor(p)));

    public static List<Command> Commands(ClientSession s, IEnumerable<(string Layer, PieceInstance Piece)> targets, int n)
    {
        var touchable = targets.Where(t => s.CanTouch(t.Layer, t.Piece)).ToList();
        var cmds = new List<Command>();
        foreach (var (layer, p) in touchable)
        {
            var def = s.Store.Def(p.Def);
            if (ToScript(s, layer, p)) cmds.Add(new NumberTyped { Layer = layer, Id = p.Id, Number = n });
            else if (IsDie(def, p))
            {
                if (PieceInstance.FacesOf(def, p).Count >= n && p.ShownFace != n - 1) cmds.Add(new TurnDie { Layer = layer, Id = p.Id, Face = n - 1 });
            }
            else if (def != null && def.States.Count > 1 && def.States.Count >= n && p.State != n - 1) cmds.Add(new SetState { Layer = layer, Id = p.Id, State = n - 1 });
        }
        return cmds;
    }
}
