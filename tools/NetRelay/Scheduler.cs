using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KTable.NetRelay;

public static class Clock
{
    static readonly Stopwatch Watch = Stopwatch.StartNew();

    public static double Now => Watch.Elapsed.TotalMilliseconds;
}

public sealed class Scheduler : IDisposable
{
    const double FineSpinBelowMs = 2;
    const double CoarseSpinBelowMs = 16;
    const uint OneMillisecond = 1;
    const uint TimerGranted = 0;

    readonly PriorityQueue<Action, (double At, long Order)> _due = new();
    readonly object _gate = new();
    readonly Thread _thread;
    readonly double _spinBelowMs;
    long _order;
    bool _stopping;

    public Scheduler()
    {
        _spinBelowMs = OperatingSystem.IsWindows() && timeBeginPeriod(OneMillisecond) != TimerGranted ? CoarseSpinBelowMs : FineSpinBelowMs;
        _thread = new Thread(Run) { IsBackground = true, Name = "deliveries", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint milliseconds);

    public void At(double at, Action deliver)
    {
        lock (_gate)
        {
            _due.Enqueue(deliver, (at, _order++));
            Monitor.Pulse(_gate);
        }
    }

    void Run()
    {
        while (Next() is { } deliver) deliver();
    }

    Action? Next()
    {
        while (true)
        {
            lock (_gate)
            {
                if (_stopping) return null;
                if (!_due.TryPeek(out _, out var due)) { Monitor.Wait(_gate); continue; }
                var wait = due.At - Clock.Now;
                if (wait <= 0) return _due.Dequeue();
                if (wait > _spinBelowMs) { Monitor.Wait(_gate, TimeSpan.FromMilliseconds(wait - _spinBelowMs)); continue; }
            }
            Thread.Yield();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            Monitor.PulseAll(_gate);
        }
        _thread.Join();
    }
}
