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

    DynValue StateSources(PieceDef def)
    {
        var t = T();
        foreach (var st in def.States)
        {
            var m = st.Module;
            var e = T();
            if (m != null)
            {
                e.Table["type"] = S(m.Type);
                e.Table["guid"] = S(m.Guid);
                e.Table["description"] = S(m.Description ?? "");
                e.Table["gm_notes"] = S(m.GmNotes ?? "");
                e.Table["memo"] = S(m.Memo ?? "");
                e.Table["script_state"] = S(m.ScriptState ?? "");
                e.Table["tags"] = ListTable((m.Tags ?? new()).Select(tg => S(tg)));
            }
            e.Table["script"] = S(st.Script ?? "");
            t.Table.Append(e);
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
