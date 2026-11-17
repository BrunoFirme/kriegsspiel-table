namespace KTable.Client;

public sealed class Strangers(double graceSeconds)
{
    readonly Dictionary<long, double> _since = new();

    public List<long> Overstayed(IEnumerable<long> connected, Func<long, bool> known, double now)
    {
        var here = connected.ToHashSet();
        foreach (var gone in _since.Keys.Where(id => !here.Contains(id)).ToList()) _since.Remove(gone);
        var overstayed = new List<long>();
        foreach (var id in here)
        {
            if (known(id)) { _since.Remove(id); continue; }
            if (!_since.TryGetValue(id, out var since)) _since[id] = now;
            else if (now - since >= graceSeconds) { overstayed.Add(id); _since.Remove(id); }
        }
        return overstayed;
    }
}

public sealed class PerSender(double everyMs)
{
    readonly Dictionary<long, double> _last = new();

    public bool Lets(long sender, double nowMs)
    {
        if (_last.TryGetValue(sender, out var at) && nowMs - at < everyMs) return false;
        _last[sender] = nowMs;
        return true;
    }

    public void Forget(long sender) => _last.Remove(sender);
}
