using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class ScriptLibraries
{
    static readonly string[] Names = ["string", "table", "math", "os", "bit32", "coroutine"];

    const string MergedPairs = """
        local base = ...
        return function(own)
          local merged = {}
          for k, v in next, base do merged[k] = v end
          for k, v in next, own do merged[k] = v end
          return next, merged, nil
        end
        """;

    readonly Script _vm;
    readonly Func<Table?> _currentEnv;
    readonly Dictionary<string, Table> _metas = new(StringComparer.Ordinal);
    readonly Table? _strings;

    public ScriptLibraries(Script vm, Func<Table?> currentEnv)
    {
        _vm = vm;
        _currentEnv = currentEnv;
        var pairs = vm.LoadString(MergedPairs, null, "libraries");
        foreach (var name in Names)
        {
            if (vm.Globals.Get(name) is not { Type: DataType.Table } lib) continue;
            var meta = new Table(vm);
            meta["__index"] = lib;
            meta["__pairs"] = vm.Call(pairs, lib);
            _metas[name] = meta;
        }
        _strings = vm.Globals.Get("string") is { Type: DataType.Table } s ? s.Table : null;
        if (_strings == null) return;
        var forStrings = new Table(vm);
        forStrings["__index"] = DynValue.NewCallback((_, args) => StringMember(args.Count > 1 ? args[1] : DynValue.Nil));
        vm.SetTypeMetatable(DataType.String, forStrings);
    }

    public void Give(Table env)
    {
        foreach (var (name, meta) in _metas) env[name] = new Table(_vm) { MetaTable = meta };
    }

    DynValue StringMember(DynValue key)
    {
        if (_currentEnv() is { } env && env.RawGet("string") is { Type: DataType.Table } own && own.Table.RawGet(key) is { } mine && !mine.IsNil()) return mine;
        return _strings?.Get(key) ?? DynValue.Nil;
    }
}
