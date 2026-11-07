using System.Globalization;
using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

internal sealed partial class PieceHandles
{
    DynValue? DieField(PieceInstance p, PieceDef? def, string key)
    {
        var faces = PieceInstance.FacesOf(def, p);
        switch (key)
        {
            case "faces": return ListTable(faces.Select(FaceTable));
            case "face": return faces.Count > 0 ? N(Math.Clamp(p.ShownFace, 0, faces.Count - 1) + 1) : DynValue.Nil;
            case "value": return faces.Count > 0 ? ValueOf(faces[Math.Clamp(p.ShownFace, 0, faces.Count - 1)].Value) : DynValue.Nil;
            case "worth": return N(p.Worth);
            case "page_offset": return N(def?.PageOffset ?? 0);
            case "face_picture": return p.FacePicture == null ? DynValue.Nil : S(p.FacePicture);
            case "picked_up":
            {
                if (!host.PickedUp.TryGetValue(p.Id, out var at)) return DynValue.Nil;
                var t = T();
                t.Table["x"] = N(at.Pos.X);
                t.Table["y"] = N(at.Y);
                t.Table["z"] = N(at.Pos.Z);
                t.Table["rot"] = N(at.Rot);
                return t;
            }
            case "worth_groups": return N(p.WorthGroups);
            default: return null;
        }
    }

    DynValue FaceTable(DieFace f)
    {
        var t = T();
        t.Table["value"] = ValueOf(f.Value);
        var up = T();
        up.Table["x"] = N(f.Up[0]);
        up.Table["y"] = N(f.Up[1]);
        up.Table["z"] = N(f.Up[2]);
        t.Table["up"] = up;
        var (x, y, z) = DieTurns.RotationOf(f.Up);
        var rot = T();
        rot.Table["x"] = N(x);
        rot.Table["y"] = N(y);
        rot.Table["z"] = N(z);
        t.Table["rotation"] = rot;
        return t;
    }

    static DynValue ValueOf(string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? N(n) : S(v);

    DynValue? DieSetter(string id, PieceInstance p, PieceDef? def, string key)
    {
        const string L = Layers.Umpire;
        var faces = PieceInstance.FacesOf(def, p);
        switch (key)
        {
            case "set_face": return Fn(a =>
            {
                int n = (int)(Arg(a, 0).CastToNumber() ?? 0) - 1;
                if (n < 0 || n >= faces.Count) return DynValue.False;
                host.Emit(new TurnDie { Layer = L, Id = id, Face = n });
                return DynValue.True;
            });
            case "set_value": return Fn(a =>
            {
                var v = Arg(a, 0);
                int n = faces.FindIndex(f => Same(f.Value, v));
                if (n < 0) return DynValue.False;
                host.Emit(new TurnDie { Layer = L, Id = id, Face = n });
                return DynValue.True;
            });
            case "set_faces": return Fn(a =>
            {
                if (Arg(a, 0).Type != DataType.Table) { host.Emit(new SetDieFaces { Layer = L, Id = id, Faces = null }); return DynValue.True; }
                var list = new List<DieFace>();
                foreach (var e in Arg(a, 0).Table.Values)
                {
                    if (e.Type != DataType.Table) return DynValue.False;
                    var value = e.Table.Get("value");
                    if (value.Type is not (DataType.String or DataType.Number)) return DynValue.False;
                    list.Add(new DieFace { Value = value.Type == DataType.Number ? value.Number.ToString(CultureInfo.InvariantCulture) : value.String, Up = UpOf(e.Table) });
                }
                host.Emit(new SetDieFaces { Layer = L, Id = id, Faces = list });
                return DynValue.True;
            });
            case "set_face_picture": return Fn(a =>
            {
                var link = Arg(a, 0).IsNil() ? null : Arg(a, 0).CastToString();
                if (string.IsNullOrWhiteSpace(link) || !ScriptHost.IsWebLink(link)) { host.Emit(new SetFacePicture { Layer = L, Id = id, Picture = link }); return DynValue.Nil; }
                host.FetchThen(link, pic =>
                {
                    if (pic != null && host.Find(id) != null) host.Emit(new SetFacePicture { Layer = L, Id = id, Picture = pic });
                    else host.Report(id, $"no picture came from {link}");
                });
                return DynValue.Nil;
            });
            case "set_worth": return Fn(a =>
            {
                int worth = (int)(Arg(a, 0).CastToNumber() ?? 0), groups = (int)(Arg(a, 1).CastToNumber() ?? p.WorthGroups);
                host.Emit(new SetWorth { Layer = L, Id = id, Worth = worth, Groups = groups });
                return DynValue.Nil;
            });
            case "roll": return Fn(_ =>
            {
                if (faces.Count > 1) { host.Emit(new RollDice { Layer = L, PieceIds = [id], Count = 1, Sides = faces.Count }); return DynValue.True; }
                if (def is { States.Count: > 1 }) { host.Emit(new SetState { Layer = L, Id = id, State = Random.Shared.Next(def.States.Count) }); return DynValue.True; }
                return DynValue.False;
            });
            default: return null;
        }
    }

    static bool Same(string value, DynValue v) => v.Type switch
    {
        DataType.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && Math.Abs(n - v.Number) < 1e-9,
        DataType.String => string.Equals(value, v.String, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    static double[] UpOf(Table t)
    {
        double Num(Table at, string k, int i) => at.Get(k).CastToNumber() ?? at.Get(i).CastToNumber() ?? 0;
        if (t.Get("up").Type == DataType.Table)
        {
            var u = t.Get("up").Table;
            return [Num(u, "x", 1), Num(u, "y", 2), Num(u, "z", 3)];
        }
        if (t.Get("rotation").Type == DataType.Table)
        {
            var r = t.Get("rotation").Table;
            return DieTurns.UpOf(Num(r, "x", 1), Num(r, "z", 3));
        }
        return [0, 1, 0];
    }
}
