using System.Numerics;

namespace KTable.Client.Hand;

public readonly record struct PieceKey(string Layer, string Id);

public readonly record struct Pose(Vector3 At, Quaternion Turn);

public readonly record struct MotionSample(PieceKey Piece, Pose Pose);

public sealed class MotionBuffer
{
    public const double DelaySeconds = 0.12;
    public const double EndAfterSeconds = 0.2;
    public const double SilentForSeconds = 1.5;
    const double ClockRefreshSeconds = 5;
    const double KeepBehindSeconds = 1;

    sealed class Track(long carrier, Pose first)
    {
        public long Carrier { get; } = carrier;
        public List<(double T, Pose Pose)> Samples { get; } = new();
        public Pose First { get; } = first;
        public double Heard { get; set; }
    }

    sealed class Clock
    {
        public double Offset = double.PositiveInfinity, Fresh = double.PositiveInfinity, Since;
    }

    readonly Dictionary<PieceKey, Track> _tracks = new();
    readonly Dictionary<long, Clock> _clocks = new();

    public int Count => _tracks.Count;

    public void Add(long carrier, double t, IEnumerable<MotionSample> samples, double now)
    {
        var clock = Observe(carrier, t, now);
        foreach (var s in samples)
        {
            if (!_tracks.TryGetValue(s.Piece, out var track) || track.Carrier != carrier) _tracks[s.Piece] = track = new Track(carrier, s.Pose);
            track.Heard = now;
            if (track.Samples.Count > 0 && t <= track.Samples[^1].T) continue;
            track.Samples.Add((t, s.Pose));
            var behind = PlayTime(clock, now) - KeepBehindSeconds;
            while (track.Samples.Count > 2 && track.Samples[1].T < behind) track.Samples.RemoveAt(0);
        }
    }

    Clock Observe(long carrier, double t, double now)
    {
        if (!_clocks.TryGetValue(carrier, out var clock)) _clocks[carrier] = clock = new Clock { Since = now };
        var offset = now - t;
        clock.Offset = Math.Min(clock.Offset, offset);
        clock.Fresh = Math.Min(clock.Fresh, offset);
        if (now - clock.Since < ClockRefreshSeconds) return clock;
        clock.Offset = clock.Fresh;
        clock.Fresh = double.PositiveInfinity;
        clock.Since = now;
        return clock;
    }

    static double PlayTime(Clock clock, double now) => now - clock.Offset - DelaySeconds;

    public IEnumerable<(PieceKey Piece, Pose Pose)> Poses(double now)
    {
        foreach (var (piece, track) in _tracks)
        {
            var play = PlayTime(_clocks[track.Carrier], now);
            var samples = track.Samples;
            if (play < samples[0].T) continue;
            var i = samples.FindLastIndex(s => s.T <= play);
            if (i == samples.Count - 1) { yield return (piece, samples[i].Pose); continue; }
            var (t0, a) = samples[i];
            var (t1, b) = samples[i + 1];
            var k = (float)((play - t0) / (t1 - t0));
            yield return (piece, new Pose(Vector3.Lerp(a.At, b.At, k), Quaternion.Slerp(a.Turn, b.Turn, k)));
        }
    }

    public List<(PieceKey Piece, Vector3 From, Vector3 To)> Finished(double now, Func<long, PieceKey, bool> stillHeld)
    {
        var done = new List<(PieceKey, Vector3, Vector3)>();
        foreach (var (piece, track) in _tracks.ToList())
        {
            var play = PlayTime(_clocks[track.Carrier], now);
            var heard = now - track.Heard <= SilentForSeconds;
            if (heard && (stillHeld(track.Carrier, piece) || play <= track.Samples[^1].T + EndAfterSeconds)) continue;
            _tracks.Remove(piece);
            done.Add((piece, track.First.At, track.Samples[^1].Pose.At));
        }
        return done;
    }

    public bool Playing(PieceKey piece) => _tracks.ContainsKey(piece);
}

public sealed class FlightShare
{
    public const double EverySeconds = 1.0 / 30;
    public const double SlackSeconds = 0.001;
    const double ResendStillSeconds = 0.25;
    const float StillWithin = 1e-4f;

    readonly Dictionary<PieceKey, (Pose Pose, double At)> _sent = new();
    double _lastAt = double.NegativeInfinity;

    public event Action<double, IReadOnlyList<MotionSample>>? Send;

    public void Tick(double now, IReadOnlyList<MotionSample> moving)
    {
        if (now - _lastAt < EverySeconds - SlackSeconds) return;
        var still = moving.Select(m => m.Piece).ToHashSet();
        foreach (var gone in _sent.Keys.Where(k => !still.Contains(k)).ToList()) _sent.Remove(gone);
        var due = moving.Where(m => !_sent.TryGetValue(m.Piece, out var s) || Moved(s.Pose, m.Pose) || now - s.At >= ResendStillSeconds).ToList();
        if (due.Count == 0) return;
        _lastAt = now;
        foreach (var m in due) _sent[m.Piece] = (m.Pose, now);
        Send?.Invoke(now, due);
    }

    static bool Moved(Pose a, Pose b) =>
        Vector3.DistanceSquared(a.At, b.At) > StillWithin * StillWithin || 1 - Math.Abs(Quaternion.Dot(a.Turn, b.Turn)) > StillWithin;
}
