using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using KTable.Client;
using KTable.Client.Studio;

namespace KTable.App;

public sealed class MeshData
{
    public List<Vector3> V = new();
    public List<Vector3> N = new();
    public List<Vector2> UV = new();
    public List<Color> C = new();
    public List<int> Mat = new();
    public List<Image?> Tex = new();
    public List<Color> MatColor = new();

    public int Tris => V.Count / 3;
    public Aabb Bounds()
    {
        if (V.Count == 0) return new Aabb();
        var b = new Aabb(V[0], Vector3.Zero);
        foreach (var v in V) b = b.Expand(v);
        return b;
    }

    public void Add(Vector3 v, Vector3 n, Vector2 uv, Color c)
    {
        V.Add(v);
        N.Add(n);
        UV.Add(uv);
        C.Add(c);
    }

    public MeshData WithUV(List<Vector2> uv) => new() { V = V, N = N, UV = uv, C = C, Mat = Mat, Tex = Tex, MatColor = MatColor };

    public void Transform(Transform3D t)
    {
        var nb = t.Basis.Inverse().Transposed();
        for (int i = 0; i < V.Count; i++) { V[i] = t * V[i]; N[i] = (nb * N[i]).Normalized(); }
        if (t.Basis.Determinant() < 0)
            for (int i = 0; i + 2 < V.Count; i += 3)
            {
                (V[i + 1], V[i + 2]) = (V[i + 2], V[i + 1]);
                (N[i + 1], N[i + 2]) = (N[i + 2], N[i + 1]);
                (UV[i + 1], UV[i + 2]) = (UV[i + 2], UV[i + 1]);
                (C[i + 1], C[i + 2]) = (C[i + 2], C[i + 1]);
            }
    }

    public void Append(MeshData o)
    {
        int mo = Tex.Count;
        Tex.AddRange(o.Tex);
        MatColor.AddRange(o.MatColor);
        V.AddRange(o.V);
        N.AddRange(o.N);
        UV.AddRange(o.UV);
        C.AddRange(o.C);
        Mat.AddRange(o.Mat.Select(m => m + mo));
    }

    public ArrayMesh ToMesh()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = V.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = UV.ToArray();
        var m = new ArrayMesh();
        if (V.Count > 0) m.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        return m;
    }

    public void FaceNormals()
    {
        for (int i = 0; i + 2 < V.Count; i += 3)
        {
            var n = (V[i + 2] - V[i]).Cross(V[i + 1] - V[i]).Normalized();
            N[i] = N[i + 1] = N[i + 2] = n;
        }
    }
}

public static class ModelImport
{
    public static readonly string[] Extensions = { "glb", "gltf", "fbx", "obj", "stl", "ply", "dae" };
    public static string Filter => "*." + string.Join(", *.", Extensions) + " ; 3D models";
    public static bool IsModel(string path) => Array.IndexOf(Extensions, Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) >= 0;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static MeshData? Load(string path, out string? error)
    {
        error = null;
        try
        {
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            var m = ext switch
            {
                "glb" or "gltf" => FromGodotScene(path, fbx: false),
                "fbx" => FromGodotScene(path, fbx: true),
                "obj" => Obj(path),
                "stl" => FromFile(ModelFiles.Stl(ModelFiles.Bytes(path))),
                "ply" => FromFile(ModelFiles.Ply(ModelFiles.Bytes(path))),
                "dae" => FromFile(ColladaFile.Read(path)),
                _ => null,
            };
            if (m == null || m.Tris == 0) { error ??= "no triangles in it"; return null; }
            if (m.N.All(n => n == Vector3.Zero)) m.FaceNormals();
            return m;
        }
        catch (Exception e) { error = e.Message; return null; }
    }

    static MeshData? FromGodotScene(string path, bool fbx)
    {
        GltfDocument doc = fbx ? new FbxDocument() : new GltfDocument();
        GltfState state = fbx ? new FbxState() : new GltfState();
        var err = doc.AppendFromFile(path, state, 0, Path.GetDirectoryName(path) ?? "");
        if (err != Error.Ok) throw new Exception($"could not read it ({err})");
        var root = doc.GenerateScene(state);
        if (root == null) throw new Exception("it holds no scene");
        var m = new MeshData();
        void Walk(Node n, Transform3D parent)
        {
            var t = n is Node3D n3 ? parent * n3.Transform : parent;
            Mesh? mesh = null;
            Func<int, Material?> mat = _ => null;
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                mesh = mi.Mesh;
                var mi2 = mi;
                mat = s => mi2.GetActiveMaterial(s);
            }
            else if (n is ImporterMeshInstance3D imi && imi.Mesh != null)
            {
                var am = imi.Mesh.GetMesh();
                mesh = am;
                mat = s => am.SurfaceGetMaterial(s);
            }
            if (mesh != null && (n is not Node3D v3 || v3.Visible))
                for (int s = 0; s < mesh.GetSurfaceCount(); s++) AddSurface(m, mesh.SurfaceGetArrays(s), mat(s) ?? mesh.SurfaceGetMaterial(s), t);
            foreach (var c in n.GetChildren()) Walk(c, t);
        }
        Walk(root, Transform3D.Identity);
        root.Free();
        return m;
    }

    static void AddSurface(MeshData m, Godot.Collections.Array arrays, Material? mat, Transform3D t)
    {
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (verts.Length == 0) return;
        var norms = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? Array.Empty<Vector3>() : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? Array.Empty<Vector2>() : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var cols = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? Array.Empty<Color>() : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var idx = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? Enumerable.Range(0, verts.Length).ToArray() : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int matIndex = m.Tex.Count;
        Image? tex = null;
        var col = Colors.White;
        if (mat is BaseMaterial3D bm)
        {
            col = bm.AlbedoColor;
            tex = ImageOf(bm.AlbedoTexture);
        }
        m.Tex.Add(tex);
        m.MatColor.Add(col);
        var nb = t.Basis.Inverse().Transposed();
        bool flip = t.Basis.Determinant() < 0;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            var tri = flip ? new[] { idx[i], idx[i + 2], idx[i + 1] } : new[] { idx[i], idx[i + 1], idx[i + 2] };
            foreach (var k in tri)
                m.Add(t * verts[k], norms.Length > k ? (nb * norms[k]).Normalized() : Vector3.Zero, uvs.Length > k ? uvs[k] : Vector2.Zero, cols.Length > k ? cols[k] : Colors.White);
            m.Mat.Add(matIndex);
        }
    }

    static Image? ImageOf(Texture2D? t)
    {
        if (t == null) return null;
        var img = t.GetImage();
        if (img == null || img.IsEmpty()) return null;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        int s = Math.Max(img.GetWidth(), img.GetHeight());
        if (s > 2048) img.Resize(Math.Max(1, img.GetWidth() * 2048 / s), Math.Max(1, img.GetHeight() * 2048 / s), Image.Interpolation.Bilinear);
        return img;
    }

    public static MeshData Obj(string path) => FromFile(ModelFiles.Obj(path));

    static MeshData FromFile(ModelMesh f)
    {
        var m = new MeshData();
        for (int i = 0; i < f.V.Count; i++)
            m.Add(new Vector3(f.V[i].X, f.V[i].Y, f.V[i].Z), new Vector3(f.N[i].X, f.N[i].Y, f.N[i].Z), new Vector2(f.UV[i].X, f.UV[i].Y), ColorOf(f.C[i]));
        m.Mat.AddRange(f.Mat);
        foreach (var mat in f.Materials)
        {
            m.Tex.Add(mat.Picture != null ? ImageFiles.Load(mat.Picture) : null);
            m.MatColor.Add(ColorOf(mat.Color));
        }
        return m;
    }

    static Color ColorOf(Rgba c) => new(c.R, c.G, c.B, c.A);

    public static MeshData Primitive(string kind)
    {
        PrimitiveMesh pm = kind switch
        {
            "cylinder" => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 32, Rings = 1 },
            "cone" => new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.5f, Height = 1, RadialSegments = 32, Rings = 1 },
            "hex" => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.25f, RadialSegments = 6, Rings = 1 },
            "sphere" => new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 32, Rings = 16 },
            "wedge" => new PrismMesh { Size = Vector3.One, LeftToRight = 0.5f },
            "torus" => new TorusMesh { InnerRadius = 0.3f, OuterRadius = 0.5f, Rings = 32, RingSegments = 16 },
            "capsule" => new CapsuleMesh { Radius = 0.25f, Height = 1, RadialSegments = 24, Rings = 8 },
            "flag" => new BoxMesh { Size = new Vector3(1, 0.66f, 0.02f) },
            "base" => new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.52f, Height = 0.08f, RadialSegments = 48, Rings = 1 },
            _ => new BoxMesh { Size = Vector3.One },
        };
        var m = new MeshData { Tex = { null }, MatColor = { new Color(0.82f, 0.80f, 0.74f) } };
        AddSurface(m, pm.GetMeshArrays(), null, Transform3D.Identity);
        m.MatColor[0] = new Color(0.82f, 0.80f, 0.74f);
        var b = m.Bounds();
        m.Transform(new Transform3D(Basis.Identity, new Vector3(0, -b.Position.Y, 0)));
        return m;
    }

    public static Image Unwrap(MeshData m, int size)
    {
        int tris = m.Tris;
        var b = m.Bounds();
        float eps = Math.Max(1e-6f, b.Size.Length() * 1e-5f);
        var ids = new int[m.V.Count];
        var weld = new Dictionary<(long, long, long), int>();
        for (int i = 0; i < m.V.Count; i++)
        {
            var v = m.V[i];
            var key = ((long)Math.Round(v.X / eps), (long)Math.Round(v.Y / eps), (long)Math.Round(v.Z / eps));
            if (!weld.TryGetValue(key, out var id)) weld[key] = id = weld.Count;
            ids[i] = id;
        }
        var normal = new Vector3[tris];
        var axis = new int[tris];
        for (int t = 0; t < tris; t++)
        {
            var n = (m.V[t * 3 + 2] - m.V[t * 3]).Cross(m.V[t * 3 + 1] - m.V[t * 3]);
            normal[t] = n.LengthSquared() > 0 ? n.Normalized() : Vector3.Up;
            var a = normal[t].Abs();
            int ax = a.X >= a.Y && a.X >= a.Z ? 0 : a.Y >= a.Z ? 1 : 2;
            axis[t] = ax * 2 + (normal[t][ax] < 0 ? 1 : 0);
        }
        var parent = Enumerable.Range(0, tris).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var edges = new Dictionary<(int, int), int>();
        for (int t = 0; t < tris; t++)
            for (int e = 0; e < 3; e++)
            {
                int a = ids[t * 3 + e], c = ids[t * 3 + (e + 1) % 3];
                var key = a < c ? (a, c) : (c, a);
                if (edges.TryGetValue(key, out var o))
                {
                    if (axis[o] == axis[t] && normal[o].Dot(normal[t]) > 0.5f) parent[Find(o)] = Find(t);
                }
                else edges[key] = t;
            }
        var islands = new Dictionary<int, List<int>>();
        for (int t = 0; t < tris; t++)
        {
            int r = Find(t);
            if (!islands.TryGetValue(r, out var l)) islands[r] = l = new();
            l.Add(t);
        }
        var proj = new Vector2[m.V.Count];
        Vector2 Flat(Vector3 v, int ax) => ax switch
        {
            0 => new Vector2(-v.Z, -v.Y), 1 => new Vector2(v.Z, -v.Y),
            2 => new Vector2(v.X, v.Z), 3 => new Vector2(v.X, -v.Z),
            4 => new Vector2(v.X, -v.Y), _ => new Vector2(-v.X, -v.Y),
        };
        var list = islands.Values.ToList();
        var lo = new Vector2[list.Count];
        var span = new Vector2[list.Count];
        double area = 0;
        for (int i = 0; i < list.Count; i++)
        {
            int ax = axis[list[i][0]];
            var mn = new Vector2(float.MaxValue, float.MaxValue);
            var mx = -mn;
            foreach (var t in list[i])
                for (int c = 0; c < 3; c++)
                {
                    var p = Flat(m.V[t * 3 + c], ax);
                    proj[t * 3 + c] = p;
                    mn = mn.Min(p);
                    mx = mx.Max(p);
                }
            lo[i] = mn;
            span[i] = (mx - mn).Max(new Vector2(1e-5f, 1e-5f));
            area += span[i].X * span[i].Y;
        }
        const int Pad = 4;
        double k = Math.Sqrt(size * (double)size * 0.62 / Math.Max(1e-9, area));
        Vector2[] at = Array.Empty<Vector2>();
        for (int attempt = 0; attempt < 24; attempt++)
        {
            var sizes = span.Select(s => new Vector2(Mathf.Ceil((float)(s.X * k)) + Pad * 2, Mathf.Ceil((float)(s.Y * k)) + Pad * 2)).ToArray();
            var res = Geometry2D.MakeAtlas(sizes);
            var pts = res["points"].AsVector2Array();
            var total = res["size"].AsVector2I();
            if (total.X <= size && total.Y <= size) { at = pts; break; }
            k *= 0.92 * Math.Min(1, Math.Sqrt(size * (double)size / Math.Max(1, (double)total.X * total.Y)) + 0.05);
        }
        if (at.Length != list.Count) throw new Exception("the model has too many separate pieces to lay out");
        var oldUV = m.UV.ToArray();
        for (int i = 0; i < list.Count; i++)
            foreach (var t in list[i])
                for (int c = 0; c < 3; c++)
                    m.UV[t * 3 + c] = (at[i] + new Vector2(Pad, Pad) + (proj[t * 3 + c] - lo[i]) * (float)k) / size;
        return Transfer(m, oldUV, size);
    }

    static Image Transfer(MeshData m, Vector2[] oldUV, int size)
    {
        var px = new byte[size * size * 4];
        var filled = new bool[size * size];
        var texData = m.Tex.Select(t => t?.GetData()).ToArray();
        for (int t = 0; t < m.Tris; t++)
        {
            int mat = t < m.Mat.Count ? m.Mat[t] : 0;
            var img = mat < m.Tex.Count ? m.Tex[mat] : null;
            var data = mat < texData.Length ? texData[mat] : null;
            var mc = mat < m.MatColor.Count ? m.MatColor[mat] : Colors.White;
            var a = m.UV[t * 3] * size;
            var b = m.UV[t * 3 + 1] * size;
            var c = m.UV[t * 3 + 2] * size;
            Raster(a, b, c, size, (x, y, w0, w1, w2) =>
            {
                var col = mc * (m.C[t * 3] * w0 + m.C[t * 3 + 1] * w1 + m.C[t * 3 + 2] * w2);
                if (img != null && data != null)
                {
                    var uv = oldUV[t * 3] * w0 + oldUV[t * 3 + 1] * w1 + oldUV[t * 3 + 2] * w2;
                    col *= Sample(data, img.GetWidth(), img.GetHeight(), uv);
                }
                int o = (y * size + x) * 4;
                px[o] = (byte)Math.Clamp(col.R * 255, 0, 255);
                px[o + 1] = (byte)Math.Clamp(col.G * 255, 0, 255);
                px[o + 2] = (byte)Math.Clamp(col.B * 255, 0, 255);
                px[o + 3] = 255;
                filled[y * size + x] = true;
            });
        }
        Dilate(px, filled, size, SeamPasses);
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, px);
    }

    const int SeamPasses = 4;
    const float MinLayoutSpan = 1e-3f;

    public static bool HasOwnLayout(MeshData m)
    {
        if (m.UV.Count == 0) return false;
        var lo = m.UV[0];
        var hi = lo;
        foreach (var uv in m.UV) { lo = lo.Min(uv); hi = hi.Max(uv); }
        return hi.X - lo.X > MinLayoutSpan && hi.Y - lo.Y > MinLayoutSpan;
    }

    public static void Lay(MeshData m, IReadOnlyList<Vector2> drawn, Image tex, byte[] paint, int w, int h)
    {
        var data = tex.GetData();
        int tw = tex.GetWidth(), th = tex.GetHeight();
        var filled = new bool[w * h];
        var scale = new Vector2(w, h);
        var clip = new Rect2I(0, 0, w - 1, h - 1);
        for (int t = 0; t < m.Tris; t++)
        {
            int k = t * 3;
            Raster(m.UV[k] * scale, m.UV[k + 1] * scale, m.UV[k + 2] * scale, Math.Max(w, h), clip, (x, y, w0, w1, w2) =>
            {
                var col = Sample(data, tw, th, drawn[k] * w0 + drawn[k + 1] * w1 + drawn[k + 2] * w2);
                int o = (y * w + x) * 4;
                paint[o] = (byte)Math.Clamp(col.R * 255, 0, 255);
                paint[o + 1] = (byte)Math.Clamp(col.G * 255, 0, 255);
                paint[o + 2] = (byte)Math.Clamp(col.B * 255, 0, 255);
                paint[o + 3] = 255;
                filled[y * w + x] = true;
            });
        }
        if (w == h) Dilate(paint, filled, w, SeamPasses);
    }

    public static void Raster(Vector2 a, Vector2 b, Vector2 c, int size, Action<int, int, float, float, float> plot) => Raster(a, b, c, size, null, plot);

    public static void Raster(Vector2 a, Vector2 b, Vector2 c, int size, Rect2I? clip, Action<int, int, float, float, float> plot)
    {
        int x0 = Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.X, Mathf.Min(b.X, c.X)) - 1)), x1 = Math.Min(size - 1, (int)Mathf.Ceil(Mathf.Max(a.X, Mathf.Max(b.X, c.X)) + 1));
        int y0 = Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)) - 1)), y1 = Math.Min(size - 1, (int)Mathf.Ceil(Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y)) + 1));
        if (clip is { } k)
        {
            x0 = Math.Max(x0, k.Position.X);
            y0 = Math.Max(y0, k.Position.Y);
            x1 = Math.Min(x1, k.End.X);
            y1 = Math.Min(y1, k.End.Y);
        }
        float den = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Mathf.Abs(den) < 1e-9f) return;
        float la = (b - c).Length(), lb = (c - a).Length(), lc = (a - b).Length();
        float ea = la > 0 ? 0.71f * la / Mathf.Abs(den) : 0, eb = lb > 0 ? 0.71f * lb / Mathf.Abs(den) : 0, ec = lc > 0 ? 0.71f * lc / Mathf.Abs(den) : 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / den;
                float w1 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / den;
                float w2 = 1 - w0 - w1;
                if (w0 < -ea || w1 < -eb || w2 < -ec) continue;
                float s0 = Mathf.Max(0, w0), s1 = Mathf.Max(0, w1), s2 = Mathf.Max(0, w2), sum = s0 + s1 + s2;
                plot(x, y, s0 / sum, s1 / sum, s2 / sum);
            }
    }

    static Color Sample(byte[] d, int w, int h, Vector2 uv)
    {
        float u = uv.X - Mathf.Floor(uv.X), v = uv.Y - Mathf.Floor(uv.Y);
        float fx = u * w - 0.5f, fy = v * h - 0.5f;
        int x0 = (int)Mathf.Floor(fx), y0 = (int)Mathf.Floor(fy);
        float tx = fx - x0, ty = fy - y0;
        Color P(int x, int y)
        {
            x = ((x % w) + w) % w;
            y = ((y % h) + h) % h;
            int o = (y * w + x) * 4;
            return new Color(d[o] / 255f, d[o + 1] / 255f, d[o + 2] / 255f, d[o + 3] / 255f);
        }
        return P(x0, y0).Lerp(P(x0 + 1, y0), tx).Lerp(P(x0, y0 + 1).Lerp(P(x0 + 1, y0 + 1), tx), ty);
    }

    public static void Dilate(byte[] px, bool[] filled, int size, int passes)
    {
        for (int p = 0; p < passes; p++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (filled[y * size + x]) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= size || ny >= size || !filled[ny * size + nx]) continue;
                            int o = (ny * size + nx) * 4;
                            r += px[o];
                            g += px[o + 1];
                            b += px[o + 2];
                            n++;
                        }
                    if (n == 0) continue;
                    int q = (y * size + x) * 4;
                    px[q] = (byte)(r / n);
                    px[q + 1] = (byte)(g / n);
                    px[q + 2] = (byte)(b / n);
                    px[q + 3] = 255;
                    next[y * size + x] = true;
                }
            Array.Copy(next, filled, filled.Length);
        }
    }

    public static string ToObj(MeshData m)
    {
        var sb = new StringBuilder();
        sb.Append("# Kriegsspiel Table piece studio\n");
        var vi = new Dictionary<Vector3, int>();
        var ti = new Dictionary<Vector2, int>();
        var ni = new Dictionary<Vector3, int>();
        var vs = new StringBuilder();
        var ts = new StringBuilder();
        var ns = new StringBuilder();
        var fs = new StringBuilder();
        int Idx<T>(Dictionary<T, int> d, T key, StringBuilder into, string line) where T : notnull
        {
            if (d.TryGetValue(key, out var i)) return i;
            d[key] = i = d.Count + 1;
            into.Append(line);
            return i;
        }
        for (int t = 0; t < m.Tris; t++)
        {
            var ix = new int[3, 3];
            for (int c = 0; c < 3; c++)
            {
                int k = t * 3 + c;
                var v = m.V[k];
                var uv = m.UV[k];
                var n = m.N[k];
                ix[c, 0] = Idx(vi, v, vs, string.Format(Inv, "v {0:0.######} {1:0.######} {2:0.######}\n", v.X, v.Y, v.Z));
                ix[c, 1] = Idx(ti, uv, ts, string.Format(Inv, "vt {0:0.######} {1:0.######}\n", uv.X, 1 - uv.Y));
                ix[c, 2] = Idx(ni, n, ns, string.Format(Inv, "vn {0:0.####} {1:0.####} {2:0.####}\n", n.X, n.Y, n.Z));
            }
            fs.Append(string.Format(Inv, "f {0}/{1}/{2} {3}/{4}/{5} {6}/{7}/{8}\n", ix[0, 0], ix[0, 1], ix[0, 2], ix[2, 0], ix[2, 1], ix[2, 2], ix[1, 0], ix[1, 1], ix[1, 2]));
        }
        sb.Append(vs).Append(ts).Append(ns).Append(fs);
        return sb.ToString();
    }
}
