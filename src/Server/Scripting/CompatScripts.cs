using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class CompatScripts
{
    static readonly string[] Files = ["compat.lua", "compat_calls.lua", "compat_cast.lua"];

    static readonly string[] Namespaces = ["Global", "Player", "Turns", "Notes", "Lighting", "Grid", "Hands", "MusicPlayer", "Backgrounds", "Time", "Timer", "Vector", "Color"];

    public static DynValue Install(Script vm)
    {
        var compat = DynValue.Nil;
        foreach (var file in Files)
        {
            using var stream = typeof(CompatScripts).Assembly.GetManifestResourceStream(file);
            if (stream == null) return compat;
            using var reader = new StreamReader(stream);
            var result = vm.DoString(reader.ReadToEnd(), null, file);
            if (compat.IsNil())
            {
                compat = result;
                vm.Globals["compat"] = compat;
            }
        }
        if (compat.Type != DataType.Table) return compat;
        foreach (var name in Namespaces)
        {
            var v = compat.Table.Get(name);
            if (!v.IsNil()) vm.Globals[name] = v;
        }
        return compat;
    }
}
