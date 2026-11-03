namespace KTable.Client;

public sealed class Pacer
{
    public const int MinInFlight = 2;
    public const int MaxInFlight = 64;
    const double QueueAllowanceSeconds = 0.25;
    const double MeasureEverySeconds = 1;
    const double StallSeconds = 10;

    readonly Func<double> _clock;
    readonly int _chunkBytes;
    readonly Dictionary<string, (double SentAt, int Bytes)> _inFlight = new();
    double _measuredFrom, _lastHeard, _fastestRoundTrip = double.PositiveInfinity;
    long _ackedBytes;
    bool _heldBack;

    public Pacer(Func<double> clock, int chunkBytes)
    {
        _clock = clock;
        _chunkBytes = chunkBytes;
        _measuredFrom = _lastHeard = clock();
    }

    public int Window { get; private set; } = MinInFlight;
    public int InFlight => _inFlight.Count;

    public bool CanSend()
    {
        if (_inFlight.Count > 0 && _clock() - _lastHeard > StallSeconds) { _inFlight.Clear(); Window = MinInFlight; }
        if (_inFlight.Count < Window) return true;
        _heldBack = true;
        return false;
    }

    public void Sent(string key, int bytes)
    {
        var now = _clock();
        if (_inFlight.Count == 0) _lastHeard = now;
        _inFlight[key] = (now, bytes);
    }

    public void Acked(string key)
    {
        if (!_inFlight.Remove(key, out var sent)) return;
        var now = _clock();
        _lastHeard = now;
        _fastestRoundTrip = Math.Min(_fastestRoundTrip, now - sent.SentAt);
        _ackedBytes += sent.Bytes;
        if (now - _measuredFrom >= MeasureEverySeconds) Measure(now);
    }

    void Measure(double now)
    {
        var bytesPerSecond = _ackedBytes / (now - _measuredFrom);
        var fits = (int)Math.Clamp(bytesPerSecond * (_fastestRoundTrip + QueueAllowanceSeconds) / _chunkBytes, MinInFlight, MaxInFlight);
        Window = _heldBack ? fits : Math.Max(Window, fits);
        _heldBack = false;
        _ackedBytes = 0;
        _measuredFrom = now;
    }
}
