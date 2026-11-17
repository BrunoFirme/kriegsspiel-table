using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

internal sealed class ModelBake
{
    readonly PackageStore _store;
    readonly PackManifest _pack;

    public ModelBake(PackageStore store, PackManifest pack) { _store = store; _pack = pack; }

    public const int GlassMat = 1;
    static readonly byte GlassCode = Finishes.Code(Finishes.Glass);

    public static int GrainMat(string grain) => GlassMat + 1 + Array.IndexOf(Finishes.Grained, grain);

    public static readonly int Slots = GlassMat + 1 + Finishes.Grained.Length;

    public static SurfaceDraw? DrawOf(int slot) => slot == GlassMat ? SurfaceDraw.Plain with { Alpha = SurfaceAlpha.Blend }
        : slot > GlassMat && slot < Slots ? SurfaceDraw.Plain with { Grain = Finishes.Grained[slot - GlassMat - 1] } : null;

    static int SlotOf(byte code) => code == GlassCode ? GlassMat : Finishes.OfCode(code) is { } f && Finishes.Grains(f) ? GrainMat(f) : 0;

    public static (Image atlas, Image? finishes, int size, MeshData mesh) Pack(List<ModelPartView> items)
    {
        int total = (int)Math.Sqrt(items.Sum(v => (double)v.W * v.H) * 1.2);
        int S = Math.Clamp((int)Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(256, total)))), 256, 2048);
        Vector2[] at = Array.Empty<Vector2>();
        float k = 1;
        for (int i = 0; i < 30; i++)
        {
            var sizes = items.Select(v => new Vector2(Mathf.Floor(v.W * k), Mathf.Floor(v.H * k))).ToArray();
            var res = Geometry2D.MakeAtlas(sizes);
            var size = res["size"].AsVector2I();
            if (size.X <= S && size.Y <= S) { at = res["points"].AsVector2Array(); break; }
            k *= 0.9f;
        }
        var atlas = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        atlas.Fill(new Color(0.5f, 0.5f, 0.5f));
        Image? finishes = null;
        if (items.Any(v => v.Finished))
        {
            finishes = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
            finishes.Fill(PieceFinish.Texel(0));
        }
        var mesh = new MeshData();
        for (int s = 0; s < Slots; s++) { mesh.Tex.Add(null); mesh.MatColor.Add(Colors.White); }
        for (int i = 0; i < items.Count; i++)
        {
            var v = items[i];
            int rw = Math.Max(1, (int)(v.W * k)), rh = Math.Max(1, (int)(v.H * k));
            var look = Composite(v);
            if (rw != v.W || rh != v.H) look.Resize(rw, rh, Image.Interpolation.Lanczos);
            var pos = new Vector2I((int)at[i].X, (int)at[i].Y);
            atlas.BlitRect(look, new Rect2I(0, 0, rw, rh), pos);
            if (finishes != null)
            {
                var sheen = FinishLook(v);
                if (rw != v.W || rh != v.H) sheen.Resize(rw, rh, Image.Interpolation.Nearest);
                finishes.BlitRect(sheen, new Rect2I(0, 0, rw, rh), pos);
            }
            v.Part.Region = new[] { pos.X, pos.Y, rw, rh };
            var mats = SlotsOf(v);
            foreach (var xf in v.Places)
            {
                var uvs = v.Data.UV.Select(uv => (new Vector2(pos.X, pos.Y) + uv * new Vector2(rw, rh)) / S).ToList();
                var pm = new MeshData { V = v.Data.V.ToList(), N = v.Data.N.ToList(), UV = uvs, C = v.Data.C.ToList(), Mat = mats, Tex = { null }, MatColor = { Colors.White } };
                pm.Transform(xf);
                mesh.V.AddRange(pm.V);
                mesh.N.AddRange(pm.N);
                mesh.UV.AddRange(pm.UV);
                mesh.C.AddRange(pm.C);
                mesh.Mat.AddRange(pm.Mat);
            }
        }
        return (atlas, finishes, S, mesh);
    }

    static List<int> SlotsOf(ModelPartView v)
    {
        var mats = new int[v.Data.Tris];
        if (!v.Slotted) return mats.ToList();
        var finish = v.ShownFinish();
        var scale = new Vector2(v.W, v.H);
        var clip = new Rect2I(0, 0, v.W - 1, v.H - 1);
        for (int t = 0; t < v.Data.Tris; t++)
        {
            var counts = new int[Slots];
            ModelImport.Raster(v.Data.UV[t * 3] * scale, v.Data.UV[t * 3 + 1] * scale, v.Data.UV[t * 3 + 2] * scale, Math.Max(v.W, v.H), clip, (x, y, _, _, _) =>
            {
                if (x < v.W && y < v.H) counts[SlotOf(finish[y * v.W + x])]++;
            });
            int most = 0;
            for (int s = 1; s < Slots; s++) if (counts[s] > counts[most]) most = s;
            mats[t] = counts[GlassMat] > 0 ? GlassMat : most;
        }
        return mats.ToList();
    }

    static Image FinishLook(ModelPartView v)
    {
        var codes = v.ShownFinish();
        var px = new byte[codes.Length * 4];
        for (int i = 0; i < codes.Length; i++)
        {
            var c = PieceFinish.Texel(codes[i]);
            px[i * 4] = (byte)c.R8;
            px[i * 4 + 1] = (byte)c.G8;
            px[i * 4 + 2] = (byte)c.B8;
            px[i * 4 + 3] = 255;
        }
        return Image.CreateFromData(v.W, v.H, false, Image.Format.Rgba8, px);
    }

    public void KeepPaint(IEnumerable<ModelPartView> views)
    {
        foreach (var v in views)
            if (v.PaintDirty || (v.Part.Paint == null && v.Paint.Any(x => x != 0)) || (v.Part.FinishPaint == null && v.Finished))
            {
                v.Part.Paint = SavePaint(v);
                v.Part.FinishPaint = SaveFinish(_store, _pack.Id, v);
            }
    }

    public static string? SaveFinish(PackageStore store, string packId, ModelPartView v) =>
        v.Finished ? ArtStore.AddSrc(store, packId, Image.CreateFromData(v.W, v.H, false, Image.Format.L8, v.Finish)) : null;

    public static byte[] LoadFinish(PackageStore store, string packId, string? name, int w, int h)
    {
        var codes = new byte[w * h];
        if (name == null || ArtStore.LoadSrc(store, packId, name) is not { } img || img.GetWidth() != w || img.GetHeight() != h) return codes;
        var d = img.GetData();
        for (int i = 0; i < codes.Length; i++) codes[i] = d[i * 4] <= Finishes.All.Length ? d[i * 4] : (byte)0;
        return codes;
    }

    public string AddMesh(string obj)
    {
        var bytes = Encoding.UTF8.GetBytes(obj);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var rel = $"assets/{sha}.obj";
        var abs = Path.Combine(_store.PackageDir(_pack.Id), rel);
        PackEdit.WriteContent(abs, bytes);
        _pack.Assets[sha] = new AssetEntry { File = rel, Kind = "mesh" };
        return sha;
    }

    string SavePaint(ModelPartView v)
    {
        v.PaintDirty = false;
        return ArtStore.AddSrc(_store, _pack.Id, Straight(v.W, v.H, v.Paint));
    }

    public static Image Straight(int w, int h, byte[] paint)
    {
        var d = (byte[])paint.Clone();
        for (int i = 0; i < d.Length; i += 4)
        {
            int a = d[i + 3];
            if (a == 0) continue;
            d[i] = (byte)Math.Min(255, d[i] * 255 / a);
            d[i + 1] = (byte)Math.Min(255, d[i + 1] * 255 / a);
            d[i + 2] = (byte)Math.Min(255, d[i + 2] * 255 / a);
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, d);
    }

    const int MarginPasses = 4;

    internal static byte[] PaintIntoMargins(byte[] paint, IReadOnlyList<Vector2> uv, int tris, int w, int h) => WithMargins(paint, MarginMap(uv, tris, w, h));

    internal static int[] MarginMap(IReadOnlyList<Vector2> uv, int tris, int w, int h)
    {
        var map = new int[w * h];
        Array.Fill(map, -1);
        var seen = new bool[w * h];
        var scale = new Vector2(w, h);
        var clip = new Rect2I(0, 0, w - 1, h - 1);
        for (int t = 0; t < tris; t++)
            ModelImport.Raster(uv[t * 3] * scale, uv[t * 3 + 1] * scale, uv[t * 3 + 2] * scale, Math.Max(w, h), clip, (x, y, _, _, _) => seen[y * w + x] = true);
        var edge = new List<int>();
        for (int i = 0; i < seen.Length; i++) if (seen[i]) edge.Add(i);
        for (int pass = 0; pass < MarginPasses && edge.Count > 0; pass++)
        {
            var next = new List<int>();
            foreach (int i in edge)
            {
                int from = map[i] >= 0 ? map[i] : i, x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (seen[n]) continue;
                        seen[n] = true;
                        map[n] = from;
                        next.Add(n);
                    }
            }
            edge = next;
        }
        return map;
    }

    internal static byte[] WithMargins(byte[] paint, int[] map, int stride = 4)
    {
        var px = (byte[])paint.Clone();
        for (int i = 0; i < map.Length; i++)
        {
            int from = map[i];
            if (from < 0) continue;
            Buffer.BlockCopy(paint, from * stride, px, i * stride, stride);
        }
        return px;
    }

    internal static Image Composite(ModelPartView v)
    {
        var b = v.Base.GetData();
        var o = new byte[b.Length];
        var t = Hex.To(v.Part.Tint, Colors.White);
        bool block = v.Part.Kind == "block";
        var paint = block ? v.Paint : WithMargins(v.Paint, v.Margins());
        var finish = v.ShownFinish();
        for (int i = 0; i < b.Length; i += 4)
        {
            float a = paint[i + 3] / 255f;
            o[i] = (byte)Math.Clamp((b[i] * (1 - a) + paint[i]) * t.R, 0, 255);
            o[i + 1] = (byte)Math.Clamp((b[i + 1] * (1 - a) + paint[i + 1]) * t.G, 0, 255);
            o[i + 2] = (byte)Math.Clamp((b[i + 2] * (1 - a) + paint[i + 2]) * t.B, 0, 255);
            byte opaque = block ? Math.Max(b[i + 3], paint[i + 3]) : (byte)255;
            o[i + 3] = finish[i / 4] == GlassCode ? (byte)(opaque * PieceFinish.GlassClear) : opaque;
        }
        return Image.CreateFromData(v.W, v.H, false, Image.Format.Rgba8, o);
    }

    public static string ThumbPath(PackageStore store, string packId, string meshSha) => Path.Combine(ArtStore.Dir(store, packId), meshSha + ".thumb.png");

    public async System.Threading.Tasks.Task Thumbnail(Node host, World3D world, Transform3D stand, string meshSha, List<ModelPartView> views)
    {
        var tv = new SubViewport { World3D = world, Size = new Vector2I(160, 160), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        var cam = new Camera3D { Fov = 30, CullMask = 1 };
        tv.AddChild(cam);
        host.AddChild(tv);
        cam.MakeCurrent();
        var aabbs = views.Where(v => GodotObject.IsInstanceValid(v.Node) && v.Node.Visible).Select(v => stand * (v.Node.Transform * v.Node.GetAabb())).ToList();
        var b = aabbs.Count > 0 ? aabbs.Aggregate((x, y) => x.Merge(y)) : new Aabb(Vector3.Zero, Vector3.One);
        var c = b.GetCenter();
        float d = b.Size.Length() * 1.9f;
        cam.Position = c + new Vector3(Mathf.Sin(Mathf.Pi - 0.7f), 0.55f, Mathf.Cos(Mathf.Pi - 0.7f)).Normalized() * d;
        cam.LookAt(c, Vector3.Up);
        for (int i = 0; i < 3; i++) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = tv.GetTexture().GetImage();
        tv.QueueFree();
        if (img == null || img.IsEmpty()) return;
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        BestEffort.Try(() => { Directory.CreateDirectory(ArtStore.Dir(_store, _pack.Id)); img.SavePng(ThumbPath(_store, _pack.Id, meshSha)); });
    }
}
