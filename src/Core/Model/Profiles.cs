namespace KTable.Core.Model;

public enum Hold : byte { Banded, Natural, Fixed, Free }

public readonly record struct Band(Hold Hold, double CutM, double FillM);

public sealed record Profile(List<Spot> At, double[] Along, double[] Bare, double[] Level, Hold[] Holds, double Grade)
{
    public int Count => At.Count;

    public double ChangeM(int i) => Level[i] - Bare[i];

    public bool Stepped(int i) => i + 1 < Count && Holds[i] is Hold.Natural or Hold.Fixed && Holds[i + 1] is Hold.Natural or Hold.Fixed && (Holds[i] == Hold.Natural || Holds[i + 1] == Hold.Natural);

    public bool Shared(int i) => i + 1 < Count && Holds[i] == Hold.Fixed && Holds[i + 1] == Hold.Fixed;

    public bool Written(int i) => i + 1 < Count && Holds[i] != Hold.Free && Holds[i + 1] != Hold.Free;
}

static class Profiles
{
    const int MostRounds = 64;
    const double Slack = 1e-6, SteepestShare = 0.8;

    static double Steep(double[] along, double[] bare, int i) => Math.Abs(bare[i + 1] - bare[i]) / Math.Max(along[i + 1] - along[i], 1e-9);

    internal static List<Spot> Stations(IReadOnlyList<Spot> path, double step)
    {
        var at = new List<Spot>();
        for (int i = 0; i + 1 < path.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((path[i + 1] - path[i]).Length / step));
            for (int k = i == 0 ? 0 : 1; k <= steps; k++) at.Add(path[i] + (path[i + 1] - path[i]) * (k / (double)steps));
        }
        if (path.Count == 1) at.Add(path[0]);
        return at;
    }

    static bool Pinned(Hold hold) => hold is Hold.Natural or Hold.Fixed;

    static bool Parted(Hold[] holds, int a, int b) => Pinned(holds[a]) && Pinned(holds[b]);

    internal static bool Fit(double[] along, double[] bare, double grade, Band[] bands, Hold[] holds, double[] pinned, double[] level, bool loosen = true, double aimM = 0)
    {
        int n = along.Length;
        var low = new double[n];
        var high = new double[n];
        bool held;
        for (int round = 0; ; round++)
        {
            Bounds(bare, bands, holds, pinned, low, high);
            Spread(along, grade, holds, low, 1);
            Spread(along, grade, holds, high, -1);
            var wrong = Enumerable.Range(0, n).Where(i => low[i] > high[i] + Slack).ToList();
            held = wrong.Count == 0;
            if (held || !loosen || round >= MostRounds) break;
            double Local(int i) => holds[i] == Hold.Free ? 0 : Math.Max(i > 0 ? Steep(along, bare, i - 1) : 0, i + 1 < n ? Steep(along, bare, i) : 0);
            double steepest = wrong.Max(Local), from = steepest > grade ? Math.Max(grade, steepest * SteepestShare) : steepest;
            bool loosened = false;
            foreach (int i in wrong.Where(i => Local(i) >= from - Slack)) loosened |= Loosen(holds, i);
            if (!loosened)
                foreach (int i in wrong) loosened |= Loosen(holds, i);
            if (!loosened) break;
        }
        var mid = aimM > 0 ? Midline(along, Aim(along, bare, holds, aimM), grade, holds) : null;
        for (int i = 0; i < n; i++)
            level[i] = holds[i] switch
            {
                Hold.Natural => bare[i],
                Hold.Fixed => pinned[i],
                _ => Placed(low[i], high[i], mid?[i], bare[i]),
            };
        return held;
    }

    static double Placed(double low, double high, double? aim, double bare) =>
        !double.IsFinite(low) || !double.IsFinite(high) ? aim ?? bare : aim is { } a ? Math.Clamp(a, low, Math.Max(low, high)) : (low + high) / 2;

    static double[] Aim(double[] along, double[] bare, Hold[] holds, double window)
    {
        int n = along.Length;
        var aim = new double[n];
        double sum = 0;
        int count = 0;
        for (int i = 0, a = 0, b = -1; i < n; i++)
        {
            while (b + 1 < n && along[b + 1] - along[i] <= window)
                if (holds[++b] != Hold.Free) (sum, count) = (sum + bare[b], count + 1);
            for (; along[i] - along[a] > window; a++)
                if (holds[a] != Hold.Free) (sum, count) = (sum - bare[a], count - 1);
            aim[i] = count > 0 ? sum / count : bare[i];
        }
        return aim;
    }

    static double[] Midline(double[] along, double[] aim, double grade, Hold[] holds)
    {
        int n = along.Length;
        double[] low = new double[n], high = new double[n], mid = new double[n];
        for (int i = 0; i < n; i++) (low[i], high[i]) = holds[i] == Hold.Free ? (double.NegativeInfinity, double.PositiveInfinity) : (aim[i], aim[i]);
        Spread(along, grade, holds, low, 1);
        Spread(along, grade, holds, high, -1);
        for (int i = 0; i < n; i++) mid[i] = double.IsFinite(low[i]) && double.IsFinite(high[i]) ? (low[i] + high[i]) / 2 : aim[i];
        return mid;
    }

    static void Bounds(double[] bare, Band[] bands, Hold[] holds, double[] pinned, double[] low, double[] high)
    {
        for (int i = 0; i < bare.Length; i++)
            (low[i], high[i]) = holds[i] switch
            {
                Hold.Natural => (bare[i], bare[i]),
                Hold.Fixed => (pinned[i], pinned[i]),
                Hold.Free => (double.NegativeInfinity, double.PositiveInfinity),
                _ => (bare[i] - bands[i].CutM, bare[i] + bands[i].FillM),
            };
    }

    static void Spread(double[] along, double grade, Hold[] holds, double[] bound, int sign)
    {
        int n = along.Length;
        for (int i = 1; i < n; i++)
            if (!Parted(holds, i - 1, i) && !Pinned(holds[i])) bound[i] = Toward(bound[i], bound[i - 1] - sign * grade * (along[i] - along[i - 1]), sign);
        for (int i = n - 2; i >= 0; i--)
            if (!Parted(holds, i, i + 1) && !Pinned(holds[i])) bound[i] = Toward(bound[i], bound[i + 1] - sign * grade * (along[i + 1] - along[i]), sign);
    }

    static double Toward(double own, double carried, int sign) => sign > 0 ? Math.Max(own, carried) : Math.Min(own, carried);

    static bool Loosen(Hold[] holds, int i)
    {
        if (holds[i] == Hold.Banded)
        {
            holds[i] = Hold.Natural;
            return true;
        }
        bool any = false;
        foreach (int way in new[] { -1, 1 })
        {
            int j = i;
            while (j >= 0 && j < holds.Length && holds[j] == Hold.Free) j += way;
            if (j < 0 || j >= holds.Length || holds[j] != Hold.Banded) continue;
            holds[j] = Hold.Natural;
            any = true;
        }
        return any;
    }

    internal static List<Spot> Simplified(IReadOnlyList<Spot> points, double within)
    {
        if (points.Count < 3) return points.ToList();
        var kept = new List<Spot> { points[0] };
        for (int from = 0; from + 1 < points.Count;)
        {
            int to = from + 1;
            while (to + 1 < points.Count && Enumerable.Range(from + 1, to - from).All(k => points[k].FromSegment(points[from], points[to + 1]) <= within)) to++;
            kept.Add(points[to]);
            from = to;
        }
        return kept;
    }

    internal static IEnumerable<(int From, int To)> Runs(int count, Func<int, bool> takes, int least = 1)
    {
        for (int i = 0; i < count;)
        {
            if (!takes(i))
            {
                i++;
                continue;
            }
            int j = i;
            while (j + 1 < count && takes(j + 1)) j++;
            if (j - i + 1 >= least) yield return (i, j);
            i = j + 1;
        }
    }
}
