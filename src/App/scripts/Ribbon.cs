using System;
using System.Collections.Generic;
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
            var a = pts[i - 1]; var b = pts[i];
            double cell = Math.Min(ground.CellSize(a), ground.CellSize(b));
            int n = double.IsFinite(cell) ? Math.Clamp((int)Math.Ceiling((b - a).Length / cell), 1, MostDrapedPoints / pts.Count + 1) : 1;
            for (int k = 1; k <= n; k++) o.Add(new Vec2(a.X + (b.X - a.X) * k / n, a.Z + (b.Z - a.Z) * k / n));
        }
        return o;
    }

    public static void Draw(ImmediateMesh im, StandardMaterial3D mat, List<Vec2> pts, double width, float y, Ground? ground = null)
    {
        var g = ground ?? Ground.Flat;
        pts = Draped(pts, g);
        bool closed = pts.Count > 2 && (pts[0] - pts[^1]).Length < 1e-6;
        var ps = closed ? pts.GetRange(0, pts.Count - 1) : pts;
        int m = ps.Count;
        if (m < 2) return;
        Vector3 W(int i) => Coords.ToWorld(ps[i], y + (float)g.At(ps[i]));
        Vector3 Dir(int i, int j) { var d = W(j) - W(i); d.Y = 0; return d.LengthSquared() > 1e-10 ? d.Normalized() : Vector3.Zero; }
        float half = (float)(width / 2);
        im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
        for (int k = 0; k <= (closed ? m : m - 1); k++)
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
            im.SurfaceAddVertex(p + off);
            im.SurfaceAddVertex(p - off);
        }
        im.SurfaceEnd();
    }
}
