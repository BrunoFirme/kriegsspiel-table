using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class LuaJson
{
    public static string Encode(DynValue v)
    {
        var sb = new StringBuilder();
        WriteJson(sb, v, 0);
        return sb.ToString();
    }

    static void WriteJson(StringBuilder sb, DynValue v, int depth)
    {
        switch (v.Type)
        {
            case DataType.Table: WriteJsonTable(sb, v.Table, depth); break;
            case DataType.String: sb.Append(System.Text.Json.JsonSerializer.Serialize(v.String)); break;
            case DataType.Number: sb.Append(v.Number.ToString("R", CultureInfo.InvariantCulture)); break;
            case DataType.Boolean: sb.Append(v.Boolean ? "true" : "false"); break;
            default: sb.Append("null"); break;
        }
    }

    static void WriteJsonTable(StringBuilder sb, Table t, int depth)
    {
        if (depth > 64) throw new ScriptRuntimeException("json.encode: the table nests too deep (a cycle?)");
        var pairs = t.Pairs.ToList();
        int max = 0;
        bool array = pairs.Count > 0;
        foreach (var p in pairs)
        {
            if (p.Key.Type == DataType.Number && p.Key.Number >= 1 && p.Key.Number <= 100_000 && Math.Abs(p.Key.Number - Math.Round(p.Key.Number)) < 1e-9) max = Math.Max(max, (int)p.Key.Number);
            else { array = false; break; }
        }
        if (array)
        {
            sb.Append('[');
            for (int i = 1; i <= max; i++) { if (i > 1) sb.Append(','); WriteJson(sb, t.Get(i), depth + 1); }
            sb.Append(']');
            return;
        }
        sb.Append('{');
        bool first = true;
        foreach (var p in pairs)
        {
            if (!first) sb.Append(',');
            first = false;
            var key = p.Key.Type == DataType.String ? p.Key.String : p.Key.Type == DataType.Number ? p.Key.Number.ToString("R", CultureInfo.InvariantCulture) : p.Key.ToPrintString();
            sb.Append(System.Text.Json.JsonSerializer.Serialize(key)).Append(':');
            WriteJson(sb, p.Value, depth + 1);
        }
        sb.Append('}');
    }

    public static DynValue Decode(Script vm, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DynValue.Nil;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            return FromJson(vm, doc.RootElement);
        }
        catch (System.Text.Json.JsonException e) { throw new ScriptRuntimeException("json.decode: " + e.Message); }
    }

    static DynValue FromJson(Script vm, System.Text.Json.JsonElement e)
    {
        switch (e.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
            {
                var t = new Table(vm);
                foreach (var prop in e.EnumerateObject()) { var v = FromJson(vm, prop.Value); if (!v.IsNil()) t[prop.Name] = v; }
                return DynValue.NewTable(t);
            }
            case System.Text.Json.JsonValueKind.Array:
            {
                var t = new Table(vm);
                int i = 1;
                foreach (var item in e.EnumerateArray()) { var v = FromJson(vm, item); if (!v.IsNil()) t[i] = v; i++; }
                return DynValue.NewTable(t);
            }
            case System.Text.Json.JsonValueKind.String: return DynValue.NewString(e.GetString() ?? "");
            case System.Text.Json.JsonValueKind.Number: return DynValue.NewNumber(e.GetDouble());
            case System.Text.Json.JsonValueKind.True: return DynValue.True;
            case System.Text.Json.JsonValueKind.False: return DynValue.False;
            default: return DynValue.Nil;
        }
    }
}
