namespace KTable.Server.Scripting;

static class MemoryWatch
{
    const int EveryMs = 10;

    static readonly List<WeakReference<ScriptGuards>> Watched = new();
    static Thread? _thread;

    public static void Watch(ScriptGuards guards)
    {
        lock (Watched)
        {
            Watched.Add(new WeakReference<ScriptGuards>(guards));
            if (_thread != null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "Script memory watch" };
            _thread.Start();
        }
    }

    static void Run()
    {
        var live = new List<ScriptGuards>();
        while (true)
        {
            Thread.Sleep(EveryMs);
            try
            {
                lock (Watched)
                    Watched.RemoveAll(w =>
                    {
                        if (!w.TryGetTarget(out var g)) return true;
                        live.Add(g);
                        return false;
                    });
                if (live.Count == 0) continue;
                long heap = GC.GetTotalMemory(false);
                foreach (var g in live) g.Hurry(heap);
            }
            catch (Exception e) { KTable.Core.BestEffort.Note(e); continue; }
            finally { live.Clear(); }
        }
    }
}
