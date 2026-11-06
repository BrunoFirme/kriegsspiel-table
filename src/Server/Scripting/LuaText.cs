using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class LuaText
{
    public static string Inspect(DynValue v, Func<string, string> nameOf, int depth = 0)
    {
        switch (v.Type)
        {
            case DataType.String: return depth == 0 ? v.String : System.Text.Json.JsonSerializer.Serialize(v.String);
            case DataType.Nil or DataType.Void: return "nil";
            case DataType.Number: return v.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            case DataType.Boolean: return v.Boolean ? "true" : "false";
            case DataType.Function or DataType.ClrFunction: return "function";
            case DataType.Table:
            {
                var t = v.Table;
                if (t.Get("id").Type == DataType.String && t.MetaTable?.Get("__tostring").Type == DataType.ClrFunction) return $"piece {t.Get("id").String} ({nameOf(t.Get("id").String)})";
                if (depth >= 3) return "{…}";
                var parts = new List<string>();
                int n = 0;
                foreach (var pair in t.Pairs)
                {
                    if (++n > 40) { parts.Add("…"); break; }
                    var key = pair.Key.Type == DataType.String ? pair.Key.String : "[" + Inspect(pair.Key, nameOf, depth + 1) + "]";
                    parts.Add(key + " = " + Inspect(pair.Value, nameOf, depth + 1));
                }
                return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
            }
            default: return v.ToPrintString();
        }
    }
}
