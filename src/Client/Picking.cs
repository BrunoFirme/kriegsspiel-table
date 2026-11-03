namespace KTable.Client;

public sealed class Picking<TKey> where TKey : notnull
{
    readonly List<TKey> _order = new();
    readonly HashSet<TKey> _picked = new();
    TKey? _anchor;
    bool _hasAnchor;

    public IReadOnlyList<TKey> Picked => _order.Where(_picked.Contains).ToList();

    public int Count => _picked.Count;

    public bool Has(TKey key) => _picked.Contains(key);

    public TKey? Single => _picked.Count == 1 ? _picked.First() : default;

    public void Show(IEnumerable<TKey> order)
    {
        _order.Clear();
        _order.AddRange(order);
        _picked.IntersectWith(_order);
        if (_hasAnchor && !_order.Contains(_anchor!)) _hasAnchor = false;
    }

    public void Click(TKey key, bool toggle, bool range)
    {
        if (range && _hasAnchor && _order.IndexOf(_anchor!) is var from and >= 0 && _order.IndexOf(key) is var to and >= 0)
        {
            if (!toggle) _picked.Clear();
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _picked.Add(_order[i]);
            return;
        }
        if (toggle) { if (!_picked.Remove(key)) _picked.Add(key); }
        else { _picked.Clear(); _picked.Add(key); }
        _anchor = key;
        _hasAnchor = true;
    }

    public void PickForMenu(TKey key)
    {
        if (_picked.Contains(key)) return;
        Click(key, toggle: false, range: false);
    }

    public void All() => _picked.UnionWith(_order);

    public void Clear()
    {
        _picked.Clear();
        _hasAnchor = false;
    }
}
