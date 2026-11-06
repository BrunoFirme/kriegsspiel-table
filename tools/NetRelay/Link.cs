namespace KTable.NetRelay;

public enum Direction { Up, Down }

public enum Fate { Delivered, Lost, Overflow }

public readonly record struct Passage(Fate Fate, double At, double? CopyAt)
{
    public static readonly Passage Dropped = new(Fate.Lost, 0, null);
    public static readonly Passage Overflowed = new(Fate.Overflow, 0, null);
}

public sealed class Link(Profile profile, Direction direction, Random? random = null)
{
    const int UdpIpHeaderBytes = 28;
    const double BitsPerByte = 8;
    const double LateByAtLeastMs = 10;

    readonly Random _random = random ?? new Random();
    readonly object _gate = new();
    Profile _profile = profile;
    double _freeAt, _lastArrival;
    bool _losing;

    public Profile Profile
    {
        get { lock (_gate) return _profile; }
        set { lock (_gate) _profile = value; }
    }

    public double QueuedMs(double now)
    {
        lock (_gate) return Math.Max(0, _freeAt - now);
    }

    public Passage Pass(int bytes, double at)
    {
        lock (_gate)
        {
            var p = _profile;
            if (Losing(p)) return Passage.Dropped;
            var sent = at;
            var kbps = direction == Direction.Up ? p.UpKbps : p.DownKbps;
            if (kbps > 0)
            {
                var start = Math.Max(at, _freeAt);
                if (start - at > p.QueueMs) return Passage.Overflowed;
                sent = _freeAt = start + (bytes + UdpIpHeaderBytes) * BitsPerByte / kbps;
            }
            var arrival = sent + Math.Max(0, p.PingMs / 2 + (_random.NextDouble() * 2 - 1) * p.JitterMs);
            if (Chance(p.ReorderPercent)) arrival += Math.Max(p.JitterMs, LateByAtLeastMs);
            else _lastArrival = arrival = Math.Max(arrival, _lastArrival);
            return new(Fate.Delivered, arrival, Chance(p.DuplicatePercent) ? arrival : null);
        }
    }

    bool Losing(Profile p)
    {
        var loss = p.LossPercent / 100;
        if (loss <= 0) return _losing = false;
        if (loss >= 1) return _losing = true;
        var leave = 1 / p.BurstLength;
        var enter = Math.Min(1, loss * leave / (1 - loss));
        return _losing = _losing ? _random.NextDouble() >= leave : _random.NextDouble() < enter;
    }

    bool Chance(double percent) => percent > 0 && _random.NextDouble() * 100 < percent;
}
