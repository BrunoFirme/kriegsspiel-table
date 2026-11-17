using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class PieceStretch
{
    public (double Scale, double Wide, double Deep, double Tall) Built { get; set; } = (1, 1, 1, 1);
    public Vector3 Shown { get; set; } = Vector3.One;
    (double Wide, double Deep)? _uprightBy;

    public void Preview(PieceNode n, Vector3 by)
    {
        n.Scale = n.Scale / Shown * by;
        Shown = by;
    }

    public float Upright(float across, float deep, double? kept, PieceInstance p)
    {
        _uprightBy = kept != null ? (1, 0) : (across / p.WideBy, deep / p.DeepBy);
        return (kept is { } upright ? across * (float)upright : (across + deep) / 2) * (float)p.TallBy;
    }

    public double TallKeeping(PieceNode n, double wide, double deep)
    {
        var p = n.Piece;
        if (!n.ShowsModel || _uprightBy is not { } by) return p.TallBy;
        double was = by.Wide * p.WideBy + by.Deep * p.DeepBy, now = by.Wide * wide + by.Deep * deep;
        return was > 0 && now > 0 ? Math.Clamp(p.TallBy * was / now, PieceInstance.LeastStretch, PieceInstance.MostStretch) : p.TallBy;
    }

    public static bool HasHeight(PieceNode n) => !n.IsEffect;

    public static Vector3 SideAt(PieceNode n, Vector2 way) =>
        n.Transform * new Vector3(n.FootprintCentre.X + way.X * n.Footprint.X / 2, 0, n.FootprintCentre.Y + way.Y * n.Footprint.Y / 2);

    public static Vector3 TopMiddle(PieceNode n) => n.Transform * new Vector3(n.FootprintCentre.X, n.Piece.FaceDown ? 0 : n.Thickness, n.FootprintCentre.Y);
}
