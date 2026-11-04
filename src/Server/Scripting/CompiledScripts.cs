using KTable.Core;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class CompiledScripts(Script vm)
{
    public sealed record Compiled(ScriptSafety.Verdict Verdict, DynValue? Chunk, string? Failure);

    readonly Dictionary<string, Compiled> _compiled = new(StringComparer.Ordinal);

    public Compiled For(string code)
    {
        if (_compiled.TryGetValue(code, out var c)) return c;
        var verdict = ScriptSafety.Inspect(code);
        DynValue? chunk = null;
        string? failure = null;
        if (!verdict.Hostile)
        {
            var src = verdict.Script.StartsWith('#') ? "--" + verdict.Script : verdict.Script;
            try { chunk = vm.LoadString("local _ENV = ...; " + src + "\n", null, "script"); }
            catch (InterpreterException e) { failure = e.DecoratedMessage ?? e.Message; }
        }
        return _compiled[code] = new Compiled(verdict, chunk, failure);
    }
}
