namespace KTable.Client.Hand;

public static class Riders
{
    public static void Seat<T>(List<HeldPiece<T>> held, Footing<T> footing) where T : class, ITablePiece
    {
        var holders = new Dictionary<string, HeldPiece<T>>();
        foreach (var u in held)
            foreach (var id in u.Node.Piece.Attached) holders.TryAdd(id, u);
        foreach (var h in held)
        {
            if (h.Parent != null) continue;
            var under = holders.TryGetValue(h.Node.Piece.Id, out var a) && !Carries(h, a) ? a
                : h.Node.Piece.Y > 0 ? held.FirstOrDefault(u => Near(h, u) && !Carries(h, u) && footing.RestsOn(h.Node, u.Node)) : null;
            if (under == null) continue;
            h.Parent = under;
            h.Lift = (float)Math.Max(0, BaseY(footing, h.Node) - BaseY(footing, under.Node));
        }
    }

    static bool Near<T>(HeldPiece<T> h, HeldPiece<T> u) where T : class, ITablePiece =>
        (h.Node.Piece.Pos - u.Node.Piece.Pos).Length <= u.Node.SurfaceReach;

    static bool Carries<T>(HeldPiece<T> h, HeldPiece<T> u) where T : class
    {
        for (var p = u; p != null; p = p.Parent)
            if (p == h) return true;
        return false;
    }

    static double BaseY<T>(Footing<T> footing, T n) where T : class, ITablePiece =>
        footing.AbsoluteY(n, n.Piece.Pos, n.Piece.RotDeg, n.Piece.Y);
}
