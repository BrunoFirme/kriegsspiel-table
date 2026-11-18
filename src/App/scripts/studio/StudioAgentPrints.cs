using System;
using System.Linq;
using System.Text.Json;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

static class AgentPrints
{
    internal static readonly Arg State = new("state", "number", "the state where it prints so, its number from 1 (default 1)");

    internal static bool Moves(JsonElement a) => a.Str("side") != null || new[] { "x", "y", "w", "h" }.Any(k => a.Num(k) != null);

    internal static int StateOf(JsonElement a, PieceDef d, out string? problem)
    {
        int state = (int)(a.Num("state") ?? 1) - 1;
        problem = state < 0 || state >= Math.Max(1, d.States.Count) ? $"{d.Name} has {Math.Max(1, d.States.Count)} state(s)." : null;
        return Math.Max(0, state);
    }

    internal static PrintPlace? Place(JsonElement a, PrintPlace? now, int index, out string? problem)
    {
        problem = null;
        var side = a.Str("side");
        if (side == PrintPlace.Hidden) return null;
        if (side != null && side is not (PrintPlace.Front or PrintPlace.Back)) { problem = "side is front, back or hidden"; return now; }
        if (side == null && a.Num("x") == null && a.Num("y") == null && a.Num("w") == null && a.Num("h") == null) return now;
        var p = now ?? PiecePrinting.Fresh(side ?? PrintPlace.Back, index);
        return new PrintPlace
        {
            Side = side ?? p.Side,
            X = Math.Clamp(a.Num("x") ?? p.X, 0, 1), Y = Math.Clamp(a.Num("y") ?? p.Y, 0, 1), W = Math.Clamp(a.Num("w") ?? p.W, 0, 1), H = Math.Clamp(a.Num("h") ?? p.H, 0, 1),
        };
    }
}
