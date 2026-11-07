using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class ScriptTimer
{
    public int Handle;
    public double Due = double.NaN;
    public long DueFrame = -1;
    public DynValue Fn = DynValue.Nil;
    public DynValue Predicate = DynValue.Nil;
    public double Timeout = double.NaN;
    public DynValue OnTimeout = DynValue.Nil;
    public PieceScript Owner = null!;
    public bool Gone;
}

internal sealed class ScriptTimers
{
    readonly List<ScriptTimer> _timers = new();
    readonly List<(PieceScript owner, DynValue co, int loadFrames)> _resumes = new();
    int _nextTimer = 1;

    public double Now { get; private set; }
    public long Frame { get; private set; }
    public double Delta { get; private set; }

    public List<ScriptTimer> Advance(double nowSeconds)
    {
        Delta = Frame == 0 ? 0 : Math.Max(0, nowSeconds - Now);
        Now = nowSeconds;
        Frame++;
        return _timers.Where(t => (!double.IsNaN(t.Due) && t.Due <= Now) || (t.DueFrame >= 0 && t.DueFrame <= Frame) || t.Predicate.Type == DataType.Function)
            .OrderBy(Kind).ToList();
    }

    static int Kind(ScriptTimer t)
    {
        const int Frames = 0, Seconds = 1, Conditions = 2;
        return t.Predicate.Type == DataType.Function ? Conditions : t.DueFrame >= 0 ? Frames : Seconds;
    }

    public List<(PieceScript owner, DynValue co, int loadFrames)> TakeResumes()
    {
        var resumes = _resumes.ToList();
        _resumes.Clear();
        return resumes;
    }

    public void ResumeNextFrame(PieceScript owner, DynValue co, int loadFrames) => _resumes.Add((owner, co, loadFrames));

    public int Add(ScriptTimer t, PieceScript owner)
    {
        t.Owner = owner;
        t.Handle = _nextTimer++;
        if (t.Fn.Type != DataType.Function) throw new ScriptRuntimeException("wait: the second argument must be a function");
        _timers.Add(t);
        return t.Handle;
    }

    public void Remove(ScriptTimer t) { t.Gone = true; _timers.Remove(t); }

    public int RemoveWhere(Predicate<ScriptTimer> which) => _timers.RemoveAll(t =>
    {
        if (!which(t)) return false;
        t.Gone = true;
        return true;
    });

    public void DropResumes(PieceScript ps) => _resumes.RemoveAll(r => r.owner == ps);

    public void Clear()
    {
        foreach (var t in _timers) t.Gone = true;
        _timers.Clear();
        _resumes.Clear();
    }

    public Dictionary<PieceScript, int> CountByOwner() => _timers.GroupBy(t => t.Owner).ToDictionary(g => g.Key, g => g.Count());

    public void AdoptTimers(PieceScript temp, PieceScript? real)
    {
        foreach (var t in _timers.Where(t => t.Owner == temp).ToList()) { if (real != null) t.Owner = real; else Remove(t); }
    }

    public void AdoptResumes(PieceScript temp, PieceScript? real)
    {
        for (int i = _resumes.Count - 1; i >= 0; i--)
            if (_resumes[i].owner == temp) { if (real != null) _resumes[i] = (real, _resumes[i].co, _resumes[i].loadFrames); else _resumes.RemoveAt(i); }
    }
}
