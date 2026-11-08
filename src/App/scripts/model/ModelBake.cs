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

    public static (Image atlas, int size, MeshData mesh) Pack(List<ModelPartView> items)
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
        var mesh = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        for (int i = 0; i < items.Count; i++)
        {
            var v = items[i];
            int rw = Math.Max(1, (int)(v.W * k)), rh = Math.Max(1, (int)(v.H * k));
            var look = Composite(v);
            if (rw != v.W || rh != v.H) look.Resize(rw, rh, Image.Interpolation.Lanczos);
            var pos = new Vector2I((int)at[i].X, (int)at[i].Y);
            atlas.BlitRect(look, new Rect2I(0, 0, rw, rh), pos);
            v.Part.Region = new[] { pos.X, pos.Y, rw, rh };
            foreach (var xf in v.Places)
            {
                var uvs = v.Data.UV.Select(uv => (new Vector2(pos.X, pos.Y) + uv * new Vector2(rw, rh)) / S).ToList();
                var pm = new MeshData { V = v.Data.V.ToList(), N = v.Data.N.ToList(), UV = uvs, C = v.Data.C.ToList(), Mat = Enumerable.Repeat(0, v.Data.Tris).ToList(), Tex = { null },
                    MatColor = { Colors.White } };
                pm.Transform(xf);
                mesh.V.AddRange(pm.V);
                mesh.N.AddRange(pm.N);
                mesh.UV.AddRange(pm.UV);
                mesh.C.AddRange(pm.C);
                mesh.Mat.AddRange(pm.Mat);
            }
        }
        return (atlas, S, mesh);
    }

    public void KeepPaint(IEnumerable<ModelPartView> views)
    {
        foreach (var v in views) if (v.PaintDirty || (v.Part.Paint == null && v.Paint.Any(x => x != 0))) v.Part.Paint = SavePaint(v);
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

    internal static Image Composite(ModelPartView v)
    {
        var b = v.Base.GetData();
        var o = new byte[b.Length];
        var t = Hex.To(v.Part.Tint, Colors.White);
        for (int i = 0; i < b.Length; i += 4)
        {
            float a = v.Paint[i + 3] / 255f;
            o[i] = (byte)Math.Clamp((b[i] * (1 - a) + v.Paint[i]) * t.R, 0, 255);
            o[i + 1] = (byte)Math.Clamp((b[i + 1] * (1 - a) + v.Paint[i + 1]) * t.G, 0, 255);
            o[i + 2] = (byte)Math.Clamp((b[i + 2] * (1 - a) + v.Paint[i + 2]) * t.B, 0, 255);
            o[i + 3] = v.Part.Kind == "block" ? Math.Max(b[i + 3], v.Paint[i + 3]) : (byte)255;
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
