using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceLift
{
    public const double SnapStep = 0.5;

    public static bool Can(PieceNode n) => !n.IsDie && !n.IsEffect && !n.Piece.Locked;

    public static double Settled(double high, bool snap)
    {
        high = Math.Clamp(high, 0, PieceInstance.MostAltitude);
        if (snap) high = Math.Round(high / SnapStep) * SnapStep;
        return high < PieceInstance.LeastAltitude / 2 ? 0 : Math.Max(high, PieceInstance.LeastAltitude);
    }

    public static Command? To(IEnumerable<(PieceNode Node, double High)> lifts)
    {
        var items = new List<Command>();
        foreach (var (n, high) in lifts)
        {
            bool flies = high > 0;
            if (flies != n.Piece.Flies) items.Add(new SetPieceOption { Layer = n.Layer, Id = n.Piece.Id, Name = PieceOptions.Flies, Value = flies });
            if (flies && Math.Abs(high - n.Piece.Altitude) > 1e-6) items.Add(new SetAltitude { Layer = n.Layer, Id = n.Piece.Id, Value = high });
        }
        return items.Count == 0 ? null : items.Count == 1 ? items[0] : new Batch { Items = items };
    }

    public static string Said(double high) => high > 0 ? FormattableString.Invariant($"Flying {high:0.#} u over the ground") : "On the ground";
}
