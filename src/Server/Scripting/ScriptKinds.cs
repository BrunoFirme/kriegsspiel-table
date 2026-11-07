using KTable.Core.Model;
using KTable.Core.Protocol;
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

    DynValue? MarkSetter(string id, PieceInstance p, PieceDef? def, string key) => key switch
    {
        "set_highlight" => Fn(a => Highlight(id, Arg(a, 0))),
        "set_book_highlight" => Fn(a => BookHighlight(id, Arg(a, 0))),
        "show_contents" => Fn(a => ShowContents(id, p.HoldsPieces(def) || def?.Tags.Contains("bag") == true, Arg(a, 0), Arg(a, 1))),
        "play_effect" => Fn(a => PlayOnce(id, def, Arg(a, 0))),
        "show_glide" => Fn(a => ShowGlide(id, Arg(a, 0))),
        _ => null,
    };

    static readonly string[] BoxSides = ["left", "bottom", "right", "top"];

    DynValue BoxTable(double[] box)
    {
        var t = T();
        for (int i = 0; i < BoxSides.Length; i++) t.Table[BoxSides[i]] = N(box[i]);
        return t;
    }

    DynValue BookHighlight(string id, DynValue box)
    {
        if (box.Type != DataType.Table)
        {
            host.Emit(new SetBookHighlight { Layer = Layers.Umpire, Id = id });
            return DynValue.Nil;
        }
        var sides = BoxSides.Select(side => box.Table.Get(side).CastToNumber()).ToArray();
        if (sides.Any(v => v is null || !double.IsFinite(v.Value))) throw new ScriptRuntimeException("set_book_highlight needs { left =, bottom =, right =, top = } as numbers");
        host.Emit(new SetBookHighlight { Layer = Layers.Umpire, Id = id, Box = sides.Select(v => v!.Value).ToArray() });
        return DynValue.Nil;
    }

    DynValue ShowContents(string id, bool holds, DynValue who, DynValue most)
    {
        var peer = PeerOf(who);
        if (!holds || peer < 0) return DynValue.False;
        var n = most.CastToNumber() is { } m && double.IsFinite(m) && m >= 0 ? (int)Math.Min(m, int.MaxValue) : -1;
        host.Emit(new ShowContents { Layer = Layers.Umpire, Id = id, To = $"p:{peer}", Most = n });
        return DynValue.True;
    }

    DynValue PlayOnce(string id, PieceDef? def, DynValue index)
    {
        if (def?.Tags.Contains("effect") != true) return DynValue.False;
        var n = index.CastToNumber() is { } v && double.IsFinite(v) ? (int)Math.Clamp(v, 0, int.MaxValue) : 0;
        host.Emit(new PlayEffect { Layer = Layers.Umpire, Id = id, Index = n });
        return DynValue.True;
    }

    DynValue ShowGlide(string id, DynValue spec)
    {
        if (spec.Type != DataType.Table) return DynValue.False;
        double? x = spec.Table.Get("x").CastToNumber(), z = spec.Table.Get("z").CastToNumber(), rot = spec.Table.Get("rot").CastToNumber();
        Vec2? to = x is { } gx && z is { } gz && double.IsFinite(gx) && double.IsFinite(gz) ? new Vec2(gx, gz) : null;
        if (rot is { } r && !double.IsFinite(r)) rot = null;
        if (to == null && rot == null) return DynValue.False;
        host.Emit(new GlidePiece { Layer = Layers.Umpire, Id = id, To = to, Rot = rot, Fast = spec.Table.Get("fast").CastToBool() });
        return DynValue.True;
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
