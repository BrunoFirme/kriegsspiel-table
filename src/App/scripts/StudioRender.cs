using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using N = System.Numerics;

namespace KTable.App;

public static class ArtGeom
{
    static N.Vector2 In(Vector2 v) => new(v.X, v.Y);
    static Vector2 Out(N.Vector2 v) => new(v.X, v.Y);
    static List<Vector2> Out(List<N.Vector2> l) => l.Select(Out).ToList();

    public static List<Vector2> Rect(Vector2 a, Vector2 b) => Out(ArtShapes.Rect(In(a), In(b)));
    public static List<Vector2> RoundRect(Vector2 a, Vector2 b, float r) => Out(ArtShapes.RoundRect(In(a), In(b), r));
    public static List<Vector2> Ellipse(Vector2 a, Vector2 b, int n = 96) => Out(ArtShapes.Ellipse(In(a), In(b), n));
    public static List<Vector2> Ngon(Vector2 c, Vector2 corner, int sides) => Out(ArtShapes.Ngon(In(c), In(corner), sides));
    public static List<Vector2> Star(Vector2 c, Vector2 tip, int points, float inner) => Out(ArtShapes.Star(In(c), In(tip), points, inner));
    public static List<Vector2> ArrowHead(Vector2 a, Vector2 b, float width) => Out(ArtShapes.ArrowHead(In(a), In(b), width));
    public static Vector2 ArrowShaftEnd(Vector2 a, Vector2 b, float width) => Out(ArtShapes.ArrowShaftEnd(In(a), In(b), width));

    public static List<Transform2D> Symmetry(string mode, int radial, Vector2 c) =>
        ArtShapes.Symmetry(mode, radial, In(c)).Select(t => new Transform2D(Out(t.X), Out(t.Y), Out(t.Origin))).ToList();

    public static float[] PolyCoverage(int w, int h, IReadOnlyList<Vector2> poly) => ArtShapes.PolyCoverage(w, h, poly.Select(In).ToList());

    public static List<Vector2> TraceAlpha(Image img, int grid = 192, float tolerance = 1.5f)
    {
        var (pts, k) = ArtTrace.Outline(img.GetData(), img.GetWidth(), img.GetHeight(), grid);
        return Simplify(Out(pts), tolerance * k);
    }

    public static List<Vector2> Simplify(List<Vector2> pts, float eps)
    {
        if (pts.Count < 8) return pts;
        var keep = new bool[pts.Count];
        void Rec(int a, int b)
        {
            float best = 0; int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                var c = Geometry2D.GetClosestPointToSegment(pts[i], pts[a], pts[b]);
                float dd = c.DistanceTo(pts[i]);
                if (dd > best) { best = dd; at = i; }
            }
            if (at >= 0 && best > eps) { keep[at] = true; Rec(a, at); Rec(at, b); }
        }
        int half = pts.Count / 2;
        keep[0] = keep[half] = true;
        Rec(0, half); Rec(half, pts.Count - 1);
        keep[pts.Count - 1] = true;
        var res = new List<Vector2>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) res.Add(pts[i]);
        return res;
    }
}

public static class ArtDraw
{
    static readonly Dictionary<string, Font> FontCache = new();

    public static Font FontFor(string name, bool bold, bool italic)
    {
        var key = $"{name}|{bold}|{italic}";
        if (FontCache.TryGetValue(key, out var f)) return f;
        var names = name switch
        {
            "serif" => new[] { "Georgia", "Cambria", "Times New Roman", "Noto Serif" },
            "sans" or "bold" or "" => new[] { "Segoe UI", "Arial", "Helvetica", "Noto Sans" },
            _ => new[] { name, "Segoe UI", "Arial" },
        };
        f = new SystemFont { FontNames = names, FontWeight = bold || name == "bold" ? 700 : 400, FontItalic = italic, Oversampling = 1, MultichannelSignedDistanceField = false };
        FontCache[key] = f;
        return f;
    }

    public static Rect2 TextBounds(ArtLayer l)
    {
        var font = FontFor(l.Font, l.Bold, l.Italic);
        int size = Math.Max(4, (int)l.Size);
        var lines = (l.Text ?? "").Split('\n');
        float w = lines.Max(s => font.GetStringSize(s.Length == 0 ? " " : s, HorizontalAlignment.Left, -1, size).X);
        float lh = font.GetHeight(size), h = lh * lines.Length;
        float pad = l.OutlineColor != null ? l.OutlineW : 0;
        return new Rect2(-w / 2 - pad, -h / 2 - pad, w + pad * 2, h + pad * 2);
    }

    public static void DrawText(CanvasItem ci, ArtLayer l)
    {
        var font = FontFor(l.Font, l.Bold, l.Italic);
        int size = Math.Max(4, (int)l.Size);
        var lines = (l.Text ?? "").Split('\n');
        float lh = font.GetHeight(size);
        float top = -lh * lines.Length / 2;
        var col = Hex.To(l.Color, Colors.White);
        for (int i = 0; i < lines.Length; i++)
        {
            float w = font.GetStringSize(lines[i], HorizontalAlignment.Left, -1, size).X;
            var at = new Vector2(-w / 2, top + lh * i + font.GetAscent(size));
            if (l.OutlineColor != null && l.OutlineW > 0)
                ci.DrawStringOutline(font, at, lines[i], HorizontalAlignment.Left, -1, size, Mathf.Max(1, (int)(l.OutlineW * 2)), Hex.To(l.OutlineColor, Colors.Black));
            ci.DrawString(font, at, lines[i], HorizontalAlignment.Left, -1, size, col);
        }
    }

    public static Rect2 ImageBounds(ArtLayer l)
    {
        var c = l.Crop is { Length: 4 } cr ? new Rect2(cr[0], cr[1], cr[2], cr[3]) : new Rect2(0, 0, l.SrcW, l.SrcH);
        return new Rect2(c.Position - new Vector2(l.SrcW, l.SrcH) / 2, c.Size);
    }

    public static void DrawImage(CanvasItem ci, ArtLayer l, Texture2D tex)
    {
        var src = l.Crop is { Length: 4 } cr ? new Rect2(cr[0], cr[1], cr[2], cr[3]) : new Rect2(0, 0, l.SrcW, l.SrcH);
        ci.DrawTextureRectRegion(tex, ImageBounds(l), src);
    }

    public static Rect2 OpBounds(PaintOp op)
    {
        if (op.K == "fill" && op.Mx is { Length: 6 } m && op.P.Count >= 2)
        {
            var t = new Transform2D(m[0], m[1], m[2], m[3], m[4], m[5]);
            var r = new Rect2(0, 0, op.P[0], op.P[1]);
            return (t * r);
        }
        var pts = op.Pts();
        if (pts.Length == 0) return new Rect2();
        var b = new Rect2(pts[0], Vector2.Zero);
        foreach (var p in pts) b = b.Expand(p);
        return b.Grow(op.W / 2);
    }

    public static void DrawOp(CanvasItem ci, PaintOp op, Func<string, Texture2D?> tex, float alphaScale = 1)
    {
        Color Col(string? hex) { var c = Hex.To(hex, Colors.Black); if (alphaScale != 1) c.A = Mathf.Clamp(c.A / alphaScale, 0, 1); return op.E ? Colors.White : c; }
        switch (op.K)
        {
            case "fill":
                if (op.M == null || op.Mx is not { Length: 6 } m || tex(op.M) is not { } mt) return;
                ci.DrawSetTransformMatrix(new Transform2D(m[0], m[1], m[2], m[3], m[4], m[5]));
                ci.DrawTexture(mt, Vector2.Zero, Col(op.Fc ?? op.C));
                ci.DrawSetTransformMatrix(Transform2D.Identity);
                return;
            case "shape":
            {
                var pts = op.Pts();
                if (pts.Length < 2) return;
                if (op.Fc != null && pts.Length >= 3)
                {
                    var fc = Col(op.Fc);
                    FillPolygon(ci, pts, fc);
                    if (op.W <= 0) ci.DrawPolyline(pts.Append(pts[0]).ToArray(), fc, 1.2f, antialiased: true);
                }
                if (op.W > 0) Line(ci, pts.Append(pts[0]).ToArray(), Col(op.C), op.W);
                return;
            }
            default:
                Line(ci, op.Pts(), Col(op.C), op.W);
                return;
        }
    }

    public static void Line(CanvasItem ci, Vector2[] pts, Color c, float w)
    {
        if (pts.Length == 0) return;
        if (pts.Length == 1 || (pts.Length == 2 && pts[0].DistanceSquaredTo(pts[1]) < 0.01f)) { ci.DrawCircle(pts[0], Mathf.Max(0.5f, w / 2), c, true, -1, true); return; }
        ci.DrawPolyline(pts, c, Mathf.Max(0.75f, w), true);
        if (w > 2.5f)
        {
            float r = w / 2 - 0.35f;
            Vector2 last = new(float.NaN, float.NaN);
            for (int i = 0; i < pts.Length; i++)
            {
                if (i != 0 && i != pts.Length - 1 && last.DistanceSquaredTo(pts[i]) < r * r * 0.25f) continue;
                ci.DrawCircle(pts[i], r, c, true, -1, true);
                last = pts[i];
            }
        }
    }

    public static void FillPolygon(CanvasItem ci, Vector2[] pts, Color c)
    {
        if (Geometry2D.TriangulatePolygon(pts).Length >= 3) { ci.DrawColoredPolygon(pts, c); return; }
        foreach (var p in Geometry2D.MergePolygons(pts, pts))
            if (!Geometry2D.IsPolygonClockwise(p) || true)
                if (Geometry2D.TriangulatePolygon(p).Length >= 3) ci.DrawColoredPolygon(p, c);
    }

    public static List<Vector2> Outline(string shape, float w, float h)
    {
        var pts = new List<Vector2>();
        switch (shape)
        {
            case "circle":
                for (int i = 0; i < 96; i++) { var a = Mathf.Tau * i / 96; pts.Add(new Vector2(w / 2 + Mathf.Cos(a) * w / 2, h / 2 + Mathf.Sin(a) * h / 2)); }
                break;
            case "hex":
                for (int i = 0; i < 6; i++) { var a = Mathf.Tau * i / 6; pts.Add(new Vector2(w / 2 + Mathf.Cos(a) * w / 2, h / 2 + Mathf.Sin(a) * h / 2)); }
                break;
            case "rounded":
                return ArtGeom.RoundRect(Vector2.Zero, new Vector2(w, h), Mathf.Min(w, h) * 0.14f);
            default:
                pts.AddRange(new[] { new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h) });
                break;
        }
        return pts;
    }

    public static void DrawBlock(CanvasItem ci, BlockSpec p)
    {
        float w = p.BW, h = p.BH;
        var pts = Outline(p.Shape, w, h);
        var fill = Hex.To(p.Fill, Colors.SteelBlue);
        ci.DrawColoredPolygon(pts.ToArray(), fill);
        ci.DrawPolyline(pts.Append(pts[0]).ToArray(), fill, 1.2f, antialiased: true);
        if (p.BorderPx > 0)
        {
            var c = new Vector2(w / 2, h / 2);
            float d = p.BorderPx / 2, kx = (w / 2 - d) / (w / 2), ky = (h / 2 - d) / (h / 2);
            var inner = p.Shape == "box"
                ? new List<Vector2> { new(d, d), new(w - d, d), new(w - d, h - d), new(d, h - d) }
                : pts.Select(q => c + new Vector2((q.X - c.X) * kx, (q.Y - c.Y) * ky)).ToList();
            ci.DrawPolyline(inner.Append(inner[0]).ToArray(), Hex.To(p.Border, Colors.White), p.BorderPx, true);
        }
        bool boxy = p.Shape is "box" or "rounded";
        float band = h * (boxy ? 0.24f : 0.2f);
        if (p.Label.Length > 0)
        {
            var font = FontFor(p.Font, p.Font != "sans", false);
            int size = (int)(band * 0.62f);
            var usable = w * (boxy ? 0.86f : 0.6f);
            while (size > 6 && font.GetStringSize(p.Label, HorizontalAlignment.Left, -1, size).X > usable) size--;
            float top = boxy ? p.BorderPx * 0.8f + band * 0.12f : h * 0.14f;
            var tw = font.GetStringSize(p.Label, HorizontalAlignment.Left, -1, size).X;
            ci.DrawString(font, new Vector2(w / 2 - tw / 2, top + font.GetAscent(size)), p.Label, HorizontalAlignment.Left, -1, size, Hex.To(p.LabelColor, Colors.White));
        }
        if (p.Symbol == "none") return;
        float sw = Mathf.Min(w * 0.46f, h * 0.9f) * p.SymbolScale, sh = sw * 0.6f;
        float cy = p.Label.Length > 0 && boxy ? band + (h - band) / 2 : h / 2;
        var r = new Rect2(w / 2 - sw / 2, cy - sh / 2, sw, sh);
        float lw = Mathf.Max(2, Mathf.Min(sw, sh) * 0.07f);
        var sc = Hex.To(p.SymbolColor, Colors.White);
        ci.DrawRect(r, sc, false, lw, true);
        void Diag(bool both)
        {
            ci.DrawLine(new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.Position.Y), sc, lw, true);
            if (both) ci.DrawLine(r.Position, r.End, sc, lw, true);
        }
        switch (p.Symbol)
        {
            case "infantry": Diag(true); break;
            case "cavalry": Diag(false); break;
            case "artillery": ci.DrawCircle(r.GetCenter(), sh * 0.2f, sc, true, -1, true); break;
            case "horse-artillery": Diag(false); ci.DrawCircle(r.GetCenter(), sh * 0.2f, sc, true, -1, true); break;
            case "engineers": SymbolText(ci, r, "E", sc); break;
            case "hq": SymbolText(ci, r, "HQ", sc); break;
            case "text": SymbolText(ci, r, p.SymbolText, sc); break;
        }
    }

    static void SymbolText(CanvasItem ci, Rect2 r, string text, Color c)
    {
        if (text.Length == 0) return;
        var font = FontFor("bold", true, false);
        int size = (int)(r.Size.Y * 0.7f);
        while (size > 6 && font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > r.Size.X * 0.85f) size--;
        var ts = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        ci.DrawString(font, new Vector2(r.GetCenter().X - ts.X / 2, r.GetCenter().Y + font.GetAscent(size) / 2 - font.GetDescent(size) / 2), text, HorizontalAlignment.Left, -1, size, c);
    }
}

public partial class ArtItem : Node2D
{
    public Action<ArtItem>? Paint;
    public override void _Draw() => Paint?.Invoke(this);
}

public partial class ArtRenderer : Node
{
    public ArtDoc Doc = new();
    public Func<string, Image?> LoadSrc = _ => null;

    sealed class LayerVp
    {
        public required SubViewport Vp;
        public Node2D Xf = null!;
        public ArtItem Live = null!;
        public CanvasGroup LiveGroup = null!;
        public ArtItem LiveInGroup = null!;
    }

    readonly Dictionary<string, LayerVp> _vps = new();
    Dictionary<string, Texture2D> _tex = new();
    Dictionary<string, Image> _img = new();
    static ShaderMaterial? _erase;

    public ArtRenderer Twin(ArtDoc doc) => new() { Doc = doc, LoadSrc = LoadSrc, _tex = new(_tex), _img = new(_img), _unreadable = new(_unreadable) };

    public IEnumerable<string> CachedNames => _img.Keys.Concat(_tex.Keys).ToList();

    public void KeepOnly(ArtDoc doc)
    {
        var keep = doc.Layers.Select(l => l.Src).Concat(doc.Layers.SelectMany(l => l.Ops.Select(o => o.M))).OfType<string>().ToHashSet();
        foreach (var k in _img.Keys.Where(k => !keep.Contains(k)).ToList()) _img.Remove(k);
        foreach (var k in _tex.Keys.Where(k => !keep.Contains(k)).ToList()) _tex.Remove(k);
        _unreadable.Clear();
    }

    public PaintOp? LiveOp;
    public List<PaintOp> LiveCopies = new();
    public string? LiveLayer;

    public static ShaderMaterial EraseMaterial => _erase ??= new ShaderMaterial
    {
        Shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode blend_mul;
                void fragment() { float k = 1.0 - COLOR.a; COLOR = vec4(k, k, k, k); }
                """,
        },
    };

    public Texture2D? Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        var img = Img(name);
        if (img == null) return null;
        var copy = (Image)img.Duplicate();
        copy.GenerateMipmaps();
        t = ImageTexture.CreateFromImage(copy);
        _tex[name] = t;
        return t;
    }

    public Image? Img(string name)
    {
        if (_img.TryGetValue(name, out var i)) return i;
        if (_unreadable.Contains(name)) return null;
        i = LoadSrc(name);
        if (i != null) _img[name] = i; else _unreadable.Add(name);
        return i;
    }

    HashSet<string> _unreadable = new();

    public void Remember(string name, Image img) { _img[name] = img; _tex.Remove(name); _unreadable.Remove(name); }

    public Texture2D? LayerTexture(string id) => _vps.TryGetValue(id, out var v) ? v.Vp.GetTexture() : null;

    public void RebuildAll()
    {
        foreach (var v in _vps.Values) v.Vp.QueueFree();
        _vps.Clear();
        foreach (var l in Doc.Layers) Rebuild(l);
    }

    public void Sync()
    {
        var ids = Doc.Layers.Select(l => l.Id).ToHashSet();
        foreach (var id in _vps.Keys.Where(k => !ids.Contains(k)).ToList()) { _vps[id].Vp.QueueFree(); _vps.Remove(id); }
        foreach (var l in Doc.Layers) if (!_vps.ContainsKey(l.Id)) Rebuild(l);
    }

    LayerVp Vp(ArtLayer l)
    {
        if (_vps.TryGetValue(l.Id, out var v) && GodotObject.IsInstanceValid(v.Vp))
        {
            if (v.Vp.Size != new Vector2I(Doc.W, Doc.H)) v.Vp.Size = new Vector2I(Doc.W, Doc.H);
            return v;
        }
        v = new LayerVp
        {
            Vp = new SubViewport
            {
                Size = new Vector2I(Doc.W, Doc.H), TransparentBg = true, Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Once, RenderTargetClearMode = SubViewport.ClearMode.Always,
                CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.LinearWithMipmaps,
            },
        };
        AddChild(v.Vp);
        v.Xf = new Node2D();
        v.Vp.AddChild(v.Xf);
        _vps[l.Id] = v;
        return v;
    }

    public void Rebuild(ArtLayer l)
    {
        var v = Vp(l);
        foreach (var c in v.Xf.GetChildren()) { v.Xf.RemoveChild(c); c.QueueFree(); }
        v.Xf.Transform = l.Xf();
        var content = new ArtItem();
        content.Paint = ci =>
        {
            switch (l.Kind)
            {
                case "image": if (l.Src != null && Tex(l.Src) is { } t) ArtDraw.DrawImage(ci, l, t); break;
                case "text": ArtDraw.DrawText(ci, l); break;
                case "block": if (l.Block != null) ArtDraw.DrawBlock(ci, l.Block); break;
            }
        };
        v.Xf.AddChild(content);
        ArtItem? run = null;
        List<PaintOp>? runOps = null;
        foreach (var op in l.Ops)
        {
            float a = OpAlpha(op);
            if (op.E)
            {
                run = null;
                var o = op;
                var item = new ArtItem { Material = EraseMaterial, Paint = ci => ArtDraw.DrawOp(ci, o, Tex) };
                v.Xf.AddChild(item);
            }
            else if (a < 0.999f)
            {
                run = null;
                var o = op;
                var g = new CanvasGroup { SelfModulate = new Color(1, 1, 1, a), FitMargin = 4, ClearMargin = 4 };
                g.AddChild(new ArtItem { Paint = ci => ArtDraw.DrawOp(ci, o, Tex, a) });
                v.Xf.AddChild(g);
            }
            else
            {
                if (run == null)
                {
                    var ops = runOps = new List<PaintOp>();
                    run = new ArtItem { Paint = ci => { foreach (var x in ops) ArtDraw.DrawOp(ci, x, Tex); } };
                    v.Xf.AddChild(run);
                }
                runOps!.Add(op);
            }
        }
        v.Live = new ArtItem { Paint = ci => { if (LiveLayer == l.Id && LiveOp is { } lo && !lo.E && OpAlpha(lo) >= 0.999f) foreach (var x in LiveCopies) ArtDraw.DrawOp(ci, x, Tex); } };
        v.Xf.AddChild(v.Live);
        v.LiveGroup = new CanvasGroup { FitMargin = 4, ClearMargin = 4 };
        v.LiveInGroup = new ArtItem { Paint = ci => { if (LiveLayer == l.Id && LiveOp is { } lo && !lo.E && OpAlpha(lo) < 0.999f) foreach (var x in LiveCopies) ArtDraw.DrawOp(ci, x, Tex, OpAlpha(lo)); } };
        v.LiveGroup.AddChild(v.LiveInGroup);
        v.Xf.AddChild(v.LiveGroup);
        var liveErase = new ArtItem { Material = EraseMaterial, Paint = ci => { if (LiveLayer == l.Id && LiveOp is { E: true }) foreach (var x in LiveCopies) ArtDraw.DrawOp(ci, x, Tex); } };
        v.Xf.AddChild(liveErase);
        v.Live.SetMeta("erase", liveErase);
        v.Vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    static float OpAlpha(PaintOp op) => op.E ? 1 : Math.Max(op.W > 0 && op.K != "fill" ? Hex.To(op.C, Colors.Black).A : 0, op.Fc != null ? Hex.To(op.Fc, Colors.Black).A : op.K == "fill" ? Hex.To(op.C, Colors.Black).A : 0);

    public void Moved(ArtLayer l)
    {
        if (!_vps.TryGetValue(l.Id, out var v)) { Rebuild(l); return; }
        v.Xf.Transform = l.Xf();
        v.Vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    public void LiveChanged()
    {
        if (LiveLayer == null || !_vps.TryGetValue(LiveLayer, out var v)) return;
        v.Live.QueueRedraw();
        v.LiveInGroup.QueueRedraw();
        if (v.Live.GetMeta("erase").As<ArtItem>() is { } e) e.QueueRedraw();
        v.Vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    public void Resized()
    {
        foreach (var v in _vps.Values) v.Vp.Size = new Vector2I(Doc.W, Doc.H);
        foreach (var l in Doc.Layers) Rebuild(l);
    }

    public async Task<Image> Flatten(bool cut = true, string? only = null)
    {
        var doc = Doc;
        int w = doc.W, h = doc.H;
        var outline = doc.Outline?.ToList();
        var vp = new SubViewport
        {
            Size = new Vector2I(w, h), TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var root = new ArtItem { Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha } };
        var layers = doc.Layers.Where(l => only != null ? l.Id == only : l.Visible).ToList();
        string? bg = only == null ? doc.Bg : null;
        root.Paint = ci =>
        {
            if (bg != null) { var c = Hex.To(bg, Colors.White); ci.DrawRect(new Rect2(0, 0, w, h), new Color(c.R * c.A, c.G * c.A, c.B * c.A, c.A)); }
            foreach (var l in layers)
                if (LayerTexture(l.Id) is { } t)
                {
                    float o = only != null ? 1 : Mathf.Clamp(l.Opacity, 0, 1);
                    ci.DrawTextureRect(t, new Rect2(0, 0, w, h), false, new Color(o, o, o, o));
                }
        };
        vp.AddChild(root);
        AddChild(vp);
        foreach (var v in _vps.Values) v.Vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        if (img == null || img.IsEmpty()) throw new InvalidOperationException("the picture came back empty from the graphics card");
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        ImageFiles.Unpremultiply(img);
        if (cut && only == null && outline is { Count: >= 6 } ol && img.GetWidth() == w && img.GetHeight() == h)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i + 1 < ol.Count; i += 2) pts.Add(new Vector2(ol[i], ol[i + 1]));
            var cov = ArtGeom.PolyCoverage(w, h, pts);
            var d = img.GetData();
            for (int i = 0; i < cov.Length; i++) d[i * 4 + 3] = (byte)(d[i * 4 + 3] * Math.Clamp(cov[i], 0, 1));
            img.SetData(w, h, false, Image.Format.Rgba8, d);
        }
        return img;
    }
}
