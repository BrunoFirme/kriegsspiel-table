using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public sealed class ShapeSpec
{
    public string Kind { get; set; } = ShapeMaker.Lathe;
    public float[] Points { get; set; } = Array.Empty<float>();
    public float Depth { get; set; } = ShapeMaker.DefaultDepth;

    public int Stride => Kind == ShapeMaker.Sweep ? 4 : 2;
    public int Count => Points.Length / Stride;
    public ShapeSpec Copy() => new() { Kind = Kind, Points = Points.ToArray(), Depth = Depth };
}

internal static class ShapeMaker
{
    public const string Lathe = "lathe", Sweep = "sweep", Extrude = "extrude";
    public static readonly string[] Kinds = { Lathe, Sweep, Extrude };
    public const float DefaultDepth = 0.2f;
    const int Round = 48, TubeSides = 20, SpanSteps = 8;
    const float Tiny = 1e-6f;

    public static bool IsSpec(string kind) => Kinds.Contains(kind);

    public static ShapeSpec Default(string kind) => kind switch
    {
        Sweep => new ShapeSpec { Kind = Sweep, Points = new[] { -0.4f, 0f, 0f, 0.1f, -0.15f, 0.35f, 0f, 0.09f, 0.15f, 0.55f, 0f, 0.07f, 0.4f, 0.6f, 0f, 0.05f } },
        Extrude => new ShapeSpec { Kind = Extrude, Points = new[] { -0.5f, 0.5f, 0.5f, 0.5f, 0.5f, -0.1f, 0f, -0.5f, -0.5f, -0.1f }, Depth = DefaultDepth },
        _ => new ShapeSpec { Kind = Lathe, Points = new[] { 0f, 0f, 0.5f, 0f, 0.5f, 0.15f, 0.35f, 0.55f, 0.38f, 0.9f, 0f, 1f } },
    };

    public static bool Valid(ShapeSpec s) => s.Points.Length % s.Stride == 0 && s.Count >= (s.Kind == Extrude ? 3 : 2) && s.Points.All(float.IsFinite) && float.IsFinite(s.Depth)
        && (s.Kind != Extrude || s.Depth > Tiny);

    public static MeshData Raw(ShapeSpec s) => s.Kind switch
    {
        Sweep => Tube(SweepPath(s)),
        Extrude => Prism(Outline(s), Mathf.Max(Tiny, s.Depth)),
        _ => Spin(Profile(s)),
    };

    public static MeshData Make(ShapeSpec s)
    {
        var m = Raw(s);
        var c = m.Bounds().GetCenter();
        m.Transform(new Transform3D(Basis.Identity, -c));
        return m;
    }

    public static Vector3 Offset(ShapeSpec s) => Raw(s).Bounds().GetCenter();

    public static List<Vector2> Profile(ShapeSpec s)
    {
        var l = new List<Vector2>();
        for (int i = 0; i + 1 < s.Points.Length; i += 2) l.Add(new Vector2(Mathf.Max(0, s.Points[i]), s.Points[i + 1]));
        return l;
    }

    public static List<Vector2> Outline(ShapeSpec s)
    {
        var l = new List<Vector2>();
        for (int i = 0; i + 1 < s.Points.Length; i += 2) l.Add(new Vector2(s.Points[i], s.Points[i + 1]));
        float area = 0;
        for (int i = 0; i < l.Count; i++) area += l[i].X * l[(i + 1) % l.Count].Y - l[(i + 1) % l.Count].X * l[i].Y;
        if (area < 0) l.Reverse();
        return l;
    }

    public static List<Vector4> SweepPath(ShapeSpec s)
    {
        var keys = new List<Vector4>();
        for (int i = 0; i + 3 < s.Points.Length; i += 4) keys.Add(new Vector4(s.Points[i], s.Points[i + 1], s.Points[i + 2], Mathf.Max(0, s.Points[i + 3])));
        if (keys.Count < 3) return keys;
        var smooth = new List<Vector4>();
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            var p0 = keys[Math.Max(0, i - 1)];
            var p1 = keys[i];
            var p2 = keys[i + 1];
            var p3 = keys[Math.Min(keys.Count - 1, i + 2)];
            for (int k = 0; k < SpanSteps; k++)
            {
                float t = k / (float)SpanSteps;
                smooth.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
            }
        }
        smooth.Add(keys[^1]);
        return smooth;
    }

    static MeshData NewMesh() => new() { Tex = { null }, MatColor = { new Color(0.82f, 0.80f, 0.74f) } };

    static void Tri(MeshData m, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        var n = (c - a).Cross(b - a);
        if (n.LengthSquared() < Tiny * Tiny) return;
        if (n.Dot(outward) < 0) (b, c) = (c, b);
        var face = (c - a).Cross(b - a).Normalized();
        m.Add(a, face, Vector2.Zero, Colors.White);
        m.Add(b, face, Vector2.Zero, Colors.White);
        m.Add(c, face, Vector2.Zero, Colors.White);
        m.Mat.Add(0);
    }

    static void Quad(MeshData m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        Tri(m, a, b, c, outward);
        Tri(m, a, c, d, outward);
    }

    static Vector3 Around(Vector2 p, float angle) => new(p.X * Mathf.Sin(angle), p.Y, p.X * Mathf.Cos(angle));

    static MeshData Spin(List<Vector2> profile)
    {
        var m = NewMesh();
        if (profile.Count < 2) return m;
        float area = 0;
        for (int i = 0; i + 1 < profile.Count; i++) area += (profile[i + 1].Y - profile[i].Y) * (profile[i + 1].X + profile[i].X);
        float side = area >= 0 ? 1 : -1;
        for (int i = 0; i + 1 < profile.Count; i++)
        {
            var a = profile[i];
            var b = profile[i + 1];
            var edge = b - a;
            var outward2 = new Vector2(edge.Y, -edge.X) * side;
            for (int k = 0; k < Round; k++)
            {
                float t0 = Mathf.Tau * k / Round, t1 = Mathf.Tau * (k + 1) / Round;
                var mid = (a + b) / 2;
                var outward = new Vector3(outward2.X * Mathf.Sin((t0 + t1) / 2), outward2.Y, outward2.X * Mathf.Cos((t0 + t1) / 2));
                if (outward.LengthSquared() < Tiny) outward = Around(mid, (t0 + t1) / 2) with { Y = 0 };
                Quad(m, Around(a, t0), Around(b, t0), Around(b, t1), Around(a, t1), outward);
            }
        }
        foreach (var (end, edge) in new[] { (profile[0], profile[0] - new Vector2(0, profile[0].Y)), (profile[^1], new Vector2(0, profile[^1].Y) - profile[^1]) })
        {
            if (end.X < Tiny) continue;
            var centre = new Vector3(0, end.Y, 0);
            var outward = -edge.X * side < 0 ? Vector3.Down : Vector3.Up;
            for (int k = 0; k < Round; k++) Tri(m, centre, Around(end, Mathf.Tau * k / Round), Around(end, Mathf.Tau * (k + 1) / Round), outward);
        }
        return m;
    }

    static MeshData Prism(List<Vector2> outline, float depth)
    {
        var m = NewMesh();
        if (outline.Count < 3) return m;
        var idx = Geometry2D.TriangulatePolygon(outline.ToArray());
        float h = depth / 2;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            Vector3 P(int k, float z) => new(outline[idx[i + k]].X, outline[idx[i + k]].Y, z);
            Tri(m, P(0, h), P(1, h), P(2, h), Vector3.Back);
            Tri(m, P(0, -h), P(1, -h), P(2, -h), Vector3.Forward);
        }
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            var outward = new Vector3(b.Y - a.Y, a.X - b.X, 0);
            Quad(m, new Vector3(a.X, a.Y, -h), new Vector3(b.X, b.Y, -h), new Vector3(b.X, b.Y, h), new Vector3(a.X, a.Y, h), outward);
        }
        return m;
    }

    static MeshData Tube(List<Vector4> path)
    {
        var m = NewMesh();
        var pts = path.Select(p => new Vector3(p.X, p.Y, p.Z)).ToList();
        for (int i = pts.Count - 1; i > 0; i--) if (pts[i].DistanceTo(pts[i - 1]) < Tiny) { pts.RemoveAt(i); path.RemoveAt(i); }
        if (pts.Count < 2) return m;
        var tangents = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++) tangents[i] = (pts[Math.Min(pts.Count - 1, i + 1)] - pts[Math.Max(0, i - 1)]).Normalized();
        var normal = Mathf.Abs(tangents[0].Dot(Vector3.Up)) < 0.9f ? tangents[0].Cross(Vector3.Up).Normalized() : tangents[0].Cross(Vector3.Right).Normalized();
        var rings = new List<Vector3[]>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                var axis = tangents[i - 1].Cross(tangents[i]);
                if (axis.LengthSquared() > Tiny) normal = normal.Rotated(axis.Normalized(), tangents[i - 1].AngleTo(tangents[i]));
                normal = (normal - tangents[i] * normal.Dot(tangents[i])).Normalized();
            }
            var bi = tangents[i].Cross(normal);
            var ring = new Vector3[TubeSides];
            for (int k = 0; k < TubeSides; k++)
            {
                float t = Mathf.Tau * k / TubeSides;
                ring[k] = pts[i] + (normal * Mathf.Cos(t) + bi * Mathf.Sin(t)) * path[i].W;
            }
            rings.Add(ring);
        }
        for (int i = 0; i + 1 < rings.Count; i++)
            for (int k = 0; k < TubeSides; k++)
            {
                int k1 = (k + 1) % TubeSides;
                var mid = (pts[i] + pts[i + 1]) / 2;
                var outward = (rings[i][k] + rings[i + 1][k1]) / 2 - mid;
                Quad(m, rings[i][k], rings[i + 1][k], rings[i + 1][k1], rings[i][k1], outward);
            }
        foreach (var (i, outward) in new[] { (0, -tangents[0]), (pts.Count - 1, tangents[^1]) })
        {
            if (path[i].W < Tiny) continue;
            for (int k = 0; k < TubeSides; k++) Tri(m, pts[i], rings[i][k], rings[i][(k + 1) % TubeSides], outward);
        }
        return m;
    }
}
