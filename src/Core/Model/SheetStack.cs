namespace KTable.Core.Model;

public static class SheetStack
{
    const double Touching = 1e-3, Sliver = 0.002;

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

    public static Dictionary<string, List<SheetDef>> Slivers(IEnumerable<SheetDef> sheets)
    {
        var stack = TopDown(sheets);
        var over = stack.ToDictionary(s => s.Id, _ => new List<SheetDef>());
        for (int i = 0; i < stack.Count; i++)
            for (int j = i + 1; j < stack.Count; j++)
            {
                var (a, b) = (BoxOf(stack[i]), BoxOf(stack[j]));
                if (a.Meets(b) && !a.Overlaps(b)) over[stack[j].Id].Add(stack[i]);
            }
        return over;
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

    readonly record struct Box(double X, double Z, double HalfW, double HalfH, double Cos, double Sin)
    {
        (double Low, double High) Along(double ax, double az)
        {
            double mid = X * ax + Z * az, reach = HalfW * Math.Abs(Cos * ax + Sin * az) + HalfH * Math.Abs(-Sin * ax + Cos * az);
            return (mid - reach, mid + reach);
        }

        public bool Overlaps(Box o) => Over(o, Math.Max(Touching, Sliver * 2 * Math.Min(Math.Min(HalfW, HalfH), Math.Min(o.HalfW, o.HalfH))));

        public bool Meets(Box o) => Over(o, Touching);

        bool Over(Box o, double least)
        {
            foreach (var (ax, az) in new[] { (Cos, Sin), (-Sin, Cos), (o.Cos, o.Sin), (-o.Sin, o.Cos) })
            {
                var (mine, theirs) = (Along(ax, az), o.Along(ax, az));
                if (Math.Min(mine.High, theirs.High) - Math.Max(mine.Low, theirs.Low) <= least) return false;
            }
            return true;
        }
    }

    static Box BoxOf(SheetDef s)
    {
        double turn = s.RotDeg * Math.PI / 180;
        return new Box(s.Pos.X, s.Pos.Z, s.Size.W / 2, s.Size.H / 2, Math.Cos(turn), Math.Sin(turn));
    }
}
