using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

public sealed class PieceArt
{
    readonly Assets _assets;
    public PieceArt(Assets assets) => _assets = assets;

    public PieceLook Look { get; } = new();

    public static readonly Color Placeholder = new(0.62f, 0.60f, 0.56f);

    readonly Dictionary<(string shape, float w, float h, float t, bool sameUnderside), ArrayMesh> _slabs = new();
    readonly Dictionary<(Vector2 size, bool flip), PlaneMesh> _planes = new();
    readonly Dictionary<(string tex, float w, float h, float t), ArrayMesh?> _walls = new();
    readonly Dictionary<(string pkg, string rel, int size, Color tint, bool scissor), StandardMaterial3D> _prints = new();
    readonly Dictionary<Color, StandardMaterial3D> _edges = new();

    public int Meshes => _slabs.Count + _planes.Count + _walls.Count;
    public int Materials => _prints.Count + _edges.Count;

    public ArrayMesh Slab(string shape, float w, float h, float t, bool sameUnderside)
    {
        var key = (shape, w, h, t, sameUnderside);
        if (_slabs.TryGetValue(key, out var m)) return m;
        return _slabs[key] = BuildSlab(PieceNode.Outline(shape, w, h), w, h, t, sameUnderside);
    }

    public PlaneMesh Plane(Vector2 size, bool flip = false)
    {
        var key = (size, flip);
        if (_planes.TryGetValue(key, out var m)) return m;
        return _planes[key] = new PlaneMesh { Size = size, FlipFaces = flip };
    }

    public ArrayMesh? Walls(string texKey, AlphaMask mask, float w, float h, float t)
    {
        var key = (texKey, w, h, t);
        if (_walls.TryGetValue(key, out var m)) return m;
        return _walls[key] = SilhouetteWalls(mask, w, h, t);
    }

    internal static ArrayMesh? SilhouetteWalls(AlphaMask mask, float w, float h, float t)
    {
        const int N = AlphaMask.N;
        bool S(int x, int y) => x >= 0 && y >= 0 && x < N && y < N && mask.Solid[y * N + x];
        var next = new Dictionary<(int, int), List<(int, int)>>();
        void Edge(int x0, int y0, int x1, int y1)
        {
            if (!next.TryGetValue((x0, y0), out var l)) next[(x0, y0)] = l = new List<(int, int)>();
            l.Add((x1, y1));
        }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (!S(x, y)) continue;
                if (!S(x, y - 1)) Edge(x, y, x + 1, y);
                if (!S(x + 1, y)) Edge(x + 1, y, x + 1, y + 1);
                if (!S(x, y + 1)) Edge(x + 1, y + 1, x, y + 1);
                if (!S(x - 1, y)) Edge(x, y + 1, x, y);
            }
        if (next.Count == 0) return null;
        var loops = new List<List<Vector2>>();
        while (next.Count > 0)
        {
            var start = next.Keys.First();
            var loop = new List<Vector2>();
            var at = start;
            while (next.TryGetValue(at, out var outs) && outs.Count > 0)
            {
                loop.Add(new Vector2(at.Item1, at.Item2));
                var to = outs[^1];
                outs.RemoveAt(outs.Count - 1);
                if (outs.Count == 0) next.Remove(at);
                at = to;
                if (at == start) break;
            }
            if (loop.Count >= 3) loops.Add(loop);
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int quads = 0;
        foreach (var raw in loops)
        {
            var pts = raw;
            for (int pass = 0; pass < 4; pass++)
            {
                var sm = new List<Vector2>(pts.Count);
                for (int i = 0; i < pts.Count; i++) sm.Add((pts[(i + pts.Count - 1) % pts.Count] + pts[i] * 2 + pts[(i + 1) % pts.Count]) / 4);
                pts = sm;
            }
            var thin = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++)
            {
                var prev = thin.Count > 0 ? thin[^1] : pts[^1];
                var nxt = pts[(i + 1) % pts.Count];
                var d = nxt - prev;
                float dev = d.LengthSquared() < 1e-6f ? 0 : Mathf.Abs(d.Cross(pts[i] - prev)) / d.Length();
                if (dev > 0.08f || thin.Count == 0) thin.Add(pts[i]);
            }
            if (thin.Count < 3) continue;
            Vector3 P(Vector2 g, float y) => new((g.X / N - 0.5f) * w, y, (g.Y / N - 0.5f) * h);
            for (int i = 0; i < thin.Count; i++)
            {
                var a = thin[i];
                var b = thin[(i + 1) % thin.Count];
                var a0 = P(a, 0);
                var b0 = P(b, 0);
                var a1 = P(a, t);
                var b1 = P(b, t);
                foreach (var v in new[] { a0, b0, b1, a0, b1, a1 }) st.AddVertex(v);
                quads++;
            }
        }
        if (quads == 0) return null;
        st.GenerateNormals();
        var mesh = new ArrayMesh();
        st.Commit(mesh);
        return mesh;
    }

    public StandardMaterial3D Print(string? pkg, string? rel, int size, Color tint, bool scissor)
    {
        var key = (pkg ?? "", rel ?? "", size, tint, scissor);
        if (_prints.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D
        {
            Roughness = 1f, Metallic = 0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            AlbedoColor = tint,
            Transparency = scissor ? BaseMaterial3D.TransparencyEnum.AlphaScissor : BaseMaterial3D.TransparencyEnum.Disabled,
            AlphaScissorThreshold = 0.5f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            TextureRepeat = false,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _prints[key] = m;
        if (pkg != null && rel != null)
        {
            var tex = _assets.Texture(pkg, rel, t => { m.AlbedoTexture = t; m.AlbedoColor = tint; }, maxSize: size);
            if (tex != null) m.AlbedoTexture = tex;
            else m.AlbedoColor = Placeholder * tint;
        }
        return m;
    }

    public StandardMaterial3D Edge(Color c)
    {
        if (_edges.TryGetValue(c, out var m)) return m;
        return _edges[c] = new StandardMaterial3D
        {
            AlbedoColor = c, Roughness = 1f, Metallic = 0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    public void Clear()
    {
        _slabs.Clear();
        _planes.Clear();
        _walls.Clear();
        _prints.Clear();
        _edges.Clear();
    }

    static ArrayMesh BuildSlab(List<Vector2> outline, float w, float h, float t, bool sameUnderside)
    {
        var mesh = new ArrayMesh();
        var top = new SurfaceTool();
        top.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 1; i + 1 < outline.Count; i++)
            foreach (var p in new[] { outline[0], outline[i], outline[i + 1] })
            {
                top.SetUV(new Vector2(p.X / w + 0.5f, p.Y / h + 0.5f));
                top.SetNormal(Vector3.Up);
                top.AddVertex(new Vector3(p.X, t, p.Y));
            }
        top.Commit(mesh);
        var side = new SurfaceTool();
        side.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            var a0 = new Vector3(a.X, 0, a.Y);
            var b0 = new Vector3(b.X, 0, b.Y);
            var a1 = a0 with { Y = t };
            var b1 = b0 with { Y = t };
            foreach (var v in new[] { a0, b0, b1, a0, b1, a1 }) side.AddVertex(v);
        }
        side.GenerateNormals();
        side.Commit(mesh);
        var bottom = new SurfaceTool();
        bottom.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 1; i + 1 < outline.Count; i++)
            foreach (var p in new[] { outline[0], outline[i + 1], outline[i] })
            {
                bottom.SetUV(new Vector2(sameUnderside ? p.X / w + 0.5f : 0.5f - p.X / w, p.Y / h + 0.5f));
                bottom.SetNormal(Vector3.Down);
                bottom.AddVertex(new Vector3(p.X, 0, p.Y));
            }
        bottom.Commit(mesh);
        return mesh;
    }
}
