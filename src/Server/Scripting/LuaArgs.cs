using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class LuaArgs
{
    public static DynValue S(string s) => DynValue.NewString(s);
    public static DynValue N(double n) => DynValue.NewNumber(n);
    public static DynValue B(bool b) => DynValue.NewBoolean(b);
    public static DynValue Arg(CallbackArguments a, int i) => i < a.Count ? a[i] : DynValue.Nil;
    public static DynValue Fn(Func<CallbackArguments, DynValue> f) => DynValue.NewCallback((_, a) => f(a));

    static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000", ["white"] = "#FFFFFF", ["red"] = "#DA1917", ["green"] = "#30B32B", ["blue"] = "#1F87FF",
        ["yellow"] = "#E7E52C", ["orange"] = "#F3701F", ["purple"] = "#A020F0", ["pink"] = "#F570CE", ["brown"] = "#703A16",
        ["teal"] = "#21B19B", ["grey"] = "#7F7F7F", ["gray"] = "#7F7F7F",
    };

    public static string? ColorOf(DynValue v)
    {
        if (v.Type == DataType.String) return NamedColors.TryGetValue(v.String, out var named) ? named : v.String;
        if (v.Type != DataType.Table) return null;
        double C(int i, string k)
        {
            var byKey = v.Table.Get(k);
            if (byKey.Type == DataType.Number) return byKey.Number;
            var byIndex = v.Table.Get(i);
            return byIndex.Type == DataType.Number ? byIndex.Number : (i == 4 ? 1 : 0);
        }
        int To(double x) => (int)Math.Clamp(Math.Round(x * 255), 0, 255);
        var a = C(4, "a");
        var s = $"#{To(C(1, "r")):X2}{To(C(2, "g")):X2}{To(C(3, "b")):X2}";
        return a < 0.999 ? s + To(a).ToString("X2") : s;
    }

    public static List<string> Strings(DynValue v)
    {
        var l = new List<string>();
        if (v.Type == DataType.Table) foreach (var x in v.Table.Values) if (x.CastToString() is { } s && s.Length > 0) l.Add(s);
        return l;
    }

    public static long PeerOf(DynValue v) => v.Type == DataType.Number ? (long)v.Number : v.Type == DataType.Table && v.Table.Get("id").Type == DataType.Number ? (long)v.Table.Get("id").Number : -1;

    public static string IdOf(DynValue v) => v.Type == DataType.Table && v.Table.Get("id").Type == DataType.String ? v.Table.Get("id").String : v.CastToString() ?? "";
}
