namespace KTable.Core.Model;

public sealed class ClockWatch
{
    readonly Dictionary<string, long> _seen = new();

    static string Key(string? place) => place ?? "";

    public void Forget() => _seen.Clear();

    public long? Seen(string? place) => _seen.TryGetValue(Key(place), out long seen) ? seen : null;

    public bool AnyDue(GameClock clock) =>
        clock.All().Any(c => c.Time.Timers != null && _seen.TryGetValue(Key(c.Place), out long seen) && c.Time.Due(seen, c.Time.Now).Any());

    public List<(string? Place, GameTimer Timer)> Rung(GameClock clock)
    {
        var rung = new List<(string?, GameTimer)>();
        var here = new HashSet<string>();
        foreach (var (place, time) in clock.All())
        {
            long now = time.Now;
            here.Add(Key(place));
            if (_seen.TryGetValue(Key(place), out long seen) && now > seen) rung.AddRange(time.Due(seen, now).Select(t => (place, t)));
            _seen[Key(place)] = now;
        }
        foreach (string gone in _seen.Keys.Where(k => !here.Contains(k)).ToList()) _seen.Remove(gone);
        return rung;
    }
}
