using KTable.Core.Model;

namespace KTable.Client;

public static class InkOrder
{
    public sealed class Run<TKey>
    {
        public required TKey Key { get; init; }
        public required bool Wash { get; init; }
        public List<Stroke> Strokes { get; } = new();
        public int Step { get; set; }
        internal Box Over = Box.Nothing;
    }

    internal readonly record struct Box(double X0, double Z0, double X1, double Z1)
    {
        public static readonly Box Nothing = new(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        public bool Meets(Box o) => X0 < o.X1 && o.X0 < X1 && Z0 < o.Z1 && o.Z0 < Z1;
        public Box With(Box o) => new(Math.Min(X0, o.X0), Math.Min(Z0, o.Z0), Math.Max(X1, o.X1), Math.Max(Z1, o.Z1));
    }

    public static List<Run<TKey>> Runs<TKey>(IReadOnlyList<BoardDef> boards, IEnumerable<Stroke> strokes, Func<Stroke, TKey> ink, int steps)
    {
        var place = new Dictionary<string, int>();
        for (int i = 0; i < boards.Count; i++) place.TryAdd(boards[i].Id, boards.Count - 1 - i);
        int Under(Stroke s) => place.TryGetValue(s.Board ?? "", out var p) ? p : -1;
        var runs = new List<Run<TKey>>();
        var perBoard = new List<int>();
        foreach (var board in strokes.OrderBy(Under).GroupBy(Under))
        {
            var fills = new List<Run<TKey>>();
            var lines = new List<Run<TKey>>();
            foreach (var st in board)
            {
                var key = ink(st);
                var box = Around(st);
                if (st.Fill) Into(fills, key, true, st, box);
                Into(lines, key, false, st, box);
            }
            runs.AddRange(fills);
            runs.AddRange(lines);
            perBoard.Add(fills.Count + lines.Count);
        }
        var at = Steps(perBoard, steps);
        for (int i = 0; i < runs.Count; i++) runs[i].Step = at[i];
        return runs;
    }

    static void Into<TKey>(List<Run<TKey>> runs, TKey key, bool wash, Stroke st, Box box)
    {
        for (int i = runs.Count - 1; i >= 0; i--)
        {
            var r = runs[i];
            if (EqualityComparer<TKey>.Default.Equals(r.Key, key))
            {
                r.Strokes.Add(st);
                r.Over = r.Over.With(box);
                return;
            }
            if (r.Over.Meets(box)) break;
        }
        var run = new Run<TKey> { Key = key, Wash = wash, Over = box };
        run.Strokes.Add(st);
        runs.Add(run);
    }

    static Box Around(Stroke st)
    {
        double reach = Math.Max(0, st.Width) / 2;
        var b = Box.Nothing;
        foreach (var p in st.Points) b = b.With(new Box(p.X - reach, p.Z - reach, p.X + reach, p.Z + reach));
        return b;
    }

    public static int[] Steps(IReadOnlyList<int> runsPerBoard, int steps)
    {
        int total = runsPerBoard.Sum();
        var at = new int[total];
        if (total <= steps)
        {
            for (int i = 0; i < total; i++) at[i] = i;
            return at;
        }
        int boards = runsPerBoard.Count(n => n > 0), spare = Math.Max(0, steps - boards), extra = total - boards;
        int k = 0, start = 0;
        foreach (var n in runsPerBoard)
        {
            if (n == 0) continue;
            int width = 1 + (extra == 0 ? 0 : (int)((long)spare * (n - 1) / extra));
            for (int j = 0; j < n; j++) at[k++] = Math.Min(steps - 1, start + (int)((long)j * width / n));
            start += width;
        }
        return at;
    }
}
