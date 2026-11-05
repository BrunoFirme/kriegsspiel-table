using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class PieceFramer
{
    readonly PieceStudio _s;
    const string FrameBorder = "Frame border", FrameBacking = "Frame backing";
    internal const string AlongThePicture = "Along the picture", DrawnByHand = "Drawn by hand";
    const string BlankFace = "#ffffffff", NoBorder = "#1f1f1fff";
    const float BorderScale = 2f / 512f;

    internal static readonly string[] Outlines = { "Rectangle", "Rounded", "Round", "Hex", "Octagon", "Shield", "Banner", "Arrow", "Diamond", AlongThePicture };

    sealed record FrameLook(string Border, float Width, string Behind);

    public PieceFramer(PieceStudio s) => _s = s;

    internal static string? ShapeOf(string outline) => outline switch { "Rectangle" => "box", "Rounded" => "rounded", "Round" => "circle", "Hex" => "hex", AlongThePicture => "alpha", _ => null };

    internal static string NameOfShape(string shape) => shape switch { "rounded" => "Rounded", "circle" => "Round", "hex" => "Hex", "alpha" => AlongThePicture, _ => "Rectangle" };

    internal static int LookOf(PieceDef d, int state) => d.States.Count == 0 ? -1 : Math.Clamp(state, 0, d.States.Count - 1);

    static PieceFaces.Face Side(PieceDef d, int look, bool back) => PieceFaces.Of(d).First(f => f.Key == PieceFaces.Key(Math.Max(0, look), back));

    internal static List<Vector2> Preset(string name, int w, int h)
    {
        float m = Mathf.Min(w, h) * 0.02f;
        return name switch
        {
            "Rounded" => ArtGeom.RoundRect(new Vector2(m, m), new Vector2(w - m, h - m), Mathf.Min(w, h) * 0.16f),
            "Round" => ArtGeom.Ellipse(new Vector2(m, m), new Vector2(w - m, h - m), 72),
            "Hex" => ArtDraw.Outline("hex", w, h),
            "Octagon" => ArtGeom.Ngon(Vector2.Zero, Vector2.FromAngle(Mathf.Pi / 8), 8).Select(p => new Vector2(w / 2f + p.X * (w / 2f - m), h / 2f + p.Y * (h / 2f - m))).ToList(),
            "Shield" => new List<Vector2> { new(m, m), new(w - m, m), new(w - m, h * 0.55f), new(w / 2f, h - m), new(m, h * 0.55f) },
            "Banner" => new List<Vector2> { new(m, m), new(w - m, m), new(w - m, h - m), new(w / 2f, h * 0.72f), new(m, h - m) },
            "Arrow" => new List<Vector2> { new(m, h * 0.3f), new(w * 0.62f, h * 0.3f), new(w * 0.62f, m), new(w - m, h / 2f), new(w * 0.62f, h - m), new(w * 0.62f, h * 0.7f), new(m, h * 0.7f) },
            "Diamond" => new List<Vector2> { new(w / 2f, m), new(w - m, h / 2f), new(w / 2f, h - m), new(m, h / 2f) },
            _ => ArtGeom.Rect(new Vector2(m, m), new Vector2(w - m, h - m)),
        };
    }

    internal string NameOf(PieceDef d, int state)
    {
        int look = LookOf(d, state);
        var shape = PieceFaces.ShapeOf(d, look);
        if (shape != "alpha") return NameOfShape(shape);
        var front = PieceFaces.Of(d).FirstOrDefault(f => f.Key == PieceFaces.Key(Math.Max(0, look), false));
        if (ArtStore.Load(_s.Store, _s.Pack.Id, front?.Sha) is not { Outline: { } drawn } doc) return AlongThePicture;
        foreach (var n in Outlines.Where(n => ShapeOf(n) == null))
            if (ArtXf.Flat(Preset(n, doc.W, doc.H)).SequenceEqual(drawn)) return n;
        return DrawnByHand;
    }

    internal bool Framed(PieceDef d) => PieceFaces.Of(d).Any(f => f.Sha != null && !PieceFaces.IsShapeFace(f.Key) && ArtStore.Load(_s.Store, _s.Pack.Id, f.Sha) is { } doc && FrameOf(doc) != null);

    ArtDoc DocOf(PieceDef d, PieceFaces.Face f) => ArtStore.Deserialize(ArtStore.Serialize(ArtStore.Load(_s.Store, _s.Pack.Id, f.Sha) ?? _s.DocForPicture(d, f, out _)))!;

    async Task<bool> Ready(PieceDef d, string notDone)
    {
        if (await _s.SettleWork() is { } problem) { _s.Flash(notDone + problem.Replace("\n", "; ")); return false; }
        return !_s.Gone && _s.Pack.Pieces.Contains(d) && d.Kind != "model" && !EffectFx.IsEffect(d);
    }

    public async Task SetOutline(PieceDef d, int state, string outline)
    {
        if (!Outlines.Contains(outline) || !await Ready(d, "The outline is as it was: ")) return;
        int look = LookOf(d, state);
        var shape = ShapeOf(outline);
        _s.Edits.Before(_s.Pack, d, "the outline");
        try
        {
            PieceFaces.SetShape(d, look, shape ?? "alpha");
            var front = Side(d, look, false);
            var frontDoc = DocOf(d, front);
            var frontHad = frontDoc.Outline;
            frontDoc.Outline = shape == null ? ArtXf.Flat(Preset(outline, frontDoc.W, frontDoc.H)) : null;
            if (front.Sha == null) frontDoc.Bg ??= BlankFace;
            if (front.Sha != null || shape == null)
            {
                if (FrameOf(frontDoc) is { } fr) Ring(frontDoc, Path(frontDoc, d, look, null, false) ?? await Traced(frontDoc), fr);
                if (frontHad != null || frontDoc.Outline != null || FrameOf(frontDoc) != null) await _s.Faces.Write(d, front.Key, frontDoc);
            }
            var back = Side(d, look, true);
            if (back.Sha != null)
            {
                var backDoc = DocOf(d, back);
                var backHad = backDoc.Outline;
                backDoc.Outline = null;
                if (FrameOf(backDoc) is { } fr) Ring(backDoc, Path(backDoc, d, look, frontDoc, true) ?? await Traced(backDoc), fr);
                if (backHad != null || FrameOf(backDoc) != null) await _s.Faces.Write(d, back.Key, backDoc);
            }
            PieceFaces.SetShape(d, look, shape ?? "alpha");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException) { _s.Flash("The outline could not be set: " + e.Message); }
        finally { _s.Edits.After(d); }
        Refresh(d);
    }

    public async Task Frame(PieceDef d, Color border, float borderPx, Color behind)
    {
        if (!await Ready(d, "No frame yet: ")) return;
        var faces = PieceFaces.Of(d).Where(x => x.Sha != null && !PieceFaces.IsShapeFace(x.Key)).ToList();
        if (faces.Count == 0) { _s.Flash("The piece has no picture to frame yet."); return; }
        var look = new FrameLook(Hex.Of(border), borderPx * BorderScale, Hex.Of(behind with { A = 1 }));
        _s.Edits.Before(_s.Pack, d, "the frame");
        try
        {
            foreach (var f in faces)
            {
                int state = Math.Max(0, f.State);
                var doc = DocOf(d, f);
                var frontDoc = f.Back ? DocOf(d, Side(d, state, false)) : null;
                float k = Math.Min(doc.W, doc.H);
                Ring(doc, Path(doc, d, f.State, frontDoc, f.Back) ?? await Traced(doc), look with { Width = look.Width * k });
                await _s.Faces.Write(d, f.Key, doc);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException) { _s.Flash("Could not frame the piece: " + e.Message); }
        finally { _s.Edits.After(d); }
        Refresh(d);
    }

    public async Task Unframe(PieceDef d)
    {
        if (!await Ready(d, "The frame is still on: ")) return;
        _s.Edits.Before(_s.Pack, d, "the frame");
        try
        {
            foreach (var f in PieceFaces.Of(d).Where(x => x.Sha != null && !PieceFaces.IsShapeFace(x.Key)).ToList())
            {
                var doc = DocOf(d, f);
                if (doc.Layers.RemoveAll(l => l.Name is FrameBorder or FrameBacking) > 0) await _s.Faces.Write(d, f.Key, doc);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException) { _s.Flash("The frame could not be taken off: " + e.Message); }
        finally { _s.Edits.After(d); }
        Refresh(d);
    }

    static FrameLook? FrameOf(ArtDoc doc)
    {
        var ring = doc.Layers.FirstOrDefault(l => l.Name == FrameBorder)?.Ops.FirstOrDefault();
        var backing = doc.Layers.FirstOrDefault(l => l.Name == FrameBacking)?.Ops.FirstOrDefault();
        if (ring == null && backing == null) return null;
        return new FrameLook(ring?.C ?? NoBorder, ring?.W ?? 0, backing?.Fc ?? doc.Bg ?? BlankFace);
    }

    static List<Vector2>? Path(ArtDoc doc, PieceDef d, int look, ArtDoc? front, bool back)
    {
        if (doc.Outline is { Count: >= 6 } own) return Points(own);
        var shape = PieceFaces.ShapeOf(d, look);
        if (shape != "alpha") return Preset(NameOfShape(shape), doc.W, doc.H);
        if (!back || front?.Outline is not { Count: >= 6 } fo) return null;
        float kx = doc.W / (float)Math.Max(1, front.W), ky = doc.H / (float)Math.Max(1, front.H);
        return Points(fo).Select(p => new Vector2(doc.W - p.X * kx, p.Y * ky)).ToList();
    }

    static List<Vector2> Points(List<float> flat) => Enumerable.Range(0, flat.Count / 2).Select(i => new Vector2(flat[i * 2], flat[i * 2 + 1])).ToList();

    async Task<List<Vector2>> Traced(ArtDoc doc)
    {
        var r = _s.Canvas.R.Twin(doc);
        _s.AddChild(r);
        try
        {
            r.RebuildAll();
            var pts = ArtGeom.TraceAlpha(await r.Flatten());
            return pts.Count >= 3 ? pts : Preset("Rectangle", doc.W, doc.H);
        }
        finally { r.QueueFree(); }
    }

    static void Ring(ArtDoc doc, List<Vector2> path, FrameLook look)
    {
        doc.Layers.RemoveAll(l => l.Name is FrameBorder or FrameBacking);
        var backing = new ArtLayer { Name = FrameBacking, Kind = "paint", Locked = true };
        backing.Ops.Add(new PaintOp { K = "shape", P = ArtXf.Flat(ArtGeom.Rect(Vector2.Zero, new Vector2(doc.W, doc.H))), W = 0, Fc = look.Behind, C = look.Behind });
        doc.Layers.Insert(0, backing);
        if (look.Width > 0)
        {
            var ring = new ArtLayer { Name = FrameBorder, Kind = "paint", Locked = true };
            ring.Ops.Add(new PaintOp { K = "shape", P = ArtXf.Flat(path), C = look.Border, W = look.Width });
            doc.Layers.Add(ring);
        }
    }

    internal static ArtLayer BorderLayer(List<Vector2> path, Color border, float borderPx, int size)
    {
        var ring = new ArtLayer { Name = FrameBorder, Kind = "paint", Locked = true };
        ring.Ops.Add(new PaintOp { K = "shape", P = ArtXf.Flat(path), C = Hex.Of(border), W = borderPx * BorderScale * size });
        return ring;
    }

    void Refresh(PieceDef d)
    {
        if (_s.Gone) { _s.FlushPack(); return; }
        _s.SavePack();
        _s.T.RebuildPiecesOf(d.Id);
        _s.RebuildList();
        if (d != _s.Sel) return;
        _s.RebuildInspector();
        _s.RebuildFaceBar();
        if (_s.View == "model" && _s.Model != null) _ = _s.OpenModel(d);
        else if (_s.View == "paint" && _s.Faces.Stale(_s.Sel, _s.FaceKey)) _s.OpenFace(_s.FaceKey);
        else if (_s.View == "paint") { _s.SyncCanvasPiece(); _s.RebuildToolPanel(); }
    }

    public void DrawOwn(PieceDef d, int state)
    {
        _s.PaintFace(Side(d, LookOf(d, state), false).Key);
        _s.Canvas.SetTool("outline");
    }

    public void Ask(PieceDef? d)
    {
        if (d == null || d.Kind == "model") return;
        var dlg = new ConfirmationDialog { Title = "Frame", OkButtonText = "Frame it" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        dlg.AddChild(box);
        var g = Ui.Form(70);
        g.AddChild(new Label { Text = "Border" });
        var br = new HBoxContainer();
        var col = new ColorPickerButton { Color = new Color(0.12f, 0.12f, 0.12f), EditAlpha = false, CustomMinimumSize = new Vector2(44, 24) };
        var w =
            new HSlider { MinValue = 0, MaxValue = 48, Step = 1, Value = 10, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(180, 0), TooltipText = "0: no border" };
        br.AddChild(col);
        br.AddChild(w);
        br.AddChild(Ui.ValueBox(w, v => $"{v:0} px", 44));
        g.AddChild(br);
        g.AddChild(new Label { Text = "Behind" });
        var fill = new ColorPickerButton { Color = new Color(0.85f, 0.81f, 0.71f), EditAlpha = false, CustomMinimumSize = new Vector2(44, 24), TooltipText = "The colour behind the picture, where it is see-through" };
        g.AddChild(fill);
        Ui.AlignForm(g);
        box.AddChild(g);
        box.AddChild(Ui.Hint("The frame runs along the piece's outline, on every face that has a picture."));
        if (Framed(d)) box.AddChild(Ui.Btn("Take the frame off", () => { dlg.Hide(); _ = Unframe(d); }));
        dlg.Confirmed += () => _ = Frame(d, col.Color, (float)w.Value, fill.Color);
        Ui.Open(_s.Root, dlg);
        Ui.FreeOnClose(dlg);
    }
}
