using System.Text;

namespace KTable.Converter;

public static class Progress
{
    static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);
    const int LongestItem = 120;
    static readonly object Gate = new();
    static string _stage = "", _item = "", _said = "";
    static int _done, _total, _failed;
    static long _bytes, _bytesTotal;
    static Timer? _ticker;

    public static void Stage(string stage, int total = 0, long bytesTotal = 0)
    {
        lock (Gate)
        {
            Say();
            _stage = stage;
            _total = total;
            _bytesTotal = bytesTotal;
            _item = "";
            _said = "";
            Interlocked.Exchange(ref _done, 0);
            Interlocked.Exchange(ref _failed, 0);
            Interlocked.Exchange(ref _bytes, 0);
            Console.WriteLine($"STAGE {stage} {total}");
            _ticker ??= new Timer(_ => KTable.Core.BestEffort.Try(Tick), null, Every, Every);
        }
    }

    public static void On(string? item) => Volatile.Write(ref _item, Clean(item));

    public static void Step(string? item = null)
    {
        if (item != null) On(item);
        Interlocked.Increment(ref _done);
    }

    public static void Failed() => Interlocked.Increment(ref _failed);

    public static void Bytes(long n) => Interlocked.Add(ref _bytes, n);

    public static void End()
    {
        lock (Gate)
        {
            Say();
            _ticker?.Dispose();
            _ticker = null;
            _stage = "";
        }
    }

    static void Tick() { lock (Gate) Say(); }

    static void Say()
    {
        if (_stage.Length == 0) return;
        var line = $"AT {Volatile.Read(ref _done)} {_total} {Interlocked.Read(ref _bytes)} {_bytesTotal} {Volatile.Read(ref _failed)} {Volatile.Read(ref _item)}".TrimEnd();
        if (line == _said) return;
        _said = line;
        Console.WriteLine(line);
    }

    static string Clean(string? item)
    {
        if (string.IsNullOrWhiteSpace(item)) return "";
        var sb = new StringBuilder(Math.Min(item.Length, LongestItem));
        foreach (var c in item)
        {
            if (sb.Length >= LongestItem) break;
            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString().Trim();
    }
}
