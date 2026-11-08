using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class ModelStudio
{
    bool _flat;
    int _flatState = -1;
    string? _openedSig;

    const int FacePaintMax = 2048;

    public bool Flat => _flat;
    internal int FlatState => _flatState;

    bool _turned;
    internal bool Turned => _turned;
    Node3D? _printing;

    public void TurnOver()
    {
        _turned = !_turned;
        ApplyTurn();
        ShowPrinting();
        Frame();
        _studio.RefreshViews();
    }

    void ApplyTurn() => _root.Rotation = new Vector3(0, 0, _turned ? Mathf.Pi : 0);

    bool _reprintAsked;

    internal void Reprint()
    {
        if (_reprintAsked) return;
        _reprintAsked = true;
        Callable.From(() =>
        {
            _reprintAsked = false;
            if (IsInstanceValid(this)) ShowPrinting();
        }).CallDeferred();
    }

    void ShowPrinting()
    {
        if (_printing != null && GodotObject.IsInstanceValid(_printing)) _printing.QueueFree();
        _printing = null;
        if (!_flat || _def == null || _studio.S == null) return;
        var p = _studio.Paper.Instance(_def)?.Clone() ?? new PieceInstance { Def = _def.Id };
        p.Id = "studio-printing";
        p.Pos = new Vec2(0, 0);
        p.RotDeg = 180;
        p.Stack = 0;
        p.Y = 0;
        p.Scale = 1;
        p.FaceDown = _turned;
        if (_flatState >= 0) p.State = _flatState;
        PieceNode node;
        try { node = PieceNode.Create(_studio.S, Layers.Umpire, p, ghost: true, printingOnly: true); }
        catch (Exception e) { GD.PrintErr($"[studio] the table's printing on {_def.Name}: {e.Message}"); return; }
        var holder = new Node3D { Name = "Printing" };
        holder.AddChild(node);
        holder.Transform = node.Transform.AffineInverse();
        _root.AddChild(holder);
        _printing = holder;
    }

    string FlatSig(PieceDef d) => string.Join("|", PieceFaces.Of(d).Where(f => !PieceFaces.IsShapeFace(f.Key)).Select(f => f.Sha ?? "-"))
        + $"|{d.Shape}|{d.Size.W}|{d.Size.H}|{d.Thickness}|{d.Tint}|{d.TintsPictures}|{_flatState}|{d.AddOnMesh}|{d.AddOnTexture}|"
        + string.Join(",", d.States.Select(s => $"{s.Shape}:{s.Size?.W}:{s.Size?.H}"));

    public void ShowState(int state)
    {
        if (!_flat || _def == null || state == _flatState) return;
        _ = SwitchState(_def, state);
    }

    async Task SwitchState(PieceDef d, int state)
    {
        int gen = ++_openGen;
        if (!await Settle() || gen != _openGen || _def != d) return;
        _flatState = state;
        OpenFlat(d);
        _studio.RebuildModelPanels();
    }

    void OpenFlat(PieceDef d)
    {
        bool same = _def == d && _flat;
        _flat = true;
        _def = d;
        _bake.Clear();
        _history.Clear();
        foreach (var v in _views.Values) Drop(v);
        _views.Clear();
        _solids.Reset();
        _takeBack = null;
        _dirtyMesh = _dirtyTex = false;
        _doc = (d.AddOnMesh != null ? LoadDoc(_store, _pack.Id, d.AddOnMesh) : null) ?? new ModelDoc();
        _doc.Scale = 1;
        if (d.States.Count == 0) _flatState = -1;
        else _flatState = Math.Clamp(_flatState, 0, d.States.Count - 1);
        var st = _flatState >= 0 ? d.States[_flatState] : null;
        var faces = PieceFaces.Of(d).Where(f => f.State == _flatState && !PieceFaces.IsShapeFace(f.Key)).ToList();
        var front = faces.FirstOrDefault(f => !f.Back);
        var back = faces.FirstOrDefault(f => f.Back);
        var size = st?.Size ?? d.Size;
        float w = (float)Math.Max(0.02, size.W), h = (float)Math.Max(0.02, size.H), t = Mathf.Clamp((float)d.Thickness, 0.02f, 2);
        string shape = st?.Shape ?? d.Shape ?? "box";

        var frontImg = FaceImage(front?.Sha);
        if (shape == "box" && frontImg != null && BlockMesh.SeeThrough(frontImg) is var clear and > 0.01f) shape = clear > 0.2f ? "alpha" : "rounded";
        bool cut = shape == "alpha";
        frontImg ??= BlockMesh.Blank(w, h);

        var outline = BlockMesh.Ccw(PieceNode.Outline(cut ? "box" : shape, w, h));
        var (top, under, side) =
            BlockMesh.Slab(outline, w, h, t, p => new Vector2(p.X / w + 0.5f, p.Y / h + 0.5f), p => new Vector2(0.5f - p.X / w, p.Y / h + 0.5f), (_, _) => new Vector2(0.5f, 0.5f));

        var faceTint = d.TintsPictures ? d.Tint : Colors.White.ToHtml();
        if (front != null)
        {
            var fv = AddView(new ModelPart { Id = front.Key, Name = front.Label, Kind = "face", Tint = faceTint }, top, frontImg, cut);
            fv.Face = front.Key;
            fv.OpenSha = front.Sha;
        }
        if (back != null)
        {
            var own = back.Sha != null && back.Sha != front?.Sha ? FaceImage(back.Sha) : null;
            var img = own;
            if (img == null) { img = (Image)frontImg.Duplicate(); img.FlipX(); }
            var bv = AddView(new ModelPart { Id = back.Key, Name = back.Label, Kind = "face", Tint = faceTint }, under, img, cut);
            bv.Face = back.Key;
            bv.OpenSha = back.Sha;
            bv.FlipFront = own == null;
        }
        var edges = cut ? BlockMesh.CutWalls(frontImg, w, h, t, new Vector2(0.5f, 0.5f)) : side;
        if (edges != null)
        {
            var tint = Coords.ParseColor(d.Tint, Colors.White);
            var edge = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            edge.Fill((tint * 0.82f) with { A = 1 });
            var ev = AddView(new ModelPart { Id = "edges", Name = "Edges", Kind = "edge" }, edges, edge);
            ev.Fixed = true;
        }
        var unread = MakeViews(d.Name);
        if (unread.Count > 0) _studio.Say($"Could not read {string.Join(", ", unread)} on this piece: left out of the view, kept in its document.");
        if (!same || !_views.ContainsKey(_selPart ?? "")) _selPart = front?.Key;
        _openedSig = FlatSig(d);
        _openedTex = d.Texture;
        _openedMesh = d.Mesh;
        if (!same)
        {
            _turned = false;
            ApplyTurn();
            Frame();
        }
        ShowPrinting();
        RefreshSelection();
    }

    Image? FaceImage(string? sha)
    {
        if (sha == null || PackEdit.LoadAsset(_store, _pack, sha) is not { } img) return null;
        int w = img.GetWidth(), h = img.GetHeight();
        if (Math.Max(w, h) > FacePaintMax)
        {
            float k = FacePaintMax / (float)Math.Max(w, h);
            img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), Image.Interpolation.Lanczos);
        }
        return img;
    }

    async Task<bool> BakeFlat()
    {
        var def = _def;
        if (def == null) return true;
        var views = _views.Values.Where(v => v.Face != null && !v.Fixed).ToList();
        var doc = _doc;
        var addOns = _views.Values.Where(v => IsPart(v.Part.Id) || ModelTree.IsSolidKey(v.Part.Id)).ToDictionary(v => v.Part.Id);
        bool addOnsChanged = _dirtyMesh || addOns.Values.Any(v => v.PaintDirty);
        _dirtyMesh = _dirtyTex = false;
        _bake.Clear();
        try
        {
            bool any = false;
            if (addOnsChanged)
            {
                _studio.SetSaved("saving…");
                any = true;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                BakeAddOns(def, doc, addOns);
            }
            foreach (var v in views)
            {
                bool blank = !v.Paint.AsSpan().ContainsAnyExcept((byte)0);
                if (v.Saved == null ? blank : v.Saved.AsSpan().SequenceEqual(v.Paint)) continue;
                if (!any) { _studio.SetSaved("saving…"); any = true; }
                var held = (byte[])v.Paint.Clone();
                await _studio.Faces.SaveFromModel(def, v.Face!, v.OpenSha, v.FlipFront ? v.Base : null, v.Layer, blank ? null : ModelBake.Straight(v.W, v.H, held));
                v.Saved = held;
            }
            if (def == _def) _openedSig = FlatSig(def);
            if (any) _studio.SetSaved(_dirtyTex ? "editing…" : "saved");
            Problem = null;
            return true;
        }
        catch (Exception e)
        {
            _dirtyTex = true;
            if (addOnsChanged) _dirtyMesh = true;
            if (def == _def && _bake.In < 0) _bake.In = 3;
            Problem = e.Message;
            GD.PrintErr($"[studio] could not save the faces of {def.Name}: {e}");
            _studio.Say("Could not save the paint on the block: " + e.Message);
            _studio.SetSaved("NOT SAVED");
            return false;
        }
    }

    void BakeAddOns(PieceDef def, ModelDoc doc, Dictionary<string, ModelPartView> views)
    {
        var (oldMesh, oldTex) = (def.AddOnMesh, def.AddOnTexture);
        if (doc.Parts.Count == 0)
        {
            def.AddOnMesh = def.AddOnTexture = null;
        }
        else
        {
            var items = views.Values.Where(v => v.Node.Visible && !IsGhost(v)).ToList();
            _baker.KeepPaint(views.Values);
            if (items.Count == 0)
            {
                if (oldMesh != null) KTable.Core.Json.WriteAtomic(DocPath(_store, _pack.Id, oldMesh), JsonSerializer.Serialize(doc, Opts));
                _studio.Say("Every shape on the block is hidden: the piece on the table keeps them until one is shown.");
                return;
            }
            var (atlas, size, mesh) = ModelBake.Pack(items);
            _solids.Keep();
            var texSha = PackEdit.AddImage(_store, _pack, atlas);
            var meshSha = _baker.AddMesh(ModelImport.ToObj(mesh));
            doc.Baked = texSha;
            doc.BakedSize = size;
            KTable.Core.Json.WriteAtomic(DocPath(_store, _pack.Id, meshSha), JsonSerializer.Serialize(doc, Opts));
            def.AddOnMesh = meshSha;
            def.AddOnTexture = texSha;
            _studio.MadeHere.Add(meshSha);
            _studio.MadeHere.Add(texSha);
        }
        if (def == _def) _openedSig = FlatSig(def);
        _studio.Superseded(oldMesh, def.AddOnMesh);
        _studio.Superseded(oldTex, def.AddOnTexture);
        _studio.ModelBaked(def);
    }
}

internal static class BlockMesh
{
    public static List<Vector2> Ccw(List<Vector2> outline)
    {
        float area = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        if (area < 0) outline.Reverse();
        return outline;
    }

    public static (MeshData top, MeshData under, MeshData side) Slab(List<Vector2> outline, float w, float h, float t, Func<Vector2, Vector2> top, Func<Vector2, Vector2> under,
        Func<float, float, Vector2> side)
    {
        var mt = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        var mu = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        var ms = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        for (int i = 1; i + 1 < outline.Count; i++)
        {
            foreach (var p in new[] { outline[0], outline[i], outline[i + 1] }) mt.Add(new Vector3(p.X, t, p.Y), Vector3.Up, top(p), Colors.White);
            foreach (var p in new[] { outline[0], outline[i + 1], outline[i] }) mu.Add(new Vector3(p.X, 0, p.Y), Vector3.Down, under(p), Colors.White);
            mt.Mat.Add(0);
            mu.Mat.Add(0);
        }
        float round = 0;
        for (int i = 0; i < outline.Count; i++) round += outline[i].DistanceTo(outline[(i + 1) % outline.Count]);
        float at = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            float s0 = at / round, s1 = (at += a.DistanceTo(b)) / round;
            var a0 = new Vector3(a.X, 0, a.Y);
            var b0 = new Vector3(b.X, 0, b.Y);
            var a1 = a0 with { Y = t };
            var b1 = b0 with { Y = t };
            var n = new Vector3(b.Y - a.Y, 0, a.X - b.X).Normalized();
            foreach (var (v, uv) in new[] { (a0, side(s0, 1)), (b0, side(s1, 1)), (b1, side(s1, 0)), (a0, side(s0, 1)), (b1, side(s1, 0)), (a1, side(s0, 0)) }) ms.Add(v, n, uv, Colors.White);
            ms.Mat.Add(0);
            ms.Mat.Add(0);
        }
        return (mt, mu, ms);
    }

    public static MeshData? CutWalls(Image front, float w, float h, float t, Vector2 uv)
    {
        if (PieceNode.SilhouetteWalls(Assets.BuildMask(front), w, h, t) is not { } walls) return null;
        var a = walls.SurfaceGetArrays(0);
        var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var n = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var idx = a[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : a[(int)Mesh.ArrayType.Index].AsInt32Array();
        var m = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        int count = idx?.Length ?? v.Length;
        for (int i = 0; i < count; i++) { int k = idx?[i] ?? i; m.Add(v[k], k < n.Length ? n[k] : Vector3.Up, uv, Colors.White); }
        for (int i = 0; i < m.Tris; i++) m.Mat.Add(0);
        return m;
    }

    public static Image Blank(float w, float h)
    {
        int pw = 512, ph = (int)Math.Clamp(Math.Round(512 * h / Math.Max(0.01, w)), 16, 2048);
        var img = Image.CreateEmpty(pw, ph, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        return img;
    }

    public static float SeeThrough(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight(), n = 0, clear = 0;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                n++;
                if (img.GetPixel(Math.Min(w - 1, (int)((x + 0.5f) * w / 64)), Math.Min(h - 1, (int)((y + 0.5f) * h / 64))).A < 0.5f) clear++;
            }
        return clear / (float)n;
    }

    public static (MeshData block, Image tex) AsShape(PieceDef d, Func<string?, Image?> faceImage)
    {
        var faces = PieceFaces.Of(d);
        var front = faces.FirstOrDefault(f => f.Key == "front");
        var back = faces.FirstOrDefault(f => f.Key == "back");
        float w = (float)Math.Max(0.02, d.Size.W), h = (float)Math.Max(0.02, d.Size.H), t = Mathf.Clamp((float)d.Thickness, 0.02f, 2);
        string outlineShape = d.Shape ?? "box";
        var frontImg = faceImage(front?.Sha);
        if (outlineShape == "box" && frontImg != null && SeeThrough(frontImg) is var clear and > 0.01f) outlineShape = clear > 0.2f ? "alpha" : "rounded";
        bool cut = outlineShape == "alpha";
        frontImg ??= Blank(w, h);
        if (frontImg.GetWidth() > 1024) frontImg.Resize(1024, Math.Max(1, frontImg.GetHeight() * 1024 / frontImg.GetWidth()), Image.Interpolation.Lanczos);
        int fw = frontImg.GetWidth(), fh = frontImg.GetHeight();
        var backImg = back?.Sha != null && back.Sha != front?.Sha ? faceImage(back.Sha) : null;
        if (backImg == null) { backImg = (Image)frontImg.Duplicate(); backImg.FlipX(); }
        if (backImg.GetWidth() != fw || backImg.GetHeight() != fh) backImg.Resize(fw, fh, Image.Interpolation.Lanczos);
        int eh = Math.Clamp((int)Math.Round(fh * t / h), 12, 96);
        int W = fw * 2 + 4, H = fh + 4 + eh;
        var tex = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        tex.Fill((Coords.ParseColor(d.Tint, Colors.White) * 0.82f) with { A = 1 });
        frontImg.Convert(Image.Format.Rgba8);
        backImg.Convert(Image.Format.Rgba8);
        tex.BlitRect(frontImg, new Rect2I(0, 0, fw, fh), Vector2I.Zero);
        tex.BlitRect(backImg, new Rect2I(0, 0, fw, fh), new Vector2I(fw + 4, 0));
        var outline = Ccw(PieceNode.Outline(cut ? "box" : outlineShape, w, h));
        var (top, under, side) = Slab(outline, w, h, t,
            p => new Vector2((p.X / w + 0.5f) * fw / W, (p.Y / h + 0.5f) * fh / H),
            p => new Vector2((fw + 4 + (0.5f - p.X / w) * fw) / W, (p.Y / h + 0.5f) * fh / H),
            (round, down) => new Vector2((1 + round * (W - 2)) / W, (fh + 4 + down * (eh - 1)) / H));
        if (cut) side = CutWalls(frontImg, w, h, t, new Vector2(0.5f, (fh + 4 + eh / 2f) / H)) ?? new MeshData();
        var block = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        foreach (var m in new[] { top, under, side })
        {
            block.V.AddRange(m.V);
            block.N.AddRange(m.N);
            block.UV.AddRange(m.UV);
            block.C.AddRange(m.C);
            block.Mat.AddRange(m.Mat);
        }
        return (block, tex);
    }
}
