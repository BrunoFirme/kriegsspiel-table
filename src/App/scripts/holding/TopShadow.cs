using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class TopShadow
{
    const int Texels = 192, Fine = 4, Pad = 3, Kept = 64;
    const float Fill = 0.5f, Rim = 0.85f, Half = 0.5f;
    static readonly Dictionary<string, (ImageTexture texture, Rect2 area)> Made = new();

    readonly List<(Vector2 a, Vector2 b, Vector2 c)> _triangles = new();
    readonly List<List<Vector2>> _loops = new();
    AlphaMask? _mask;
    Vector2 _maskSize;
    readonly bool _mirrored;

    TopShadow(bool mirrored) => _mirrored = mirrored;

    public static object Stamp(PieceNode n) =>
        (n.Model?.Mesh, n.AddOn?.Mesh, n.Model?.Transform, n.Shape, n.Footprint, n.FootprintCentre, n.Piece.FaceDown, n.Shape == "alpha" ? n.CutoutShape() : default);

    public static (ImageTexture texture, Rect2 area) Of(PieceNode n)
    {
        var (shadow, key) = Gather(n);
        if (key != null && Made.TryGetValue(key, out var made)) return made;
        var (image, area) = shadow.Paint();
        made = (ImageTexture.CreateFromImage(image), area);
        if (key == null) return made;
        if (Made.Count >= Kept) Made.Clear();
        return Made[key] = made;
    }

    internal static (Image image, Rect2 area) Painted(PieceNode n) => Gather(n).shadow.Paint();

    static (TopShadow shadow, string? key) Gather(PieceNode n)
    {
        var s = new TopShadow(n.Piece.FaceDown);
        string? key = $"{n.Piece.FaceDown}|{n.Footprint}|{n.FootprintCentre}";
        if (n.Model is { Mesh: { } model } m)
        {
            s.Faces(model, m.Transform);
            key += $"|m{model.GetInstanceId()}|{m.Transform}";
        }
        else if (n.Shape == "alpha" && n.CutoutShape() is var (outline, mask, picture) && (outline != null || mask != null))
        {
            if (outline != null) s.Pictured(outline, n.Footprint);
            else s.Masked(mask!, n.Footprint);
            key += $"|a{picture}|{outline != null}";
        }
        else
        {
            s.Loop(PieceArt.Outline(n.Shape == "alpha" || n.ShowsModel ? "box" : n.Shape, n.Footprint.X, n.Footprint.Y).Select(p => p + n.FootprintCentre));
            key = n.ShowsModel ? null : key + "|s" + n.Shape;
        }
        if (n.AddOn is { Mesh: { } addOn } a)
        {
            s.Faces(addOn, a.Transform);
            if (key != null) key += $"|o{addOn.GetInstanceId()}|{a.Transform}";
        }
        return (s, key);
    }

    Vector2 Flat(float x, float z) => new(_mirrored ? -x : x, z);

    void Faces(Mesh mesh, Transform3D at)
    {
        var faces = mesh.GetFaces();
        for (int i = 0; i + 2 < faces.Length; i += 3)
        {
            var (a, b, c) = (at * faces[i], at * faces[i + 1], at * faces[i + 2]);
            _triangles.Add((Flat(a.X, a.Z), Flat(b.X, b.Z), Flat(c.X, c.Z)));
        }
    }

    void Loop(IEnumerable<Vector2> points) => _loops.Add(points.Select(p => Flat(p.X, p.Y)).ToList());

    void Pictured(List<List<Vec2>> outline, Vector2 size)
    {
        foreach (var loop in outline.Where(l => l.Count >= 3))
            Loop(loop.Select(g => new Vector2(((float)g.X - Half) * size.X, ((float)g.Z - Half) * size.Y)));
    }

    void Masked(AlphaMask mask, Vector2 size) => (_mask, _maskSize) = (mask, size);

    Rect2? Area()
    {
        var points = _triangles.SelectMany(t => new[] { t.a, t.b, t.c }).Concat(_loops.SelectMany(l => l));
        if (_mask != null) points = points.Append(-_maskSize / 2).Append(_maskSize / 2);
        Rect2? area = null;
        foreach (var p in points) area = area is { } r ? r.Expand(p) : new Rect2(p, Vector2.Zero);
        return area is { Size: { X: > 0, Y: > 0 } } ? area : null;
    }

    (Image image, Rect2 area) Paint()
    {
        var shape = Area() ?? new Rect2(-Vector2.One / 2, Vector2.One);
        float texel = Mathf.Max(shape.Size.X, shape.Size.Y) / (Texels - 2 * Pad);
        int w = Mathf.CeilToInt(shape.Size.X / texel) + 2 * Pad, h = Mathf.CeilToInt(shape.Size.Y / texel) + 2 * Pad;
        var area = new Rect2(shape.GetCenter() - new Vector2(w, h) * texel / 2, new Vector2(w, h) * texel);
        var grid = new Coverage(w * Fine, h * Fine, area.Position, texel / Fine);
        foreach (var t in _triangles) grid.Triangle(t.a, t.b, t.c);
        grid.EvenOdd(_loops);
        if (_mask != null) grid.Mask(_mask, _maskSize, _mirrored);
        return (Shade(grid.Down(w, h, Fine), w, h), area);
    }

    static Image Shade(float[] cover, int w, int h)
    {
        var img = Image.CreateEmpty(w, h, true, Image.Format.Rgba8);
        bool Clear(int x, int y) => x < 0 || y < 0 || x >= w || y >= h || cover[y * w + x] < Half;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float c = cover[y * w + x];
                bool edge = Clear(x - 1, y) || Clear(x + 1, y) || Clear(x, y - 1) || Clear(x, y + 1);
                img.SetPixel(x, y, new Color(0, 0, 0, c * (edge ? Rim : Fill)));
            }
        img.GenerateMipmaps();
        return img;
    }

    sealed class Coverage(int w, int h, Vector2 origin, float cell)
    {
        readonly bool[] _on = new bool[w * h];

        Vector2 Centre(int x, int y) => origin + new Vector2(x + Half, y + Half) * cell;

        public void Triangle(Vector2 a, Vector2 b, Vector2 c)
        {
            float turn = (b - a).Cross(c - a);
            if (Mathf.IsZeroApprox(turn)) return;
            if (turn < 0) (b, c) = (c, b);
            var lo = (a.Min(b).Min(c) - origin) / cell;
            var hi = (a.Max(b).Max(c) - origin) / cell;
            for (int y = Math.Max(0, (int)lo.Y); y <= Math.Min(h - 1, (int)hi.Y); y++)
                for (int x = Math.Max(0, (int)lo.X); x <= Math.Min(w - 1, (int)hi.X); x++)
                {
                    var p = Centre(x, y);
                    if ((b - a).Cross(p - a) >= 0 && (c - b).Cross(p - b) >= 0 && (a - c).Cross(p - c) >= 0) _on[y * w + x] = true;
                }
        }

        public void EvenOdd(List<List<Vector2>> loops)
        {
            if (loops.Count == 0) return;
            var crossings = new List<float>();
            for (int y = 0; y < h; y++)
            {
                float z = Centre(0, y).Y;
                crossings.Clear();
                foreach (var loop in loops)
                    for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                        if ((loop[i].Y > z) != (loop[j].Y > z))
                            crossings.Add(loop[i].X + (z - loop[i].Y) * (loop[j].X - loop[i].X) / (loop[j].Y - loop[i].Y));
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                    for (int x = Math.Max(0, Mathf.CeilToInt((crossings[k] - origin.X) / cell - Half)); x < w && Centre(x, y).X < crossings[k + 1]; x++)
                        _on[y * w + x] = true;
            }
        }

        public void Mask(AlphaMask mask, Vector2 size, bool mirrored)
        {
            const int N = AlphaMask.N;
            float Solid(int i, int j) => i < 0 || j < 0 || i >= N || j >= N || !mask.Solid[j * N + i] ? 0 : 1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = Centre(x, y);
                    float u = (mirrored ? -p.X : p.X) / size.X + Half, v = p.Y / size.Y + Half;
                    if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                    if (!mask.HasAlpha) { _on[y * w + x] = true; continue; }
                    float gx = u * N - Half, gy = v * N - Half;
                    int i = Mathf.FloorToInt(gx), j = Mathf.FloorToInt(gy);
                    float fx = gx - i, fy = gy - j;
                    float s = Mathf.Lerp(Mathf.Lerp(Solid(i, j), Solid(i + 1, j), fx), Mathf.Lerp(Solid(i, j + 1), Solid(i + 1, j + 1), fx), fy);
                    if (s >= Half) _on[y * w + x] = true;
                }
        }

        public float[] Down(int ow, int oh, int fine)
        {
            var cover = new float[ow * oh];
            float each = 1f / (fine * fine);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (_on[y * w + x]) cover[y / fine * ow + x / fine] += each;
            return cover;
        }
    }
}
