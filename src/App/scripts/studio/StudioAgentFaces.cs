using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class StudioTools
{
    const int MaxSide = 4096, MaxLayers = 200, MaxPoints = 200_000;

    const string DocFormat =
        "A face is a layered picture: {W, H (pixels), Bg (#rrggbbaa or null), Outline (null, or [x,y,…] the piece is cut along), Layers (bottom first)}. " +
        "Every layer: {Name, Kind, Visible, Opacity 0–1, X, Y (its place in pixels), Rot (degrees), Sx, Sy (scale)}. " +
        "Kind paint: Ops, each {K: \"path\" (a line through P = [x,y,x,y,…], width W, colour C; E true makes it an eraser stroke that clears what is under it on this layer) or \"shape\" (a polygon P, outline W and C, W 0 for none, fill Fc)}. " +
        "Kind text: Text (\\n for lines), Font, Bold, Italic, Size (px), Color, OutlineColor, OutlineW; centred on X, Y. " +
        "Kind image: Src (a name from studio_add_picture_source), SrcW, SrcH, Crop [x,y,w,h] or null; centred on X, Y and scaled by Sx, Sy. " +
        "Kind block: Block {BW, BH, Shape, Fill, Border, BorderPx, Label, LabelColor, Font, Symbol, SymbolText, SymbolColor, SymbolScale}. " +
        "Colours are #rrggbbaa. Keep the layers you do not change as they were read.";

    IEnumerable<AgentTool> FaceTools()
    {
        yield return new AgentTool("studio_read_face",
            "A face of a kind (front, back, or a look's: s1, s1b…) as its layers, with a picture of it. " + DocFormat,
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("face", "string", "front (default), back, s1, s1b…")),
            ReadFace);
        yield return new AgentTool("studio_write_face",
            "Replaces a face with these layers, draws it and puts it on the kind; returns a picture of the result. Read the face first and change what is needed. also gives other faces the same layers in the same call. " + DocFormat,
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("face", "string", "front (default), back, s1, s1b…"), new Arg("doc", "object", "the face's layers, as studio_read_face gives them", true),
                new Arg("also", "array", "more faces to draw the same way: keys (s1, s2b…), or \"every look\" for this side of every look")),
            WriteFace);
        yield return new AgentTool("studio_picture_size",
            "Sets a face's picture size in pixels, as Picture size… does: scale stretches every layer with it, otherwise the layers keep their size and the picture grows or is cut round the middle. A face two units wide looks crisp at 512 px; close-up screenshots want 1024 or more.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("face", "string", "front (default), back, s1, s1b…"),
                new Arg("width", "number", $"pixels ({StudioCanvas.SmallestSide}–{StudioCanvas.LargestSide})", true), new Arg("height", "number", $"pixels ({StudioCanvas.SmallestSide}–{StudioCanvas.LargestSide})", true),
                new Arg("scale", "boolean", "stretch the layers with it (true if left out)")),
            PictureSize);
        yield return new AgentTool("studio_add_picture_source",
            "Copies a picture file into the pack so an image layer can show it; returns the Src name and its size in pixels.",
            Args.Schema(new Arg("path", "string", "full path or web link of a picture file", true)),
            AddSource);
        yield return new AgentTool("studio_view_piece",
            "Shows a kind on the studio's table, as it will look in a game (counters, text and script buttons printed), and returns a picture of the window.",
            Args.Schema(new Arg("id", "string", "the kind's id", true)),
            ViewPiece);
    }

    static PieceFaces.Face? FaceOf(PieceDef d, JsonElement a) => PieceFaces.Of(d).FirstOrDefault(f => f.Key == (a.Str("face") ?? "front"));

    Task<ToolResult> ReadFace(JsonElement a) => On(s =>
    {
        if (Def(s, a) is not { } d) return Task.FromResult(NoKind(a));
        if (FaceOf(d, a) is not { } f) return Task.FromResult(ToolResult.Fail($"{d.Name} has the faces {string.Join(", ", PieceFaces.Of(d).Select(x => x.Key))}."));
        if (PieceFaces.IsShapeFace(f.Key)) return Task.FromResult(ToolResult.Fail("A model's shape faces are painted in the 3D view (studio_steps)."));
        string? note = null;
        var doc = (s.Faces.CanvasOpen && s.Faces.OpenDef == d && s.Faces.OpenKey == f.Key ? s.Canvas.Doc : null)
            ?? ArtStore.Load(s.Store, s.Pack.Id, f.Sha) ?? s.DocForPicture(d, f, out note);
        var head = $"{d.Name} · {f.Label} ({f.Key}){(f.Sha == null ? " · nothing drawn on it yet" : "")}{(note != null ? " · " + note : "")}\n";
        return Task.FromResult(new ToolResult(head + ArtStore.Serialize(doc), false, Pictures(FacePng(s, f.Sha))));
    });

    Task<ToolResult> WriteFace(JsonElement a) => Change(a, "drew a face", async (s, d) =>
    {
        if (FaceOf(d, a) is not { } f) return ToolResult.Fail($"{d.Name} has the faces {string.Join(", ", PieceFaces.Of(d).Select(x => x.Key))}.");
        if (PieceFaces.IsShapeFace(f.Key)) return ToolResult.Fail("A model's shape faces are painted in the 3D view (studio_steps).");
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("doc", out var raw)) return ToolResult.Fail("doc is needed.");
        var doc = raw.ValueKind switch
        {
            JsonValueKind.Object => ArtStore.Deserialize(raw.GetRawText()),
            JsonValueKind.String => ArtStore.Deserialize(raw.GetString() ?? ""),
            _ => null,
        };
        if (doc == null) return ToolResult.Fail("doc could not be read as a face (studio_read_face shows the shape of one).");
        if (Problem(s, doc) is { } problem) return ToolResult.Fail(problem);
        if (doc.Selected == null || doc.Layer(doc.Selected) == null) doc.Selected = doc.Layers.LastOrDefault()?.Id;
        var keys = PieceFaces.Of(d).Where(x => !PieceFaces.IsShapeFace(x.Key)).Select(x => x.Key).ToList();
        var also = Strings(a, "also") ?? new();
        var more = also.Contains("every look")
            ? PieceFaces.Of(d).Where(x => x.Back == f.Back && x.State >= 0 && x.Key != f.Key && !PieceFaces.IsShapeFace(x.Key)).Select(x => x.Key).ToList()
            : also.Where(k => k != f.Key).Distinct().ToList();
        if (more.FirstOrDefault(k => !keys.Contains(k)) is { } unknown) return ToolResult.Fail($"{d.Name} has no face {unknown}; its faces are {string.Join(", ", keys)}.");
        var sha = await s.Faces.Write(d, f.Key, doc);
        foreach (var k in more) await s.Faces.Write(d, k, ArtStore.Deserialize(ArtStore.Serialize(doc))!);
        if (s.View == "paint" && s.Faces.Stale(s.Sel, s.FaceKey)) s.OpenFace(s.FaceKey);
        var copies = more.Count > 0 ? $", and the same on {string.Join(", ", more)}" : "";
        return new ToolResult($"Drew {d.Name} · {f.Label} ({doc.W}×{doc.H} px, {doc.Layers.Count} layers){copies}.", false, Pictures(FacePng(s, sha)));
    });

    Task<ToolResult> PictureSize(JsonElement a) => Change(a, "changed a face's picture size", async (s, d) =>
    {
        if (FaceOf(d, a) is not { } f) return ToolResult.Fail($"{d.Name} has the faces {string.Join(", ", PieceFaces.Of(d).Select(x => x.Key))}.");
        if (PieceFaces.IsShapeFace(f.Key)) return ToolResult.Fail("A model's shape faces are painted in the 3D view (studio_steps).");
        if (a.Num("width") is not { } w || a.Num("height") is not { } h) return ToolResult.Fail("width and height are needed, in pixels.");
        int nw = (int)Math.Round(w), nh = (int)Math.Round(h);
        if (nw < StudioCanvas.SmallestSide || nh < StudioCanvas.SmallestSide || nw > StudioCanvas.LargestSide || nh > StudioCanvas.LargestSide)
            return ToolResult.Fail($"width and height are {StudioCanvas.SmallestSide} to {StudioCanvas.LargestSide} px.");
        string? note = null;
        var doc = ArtStore.Load(s.Store, s.Pack.Id, f.Sha) ?? s.DocForPicture(d, f, out note);
        var was = $"{doc.W}×{doc.H}";
        StudioCanvas.Resize(doc, nw, nh, a.Flag("scale") != false);
        var sha = await s.Faces.Write(d, f.Key, doc);
        if (s.View == "paint" && s.Faces.Stale(s.Sel, s.FaceKey)) s.OpenFace(s.FaceKey);
        return new ToolResult($"{d.Name} · {f.Label} ({f.Key}) is {nw}×{nh} px, was {was}{(note != null ? " · " + note : "")}.", false, Pictures(FacePng(s, sha)));
    });

    static string? Problem(PieceStudio s, ArtDoc doc)
    {
        if (doc.W < 8 || doc.H < 8 || doc.W > MaxSide || doc.H > MaxSide) return $"W and H are 8 to {MaxSide} pixels.";
        if (doc.Layers.Count > MaxLayers) return $"At most {MaxLayers} layers.";
        if (doc.Layers.Sum(l => l.Ops.Sum(o => o.P.Count)) / 2 + (doc.Outline?.Count ?? 0) / 2 > MaxPoints) return $"At most {MaxPoints} points in all.";
        if (doc.Outline is { } o && (o.Count < 6 || o.Count % 2 != 0)) return "Outline is [x,y,…] with at least three points.";
        var ids = new HashSet<string>();
        foreach (var l in doc.Layers)
        {
            if (string.IsNullOrEmpty(l.Id) || !ids.Add(l.Id)) l.Id = ArtDoc.NewId();
            switch (l.Kind)
            {
                case "paint":
                    foreach (var op in l.Ops)
                    {
                        if (op.K is not ("path" or "shape" or "fill")) return $"Layer {l.Name}: an op's K is path or shape.";
                        if (op.K == "fill" && (op.M == null || !File.Exists(ArtStore.SrcPath(s.Store, s.Pack.Id, op.M)))) return $"Layer {l.Name}: a fill keeps only the mask it was read with.";
                        if (op.P.Count % 2 != 0) return $"Layer {l.Name}: P is [x,y,x,y,…].";
                    }
                    break;
                case "text":
                    if (l.Text == null) return $"Layer {l.Name}: a text layer needs Text.";
                    break;
                case "image":
                    if (l.Src == null || !File.Exists(ArtStore.SrcPath(s.Store, s.Pack.Id, l.Src))) return $"Layer {l.Name}: Src {l.Src} is not in the pack (studio_add_picture_source puts a file in).";
                    break;
                case "block":
                    if (l.Block == null) return $"Layer {l.Name}: a block layer needs Block.";
                    if (!Symbols.Contains(l.Block.Symbol)) return $"Layer {l.Name}: Symbol is one of {string.Join(", ", Symbols)}.";
                    break;
                default:
                    return $"Layer {l.Name}: Kind is paint, text, image or block.";
            }
        }
        return null;
    }

    Task<ToolResult> AddSource(JsonElement a) => On(async s =>
    {
        var (files, noPicture) = await PictureLinks.Local(new[] { a.Str("path") ?? "" }, s.Store.Root);
        if (noPicture != null) return ToolResult.Fail(noPicture);
        var path = files[0];
        if (!File.Exists(path)) return ToolResult.Fail($"No such file: {path}");
        var src = ArtStore.AddSrcFile(s.Store, s.Pack.Id, path, out var img);
        if (src == null || img == null) return ToolResult.Fail($"{Path.GetFileName(path)} could not be read as a picture.");
        return new ToolResult(JsonSerializer.Serialize(new { Src = src, SrcW = img.GetWidth(), SrcH = img.GetHeight() }));
    });

    Task<ToolResult> ViewPiece(JsonElement a) => On(async s =>
    {
        if (Def(s, a) is not { } d) return NoKind(a);
        if (!await Select(s, d)) return ToolResult.Fail($"{d.Name} could not be selected (the studio may be asking about unsaved work).");
        if (s.View != "table") { s.SetView("table"); await s.Frames(4); }
        if (s.Paper.Instance(d) is { } p) s.T.Rig.Frame(Coords.ToWorld(p.Pos), (float)Math.Max(3, Math.Max(d.Size.W, d.Size.H) * 2.2));
        await _a.Drawn(AgentEndpoint.SettleFrames);
        var shot = await _a.Snapshot();
        return shot == null ? ToolResult.Fail("No picture: the window is not drawing (minimised, or a run without a screen).") : new ToolResult($"{d.Name} on the studio's table.", false, new[] { shot });
    });
}
