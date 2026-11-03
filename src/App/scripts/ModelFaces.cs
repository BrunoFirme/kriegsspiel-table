using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

internal sealed class ShapeFaceMap
{
    readonly ModelStudio _m;

    public ShapeFaceMap(ModelStudio m) => _m = m;

    public sealed record ShapeFace(string Name, Rect2I Rect, bool[] Mask, float UnitsPerTexel);

    readonly Dictionary<string, List<ShapeFace>> _shapeFaces = new();

    public List<(string key, string label)> Keys()
    {
        var l = new List<(string, string)>();
        if (_m.Def == null) return l;
        foreach (var p in _m.Doc.Parts)
        {
            if (!p.Visible || !_m.Views.TryGetValue(p.Id, out var v)) continue;
            var faces = FacesOf(v);
            for (int i = 0; i < faces.Count; i++) l.Add(($"p:{p.Id}:{i}", faces.Count == 1 && p.Kind == "mesh" ? p.Name : $"{p.Name} · {faces[i].Name}"));
        }
        return l;
    }

    bool FaceOf(string key, [MaybeNullWhen(false)] out ModelPartView v, [MaybeNullWhen(false)] out ShapeFace f, out int index)
    {
        v = null; f = null; index = -1;
        var bits = key.Split(':');
        if (bits.Length != 3 || bits[0] != "p" || !_m.Views.TryGetValue(bits[1], out var pv) || !int.TryParse(bits[2], out index)) return false;
        var faces = FacesOf(pv);
        if (index < 0 || index >= faces.Count) return false;
        v = pv; f = faces[index];
        return true;
    }

    public List<ShapeFace> FacesOf(ModelPartView v)
    {
        var cacheKey = $"{v.Part.Id}|{v.Part.Mesh}|{v.W}x{v.H}";
        if (_shapeFaces.TryGetValue(cacheKey, out var known)) return known;
        var d = v.Data;
        int n = d.Tris;
        var list = new List<ShapeFace>();
        var size = new Vector2(v.W, v.H);
        if (v.Part.Kind == "mesh" || n == 0)
        {
            var all = new bool[v.W * v.H];
            Array.Fill(all, true);
            list.Add(new ShapeFace("Its picture", new Rect2I(0, 0, v.W, v.H), all, UnitsPer(d, Enumerable.Range(0, n), size)));
            return _shapeFaces[cacheKey] = list;
        }
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var corner = new Dictionary<(long, long), int>();
        for (int t = 0; t < n; t++)
            for (int c = 0; c < 3; c++)
            {
                var uv = d.UV[t * 3 + c] * size;
                var k = ((long)Math.Round(uv.X * 4), (long)Math.Round(uv.Y * 4));
                if (corner.TryGetValue(k, out var o)) parent[Find(o)] = Find(t); else corner[k] = t;
            }
        var groups = new Dictionary<int, List<int>>();
        for (int t = 0; t < n; t++) { int r = Find(t); if (!groups.TryGetValue(r, out var g)) groups[r] = g = new(); g.Add(t); }
        var found = new List<(string name, float area, List<int> tris)>();
        foreach (var g in groups.Values)
        {
            float area = 0;
            var normal = Vector3.Zero;
            foreach (var t in g)
            {
                var a = d.UV[t * 3] * size; var b = d.UV[t * 3 + 1] * size; var c = d.UV[t * 3 + 2] * size;
                area += Mathf.Abs((b - a).Cross(c - a)) / 2;
                normal += (d.V[t * 3 + 2] - d.V[t * 3]).Cross(d.V[t * 3 + 1] - d.V[t * 3]);
            }
            if (area < v.W * v.H * 0.004f && groups.Count > 1) continue;
            found.Add((FaceName(v.Part.Kind, normal, g, d), area, g));
        }
        string[] order = { "Front", "Back", "Edges", "Top", "Bottom", "Left", "Right", "Side" };
        found = found.OrderBy(f => Array.IndexOf(order, f.name.Split(' ')[0]) is var i and >= 0 ? i : 99).ThenByDescending(f => f.area).ToList();
        var seen = new Dictionary<string, int>();
        foreach (var (name, _, tris) in found)
        {
            seen[name] = seen.GetValueOrDefault(name) + 1;
            var mn = new Vector2(float.MaxValue, float.MaxValue); var mx = -mn;
            foreach (var t in tris) for (int c = 0; c < 3; c++) { var p = d.UV[t * 3 + c] * size; mn = mn.Min(p); mx = mx.Max(p); }
            int x0 = Math.Clamp((int)Mathf.Floor(mn.X) - 1, 0, v.W - 1), y0 = Math.Clamp((int)Mathf.Floor(mn.Y) - 1, 0, v.H - 1);
            int x1 = Math.Clamp((int)Mathf.Ceil(mx.X) + 1, x0 + 1, v.W), y1 = Math.Clamp((int)Mathf.Ceil(mx.Y) + 1, y0 + 1, v.H);
            var rect = new Rect2I(x0, y0, x1 - x0, y1 - y0);
            var mask = new bool[rect.Size.X * rect.Size.Y];
            foreach (var t in tris)
                ModelImport.Raster(d.UV[t * 3] * size, d.UV[t * 3 + 1] * size, d.UV[t * 3 + 2] * size, Math.Max(v.W, v.H), new Rect2I(rect.Position, rect.Size - Vector2I.One), (x, y, _, _, _) =>
                {
                    int lx = x - rect.Position.X, ly = y - rect.Position.Y;
                    if (lx >= 0 && ly >= 0 && lx < rect.Size.X && ly < rect.Size.Y) mask[ly * rect.Size.X + lx] = true;
                });
            list.Add(new ShapeFace(seen[name] > 1 ? $"{name} {seen[name]}" : name, rect, mask, UnitsPer(d, tris, size)));
        }
        if (list.Count == 0)
        {
            var all = new bool[v.W * v.H];
            Array.Fill(all, true);
            list.Add(new ShapeFace("Its picture", new Rect2I(0, 0, v.W, v.H), all, UnitsPer(d, Enumerable.Range(0, n), size)));
        }
        return _shapeFaces[cacheKey] = list;
    }

    static string FaceName(string kind, Vector3 normal, List<int> tris, MeshData d)
    {
        float spread = 0;
        foreach (var t in tris) spread += ((d.V[t * 3 + 2] - d.V[t * 3]).Cross(d.V[t * 3 + 1] - d.V[t * 3])).Length();
        var dir = normal.Length() > 1e-9f ? normal.Normalized() : Vector3.Up;
        bool flat = spread > 0 && normal.Length() / spread > 0.7f;
        if (kind == "block") return !flat ? "Edges" : dir.Y > 0 ? "Front" : "Back";
        if (!flat && Mathf.Abs(dir.Y) < 0.7f) return "Side";
        var a = dir.Abs();
        if (a.Y >= a.X && a.Y >= a.Z) return dir.Y > 0 ? "Top" : "Bottom";
        if (a.Z >= a.X) return dir.Z < 0 ? "Front" : "Back";
        return dir.X < 0 ? "Right" : "Left";
    }

    static float UnitsPer(MeshData d, IEnumerable<int> tris, Vector2 size)
    {
        double world = 0, tex = 0;
        foreach (var t in tris)
        {
            world += ((d.V[t * 3 + 1] - d.V[t * 3]).Cross(d.V[t * 3 + 2] - d.V[t * 3])).Length() / 2;
            var a = d.UV[t * 3] * size; var b = d.UV[t * 3 + 1] * size; var c = d.UV[t * 3 + 2] * size;
            tex += Mathf.Abs((b - a).Cross(c - a)) / 2;
        }
        return tex > 0 ? (float)Math.Sqrt(world / tex) : 0.01f;
    }

    static Image Look(ModelPartView v, Rect2I r)
    {
        var b = v.Base.GetData();
        var o = new byte[r.Size.X * r.Size.Y * 4];
        for (int y = 0; y < r.Size.Y; y++)
            for (int x = 0; x < r.Size.X; x++)
            {
                int s = ((r.Position.Y + y) * v.W + r.Position.X + x) * 4, k = (y * r.Size.X + x) * 4;
                float a = v.Paint[s + 3] / 255f;
                o[k] = (byte)Math.Min(255, b[s] * (1 - a) + v.Paint[s]);
                o[k + 1] = (byte)Math.Min(255, b[s + 1] * (1 - a) + v.Paint[s + 1]);
                o[k + 2] = (byte)Math.Min(255, b[s + 2] * (1 - a) + v.Paint[s + 2]);
                o[k + 3] = v.Part.Kind == "block" ? Math.Max(b[s + 3], v.Paint[s + 3]) : (byte)255;
            }
        return Image.CreateFromData(r.Size.X, r.Size.Y, false, Image.Format.Rgba8, o);
    }

    static string LookHash(Image img) => Convert.ToHexString(SHA256.HashData(img.GetData())).ToLowerInvariant()[..16];

    public ArtDoc? FaceDoc(string key)
    {
        if (!FaceOf(key, out var v, out var f, out var i)) return null;
        var look = Look(v, f.Rect);
        if (v.Part.FaceDocs?.GetValueOrDefault(i.ToString()) is { Length: 2 } kept && kept[1] == LookHash(look))
        {
            if (ArtStore.Read(_m.Store, _m.Pack.Id, ArtStore.SrcPath(_m.Store, _m.Pack.Id, kept[0])) is { } doc) return doc;
        }
        return PieceFaces.DocAround(look, ArtStore.AddSrc(_m.Store, _m.Pack.Id, look));
    }

    public Vector2 FaceUnits(string key)
    {
        if (!FaceOf(key, out var v, out var f, out _)) return Vector2.One;
        var s = v.Part.Scl;
        float k = f.UnitsPerTexel * _m.Doc.Scale * (Mathf.Abs(s[0]) + Mathf.Abs(s[1]) + Mathf.Abs(s[2])) / 3;
        return new Vector2(f.Rect.Size.X, f.Rect.Size.Y) * k;
    }

    public bool TakeFace(string key, Image img, ArtDoc doc)
    {
        if (!FaceOf(key, out var v, out var f, out var i)) return false;
        var r = f.Rect;
        img = (Image)img.Duplicate();
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        if (img.GetWidth() != r.Size.X || img.GetHeight() != r.Size.Y) img.Resize(r.Size.X, r.Size.Y, Image.Interpolation.Lanczos);
        var src = img.GetData();
        var before = (byte[])v.Paint.Clone();
        for (int y = 0; y < r.Size.Y; y++)
            for (int x = 0; x < r.Size.X; x++)
            {
                if (!f.Mask[y * r.Size.X + x]) continue;
                int s = (y * r.Size.X + x) * 4, o = ((r.Position.Y + y) * v.W + r.Position.X + x) * 4;
                int a = src[s + 3];
                v.Paint[o] = (byte)(src[s] * a / 255); v.Paint[o + 1] = (byte)(src[s + 1] * a / 255); v.Paint[o + 2] = (byte)(src[s + 2] * a / 255); v.Paint[o + 3] = (byte)a;
            }
        if (!before.AsSpan().SequenceEqual(v.Paint))
        {
            _m.History.Add(new List<(string part, byte[] before, byte[]? after)> { (v.Part.Id, before, (byte[])v.Paint.Clone()) });
            v.Repaint();
        }
        v.Part.FaceDocs ??= new();
        v.Part.FaceDocs[i.ToString()] = new[] { _m.SaveSrcText(ArtStore.Serialize(doc), ".json"), LookHash(Look(v, r)) };
        _m.Changed(mesh: false);
        return true;
    }
}
