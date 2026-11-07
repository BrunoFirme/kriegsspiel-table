using System.Globalization;
using KTable.Core.Model;
using KTable.Core.Panels;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class UiTrees
{
    const string NumberFormat = "G15";

    public static string? ClrText(DynValue v) => v.Type switch
    {
        DataType.String => v.String,
        DataType.Number => v.Number.ToString(NumberFormat, CultureInfo.InvariantCulture),
        DataType.Boolean => v.Boolean ? "True" : "False",
        DataType.Nil or DataType.Void => null,
        _ => throw new ScriptRuntimeException($"cannot convert a {v.Type.ToString().ToLowerInvariant()} to a clr type System.String"),
    };

    static string ObjectText(DynValue v) => v.Type switch
    {
        DataType.String => v.String,
        DataType.Number => v.Number.ToString(NumberFormat, CultureInfo.InvariantCulture),
        DataType.Boolean => v.Boolean ? "True" : "False",
        DataType.Table => "MoonSharp.Interpreter.Table",
        DataType.Function or DataType.ClrFunction => "MoonSharp.Interpreter.Closure",
        _ => throw new ScriptRuntimeException("Object reference not set to an instance of an object"),
    };

    static string PairText(DynValue v) => v.Type switch
    {
        DataType.Nil or DataType.Void => "nil",
        DataType.Boolean => v.Boolean ? "true" : "false",
        DataType.Number => v.Number.ToString(NumberFormat, CultureInfo.InvariantCulture),
        DataType.String => "\"" + v.String + "\"",
        DataType.Table => "(Table)",
        DataType.Function or DataType.ClrFunction => "(Function)",
        _ => "(???)",
    };

    sealed class Budget { public int Nodes; }

    public static string FromTable(Table list)
    {
        var b = new Budget();
        var roots = new List<UiNode>();
        if (list.Length == 0 && list.Get("tag").IsNotNil()) roots.Add(Node(list, 1, b));
        else
            for (int i = 1; i <= list.Length; i++)
            {
                var e = list.Get(i);
                if (e.Type != DataType.Table) throw new ScriptRuntimeException("Specified cast is not valid.");
                if (e.Table.Get("tag").IsNotNil()) roots.Add(Node(e.Table, 1, b));
            }
        return PanelData.Write(roots);
    }

    static UiNode Node(Table t, int depth, Budget b)
    {
        if (depth > PanelBuild.MaxDepth) throw new ScriptRuntimeException($"ui.set: the panel nests deeper than {PanelBuild.MaxDepth} levels (a table that holds itself?)");
        if (++b.Nodes > PanelBuild.MaxElements) throw new ScriptRuntimeException($"ui.set: more than {PanelBuild.MaxElements} elements in one panel tree");
        var n = new UiNode { Tag = ObjectText(t.Get("tag")) };
        if (t.Get("value").IsNotNil()) n.Value = ObjectText(t.Get("value"));
        if (t.Get("attributes") is { Type: DataType.Table } attrs)
            foreach (var pair in attrs.Table.Pairs)
            {
                var k = PairText(pair.Key);
                if (!n.Attributes.TryAdd(k, PairText(pair.Value))) throw new ScriptRuntimeException("An item with the same key has already been added. Key: " + k);
            }
        else if (t.Get("attributes").IsNotNil()) throw new ScriptRuntimeException("Specified cast is not valid.");
        if (t.Get("children") is not { Type: DataType.Table } kids)
        {
            if (t.Get("children").IsNotNil()) throw new ScriptRuntimeException("Specified cast is not valid.");
            return n;
        }
        if (kids.Table.Length == 0 && kids.Table.Get("tag").IsNotNil())
        {
            n.Children.Add(Node(kids.Table, depth + 1, b));
            return n;
        }
        for (int i = 1; i <= kids.Table.Length; i++)
        {
            var c = kids.Table.Get(i);
            if (c.Type != DataType.Table) throw new ScriptRuntimeException("Specified cast is not valid.");
            if (c.Table.Get("tag").IsNotNil()) n.Children.Add(Node(c.Table, depth + 1, b));
        }
        return n;
    }

    public static DynValue Tables(Script vm, IEnumerable<UiNode> roots)
    {
        var t = DynValue.NewTable(vm);
        foreach (var n in roots) t.Table.Append(NodeTable(vm, n));
        return t;
    }

    static DynValue NodeTable(Script vm, UiNode n)
    {
        var t = DynValue.NewTable(vm);
        t.Table["tag"] = DynValue.NewString(n.Tag);
        if (n.Value != null) t.Table["value"] = DynValue.NewString(n.Value);
        t.Table["attributes"] = Attributes(vm, n.Attributes);
        t.Table["children"] = Tables(vm, n.Children);
        return t;
    }

    public static DynValue Attributes(Script vm, IReadOnlyDictionary<string, string> attrs)
    {
        var t = DynValue.NewTable(vm);
        foreach (var (k, v) in attrs) t.Table[k] = DynValue.NewString(v);
        return t;
    }
}
