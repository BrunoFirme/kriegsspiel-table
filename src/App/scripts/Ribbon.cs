using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class Ribbon
{
    const int MostDrapedPoints = 6000;

    public static List<Vec2> Draped(List<Vec2> pts, Ground ground)
    {
        if (!ground.Any || pts.Count < 2) return pts;
        var o = new List<Vec2> { pts[0] };
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            double cell = Math.Min(ground.CellSize(a), ground.CellSize(b));
            int n = double.IsFinite(cell) ? Math.Clamp((int)Math.Ceiling((b - a).Length / cell), 1, MostDrapedPoints / pts.Count + 1) : 1;
            for (int k = 1; k <= n; k++) o.Add(new Vec2(a.X + (b.X - a.X) * k / n, a.Z + (b.Z - a.Z) * k / n));
        }
        return o;
    }

    public static void DrawMany(ImmediateMesh im, StandardMaterial3D mat, IReadOnlyList<List<Vec2>> lines, double width, float y, Ground? ground = null)
    {
        var strips = lines.Select(pts => Strip(pts, width, y, ground ?? Ground.Flat, out _, out _, out _)).Where(s => s.Count > 0).ToList();
        if (strips.Count == 0) return;
        im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
        for (int i = 0; i < strips.Count; i++)
        {
            if (i > 0)
            {
                im.SurfaceAddVertex(strips[i - 1][^1]);
                im.SurfaceAddVertex(strips[i][0]);
            }
            foreach (var v in strips[i]) im.SurfaceAddVertex(v);
        }
        im.SurfaceEnd();
    }

    static List<Vector3> Strip(List<Vec2> pts, double width, float y, Ground g, out bool closed, out (Vector3 At, Vector3 Way) first, out (Vector3 At, Vector3 Way) last)
    {
        var strip = new List<Vector3>();
        first = last = default;
        pts = Draped(pts, g);
        bool loops = closed = pts.Count > 2 && (pts[0] - pts[^1]).Length < 1e-6;
        var ps = closed ? pts.GetRange(0, pts.Count - 1) : pts;
        int m = ps.Count;
        if (m < 2) return strip;
        Vector3 W(int i) => Coords.ToWorld(ps[i], y + (float)g.At(ps[i]));
        Vector3 Dir(int i, int j)
        {
            var d = W(j) - W(i);
            d.Y = 0;
            return d.LengthSquared() > 1e-10 ? d.Normalized() : Vector3.Zero;
        }
        float half = (float)(width / 2);
        for (int k = 0; k <= (loops ? m : m - 1); k++)
        {
            int i = k % m;
            var into = closed || i > 0 ? Dir((i - 1 + m) % m, i) : Dir(0, 1);
            var outOf = closed || i < m - 1 ? Dir(i, (i + 1) % m) : Dir(m - 2, m - 1);
            if (into == Vector3.Zero) into = outOf;
            if (outOf == Vector3.Zero) outOf = into;
            var na = new Vector3(-into.Z, 0, into.X);
            var nb = new Vector3(-outOf.Z, 0, outOf.X);
            var mitre = na + nb;
            var n = mitre.LengthSquared() > 1e-8 ? mitre.Normalized() : na;
            float cos = n.Dot(na);
            var off = n * half * (cos > 0.2f ? 1f / cos : 5f);
            var p = W(i);
            strip.Add(p + off);
            strip.Add(p - off);
        }
        first = (W(0), -Dir(0, 1));
        last = (W(m - 1), Dir(m - 2, m - 1));
        return strip;
    }

    public static void Draw(ImmediateMesh im, StandardMaterial3D mat, List<Vec2> pts, double width, float y, Ground? ground = null, bool round = false)
    {
        var strip = Strip(pts, width, y, ground ?? Ground.Flat, out bool closed, out var first, out var last);
        if (strip.Count == 0) return;
        im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
        foreach (var v in strip) im.SurfaceAddVertex(v);
        im.SurfaceEnd();
        if (!round || closed) return;
        Cap(im, mat, first.At, first.Way, (float)(width / 2));
        Cap(im, mat, last.At, last.Way, (float)(width / 2));
    }

    public static void Fill(ImmediateMesh im, StandardMaterial3D mat, List<Vec2> ring, float y, Ground? ground = null)
    {
        var g = ground ?? Ground.Flat;
        var ps = ring.Count > 2 && (ring[0] - ring[^1]).Length < 1e-6 ? ring.GetRange(0, ring.Count - 1) : ring;
        if (ps.Count < 3) return;
        var flat = new Vector2[ps.Count];
        for (int i = 0; i < ps.Count; i++) flat[i] = new Vector2((float)ps[i].X, (float)ps[i].Z);
        var tris = Geometry2D.TriangulatePolygon(flat);
        if (tris.Length < 3) return;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        im.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        foreach (int i in tris)
        {
            im.SurfaceSetNormal(Vector3.Up);
            im.SurfaceAddVertex(Coords.ToWorld(ps[i], y + (float)g.At(ps[i])));
        }
        im.SurfaceEnd();
    }

    public static int CapSteps(double width) => (int)(CapBase + Math.Sqrt(Math.Max(0, width)) * CapPerRoot);

    const double CapBase = 4, CapPerRoot = 7;

    static void Cap(ImmediateMesh im, StandardMaterial3D mat, Vector3 at, Vector3 outward, float half)
    {
        if (outward == Vector3.Zero) return;
        var side = new Vector3(-outward.Z, 0, outward.X);
        int steps = Math.Max(1, CapSteps(half * 2));
        im.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        for (int k = 0; k < steps; k++)
        {
            float a0 = Mathf.Pi * k / steps, a1 = Mathf.Pi * (k + 1) / steps;
            im.SurfaceAddVertex(at);
            im.SurfaceAddVertex(at + (side * Mathf.Cos(a0) + outward * Mathf.Sin(a0)) * half);
            im.SurfaceAddVertex(at + (side * Mathf.Cos(a1) + outward * Mathf.Sin(a1)) * half);
        }
        im.SurfaceEnd();
    }
}
