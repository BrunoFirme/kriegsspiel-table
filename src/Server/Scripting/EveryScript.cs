using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class EveryScript(ScriptHost host)
{
    public const string ModulePrefix = "__module_any_";

    public bool Listens(string hook) =>
        host.Global?.Env.Get(hook).Type == DataType.Function || host.PieceScripts.Values.Any(ps => ps.Enabled && ps.Env.Get(ModulePrefix + hook).Type == DataType.Function);

    public bool Refuses(string hook, params DynValue[] args)
    {
        bool refused = host.Global is { } g && g.Env.Get(hook).Type == DataType.Function && host.Call(g, hook, args) is { Type: DataType.Boolean, Boolean: false };
        var name = ModulePrefix + hook;
        foreach (var ps in host.PieceScripts.Values.ToList())
            if (ps.Enabled && ps.Env.Get(name).Type == DataType.Function && host.Call(ps, name, args) is { Type: DataType.Boolean, Boolean: false }) refused = true;
        return refused;
    }

    public void Tell(string hook, params DynValue[] args)
    {
        if (host.Global is { } g) host.Call(g, hook, args);
        var name = ModulePrefix + hook;
        foreach (var ps in host.PieceScripts.Values.ToList())
            if (ps.Enabled && ps.Env.Get(name).Type == DataType.Function) host.Guard(ps.Id, () => host.Call(ps, name, args));
    }
}
