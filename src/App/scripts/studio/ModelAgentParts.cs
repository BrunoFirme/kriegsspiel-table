using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using KTable.Client.Agent;
using KTable.Client.Studio;

namespace KTable.App;

static class ModelAgentParts
{
    internal static string? Place(ModelStudio m, ModelPart p, JsonElement a)
    {
        if (a.Str("name") is { Length: > 0 } name) p.Name = name;
        if (StudioTools.Floats(a, "pos", 3) is { } pos) p.Pos = pos;
        if (StudioTools.Floats(a, "size", 3) is { } size && size.All(x => x != 0)) p.Scl = size;
        if (StudioTools.Floats(a, "rot", 3) is { } rot) p.Rot = rot;
        if (a.Str("tint") is { } tint && StudioTools.IsColour(tint)) p.Tint = StudioTools.Colour(tint, "#ffffffff");
        if (a.Flag("visible") is { } vis) p.Visible = vis;
        if (a.Flag("twin") is { } twin) p.Twin = twin;
        if (a.Num("blend") is { } blend) p.Blend = Math.Max(0, (float)blend);
        if (a.Str("join") is { } join)
        {
            if (join.Length > 0 && join != "apart" && !ModelTree.Joins.Contains(join)) return "join is add, cut, overlap, or \"\" for apart.";
            p.Join = ModelTree.Joins.Contains(join) ? join : null;
        }
        if (a.Str("parent") is { } parent)
        {
            var g = parent.Length == 0 ? null : StudioTools.PartOf(m, parent);
            if (parent.Length > 0 && g is not { IsGroup: true }) return $"{parent} is not a group: add one with shape \"group\".";
            m.Edits.Into(p, g);
        }
        if (FloatList(a, "points") is { } points || a.Num("depth") is not null)
        {
            if (p.Spec is not { } spec) return $"{p.Name} is a {p.Kind}: only lathe, sweep and extrude shapes take points or depth.";
            var next = spec.Copy();
            if (FloatList(a, "points") is { } given) next.Points = given;
            if (a.Num("depth") is { } depth) next.Depth = (float)depth;
            if (!ShapeMaker.Valid(next)) return $"points for a {spec.Kind} are {(spec.Kind == ShapeMaker.Sweep ? "x,y,z,radius fours, at least two" : "pairs, at least " + (spec.Kind == ShapeMaker.Extrude ? "three" : "two"))}.";
            m.Edits.Reshape(p, next, panels: false);
        }
        m.RefreshSelection();
        if (a.Str("sit_on") is { Length: > 0 } on && (StudioTools.PartOf(m, on) is not { } under || !m.Edits.SitOn(p, under, panels: false))) return $"{p.Name} could not sit on {on}: studio_model_parts lists the parts.";
        if (a.Str("centre_on") is { Length: > 0 } at && (StudioTools.PartOf(m, at) is not { } mid || !m.Edits.CentreOn(p, mid, a.Str("axes") ?? "xz", panels: false))) return $"{p.Name} could not be centred on {at}.";
        return null;
    }

    internal static float[]? FloatList(JsonElement a, string name)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var l = v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (float)x.GetDouble()).ToArray();
        return l.All(float.IsFinite) ? l : null;
    }

    internal static string? AddOne(ModelStudio m, JsonElement a)
    {
        var shape = a.Str("shape") ?? "";
        var file = a.Str("file");
        if (file == null && !StudioTools.PartShapes.Contains(shape)) return "shape is one of " + string.Join(", ", StudioTools.PartShapes) + ", or give file.";
        if (file != null && !File.Exists(file)) return "No such file: " + file;
        MeshData? mesh = null;
        if (file != null && (mesh = ModelImport.Load(file, out var err)) == null) return $"Could not read {Path.GetFileName(file)}: {err}";
        var name = a.Str("name") is { Length: > 0 } n ? n : file != null ? NewPieces.Pretty(Path.GetFileNameWithoutExtension(file)) : ModelPanels.AddableShapes.FirstOrDefault(x => x.kind == shape).name ?? "Group";
        if (shape == ModelTree.GroupKind && file == null)
        {
            m.Snapshot();
            var g = new ModelPart { Name = name, Kind = ModelTree.GroupKind };
            m.Doc.Parts.Add(g);
            m.SelPartId = g.Id;
        }
        else if (mesh != null) m.AddParts(mesh, name, "mesh");
        else if (ShapeMaker.IsSpec(shape))
        {
            var spec = ShapeMaker.Default(shape);
            if (FloatList(a, "points") is { } pts) spec.Points = pts;
            if (a.Num("depth") is { } depth) spec.Depth = (float)depth;
            if (!ShapeMaker.Valid(spec)) return $"points for a {shape} are {(shape == ShapeMaker.Sweep ? "x,y,z,radius fours, at least two" : "pairs, at least " + (shape == ShapeMaker.Extrude ? "three" : "two"))}.";
            m.AddParts(ShapeMaker.Make(spec), name, shape, spec);
        }
        else m.AddParts(ModelImport.Primitive(shape), name, shape);
        if (m.SelPart is not { } p) return null;
        m.Snapshot();
        var placed = Without(a, "points", "depth");
        var problem = Place(m, p, placed);
        m.RefreshSelection();
        m.Changed(mesh: true);
        return problem;
    }

    internal static JsonElement Without(JsonElement a, params string[] keys)
    {
        if (a.ValueKind != JsonValueKind.Object) return a;
        var o = JsonNode.Parse(a.GetRawText())!.AsObject();
        foreach (var k in keys) o.Remove(k);
        return JsonDocument.Parse(o.ToJsonString()).RootElement;
    }

    internal static string? SetOne(ModelStudio m, JsonElement a)
    {
        if (StudioTools.PartOf(m, a.Str("part")) is not { } p) return $"no part {a.Str("part")}: studio_model_parts lists them.";
        if (a.Str("shape") is not null) return "A part keeps the shape it was added as: add the other shape (studio_model_add_part) and remove this one (studio_model_remove_part).";
        m.Snapshot();
        var problem = Place(m, p, a);
        m.RefreshSelection();
        m.Changed(mesh: true);
        return problem;
    }

    internal static IEnumerable<JsonElement> Entries(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();

    const int SoftSteps = 8;

    internal static int SoftFill(ModelPartView v, float[] uv, List<int> tris, Dictionary<int, float> weight, Color c)
    {
        int n = 0;
        for (int step = 1; step <= SoftSteps; step++)
        {
            float lo = (step - 1) / (float)SoftSteps, hi = step / (float)SoftSteps;
            var band = tris.Where(t => weight.GetValueOrDefault(t, 1) > lo && weight.GetValueOrDefault(t, 1) <= hi + 1e-6f).ToList();
            if (band.Count == 0) continue;
            var layer = new byte[v.Paint.Length];
            var (r, g, b, a) = UvPaint.Premultiplied(c.R, c.G, c.B, c.A * hi);
            n += UvPaint.Fill(layer, v.W, v.H, uv, band, r, g, b, a);
            for (int i = 0; i < layer.Length; i += 4)
            {
                if (layer[i + 3] == 0) continue;
                float keep = 1 - layer[i + 3] / 255f;
                for (int k = 0; k < 4; k++) v.Paint[i + k] = (byte)Math.Min(255, layer[i + k] + v.Paint[i + k] * keep);
            }
        }
        return n;
    }
}
