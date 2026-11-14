namespace KTable.Core.Model;

public static class SheetStack
{
    const double Touching = 1e-3;

    public static List<SheetDef> TopDown(IEnumerable<SheetDef> sheets) => sheets.OrderByDescending(s => s.Z).ToList();

    public static int Above(IEnumerable<SheetDef> sheets) => sheets.Select(s => s.Z).DefaultIfEmpty(-1).Max() + 1;

    public static Dictionary<string, int> Levels(IEnumerable<SheetDef> sheets)
    {
        var levels = new Dictionary<string, int>();
        var under = new List<(string Id, Box Box)>();
        foreach (var s in TopDown(sheets).AsEnumerable().Reverse())
        {
            var box = BoxOf(s);
            levels[s.Id] = under.Where(u => u.Box.Overlaps(box)).Select(u => levels[u.Id] + 1).DefaultIfEmpty(0).Max();
            under.Add((s.Id, box));
        }
        return levels;
    }

    public static List<(SheetDef Sheet, int Z)> Placed(IEnumerable<SheetDef> sheets, string id, int place)
    {
        var stack = TopDown(sheets);
        int from = stack.FindIndex(s => s.Id == id);
        if (from < 0) return new();
        var moved = stack[from];
        stack.RemoveAt(from);
        stack.Insert(Math.Clamp(place, 0, stack.Count), moved);
        return stack.Select((s, i) => (Sheet: s, Z: stack.Count - 1 - i)).Where(x => x.Sheet.Z != x.Z).ToList();
    }

    readonly record struct Box(double X0, double Z0, double X1, double Z1)
    {
        public bool Overlaps(Box o) => Math.Min(X1, o.X1) - Math.Max(X0, o.X0) > Touching && Math.Min(Z1, o.Z1) - Math.Max(Z0, o.Z0) > Touching;
    }

    static Box BoxOf(SheetDef s)
    {
        double turn = s.RotDeg * Math.PI / 180, cos = Math.Abs(Math.Cos(turn)), sin = Math.Abs(Math.Sin(turn));
        double halfW = (s.Size.W * cos + s.Size.H * sin) / 2, halfH = (s.Size.W * sin + s.Size.H * cos) / 2;
        return new Box(s.Pos.X - halfW, s.Pos.Z - halfH, s.Pos.X + halfW, s.Pos.Z + halfH);
    }
}
