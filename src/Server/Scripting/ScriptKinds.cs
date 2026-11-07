using KTable.Core.Model;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

internal sealed partial class PieceHandles
{
    internal DynValue Kinds(DynValue filter)
    {
        string? link = null, type = null, tag = null;
        if (filter.Type == DataType.Table)
        {
            link = filter.Table.Get("link").CastToString();
            type = filter.Table.Get("type").CastToString();
            tag = filter.Table.Get("tag").CastToString();
        }
        var t = T();
        foreach (var def in host.Server.Store?.AllDefs ?? [])
        {
            if (link != null && !(def.Sources?.ContainsValue(link) ?? false)) continue;
            if (type != null && def.ModuleType != type) continue;
            if (tag != null && !def.Tags.Contains(tag)) continue;
            t.Table.Append(KindTable(def));
        }
        return t;
    }

    DynValue KindTable(PieceDef def)
    {
        var k = T();
        k.Table["id"] = S(def.Id);
        k.Table["name"] = S(def.Name);
        k.Table["tags"] = ListTable(def.Tags.Select(tg => S(tg)));
        k.Table["source"] = def.ModuleType != null ? SourceTable(def) : DynValue.Nil;
        return k;
    }
}
