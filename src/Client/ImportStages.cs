using System.Globalization;

namespace KTable.Client;

public sealed class ImportStages
{
    public sealed record Finished(string Stage, int Done, int Failed, long Bytes, double Seconds);

    const double RateWindow = 5, LeastForGuess = 3;
    const int LeastStepsForGuess = 3;
    readonly Queue<(double at, long bytes)> _recent = new();
    readonly List<Finished> _finished = new();

    public string? Stage { get; private set; }
    public int Done { get; private set; }
    public int Total { get; private set; }
    public int Failed { get; private set; }
    public long Bytes { get; private set; }
    public long BytesTotal { get; private set; }
    public string Item { get; private set; } = "";
    public double StageStart { get; private set; }
    public double LastNews { get; private set; }
    public IReadOnlyList<Finished> Past => _finished;

    public bool Read(string line, double now)
    {
        var space = line.IndexOf(' ');
        var head = space < 0 ? line : line[..space];
        var rest = space < 0 ? "" : line[(space + 1)..];
        if (head == "STAGE")
        {
            var p = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return true;
            Close(now);
            Stage = p[0];
            Total = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? Math.Max(0, t) : 0;
            Done = 0;
            Failed = 0;
            Bytes = 0;
            BytesTotal = 0;
            Item = "";
            StageStart = now;
            LastNews = now;
            _recent.Clear();
            _recent.Enqueue((now, 0));
            return true;
        }
        if (head != "AT") return false;
        var f = rest.Split(' ', 6);
        if (Stage == null || f.Length < 5) return true;
        if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var done) ||
            !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ||
            !long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) ||
            !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytesTotal) ||
            !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var failed)) return true;
        Done = Math.Max(0, done);
        Total = Math.Max(0, total);
        Failed = Math.Max(0, failed);
        Bytes = Math.Max(0, bytes);
        BytesTotal = Math.Max(0, bytesTotal);
        Item = f.Length > 5 ? f[5].Trim() : "";
        LastNews = now;
        _recent.Enqueue((now, Bytes));
        while (_recent.Count > 2 && now - _recent.Peek().at > RateWindow) _recent.Dequeue();
        return true;
    }

    public void End(double now) => Close(now);

    public double? Share => BytesTotal > 0 ? Math.Clamp(Bytes / (double)BytesTotal, 0, 1)
        : Total > 0 ? Math.Clamp(Done / (double)Total, 0, 1) : null;

    public double? BytesPerSecond(double now)
    {
        if (_recent.Count < 2 || Bytes == 0) return null;
        var (at, bytes) = _recent.Peek();
        var span = Math.Max(now, LastNews) - at;
        return span < 1 ? null : (Bytes - bytes) / span;
    }

    public double? SecondsLeft(double now)
    {
        var spent = now - StageStart;
        if (spent < LeastForGuess || Share is not { } share || share <= 0 || share >= 1) return null;
        if (BytesTotal <= 0 && Done < LeastStepsForGuess) return null;
        return spent * (1 - share) / share;
    }

    void Close(double now)
    {
        if (Stage == null) return;
        _finished.Add(new Finished(Stage, Done, Failed, Bytes, now - StageStart));
        Stage = null;
    }
}
