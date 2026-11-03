using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Agent;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class StudioTools
{
    static readonly string[] Sides = { "front", "back", "left", "right", "top", "three-quarter" };
    static readonly string[] PartShapes = ModelPanels.AddableShapes.Select(s => s.kind).ToArray();

    const string PartData = "A part: {name, shape, pos [x,y,z] (y up), size [x,y,z] (scale), rot [x,y,z] (degrees), tint (#rrggbb), visible}. Positions are in the model's own units; studio_model_parts gives each part's place and size.";

    IEnumerable<AgentTool> ModelTools()
    {
        yield return new AgentTool("studio_model_view",
            "A picture of a kind in the 3D view from a side (" + string.Join(", ", Sides) + ") or from yaw and pitch in degrees, with the list of its parts. For a 3D kind its model; for a flat kind the block and any shapes built onto it.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("side", "string", string.Join(", ", Sides)), new Arg("yaw", "number", "degrees round it, 0 = the front"), new Arg("pitch", "number", "degrees, negative looks down from above")),
            ModelView);
        yield return new AgentTool("studio_model_parts",
            "The parts a kind's model is built from (for a flat kind: the shapes built onto its block), each with its place, size, turn, tint and how many materials it has. " + PartData,
            Args.Schema(new Arg("id", "string", "the kind's id", true)), ModelParts);
        yield return new AgentTool("studio_model_add_part",
            "Adds a shape to a kind's model (" + string.Join(", ", PartShapes) + "), or a model from a file (glTF, GLB, FBX, OBJ, STL, PLY, Collada .dae) as Add a shape does. On a flat kind it is built onto its block, centred on top, and rides on it. Returns a picture. " + PartData,
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("shape", "string", string.Join(", ", PartShapes) + " (or give file)"), new Arg("file", "string", "full path of a model file to add instead of a shape"), new Arg("name", "string", "the part's name"),
                new Arg("pos", "array", "[x, y, z]"), new Arg("size", "array", "[x, y, z] scale"), new Arg("rot", "array", "[x, y, z] degrees"), new Arg("tint", "string", "#rrggbb")),
            AddPart);
        yield return new AgentTool("studio_model_set_part",
            "Moves, sizes, turns, tints or hides a part of a kind's model; a part keeps the shape it was added as. Returns a picture. " + PartData,
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("part", "string", "the part's id or name", true), new Arg("name", "string", "rename it"),
                new Arg("pos", "array", "[x, y, z]"), new Arg("size", "array", "[x, y, z] scale"), new Arg("rot", "array", "[x, y, z] degrees"), new Arg("tint", "string", "#rrggbb"), new Arg("visible", "boolean", "shown")),
            SetPart);
        yield return new AgentTool("studio_model_remove_part",
            "Takes a part out of a kind's model.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("part", "string", "the part's id or name", true)), RemovePart);
        yield return new AgentTool("studio_model_paint",
            "Paints a 3D kind's surface in a colour: a whole part, one of its materials (an imported model's own split: armour, trim…), or the region inside a box given as fractions of the part's size (x 0 left to 1 right, y 0 bottom to 1 top, z 0 back to 1 front). Returns a picture.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("colour", "string", "#rrggbb, or #rrggbbaa to let the model show through", true), new Arg("part", "string", "a part's id or name (default the first)"),
                new Arg("material", "number", "a material's number from 0 (studio_model_parts gives how many)"), new Arg("box", "object", "{min: [x,y,z], max: [x,y,z]} as fractions of the part"), new Arg("side", "string", "the side to show in the picture")),
            PaintModel);
        yield return new AgentTool("studio_model_decal",
            "Presses a picture onto a 3D kind's surface like a transfer, seen from a side: an emblem, a badge, a number. The picture is a file, a pack picture (Src from studio_add_picture_source), or the face of another kind (draw an emblem on a token with studio_write_face first). x and y place its centre as fractions of the view, size is its height as a fraction of the view. Returns a picture.",
            Args.Schema(new Arg("id", "string", "the kind's id", true),
                new Arg("path", "string", "a picture file"), new Arg("src", "string", "a picture already in the pack"), new Arg("from_kind", "string", "a kind whose front is the picture"),
                new Arg("side", "string", string.Join(", ", Sides)), new Arg("yaw", "number", "degrees"), new Arg("pitch", "number", "degrees"),
                new Arg("x", "number", "0–1 across the view (default 0.5)"), new Arg("y", "number", "0–1 down the view (default 0.5)"), new Arg("size", "number", "0–1 of the view's height (default 0.2)"),
                new Arg("turn", "number", "degrees"), new Arg("opacity", "number", "0–1"), new Arg("mirror", "boolean", "flipped")),
            Decal);
    }

    static (float yaw, float pitch) Angle(JsonElement a)
    {
        float yaw = 0, pitch = -0.35f;
        switch (a.Str("side"))
        {
            case "back": yaw = Mathf.Pi; break;
            case "left": yaw = -Mathf.Pi / 2; break;
            case "right": yaw = Mathf.Pi / 2; break;
            case "top": pitch = -1.45f; break;
            case "three-quarter": yaw = Mathf.Pi / 4; pitch = -0.5f; break;
        }
        if (a.Num("yaw") is { } y) yaw = Mathf.DegToRad((float)y);
        if (a.Num("pitch") is { } p) pitch = Mathf.DegToRad((float)p);
        return (yaw, pitch);
    }

    async Task<ModelStudio?> OpenModel(PieceStudio s, PieceDef d)
    {
        if (EffectFx.IsEffect(d)) return null;
        if (!await Select(s, d)) return null;
        if (s.View != "model") s.SetView("model");
        for (int i = 0; i < 300; i++)
        {
            if (s.Model is { } m && m.Holds(d) && (m.Views.Count > 0 || d.Kind != "model" || m.Doc.Parts.Count == 0)) { await s.Frames(2); return m; }
            await s.Frames(1);
        }
        return s.Model is { } late && late.Holds(d) ? late : null;
    }

    async Task<byte[]?> Shot(ModelStudio m, float yaw, float pitch)
    {
        m.Camera.Spin = false;
        m.Frame();
        m.Camera.Yaw = yaw; m.Camera.Pitch = pitch;
        m.Camera.Apply();
        if (DisplayServer.GetName() == "headless") return null;
        for (int i = 0; i < 3; i++) await m.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = m.Vp.GetTexture().GetImage();
        if (img == null || img.IsEmpty()) return null;
        int w = img.GetWidth(), h = img.GetHeight();
        if (Math.Max(w, h) > 900) img.Resize(Math.Max(1, w * 900 / Math.Max(w, h)), Math.Max(1, h * 900 / Math.Max(w, h)), Image.Interpolation.Lanczos);
        return img.SavePngToBuffer();
    }

    static JsonArray PartsOf(ModelStudio m) => new(m.Doc.Parts.Select(p =>
    {
        m.Views.TryGetValue(p.Id, out var v);
        var b = v?.Data.Bounds();
        return (JsonNode?)new JsonObject
        {
            ["id"] = p.Id, ["name"] = p.Name, ["shape"] = p.Kind,
            ["pos"] = Vec(p.Pos), ["size"] = Vec(p.Scl), ["rot"] = Vec(p.Rot), ["tint"] = p.Tint[..Math.Min(7, p.Tint.Length)], ["visible"] = p.Visible,
            ["materials"] = v == null ? 0 : Math.Max(1, v.Data.Mat.Count == 0 ? 1 : v.Data.Mat.Distinct().Count()),
            ["triangles"] = v?.Data.Tris ?? 0,
            ["ownSize"] = b == null ? null : Vec(new[] { b.Value.Size.X, b.Value.Size.Y, b.Value.Size.Z }),
            ["paintable"] = v != null && !v.Fixed,
        };
    }).ToArray());

    static JsonArray Vec(float[] v) => new(v.Select(x => (JsonNode?)JsonValue.Create(MathF.Round(x, 3))).ToArray());

    static float[]? Floats(JsonElement a, string name, int n)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var l = v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (float)x.GetDouble()).ToArray();
        return l.Length == n && l.All(float.IsFinite) ? l : null;
    }

    static ModelPart? PartOf(ModelStudio m, string? key) =>
        key == null ? m.Doc.Parts.FirstOrDefault() : m.Doc.Parts.FirstOrDefault(p => p.Id == key) ?? m.Doc.Parts.FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));

    async Task<ToolResult> Finish(PieceStudio s, ModelStudio m, string what, JsonElement a)
    {
        await m.Bake();
        var (yaw, pitch) = Angle(a);
        var shot = await Shot(m, yaw, pitch);
        return new ToolResult(what + "\n" + Json(new JsonObject { ["parts"] = PartsOf(m) }), false, shot == null ? null : new[] { shot });
    }

    Task<ToolResult> ModelView(JsonElement a) => On(async s =>
    {
        if (Def(s, a) is not { } d) return NoKind(a);
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        var (yaw, pitch) = Angle(a);
        var shot = await Shot(m, yaw, pitch);
        var text = $"{d.Name} in 3D{(d.Kind == "model" ? "" : " (a flat kind: its block and the shapes built onto it)")}.\n" + Json(new JsonObject { ["parts"] = PartsOf(m) });
        return shot == null ? new ToolResult(text + "\n(No picture: the window is not drawing.)") : new ToolResult(text, false, new[] { shot });
    });

    Task<ToolResult> ModelParts(JsonElement a) => On(async s =>
    {
        if (Def(s, a) is not { } d) return NoKind(a);
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        return new ToolResult(Json(new JsonObject { ["scale"] = MathF.Round(m.Doc.Scale, 4), ["parts"] = PartsOf(m) }));
    });

    static void Place(ModelPart p, JsonElement a)
    {
        if (Floats(a, "pos", 3) is { } pos) p.Pos = pos;
        if (Floats(a, "size", 3) is { } size && size.All(x => x > 0)) p.Scl = size;
        if (Floats(a, "rot", 3) is { } rot) p.Rot = rot;
        if (a.Str("tint") is { } tint && IsColour(tint)) p.Tint = Colour(tint, "#ffffffff");
        if (a.Flag("visible") is { } vis) p.Visible = vis;
    }

    Task<ToolResult> AddPart(JsonElement a) => Change(a, "added a shape", async (s, d) =>
    {
        var shape = a.Str("shape") ?? "";
        var file = a.Str("file");
        if (file == null && !PartShapes.Contains(shape)) return ToolResult.Fail("shape is one of " + string.Join(", ", PartShapes) + ", or give file.");
        if (file != null && !File.Exists(file)) return ToolResult.Fail("No such file: " + file);
        MeshData? mesh = null;
        if (file != null && (mesh = ModelImport.Load(file, out var err)) == null) return ToolResult.Fail($"Could not read {Path.GetFileName(file)}: {err}");
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        var name = a.Str("name") is { Length: > 0 } n ? n : file != null ? NewPieces.Pretty(Path.GetFileNameWithoutExtension(file)) : ModelPanels.AddableShapes.First(x => x.kind == shape).name;
        if (mesh != null) m.AddParts(mesh, name, "mesh"); else m.AddParts(ModelImport.Primitive(shape), name, shape);
        if (m.SelPart is { } p)
        {
            m.Snapshot();
            Place(p, a);
            m.RefreshSelection();
            m.Changed(mesh: true);
        }
        return await Finish(s, m, $"Added {name} to {d.Name}.", a);
    });

    Task<ToolResult> SetPart(JsonElement a) => Change(a, "changed a part", async (s, d) =>
    {
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        if (PartOf(m, a.Str("part")) is not { } p) return ToolResult.Fail($"{d.Name} has no part {a.Str("part")}: studio_model_parts lists them.");
        if (a.Str("shape") is not null) return ToolResult.Fail("A part keeps the shape it was added as: add the other shape (studio_model_add_part) and remove this one (studio_model_remove_part).");
        m.Snapshot();
        if (a.Str("name") is { Length: > 0 } name) p.Name = name;
        Place(p, a);
        m.RefreshSelection();
        m.Changed(mesh: true);
        return await Finish(s, m, $"Changed {p.Name}.", a);
    });

    Task<ToolResult> RemovePart(JsonElement a) => Change(a, "took a part out", async (s, d) =>
    {
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        if (PartOf(m, a.Str("part")) is not { } p) return ToolResult.Fail($"{d.Name} has no part {a.Str("part")}: studio_model_parts lists them.");
        m.RemovePart(p);
        return await Finish(s, m, $"Took {p.Name} out.", a);
    });

    Task<ToolResult> PaintModel(JsonElement a) => Change(a, "painted the model", async (s, d) =>
    {
        if (!IsColour(a.Str("colour"))) return ToolResult.Fail("colour is #rrggbb or #rrggbbaa.");
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        if (PartOf(m, a.Str("part")) is not { } p || !m.Views.TryGetValue(p.Id, out var v)) return ToolResult.Fail($"{d.Name} has no part {a.Str("part")}: studio_model_parts lists them.");
        if (v.Fixed) return ToolResult.Fail($"{p.Name} cannot be painted (it takes its look from the block's faces: change those with studio_write_face).");
        var data = v.Data;
        IEnumerable<int> tris = Enumerable.Range(0, data.Tris);
        if (a.Num("material") is { } mat)
        {
            int k = (int)mat;
            if (data.Mat.Count < data.Tris) return ToolResult.Fail($"{p.Name} has one material: paint it whole or by a box.");
            tris = tris.Where(t => data.Mat[t] == k).ToList();
            if (!tris.Any()) return ToolResult.Fail($"{p.Name} has no material {k} (materials 0 to {data.Mat.Max()}).");
        }
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("box", out var box) && box.ValueKind == JsonValueKind.Object)
        {
            var lo = Floats(box, "min", 3) ?? new[] { 0f, 0f, 0f };
            var hi = Floats(box, "max", 3) ?? new[] { 1f, 1f, 1f };
            var b = data.Bounds();
            Vector3 Frac(Vector3 x) => new((x.X - b.Position.X) / Mathf.Max(1e-6f, b.Size.X), (x.Y - b.Position.Y) / Mathf.Max(1e-6f, b.Size.Y), (x.Z - b.Position.Z) / Mathf.Max(1e-6f, b.Size.Z));
            bool In(int t)
            {
                var c = Frac((data.V[t * 3] + data.V[t * 3 + 1] + data.V[t * 3 + 2]) / 3);
                return c.X >= lo[0] && c.X <= hi[0] && c.Y >= lo[1] && c.Y <= hi[1] && c.Z >= lo[2] && c.Z <= hi[2];
            }
            tris = tris.Where(In).ToList();
        }
        var uv = new float[data.UV.Count * 2];
        for (int i = 0; i < data.UV.Count; i++) { uv[i * 2] = data.UV[i].X; uv[i * 2 + 1] = data.UV[i].Y; }
        var c = Hex.To(Colour(a.Str("colour"), "#000000ff"), Colors.Black);
        var (r, g, bl, al) = UvPaint.Premultiplied(c.R, c.G, c.B, c.A);
        m.BeginStroke();
        int n = UvPaint.Fill(v.Paint, v.W, v.H, uv, tris, r, g, bl, al);
        v.Repaint();
        m.EndStroke();
        if (n == 0) return ToolResult.Fail("Nothing of the model was there to paint (the box or material held no surface).");
        return await Finish(s, m, FormattableString.Invariant($"Painted {n:#,0} texels of {p.Name}."), a);
    });

    Image? DecalPicture(PieceStudio s, JsonElement a, out string name)
    {
        name = "decal";
        if (a.Str("path") is { Length: > 0 } path) { name = Path.GetFileNameWithoutExtension(path); return File.Exists(path) ? ImageFiles.Load(path) : null; }
        if (a.Str("src") is { Length: > 0 } src) { name = src; return ArtStore.LoadSrc(s.Store, s.Pack.Id, src); }
        if (a.Str("from_kind") is { Length: > 0 } kind && s.Pack.Pieces.FirstOrDefault(p => p.Id == kind) is { } k && PieceFaces.Of(k).FirstOrDefault()?.Sha is { } sha && s.Store.AssetPath(s.Pack, sha) is { } file)
        {
            name = k.Name;
            var img = Image.LoadFromFile(file);
            if (img != null && img.IsCompressed()) img.Decompress();
            return img;
        }
        return null;
    }

    Task<ToolResult> Decal(JsonElement a) => Change(a, "pressed a decal", async (s, d) =>
    {
        if (DecalPicture(s, a, out var name) is not { } img || img.IsEmpty()) return ToolResult.Fail("Give a picture: path (a file), src (a pack picture) or from_kind (a kind whose front is the picture).");
        if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in the 3D view.");
        if (m.Views.Values.All(v => v.Fixed)) return ToolResult.Fail($"{d.Name} has nothing a decal can go on (a flat block takes its look from its faces: use studio_write_face).");
        var (yaw, pitch) = Angle(a);
        m.Camera.Spin = false;
        m.Frame();
        m.Camera.Yaw = yaw; m.Camera.Pitch = pitch;
        m.Camera.Apply();
        await s.Frames(2);
        var vp = (Vector2)m.Vp.Size;
        m.Decal.Set(img, name);
        m.Decal.Px = Mathf.Clamp((float)(a.Num("size") ?? 0.2), 0.01f, 1f) * vp.Y;
        m.Decal.Turn = Mathf.Wrap((float)(a.Num("turn") ?? 0), -180, 180);
        m.Decal.Opacity = Mathf.Clamp((float)(a.Num("opacity") ?? 1), 0.05f, 1f);
        m.Decal.Mirror = a.Flag("mirror") ?? false;
        var at = new Vector2(Mathf.Clamp((float)(a.Num("x") ?? 0.5), 0, 1) * vp.X, Mathf.Clamp((float)(a.Num("y") ?? 0.5), 0, 1) * vp.Y);
        m.BeginStroke();
        int n = m.Decal.Project(at);
        if (n == 0)
        {
            await s.Frames(10);
            m.Frame();
            m.Camera.Yaw = yaw; m.Camera.Pitch = pitch;
            m.Camera.Apply();
            await s.Frames(2);
            n = m.Decal.Project(at);
        }
        m.EndStroke();
        if (n == 0) return ToolResult.Fail("Nothing of the model was under that place from this side: look with studio_model_view and aim again.");
        return await Finish(s, m, FormattableString.Invariant($"Pressed {name} onto {d.Name} ({n:#,0} texels)."), a);
    });
}
