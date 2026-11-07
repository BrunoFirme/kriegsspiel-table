using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class EveryScript(ScriptHost host)
{
    public const string ModulePrefix = "__module_any_";

    public void Tell(string hook, params DynValue[] args)
    {
        if (host.Global is { } g) host.Call(g, hook, args);
        var name = ModulePrefix + hook;
        foreach (var ps in host.PieceScripts.Values.ToList())
            if (ps.Enabled && ps.Env.Get(name).Type == DataType.Function) host.Guard(ps.Id, () => host.Call(ps, name, args));
    }
}
