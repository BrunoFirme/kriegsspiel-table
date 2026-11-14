using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class EveryScript(ScriptHost host)
{
    public const string ModulePrefix = "__module_any_";

    public readonly Dictionary<string, DynValue> HeldBy = new();

    readonly Dictionary<string, ((ScriptGuards By, long Entered, int Scripts) As, bool Heard)> _heard = new();

    bool Heard(string name)
    {
        var guards = host.Guards;
        var now = (guards, guards.Entered, host.PieceScripts.Count);
        if (guards.Nesting == 0 && _heard.TryGetValue(name, out var had) && had.As == now) return had.Heard;
        bool heard = host.PieceScripts.Values.Any(ps => ps.Enabled && ps.Env.Get(name).Type == DataType.Function);
        _heard[name] = (now, heard);
        return heard;
    }

    public bool Listens(string hook) => host.Global?.Env.Get(hook).Type == DataType.Function || Heard(ModulePrefix + hook);

    public bool Refuses(string hook, params DynValue[] args)
    {
        bool refused = host.Global is { } g && g.Env.Get(hook).Type == DataType.Function && host.Call(g, hook, args) is { Type: DataType.Boolean, Boolean: false };
        var name = ModulePrefix + hook;
        if (!Heard(name)) return refused;
        foreach (var ps in host.PieceScripts.Values.ToList())
            if (ps.Enabled && ps.Env.Get(name).Type == DataType.Function && host.Call(ps, name, args) is { Type: DataType.Boolean, Boolean: false }) refused = true;
        return refused;
    }

    public void Tell(string hook, params DynValue[] args)
    {
        if (host.Global is { } g) host.Call(g, hook, args);
        var name = ModulePrefix + hook;
        if (!Heard(name)) return;
        foreach (var ps in host.PieceScripts.Values.ToList())
            if (ps.Enabled && ps.Env.Get(name).Type == DataType.Function) host.Guard(ps.Id, () => host.Call(ps, name, args));
    }
}
