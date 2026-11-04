using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class ScriptGuards
{
    const int IdleWorkers = 16;

    readonly Script _vm;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    public double Elapsed => _clock.Elapsed.TotalMilliseconds;
    public double Deadline { get; set; } = double.PositiveInfinity;
    public double BudgetMs { get; set; } = ScriptHost.CallBudgetMs;
    public int Nesting { get; set; }
    public int Made { get; set; }

    public static readonly string MemoryOverrun = $"more than {ScriptHost.MaxCallMemory / (1024 * 1024)} MB of memory";

    long _allocStart, _heapStart, _nextLook;
    volatile Coroutine? _running;

    readonly DynValue _done, _workerFn;
    DynValue _nextFn = DynValue.Nil;
    DynValue[] _nextArgs = Array.Empty<DynValue>();
    readonly Stack<DynValue> _idle = new();
    readonly ConditionalWeakTable<Coroutine, object> _workers = new();

    sealed class ScriptStopped(string message) : Exception(message);

    public ScriptGuards(Script vm)
    {
        _vm = vm;
        _done = DynValue.NewTable(_vm);
        var take = DynValue.NewCallback((_, _) =>
        {
            var call = new DynValue[_nextArgs.Length + 1];
            call[0] = _nextFn;
            _nextArgs.CopyTo(call, 1);
            _nextFn = DynValue.Nil;
            _nextArgs = Array.Empty<DynValue>();
            return DynValue.NewTuple(call);
        });
        var worker = _vm.LoadString("local yield, done, take = coroutine.yield, ... local function run(fn, ...) return fn(...) end return function() while true do yield(done, run(take())) end end", null, "worker");
        _workerFn = _vm.Call(worker, _done, take);

        var co = _vm.Globals.Get("coroutine");
        if (co.Type == DataType.Table)
        {
            co.Table["create"] = DynValue.NewCallback((_, a) =>
            {
                var f = a.Count > 0 ? a[0] : DynValue.Nil;
                if (f.Type is not (DataType.Function or DataType.ClrFunction)) throw new ScriptRuntimeException("bad argument #1 to 'create' (function expected)");
                if (++Made > ScriptHost.MaxCoroutinesPerCall) throw new ScriptRuntimeException($"coroutine.create: more than {ScriptHost.MaxCoroutinesPerCall} coroutines in one call");
                var c = _vm.CreateCoroutine(f);
                if (c.Coroutine.Type == Coroutine.CoroutineType.Coroutine) c.Coroutine.AutoYieldCounter = ScriptHost.YieldEvery;
                return c;
            });
            co.Table["resume"] = DynValue.NewCallback((ctx, a) => ResumeGuarded(ctx, a));
        }

        var tonumber = _vm.Globals.Get("tonumber");
        if (tonumber.Type == DataType.ClrFunction)
            _vm.Globals["tonumber"] = DynValue.NewCallback((ctx, a) => a.Count >= 2 && !a[1].IsNil() ? ToNumberInBase(a[0], a[1]) : tonumber.Callback.Invoke(ctx, a.GetArray()));

        _vm.Call(_vm.LoadString(Library, null, "library"), DynValue.NewNumber(ScriptHost.MaxRepLength));
        MemoryWatch.Watch(this);
    }

    public void BeginCall()
    {
        Made = 0;
        _allocStart = GC.GetAllocatedBytesForCurrentThread();
        _heapStart = GC.GetTotalMemory(false);
        _nextLook = ScriptHost.MaxCallMemory;
    }

    public Coroutine? Enter(Coroutine c)
    {
        var before = _running;
        if (c.Type == Coroutine.CoroutineType.Coroutine) c.AutoYieldCounter = ScriptHost.YieldEvery;
        _running = c;
        return before;
    }

    public void Leave(Coroutine? before) => _running = before;

    public bool OverMemory()
    {
        long made = GC.GetAllocatedBytesForCurrentThread() - _allocStart;
        if (made < _nextLook) return false;
        _nextLook = made + ScriptHost.MaxCallMemory / 4;
        if (GC.GetTotalMemory(false) - _heapStart < ScriptHost.MaxCallMemory) return false;
        GC.Collect();
        if (GC.GetTotalMemory(false) - _heapStart >= ScriptHost.MaxCallMemory) return true;
        _nextLook = made + ScriptHost.MaxCallMemory;
        return false;
    }

    internal void Hurry(long heap)
    {
        if (_running is { } c && heap - _heapStart >= ScriptHost.MaxCallMemory && c.Type == Coroutine.CoroutineType.Coroutine)
            c.AutoYieldCounter = 1;
    }

    public DynValue Worker(DynValue fn, DynValue[] args)
    {
        var co = _idle.Count > 0 ? _idle.Pop() : NewWorker();
        _nextFn = fn;
        _nextArgs = args;
        return co;
    }

    DynValue ResumeGuarded(ScriptExecutionContext ctx, CallbackArguments a)
    {
        var h = a.AsType(0, "resume", DataType.Thread, false);
        var c = h.Coroutine;
        if (_workers.TryGetValue(c, out _)) return DynValue.NewTuple(DynValue.False, DynValue.NewString("cannot resume a call the host is running"));
        if (Nesting >= ScriptHost.MaxNesting) throw new ScriptRuntimeException($"coroutines inside calls more than {ScriptHost.MaxNesting} deep");
        Nesting++;
        var outer = Enter(c);
        try
        {
            var r = c.Resume(ctx, a.GetArray(1));
            while (r.Type == DataType.YieldRequest && r.YieldRequest.Forced)
            {
                if (Elapsed > Deadline) throw new ScriptStopped($"more than {BudgetMs:0} ms in one call");
                if (OverMemory()) throw new ScriptStopped($"{MemoryOverrun} in one call");
                Enter(c);
                r = c.Resume(ctx);
            }
            if (r.Type == DataType.Tuple) return DynValue.NewTuple(new[] { DynValue.True }.Concat(r.Tuple).ToArray());
            return r.Type == DataType.Void ? DynValue.True : DynValue.NewTuple(DynValue.True, r);
        }
        catch (ScriptRuntimeException e) { return DynValue.NewTuple(DynValue.False, DynValue.NewString(e.Message)); }
        finally { Nesting--; Leave(outer); }
    }

    static DynValue ToNumberInBase(DynValue e, DynValue b)
    {
        if (b.CastToNumber() is not { } radixValue || double.IsNaN(radixValue)) throw new ScriptRuntimeException("bad argument #2 to 'tonumber' (number expected)");
        int radix = (int)radixValue;
        if (radix < 2 || radix > 36) throw new ScriptRuntimeException("bad argument #2 to 'tonumber' (base out of range)");
        string s = e.Type switch
        {
            DataType.String => e.String,
            DataType.Number => e.Number.ToString(CultureInfo.InvariantCulture),
            _ => throw new ScriptRuntimeException("bad argument #1 to 'tonumber' (string expected)"),
        };
        s = s.Trim();
        bool neg = s.StartsWith('-');
        if (neg) s = s[1..];
        if (radix == 16 && (s.StartsWith("0x") || s.StartsWith("0X"))) s = s[2..];
        if (s.Length == 0) return DynValue.Nil;
        double n = 0;
        foreach (var ch in s)
        {
            int d = ch is >= '0' and <= '9' ? ch - '0' : ch is >= 'a' and <= 'z' ? ch - 'a' + 10 : ch is >= 'A' and <= 'Z' ? ch - 'A' + 10 : 99;
            if (d >= radix) return DynValue.Nil;
            n = n * radix + d;
        }
        return DynValue.NewNumber(neg ? -n : n);
    }

    DynValue NewWorker()
    {
        var w = _vm.CreateCoroutine(_workerFn);
        w.Coroutine.AutoYieldCounter = ScriptHost.YieldEvery;
        _workers.AddOrUpdate(w.Coroutine, _done);
        return w;
    }

    public void Release(DynValue worker) { if (_idle.Count < IdleWorkers) _idle.Push(worker); }

    public bool Finished(DynValue r, out DynValue result)
    {
        result = DynValue.Void;
        if (r.Type == DataType.Table && r.Table == _done.Table) return true;
        if (r.Type != DataType.Tuple || r.Tuple.Length == 0 || r.Tuple[0].Type != DataType.Table || r.Tuple[0].Table != _done.Table) return false;
        result = r.Tuple.Length == 2 ? r.Tuple[1] : DynValue.NewTuple(r.Tuple.Skip(1).ToArray());
        return true;
    }

    public static string Describe(Exception e) => e switch
    {
        ScriptStopped s => s.Message,
        IndexOutOfRangeException => "out of stack (a function that calls itself without end?)",
        OutOfMemoryException or InsufficientExecutionStackException => "out of memory",
        InterpreterException ie => ie.DecoratedMessage ?? ie.Message,
        _ => $"{e.GetType().Name}: {e.Message}",
    };

    const string Library = """
        local sort, gsub, find, sub, rep, concat = table.sort, string.gsub, string.find, string.sub, string.rep, table.concat
        local type, tostring, error, pcall, floor = type, tostring, error, pcall, math.floor
        local pack, unpack = table.pack, table.unpack
        local create, resume = coroutine.create, coroutine.resume
        local MAX_REP = ...

        local function less(a, b) return a < b end

        local function msort(t, lt, a, b, tmp)
          if b - a < 12 then
            for i = a + 1, b do
              local v, j = t[i], i - 1
              while j >= a and lt(v, t[j]) do t[j + 1] = t[j]; j = j - 1 end
              t[j + 1] = v
            end
            return
          end
          local m = floor((a + b) / 2)
          msort(t, lt, a, m, tmp); msort(t, lt, m + 1, b, tmp)
          if not lt(t[m + 1], t[m]) then return end
          local i, j, k = a, m + 1, a
          while i <= m and j <= b do
            if lt(t[j], t[i]) then tmp[k] = t[j]; j = j + 1 else tmp[k] = t[i]; i = i + 1 end
            k = k + 1
          end
          while i <= m do tmp[k] = t[i]; i = i + 1; k = k + 1 end
          for x = a, k - 1 do t[x] = tmp[x] end
        end

        function table.sort(t, lt)
          if type(t) ~= "table" or (lt ~= nil and type(lt) ~= "function") then return sort(t, lt) end
          local n = #t
          if n < 2 then return t end
          if lt == nil then
            local kind = type(t[1])
            if kind == "number" or kind == "string" then
              local same = true
              for i = 2, n do if type(t[i]) ~= kind then same = false break end end
              if same then return sort(t) end
            end
            lt = less
          end
          msort(t, lt, 1, n, {})
          return t
        end

        function string.gsub(s, p, repl, n)
          local rt = type(repl)
          if type(s) == "number" then s = tostring(s) end
          if type(p) == "number" then p = tostring(p) end
          if (rt ~= "function" and rt ~= "table") or type(s) ~= "string" or type(p) ~= "string" or (n ~= nil and type(n) ~= "number") then return gsub(s, p, repl, n) end
          local max, len = n or (#s + 1), #s
          local anchor = sub(p, 1, 1) == "^"
          local out, count, pos = {}, 0, 1
          while count < max do
            local r = pack(find(s, p, pos))
            local st, en = r[1], r[2]
            if st == nil then break end
            out[#out + 1] = sub(s, pos, st - 1)
            local whole = sub(s, st, en)
            local v
            if rt == "function" then
              if r.n > 2 then v = repl(unpack(r, 3, r.n)) else v = repl(whole) end
            else
              v = repl[r.n > 2 and r[3] or whole]
            end
            if v == nil or v == false then v = whole
            elseif type(v) ~= "string" and type(v) ~= "number" then error("invalid replacement value (a " .. type(v) .. ")", 2) end
            out[#out + 1] = tostring(v)
            count = count + 1
            if en >= st then pos = en + 1
            else
              if st <= len then out[#out + 1] = sub(s, st, st) end
              pos = st + 1
            end
            if anchor or pos > len + 1 then break end
          end
          out[#out + 1] = sub(s, pos)
          return concat(out), count
        end

        function string.rep(s, n, sep)
          if type(n) == "number" and n > 0 and (type(s) == "string" or type(s) == "number") then
            local size = n * (#tostring(s) + ((type(sep) == "string" or type(sep) == "number") and #tostring(sep) or 0))
            if size > MAX_REP then error("string.rep: the result would be " .. floor(size) .. " characters long", 2) end
          end
          return rep(s, n, sep)
        end

        function xpcall(f, handler, ...)
          local r = pack(pcall(f, ...))
          if r[1] or handler == nil then return unpack(r, 1, r.n) end
          return false, (handler(r[2]))
        end

        local function wrapped(ok, ...)
          if not ok then error((...), 2) end
          return ...
        end
        function coroutine.wrap(f)
          local co = create(f)
          return function(...) return wrapped(resume(co, ...)) end
        end
        """;
}
