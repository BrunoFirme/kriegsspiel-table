using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

internal sealed class SdfShape
{
    public required Transform3D ToLocal;
    public required Func<Vector3, float> Dist;
    public float Unit = 1;
    public Vector3 Centre;

    public float At(Vector3 p) => Dist(ToLocal * p - Centre) * Unit;
}

internal sealed class SdfOperand
{
    public required List<SdfShape> Shapes;
    public string Join = ModelTree.Add;
    public float Blend;
    public Color Colour = Colors.White;

    public float At(Vector3 p)
    {
        float d = float.MaxValue;
        foreach (var s in Shapes) d = Mathf.Min(d, s.At(p));
        return d;
    }
}

internal static class ShapeSdf
{
    const float Tiny = 1e-6f;
    const int Cells = 80, MaxCellsAside = 160;
    const float Margin = 2.5f;
    static readonly string[] Plain = { "box", "flag", "sphere", "cylinder", "base", "hex", "cone", "capsule", "torus", "wedge" };

    public static bool Fits(ModelPart p) => !p.IsGroup && (Plain.Contains(p.Kind) || (ShapeMaker.IsSpec(p.Kind) && p.Spec != null && ShapeMaker.Valid(p.Spec)));

    public static Func<Vector3, float>? Unit(ModelPart p)
    {
        if (ShapeMaker.IsSpec(p.Kind) && p.Spec is { } spec && ShapeMaker.Valid(spec))
        {
            var off = ShapeMaker.Offset(spec);
            return spec.Kind switch
            {
                ShapeMaker.Extrude => Extruded(ShapeMaker.Outline(spec), Mathf.Max(Tiny, spec.Depth) / 2, off),
                ShapeMaker.Sweep => Swept(ShapeMaker.SweepPath(spec), off),
                _ => Turned(ShapeMaker.Profile(spec), off),
            };
        }
        return p.Kind switch
        {
            "box" => q => Box(q, new Vector3(0.5f, 0.5f, 0.5f)),
            "flag" => q => Box(q, new Vector3(0.5f, 0.33f, 0.01f)),
            "sphere" => q => q.Length() - 0.5f,
            "cylinder" => q => Cylinder(q, 0.5f, 0.5f),
            "base" => q => Cylinder(q, 0.51f, 0.04f),
            "hex" => Extrude(Ngon(6, 0.5f), 0.125f, upright: true),
            "cone" => q => Cone(q, 0.5f, 0.5f, 0),
            "capsule" => q => Segment(q, new Vector3(0, -0.25f, 0), new Vector3(0, 0.25f, 0)) - 0.25f,
            "torus" => q => new Vector2(new Vector2(q.X, q.Z).Length() - 0.4f, q.Y).Length() - 0.1f,
            "wedge" => Extrude(new List<Vector2> { new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0, 0.5f) }, 0.5f, upright: false),
            _ => null,
        };
    }

    static float Box(Vector3 q, Vector3 half)
    {
        var d = q.Abs() - half;
        return new Vector3(Mathf.Max(d.X, 0), Mathf.Max(d.Y, 0), Mathf.Max(d.Z, 0)).Length() + Mathf.Min(Mathf.Max(d.X, Mathf.Max(d.Y, d.Z)), 0);
    }

    static float Cylinder(Vector3 q, float r, float h)
    {
        var d = new Vector2(Mathf.Abs(new Vector2(q.X, q.Z).Length()) - r, Mathf.Abs(q.Y) - h);
        return Mathf.Min(Mathf.Max(d.X, d.Y), 0) + new Vector2(Mathf.Max(d.X, 0), Mathf.Max(d.Y, 0)).Length();
    }

    static float Cone(Vector3 p, float h, float r1, float r2)
    {
        var q = new Vector2(new Vector2(p.X, p.Z).Length(), p.Y);
        var k1 = new Vector2(r2, h);
        var k2 = new Vector2(r2 - r1, 2 * h);
        var ca = new Vector2(q.X - Mathf.Min(q.X, q.Y < 0 ? r1 : r2), Mathf.Abs(q.Y) - h);
        var cb = q - k1 + k2 * Mathf.Clamp((k1 - q).Dot(k2) / k2.Dot(k2), 0, 1);
        float s = cb.X < 0 && ca.Y < 0 ? -1 : 1;
        return s * Mathf.Sqrt(Mathf.Min(ca.Dot(ca), cb.Dot(cb)));
    }

    static float Segment(Vector3 p, Vector3 a, Vector3 b)
    {
        var pa = p - a;
        var ba = b - a;
        float h = Mathf.Clamp(pa.Dot(ba) / Mathf.Max(Tiny, ba.Dot(ba)), 0, 1);
        return (pa - ba * h).Length();
    }

    static List<Vector2> Ngon(int n, float r) => Enumerable.Range(0, n).Select(i => new Vector2(r * Mathf.Sin(Mathf.Tau * i / n), r * Mathf.Cos(Mathf.Tau * i / n))).ToList();

    public static float Polygon(Vector2 p, List<Vector2> v)
    {
        float d = (p - v[0]).Dot(p - v[0]);
        float s = 1;
        for (int i = 0, j = v.Count - 1; i < v.Count; j = i, i++)
        {
            var e = v[j] - v[i];
            var w = p - v[i];
            var b = w - e * Mathf.Clamp(w.Dot(e) / Mathf.Max(Tiny, e.Dot(e)), 0, 1);
            d = Mathf.Min(d, b.Dot(b));
            bool c1 = p.Y >= v[i].Y, c2 = p.Y < v[j].Y, c3 = e.X * w.Y > e.Y * w.X;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }

    static float Extrusion(float d2, float z, float h)
    {
        var w = new Vector2(d2, Mathf.Abs(z) - h);
        return Mathf.Min(Mathf.Max(w.X, w.Y), 0) + new Vector2(Mathf.Max(w.X, 0), Mathf.Max(w.Y, 0)).Length();
    }

    static Func<Vector3, float> Extrude(List<Vector2> outline, float h, bool upright) =>
        upright ? q => Extrusion(Polygon(new Vector2(q.X, q.Z), outline), q.Y, h) : q => Extrusion(Polygon(new Vector2(q.X, q.Y), outline), q.Z, h);

    static Func<Vector3, float> Extruded(List<Vector2> outline, float h, Vector3 off) => q =>
    {
        var p = q + off;
        return Extrusion(Polygon(new Vector2(p.X, p.Y), outline), p.Z, h);
    };

    static Func<Vector3, float> Turned(List<Vector2> profile, Vector3 off)
    {
        var poly = profile.ToList();
        if (poly[^1].X > Tiny) poly.Add(new Vector2(0, poly[^1].Y));
        if (poly[0].X > Tiny) poly.Insert(0, new Vector2(0, poly[0].Y));
        return q =>
        {
            var p = q + off;
            return Polygon(new Vector2(new Vector2(p.X, p.Z).Length(), p.Y), poly);
        };
    }

    static Func<Vector3, float> Swept(List<Vector4> path, Vector3 off) => q =>
    {
        var p = q + off;
        float d = float.MaxValue;
        for (int i = 0; i + 1 < path.Count; i++)
            d = Mathf.Min(d, RoundCone(p, new Vector3(path[i].X, path[i].Y, path[i].Z), new Vector3(path[i + 1].X, path[i + 1].Y, path[i + 1].Z), path[i].W, path[i + 1].W));
        return d;
    };

    static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
    {
        var ba = b - a;
        float l2 = ba.Dot(ba);
        if (l2 < Tiny) return (p - a).Length() - Mathf.Max(r1, r2);
        float rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1 / l2;
        var pa = p - a;
        float y = pa.Dot(ba), z = y - l2;
        var xv = pa * l2 - ba * y;
        float x2 = xv.Dot(xv), y2 = y * y * l2, z2 = z * z * l2;
        float k = Mathf.Sign(rr) * rr * rr * x2;
        if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
        if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
        return (Mathf.Sqrt(Mathf.Max(0, x2 * a2 * il2)) + y * rr) * il2 - r1;
    }

    public static float Smooth(float a, float b, float k, string join)
    {
        switch (join)
        {
            case ModelTree.Cut: return -Smooth(-a, b, k, ModelTree.Add);
            case ModelTree.Overlap: return -Smooth(-a, -b, k, ModelTree.Add);
        }
        if (k <= Tiny) return Mathf.Min(a, b);
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0) / k;
        return Mathf.Min(a, b) - h * h * k * 0.25f;
    }

    public static float Field(List<SdfOperand> ops, Vector3 p)
    {
        float d = ops[0].At(p);
        for (int i = 1; i < ops.Count; i++) d = Smooth(d, ops[i].At(p), ops[i].Blend, ops[i].Join);
        return d;
    }

    static Color Paint(List<SdfOperand> ops, Vector3 p, float spread)
    {
        var sum = new Color(0, 0, 0, 0);
        float total = 0, nearest = float.MaxValue;
        var near = ops[0].Colour;
        foreach (var o in ops)
        {
            float d = Mathf.Abs(o.At(p));
            if (d < nearest) { nearest = d; near = o.Colour; }
            float w = Mathf.Max(0, 1 - d / spread);
            w *= w;
            sum += o.Colour * w;
            total += w;
        }
        return total > Tiny ? (sum / total) with { A = 1 } : near;
    }

    public static MeshData Mesh(List<SdfOperand> ops, Aabb reach)
    {
        float blend = ops.Max(o => o.Blend);
        float longest = Mathf.Max(reach.Size.X, Mathf.Max(reach.Size.Y, reach.Size.Z));
        float step = Mathf.Max(Tiny, longest / Cells);
        var lo = reach.Position - Vector3.One * (step * Margin + blend);
        var span = reach.Size + Vector3.One * (2 * (step * Margin + blend));
        int Count(float along) => Math.Clamp((int)Mathf.Ceil(along / step), 2, MaxCellsAside);
        int nx = Count(span.X), ny = Count(span.Y), nz = Count(span.Z);
        var cell = new Vector3(span.X / nx, span.Y / ny, span.Z / nz);
        int sx = nx + 1, sy = ny + 1;
        var f = new float[sx * sy * (nz + 1)];
        Vector3 At(int i, int j, int k) => lo + new Vector3(i * cell.X, j * cell.Y, k * cell.Z);
        int G(int i, int j, int k) => i + sx * (j + sy * k);
        System.Threading.Tasks.Parallel.For(0, nz + 1, k =>
        {
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++) f[G(i, j, k)] = Field(ops, At(i, j, k));
        });
        var vert = new int[nx * ny * nz];
        Array.Fill(vert, -1);
        var pos = new List<Vector3>();
        int C(int i, int j, int k) => i + nx * (j + ny * k);
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    var sum = Vector3.Zero;
                    int n = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        var (a, b) = CubeEdges[e];
                        float fa = f[G(i + a.X, j + a.Y, k + a.Z)], fb = f[G(i + b.X, j + b.Y, k + b.Z)];
                        if ((fa < 0) == (fb < 0)) continue;
                        float t = fa / (fa - fb);
                        sum += At(i + a.X, j + a.Y, k + a.Z).Lerp(At(i + b.X, j + b.Y, k + b.Z), t);
                        n++;
                    }
                    if (n == 0) continue;
                    vert[C(i, j, k)] = pos.Count;
                    pos.Add(sum / n);
                }
        var m = new MeshData { Tex = { null }, MatColor = { Colors.White } };
        float h = Mathf.Min(cell.X, Mathf.Min(cell.Y, cell.Z)) * 0.5f;
        Vector3 Grad(Vector3 p) => new Vector3(Field(ops, p + new Vector3(h, 0, 0)) - Field(ops, p - new Vector3(h, 0, 0)),
            Field(ops, p + new Vector3(0, h, 0)) - Field(ops, p - new Vector3(0, h, 0)), Field(ops, p + new Vector3(0, 0, h)) - Field(ops, p - new Vector3(0, 0, h))).Normalized();
        var normals = pos.Select(Grad).ToArray();
        float spread = Mathf.Max(blend, step * 2);
        var colours = pos.Select(p => Paint(ops, p, spread)).ToArray();
        void Face(int a, int b, int c, int d, Vector3 outward)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;
            foreach (var (x, y, z) in new[] { (a, b, c), (a, c, d) })
            {
                var (p0, p1, p2) = (x, y, z);
                if ((pos[p2] - pos[p0]).Cross(pos[p1] - pos[p0]).Dot(outward) < 0) (p1, p2) = (p2, p1);
                foreach (var v in new[] { p0, p1, p2 }) m.Add(pos[v], normals[v], Vector2.Zero, colours[v]);
                m.Mat.Add(0);
            }
        }
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float f0 = f[G(i, j, k)];
                    if (i < nx && j > 0 && k > 0 && (f0 < 0) != (f[G(i + 1, j, k)] < 0))
                        Face(Cv(i, j - 1, k - 1), Cv(i, j, k - 1), Cv(i, j, k), Cv(i, j - 1, k), f0 < 0 ? Vector3.Right : Vector3.Left);
                    if (j < ny && i > 0 && k > 0 && (f0 < 0) != (f[G(i, j + 1, k)] < 0))
                        Face(Cv(i - 1, j, k - 1), Cv(i, j, k - 1), Cv(i, j, k), Cv(i - 1, j, k), f0 < 0 ? Vector3.Up : Vector3.Down);
                    if (k < nz && i > 0 && j > 0 && (f0 < 0) != (f[G(i, j, k + 1)] < 0))
                        Face(Cv(i - 1, j - 1, k), Cv(i, j - 1, k), Cv(i, j, k), Cv(i - 1, j, k), f0 < 0 ? Vector3.Back : Vector3.Forward);
                }
        return m;
        int Cv(int i, int j, int k) => i < 0 || j < 0 || k < 0 || i >= nx || j >= ny || k >= nz ? -1 : vert[C(i, j, k)];
    }

    static readonly (Vector3I a, Vector3I b)[] CubeEdges =
    {
        (new(0, 0, 0), new(1, 0, 0)), (new(0, 1, 0), new(1, 1, 0)), (new(0, 0, 1), new(1, 0, 1)), (new(0, 1, 1), new(1, 1, 1)),
        (new(0, 0, 0), new(0, 1, 0)), (new(1, 0, 0), new(1, 1, 0)), (new(0, 0, 1), new(0, 1, 1)), (new(1, 0, 1), new(1, 1, 1)),
        (new(0, 0, 0), new(0, 0, 1)), (new(1, 0, 0), new(1, 0, 1)), (new(0, 1, 0), new(0, 1, 1)), (new(1, 1, 0), new(1, 1, 1)),
    };
}
