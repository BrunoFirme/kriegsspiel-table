using KTable.Core.Model;

namespace KTable.Client.Relief;

public sealed class KeptGrids
{
    public const long SpareCells = 32_000_000;

    readonly Dictionary<string, ReliefGrid> _grids = new();
    readonly List<string> _arrived = new();
    readonly Func<IEnumerable<string>> _inUse;
    readonly long _spareCells;

    public KeptGrids(Func<IEnumerable<string>> inUse, long spareCells = SpareCells) { _inUse = inUse; _spareCells = spareCells; }

    public int Count => _grids.Count;

    public ReliefGrid? Get(string sha) => _grids.GetValueOrDefault(sha);

    public void Put(string sha, ReliefGrid grid)
    {
        _grids[sha] = grid;
        _arrived.Remove(sha);
        _arrived.Add(sha);
        var inUse = _inUse().ToHashSet();
        long spare = 0;
        for (int i = _arrived.Count - 2; i >= 0; i--)
        {
            var older = _arrived[i];
            if (inUse.Contains(older)) continue;
            spare += (long)_grids[older].Cols * _grids[older].Rows;
            if (spare <= _spareCells) continue;
            _grids.Remove(older);
            _arrived.RemoveAt(i);
        }
    }
}
