using System.Text.Json.Nodes;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class GluedParts(DefFactory defs, Report rep, double tokenArea)
{
    const int Digits = 4;
    const string BundleType = "Custom_Assetbundle";

    public HashSet<string> Packs { get; } = new(StringComparer.Ordinal);

    public static bool IsGluedEffect(DefFactory defs, ModuleObj c) => c.Name == BundleType && defs.ModelOfBundle(c) == null;

    public void Fill(PieceDef def, List<ModuleObj> states, double baseScale, double r, double heightScale, PackOut pack, string where, Mirror m)
    {
        if (def.States.Count == 0 || states.Count == 1)
        {
            def.Parts = Of(states[0], r, heightScale, pack, where, m);
            return;
        }
        for (int i = 0; i < def.States.Count && i < states.Count; i++)
            def.States[i].Parts = Of(states[i], Math.Abs(states[i].ScaleX) / baseScale * r, heightScale, pack, $"{where} state \"{def.States[i].Name}\"", m);
    }

    List<PartDef>? Of(ModuleObj s, double unit, double heightScale, PackOut pack, string where, Mirror m)
    {
        var parts = new List<PartDef>();
        double sx = Math.Abs(s.ScaleX) < DefFactory.NoScale ? 1 : Math.Abs(s.ScaleX), sy = Math.Abs(s.ScaleY), depth = Math.Abs(s.ScaleZ) / sx;
        var frame = s.IsGlued ? PrefabRoots.Root.One : PrefabRoots.Of(s.Name);
        foreach (var glued in Family(s.Children))
        {
            var c = InFrame(Within(glued, m), frame, depth);
            if (IsGluedEffect(defs, c)) { rep.Note($"{where}: the glued effect '{c.Nickname}' is left out; place one of Firme's effects on the piece instead"); continue; }
            if (!defs.Shows(c)) { rep.Skipped("glued " + c.Name, where); continue; }
            if (DefFactory.NotTranslated(c) is { } why) { rep.Skipped(why, where); continue; }
            var cr = defs.DefFor(c, pack, Math.Abs(c.StateList().states[0].ScaleX) * unit, heightScale * sy, glued: true);
            if (cr == null) continue;
            if (c.Lua.Trim().Length > 0) rep.Note($"{where}: the glued {c.Name} '{c.Nickname}' carries a script, which the digital tabletop does not run on a glued part either");
            if (cr.FromPack != null && !cr.Copied) Packs.Add(cr.FromPack);
            parts.Add(Part(c, cr, new Frame(unit, sx, sy, depth, defs.LocalFloor(s) * sy), m, pack, where));
        }
        return parts.Count > 0 ? parts : null;
    }

    static IEnumerable<ModuleObj> Family(IEnumerable<ModuleObj> children)
    {
        foreach (var c in children)
        {
            if (c.StateList().states.Count > 1 || !c.Children.Any())
            {
                yield return c;
                continue;
            }
            yield return Bare(c);
            foreach (var g in Family(c.Children)) yield return Through(c, g);
        }
    }

    static ModuleObj Bare(ModuleObj c) => new(Copy(c.J, keepChildren: false));

    static JsonObject Copy(JsonObject j, bool keepChildren)
    {
        var copy = new JsonObject();
        foreach (var (k, v) in j)
            if (keepChildren || k != "ChildObjects") copy[k] = v?.DeepClone();
        return copy;
    }

    static ModuleObj Through(ModuleObj parent, ModuleObj child)
    {
        var turn = ModuleTurn.Of(parent.RotX, parent.RotY, parent.RotZ);
        var (x, y, z) = turn.Apply(child.PosX * parent.ScaleX, child.PosY * parent.ScaleY, child.PosZ * parent.ScaleZ);
        var (rx, ry, rz) = turn.Then(ModuleTurn.Of(child.RotX, child.RotY, child.RotZ)).Euler();
        var j = Copy(child.J, keepChildren: child.StateList().states.Count > 1);
        j["Transform"] = new JsonObject
        {
            ["posX"] = parent.PosX + x, ["posY"] = parent.PosY + y, ["posZ"] = parent.PosZ + z,
            ["rotX"] = rx, ["rotY"] = ry, ["rotZ"] = rz,
            ["scaleX"] = parent.ScaleX * child.ScaleX, ["scaleY"] = parent.ScaleY * child.ScaleY, ["scaleZ"] = parent.ScaleZ * child.ScaleZ,
        };
        return new ModuleObj(j);
    }

    static ModuleObj InFrame(ModuleObj c, PrefabRoots.Root parent, double depth)
    {
        if (c.J.DeepClone() is not JsonObject j) return c;
        Glue(j, parent, depth);
        if (j["States"] is JsonObject states)
            foreach (var (_, look) in states)
                if (look is JsonObject l) Glue(l, parent, depth);
        return new ModuleObj(j);
    }

    static void Glue(JsonObject j, PrefabRoots.Root parent, double depth)
    {
        j[ModuleObj.GluedMark] = true;
        if (j["Transform"] is not JsonObject t) return;
        double N(string k, double fallback) => ModuleValues.Number(t[k], fallback);
        var own = PrefabRoots.InSizesOf(ModuleValues.Text(j["Name"]) ?? "");
        bool across = Across(N("rotY", 0));
        var p = across ? parent.Across : parent;
        double stretchX = across ? depth : 1, stretchZ = across ? 1 : depth;
        (t["posX"], t["posY"], t["posZ"]) = (N("posX", 0) * parent.X, N("posY", 0) * parent.Y, N("posZ", 0) * parent.Z);
        (t["scaleX"], t["scaleY"], t["scaleZ"]) = (N("scaleX", 1) * p.X * stretchX / own.X, N("scaleY", 1) * p.Y / own.Y, N("scaleZ", 1) * p.Z * stretchZ / own.Z);
    }

    static bool Across(double yaw)
    {
        const double Quarter = 90, Within = 30;
        double a = ((yaw % 180) + 180) % 180;
        return Math.Abs(a - Quarter) < Within;
    }

    static ModuleObj Within(ModuleObj c, Mirror m)
    {
        if (!m.Any || c.J.DeepClone() is not JsonObject j || j["Transform"] is not JsonObject t) return c;
        foreach (var (axis, on) in new[] { ("scaleX", m.X), ("scaleY", m.Y), ("scaleZ", m.Z) })
            if (on) t[axis] = -ModuleValues.Number(t[axis], 1);
        return new ModuleObj(j);
    }

    sealed record Frame(double Unit, double Sx, double Sy, double Depth, double Floor);

    PartDef Part(ModuleObj c, DefResult cr, Frame f, Mirror m, PackOut pack, string where)
    {
        double unit = f.Unit;
        var d = cr.Def;
        bool rolls = d.Faces.Count > 1;
        var pose = rolls ? new PieceTilt.Pose(c.Heading, false, 0, 0) : PieceTilt.Of(c.RotX, c.RotY, c.RotZ, d.Kind == "model", c.Name == "3DText");
        double bottom = (c.PosY + defs.LocalFloor(c) * Math.Abs(c.ScaleY)) * f.Sy;
        var part = new PartDef
        {
            Def = d.Id, X = Math.Round(-c.PosX * unit * m.SignX, Digits), Z = Math.Round(c.PosZ * unit * f.Depth * m.SignZ, Digits),
            Y = Math.Round((bottom - f.Floor) * unit / f.Sx, Digits), Rot = Math.Round(m.X ^ m.Z ? -pose.Heading : pose.Heading, 3), TiltX = pose.X, TiltZ = pose.Z,
            FaceDown = pose.Down && d.Kind != "model",
            Scale = cr.FromPack != null ? Math.Round(Math.Abs(c.ScaleX) * unit * cr.Fit, Digits) : 1,
            Tint = c.Color is { } col && !string.Equals(col, d.Tint, StringComparison.OrdinalIgnoreCase) ? col : null,
            State = rolls ? 0 : Math.Min(cr.State, Math.Max(0, d.States.Count - 1)), Face = rolls ? cr.State : -1,
        };
        var decals = ScenarioBuilder.DecalsOf(c, d, tokenArea, url => defs.UiAsset(pack, url, $"{where} glued decal"), sha => defs.FlippedPicture(pack, sha));
        if (decals.Count > 0) part.Decals = decals;
        if (c.VectorLines.Count > 0) part.Lines = Lines(c, d);
        return part;
    }

    List<LineDef> Lines(ModuleObj c, PieceDef d)
    {
        var (ux, uz) = defs.LocalUnits(d, c);
        return c.VectorLines.Select(l => new LineDef
        {
            Points = l.Points.Select(p => new Vec2(Math.Round(-p.X * ux, Digits), Math.Round(p.Z * uz, Digits))).ToList(), Color = l.Color, Width = l.Thickness, Loop = l.Loop, Round = l.Round,
        }).ToList();
    }

    public (double Low, double High) Span(ModuleObj o)
    {
        double low = defs.LocalFloor(o), high = low + defs.BaseThickness(o);
        var frame = o.IsGlued ? PrefabRoots.Root.One : PrefabRoots.Of(o.Name);
        double sx = Math.Abs(o.ScaleX) < DefFactory.NoScale ? 1 : Math.Abs(o.ScaleX);
        foreach (var c in o.Children.Where(c => !IsGluedEffect(defs, c) && defs.Shows(c)).Select(c => InFrame(c, frame, Math.Abs(o.ScaleZ) / sx)))
        {
            var (cl, ch) = Span(c);
            double sy = Math.Abs(c.ScaleY);
            low = Math.Min(low, c.PosY + cl * sy);
            high = Math.Max(high, c.PosY + ch * sy);
        }
        return (low, high);
    }
}
