using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class EyeHorizon
{
    public const float ReachM = 9000;
    const float NearShare = 0.04f, Probe = 1e-3f, Inside = 1e-4f;
    const int EdgeSteps = 48, CornerSteps = 6, RecheckFrames = 30;
    static Shader? _shader;
    static readonly Vector2[] Corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    readonly Node3D _parent;
    readonly List<(GroundTiles.Sheet Sheet, MeshInstance3D Node)> _rings = new();
    float _upm = -1;
    int _frames;
    bool _showing;

    public EyeHorizon(Node3D parent) => _parent = parent;

    public bool Showing => _showing;

    public int Rings => _rings.Count;

    public void Show(float unitsPerMetre)
    {
        if (!Mathf.IsEqualApprox(unitsPerMetre, _upm) || ++_frames >= RecheckFrames)
        {
            _frames = 0;
            var sheets = GroundTiles.Showing.ToList();
            if (!Mathf.IsEqualApprox(unitsPerMetre, _upm) || !sheets.SequenceEqual(_rings.Select(r => r.Sheet))) Build(sheets, unitsPerMetre);
        }
        if (_showing) return;
        foreach (var (_, node) in _rings) node.Visible = true;
        _showing = true;
    }

    public void Hide()
    {
        if (!_showing) return;
        foreach (var (_, node) in _rings) node.Visible = false;
        _showing = false;
    }

    public void Drop()
    {
        foreach (var (_, node) in _rings) node.QueueFree();
        _rings.Clear();
        _showing = false;
        _upm = -1;
    }

    void Build(List<GroundTiles.Sheet> sheets, float unitsPerMetre)
    {
        Drop();
        _upm = unitsPerMetre;
        if (unitsPerMetre <= 0) return;
        foreach (var sheet in sheets)
        {
            if (Ring(sheet, sheets, ReachM * unitsPerMetre) is not { } mesh) continue;
            var material = new ShaderMaterial { Shader = _shader ??= GD.Load<Shader>("res://shaders/horizon.gdshader") };
            sheet.Lay(material);
            var node = new MeshInstance3D
            {
                Name = "Horizon", Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer, Visible = false,
            };
            _parent.AddChild(node);
            _rings.Add((sheet, node));
        }
    }

    static bool Covered(Vector3 at, GroundTiles.Sheet own, List<GroundTiles.Sheet> all) =>
        all.Any(s => s != own && s.UvOf(at) is { } uv && uv.X > Inside && uv.Y > Inside && uv.X < 1 - Inside && uv.Y < 1 - Inside);

    internal static ArrayMesh? Ring(GroundTiles.Sheet sheet, List<GroundTiles.Sheet> all, float reach)
    {
        var centre = sheet.World(new Vector2(0.5f, 0.5f), 0);
        var edge = new List<(Vector2 Uv, Vector3 At, Vector3 Out)>();
        for (int side = 0; side < 4; side++)
        {
            Vector2 from = Corners[side], to = Corners[(side + 1) % 4];
            var along = sheet.World(to, 0) - sheet.World(from, 0);
            var outward = new Vector3(along.Z, 0, -along.X).Normalized();
            if (outward.Dot(sheet.World(from, 0) - centre) < 0) outward = -outward;
            for (int k = 0; k < EdgeSteps; k++)
            {
                var uv = from.Lerp(to, k / (float)EdgeSteps);
                edge.Add((uv, sheet.World(uv, sheet.Rise(uv)), outward));
            }
        }
        var open = new bool[edge.Count];
        for (int k = 0; k < edge.Count; k++)
        {
            var mid = (edge[k].At + edge[(k + 1) % edge.Count].At) / 2;
            open[k] = !Covered(mid + edge[k].Out * (reach * Probe), sheet, all);
        }
        if (!open.Contains(true)) return null;
        float level = edge.Average(e => e.At.Y);
        var points = new List<Vector3>();
        var uvs = new List<Vector2>();
        var index = new List<int>();
        void Strip(int a, int b, Vector3 outA, Vector3 outB)
        {
            int i = points.Count;
            foreach (var (k, way) in new[] { (a, outA), (b, outB) })
            {
                var at = edge[k].At;
                points.Add(at);
                points.Add(at + way * (reach * NearShare));
                points.Add(new Vector3(at.X, level, at.Z) + way * reach);
                uvs.Add(edge[k].Uv);
                uvs.Add(edge[k].Uv);
                uvs.Add(edge[k].Uv);
            }
            index.AddRange([i, i + 3, i + 1, i + 1, i + 3, i + 4, i + 1, i + 4, i + 2, i + 2, i + 4, i + 5]);
        }
        for (int k = 0; k < edge.Count; k++)
        {
            int next = (k + 1) % edge.Count;
            if (open[k]) Strip(k, next, edge[k].Out, edge[k].Out);
            if (!open[k] || !open[next] || edge[k].Out.IsEqualApprox(edge[next].Out)) continue;
            for (int c = 0; c < CornerSteps; c++)
            {
                var a = edge[k].Out.Slerp(edge[next].Out, c / (float)CornerSteps).Normalized();
                var b = edge[k].Out.Slerp(edge[next].Out, (c + 1) / (float)CornerSteps).Normalized();
                Strip(next, next, a, b);
            }
        }
        if (index.Count == 0) return null;
        var normals = Enumerable.Repeat(Vector3.Up, points.Count).ToArray();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
