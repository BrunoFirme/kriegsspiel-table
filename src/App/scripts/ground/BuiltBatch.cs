using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    const float FeatherPx = 1, MostMitre = 2.5f, HairPx = 1, SharpestTurn = 0.3f;
    const int MostBatchedPoints = 30000, FewestCircleSides = 8, MostCircleSides = 48;

    readonly List<Vector2> _points = new(), _uvs = new();
    readonly List<Color> _colours = new();
    readonly List<int> _corners = new();

    protected void Flush()
    {
        if (_corners.Count > 0) RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), _corners.ToArray(), _points.ToArray(), _colours.ToArray(), _uvs.ToArray());
        _points.Clear();
        _uvs.Clear();
        _colours.Clear();
        _corners.Clear();
    }

    void Room()
    {
        if (_points.Count > MostBatchedPoints) Flush();
    }

    int Point(Vector2 at, Color colour, Vector2 uv = default)
    {
        _points.Add(at);
        _colours.Add(colour);
        _uvs.Add(uv);
        return _points.Count - 1;
    }

    void Corners(int a, int b, int c)
    {
        _corners.Add(a);
        _corners.Add(b);
        _corners.Add(c);
    }

    protected void Meshed(int[] corners, Vector2[] points, Color[] colours, Vector2[] uvs)
    {
        Room();
        int from = _points.Count;
        _points.AddRange(points);
        _colours.AddRange(colours);
        _uvs.AddRange(uvs);
        foreach (int corner in corners) _corners.Add(from + corner);
    }

    static bool Convex(Vector2[] ring)
    {
        float turn = 0;
        for (int i = 0; i < ring.Length; i++)
        {
            float cross = (ring[(i + 1) % ring.Length] - ring[i]).Cross(ring[(i + 2) % ring.Length] - ring[(i + 1) % ring.Length]);
            if (cross * turn < 0) return false;
            if (cross != 0) turn = cross;
        }
        return true;
    }

    protected void DrawColoredPolygon(Vector2[] ring, Color colour, Vector2[]? uvs = null)
    {
        if (ring.Length < 3) return;
        Room();
        int from = _points.Count;
        bool convex = ring.Length <= 3 || Convex(ring);
        int[] cut = convex ? [] : Geometry2D.TriangulatePolygon(ring);
        if (!convex && cut.Length < 3) return;
        for (int i = 0; i < ring.Length; i++) Point(ring[i], colour, uvs != null && i < uvs.Length ? uvs[i] : default);
        for (int i = 1; convex && i + 1 < ring.Length; i++) Corners(from, from + i, from + i + 1);
        foreach (int corner in cut) _corners.Add(from + corner);
    }

    protected new void DrawRect(Rect2 rect, Color colour, bool filled = true, float width = -1, bool antialiased = false)
    {
        Vector2[] ring = [rect.Position, new(rect.End.X, rect.Position.Y), rect.End, new(rect.Position.X, rect.End.Y)];
        if (filled) DrawColoredPolygon(ring, colour);
        else Stroke([.. ring, ring[0]], colour, width, antialiased);
    }

    protected new void DrawLine(Vector2 from, Vector2 to, Color colour, float width = -1, bool antialiased = false) => Stroke([from, to], colour, width, antialiased);

    protected new void DrawPolyline(Vector2[] line, Color colour, float width = -1, bool antialiased = false) => Stroke(line, colour, width, antialiased);

    protected new void DrawArc(Vector2 centre, float radius, float start, float end, int count, Color colour, float width = -1, bool antialiased = false)
    {
        var line = new Vector2[Math.Max(2, count)];
        for (int i = 0; i < line.Length; i++) line[i] = centre + Vector2.Right.Rotated(Mathf.Lerp(start, end, i / (float)(line.Length - 1))) * radius;
        Stroke(line, colour, width, antialiased);
    }

    protected new void DrawCircle(Vector2 at, float radius, Color colour, bool filled = true, float width = -1, bool antialiased = false)
    {
        int sides = Math.Clamp((int)(radius * 2.5f) + FewestCircleSides, FewestCircleSides, MostCircleSides);
        if (!filled)
        {
            DrawArc(at, radius, 0, Mathf.Tau, sides + 1, colour, width, antialiased);
            return;
        }
        Room();
        int middle = Point(at, colour), rows = antialiased ? 2 : 1;
        var clear = colour with { A = 0 };
        for (int i = 0; i < sides; i++)
        {
            var out_ = Vector2.Right.Rotated(i * Mathf.Tau / sides);
            Point(at + out_ * (antialiased ? Mathf.Max(radius - FeatherPx / 2, 0) : radius), colour);
            if (antialiased) Point(at + out_ * (radius + FeatherPx / 2), clear);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = middle + 1 + i * rows, b = middle + 1 + (i + 1) % sides * rows;
            Corners(middle, a, b);
            if (!antialiased) continue;
            Corners(a, a + 1, b + 1);
            Corners(a, b + 1, b);
        }
    }

    protected new void DrawDashedLine(Vector2 from, Vector2 to, Color colour, float width = -1, float dash = 2, bool aligned = true, bool antialiased = false)
    {
        float length = (to - from).Length();
        var way = Way(from, to);
        if (length < dash || dash <= 0 || way == Vector2.Zero)
        {
            Stroke([from, to], colour, width, antialiased);
            return;
        }
        int steps = aligned ? Mathf.CeilToInt(length / dash) : Mathf.FloorToInt(length / dash);
        if (steps % 2 == 0) steps--;
        var at = aligned ? from + way * ((length - steps * dash) / 2) : from;
        for (int i = 0; i < steps; i += 2)
        {
            Stroke([i == 0 ? from : at, aligned && i == steps - 1 ? to : at + way * dash], colour, width, antialiased);
            at += way * (dash * 2);
        }
    }

    static Vector2 Way(Vector2 a, Vector2 b)
    {
        var d = b - a;
        return d.LengthSquared() > 1e-10f ? d.Normalized() : Vector2.Zero;
    }

    void Stroke(Vector2[] line, Color colour, float width, bool soft)
    {
        int n = line.Length;
        if (n < 2) return;
        bool closed = n > 2 && line[0].DistanceSquaredTo(line[n - 1]) < 1e-6f;
        int m = closed ? n - 1 : n;
        if (m < 2) return;
        Room();
        float half = (width > 0 ? width : HairPx) / 2, core = soft ? Mathf.Max(half - FeatherPx / 2, 0) : half, edge = half + FeatherPx / 2;
        int rows = soft ? 4 : 2, from = _points.Count;
        if (soft) colour = colour with { A = colour.A * Mathf.Min(1, half * 2 / FeatherPx) };
        var clear = colour with { A = 0 };
        for (int i = 0; i < m; i++)
        {
            var before = closed || i > 0 ? Way(line[(i - 1 + m) % m], line[i]) : Vector2.Zero;
            var after = closed || i < m - 1 ? Way(line[i], line[(i + 1) % m]) : Vector2.Zero;
            if (before == Vector2.Zero) before = after;
            if (after == Vector2.Zero) after = before;
            var along = before + after;
            along = along.LengthSquared() > 1e-6f ? along.Normalized() : after;
            var side = along.Orthogonal() * Mathf.Min(MostMitre, 1 / Mathf.Max(SharpestTurn, along.Dot(after)));
            if (soft) Point(line[i] - side * edge, clear);
            Point(line[i] - side * core, colour);
            Point(line[i] + side * core, colour);
            if (soft) Point(line[i] + side * edge, clear);
        }
        for (int i = 0; i < (closed ? m : m - 1); i++)
            for (int r = 0; r + 1 < rows; r++)
            {
                int a = from + i * rows + r, b = from + (i + 1) % m * rows + r;
                Corners(a, b, b + 1);
                Corners(a, b + 1, a + 1);
            }
    }
}
