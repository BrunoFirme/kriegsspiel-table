using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class LuaJson
{
    const int MostItems = 100_000;
    const int DeepestNesting = 200;
    const string Indent = "  ";

    public static string Encode(DynValue v, bool pretty = false)
    {
        var sb = new StringBuilder();
        Write(sb, v, new HashSet<Table>(), pretty, "");
        return sb.ToString();
    }

    static void Write(StringBuilder sb, DynValue v, HashSet<Table> parents, bool pretty, string indent)
    {
        switch (v.Type)
        {
            case DataType.Nil or DataType.Void:
                sb.Append("null");
                break;
            case DataType.String:
                Quote(sb, v.String);
                break;
            case DataType.Number:
                sb.Append(NumberText(v.Number));
                break;
            case DataType.Boolean:
                sb.Append(v.Boolean ? "true" : "false");
                break;
            case DataType.Table:
                WriteTable(sb, v.Table, parents, pretty, indent);
                break;
            default:
                throw new ScriptRuntimeException($"JSON.encode() - can't convert {v.Type.ToLuaTypeString()} to JSON");
        }
    }

    static string NumberText(double n)
    {
        if (double.IsNaN(n)) return "null";
        if (double.IsPositiveInfinity(n)) return "1e+9999";
        if (double.IsNegativeInfinity(n)) return "-1e+9999";
        return n.ToString("R", CultureInfo.InvariantCulture);
    }

    static void Quote(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    static void WriteTable(StringBuilder sb, Table t, HashSet<Table> parents, bool pretty, string indent)
    {
        if (!parents.Add(t)) throw new ScriptRuntimeException($"JSON.encode() - table {t} is a child of itself");
        if (parents.Count > DeepestNesting) throw new ScriptRuntimeException("JSON.encode() - the table nests too deep");
        var (keys, max, map) = Shape(t);
        if (max is { } top)
        {
            int count = (int)Math.Min(Math.Floor(top), MostItems);
            sb.Append(pretty ? "[ " : "[");
            for (int i = 1; i <= count; i++)
            {
                if (i > 1) sb.Append(pretty ? ", " : ",");
                Write(sb, t.Get(i), parents, pretty, indent);
            }
            sb.Append(pretty ? " ]" : "]");
        }
        else if (keys is { Count: > 0 })
        {
            WriteObject(sb, keys, map!, parents, pretty, indent);
        }
        else
        {
            sb.Append("[]");
        }
        parents.Remove(t);
    }

    static void WriteObject(StringBuilder sb, List<string> keys, Dictionary<string, DynValue> map, HashSet<Table> parents, bool pretty, string indent)
    {
        var inner = indent + Indent;
        sb.Append(pretty ? "{\n" : "{");
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0) sb.Append(pretty ? ",\n" : ",");
            if (pretty) sb.Append(inner);
            Quote(sb, keys[i]);
            sb.Append(pretty ? ": " : ":");
            Write(sb, map[keys[i]], parents, pretty, inner);
        }
        sb.Append(pretty ? "\n" + indent + "}" : "}");
    }

    static (List<string>? keys, double? max, Dictionary<string, DynValue>? map) Shape(Table t)
    {
        var strings = new List<string>();
        var numbers = new List<double>();
        bool numbersAsText = false;
        double? max = null;
        foreach (var p in t.Pairs)
        {
            if (p.Key.Type == DataType.String) strings.Add(p.Key.String);
            else if (p.Key.Type == DataType.Number)
            {
                numbers.Add(p.Key.Number);
                if (p.Key.Number <= 0 || double.IsPositiveInfinity(p.Key.Number)) numbersAsText = true;
                else if (max == null || p.Key.Number > max) max = p.Key.Number;
            }
            else throw new ScriptRuntimeException("JSON.encode() can't encode table with a key of type" + p.Key.Type.ToLuaTypeString());
        }
        if (strings.Count == 0 && !numbersAsText) return (null, numbers.Count > 0 ? max : null, null);
        strings.Sort(StringComparer.Ordinal);
        var map = new Dictionary<string, DynValue>(StringComparer.Ordinal);
        foreach (var s in strings) map[s] = t.Get(s);
        numbers.Sort();
        foreach (var n in numbers)
        {
            var key = NumberText(n);
            if (map.ContainsKey(key)) throw new ScriptRuntimeException($"JSON.encode() - conflict converting table with mixed-type keys into a JSON object: key {key} exists both as a string and a number.");
            strings.Add(key);
            map[key] = t.Get(n);
        }
        return (strings, null, map);
    }

    public static DynValue Decode(Script vm, string text) => new JsonReader(vm, text).Document();
}
