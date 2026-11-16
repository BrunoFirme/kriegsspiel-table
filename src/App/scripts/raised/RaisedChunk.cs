using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RaisedChunk
{
    public const float TreeNearM = 700, TreeFarM = 3500;
    const float TreePick = 15.5f, TintSpread = 0.22f, CrownGrown = 1.3f, PropPick = 61.3f, ShiftPick = 53.1f, ShiftM = 7, PropWeather = 0.5f;

    readonly Vector3 _origin;
    readonly RaisedMesh _mesh;
    readonly Dictionary<TreeKind, List<(Transform3D At, Color Tint)>> _trees = new();

    public int Buildings { get; private set; }

    public int Trees => _trees.Values.Sum(t => t.Count);

    public IEnumerable<Vector3> TreesAt => _trees.Values.SelectMany(list => list.Select(t => t.At.Origin + _origin));

    public IReadOnlyList<Vector3> Built => _mesh.Points;

    public readonly record struct Drawn(GeometryInstance3D Node, float BeginM, float EndM);

    public RaisedChunk(Vector3 origin)
    {
        _origin = origin;
        _mesh = new RaisedMesh { Origin = origin };
    }

    public void Add(BuiltPainter.Shape shape, Vector3[] world, string? style, Ground ground, float top, float upm, Vector3 across, float[]? gaps = null, float[]? opens = null)
    {
        _mesh.PerMetre = shape.PerMetre > 0 ? shape.PerMetre : upm;
        var key = shape.Uv[0] * BuiltCanvas.KeyScale;
        _mesh.Shift = new Vector2(BuiltCanvas.Chanced(key, ShiftPick), BuiltCanvas.Chanced(key, ShiftPick + 1)) * ShiftM;
        _mesh.Weather = PropWeather;
        _mesh.OpenFor = null;
        var flat = world.Select(p => p with { Y = 0 }).ToArray();
        if (shape.Kind == PartKinds.Block)
        {
            var floor = TableRaised.Footing(world, _mesh.PerMetre, out float bottom);
            RaisedBuildings.Add(_mesh, shape, new RaisedBuildings.Site(flat, floor.Y, bottom, style, gaps, opens));
            Buildings++;
        }
        else if (shape.Kind == PartKinds.Line) RaisedLines.Add(_mesh, shape, world, p => top + (float)ground.DrawnAt(Coords.FromWorld(p)), style);
        else if (shape.Kind == PartKinds.Sign && shape.Class == "tree")
        {
            float pick = BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, TreePick);
            var kind = RaisedForest.TownKind(RaisedLooks.Aged(style, shape.Manner), pick);
            Tree(world[0], kind, RaisedForest.TownCrownM(BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, 5.5f)) * _mesh.PerMetre, pick);
        }
        else if (shape.Kind == PartKinds.Sign) RaisedProps.Add(_mesh, shape.Class, world[0], across, BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, PropPick));
    }

    public void Tree(Vector3 at, TreeKind kind, float crown, float pick)
    {
        if (!_trees.TryGetValue(kind, out var list)) _trees[kind] = list = new();
        float tall = 0.85f + 0.3f * ((pick * 7.31f) % 1);
        float wide = crown * CrownGrown;
        var basis = new Basis(Vector3.Up, pick * Mathf.Tau).Scaled(new Vector3(wide, wide * tall, wide));
        float tone = ((pick * 3.17f) % 1 - 0.5f) * TintSpread;
        list.Add((new Transform3D(basis, at - _origin), new Color(1 + tone, 1 + tone * 0.6f, 1 + tone * 0.3f)));
    }

    const int PerInstance = 16;

    static float[] Packed(List<(Transform3D At, Color Tint)> list)
    {
        var data = new float[list.Count * PerInstance];
        for (int i = 0; i < list.Count; i++)
        {
            var (t, c) = list[i];
            int k = i * PerInstance;
            data[k] = t.Basis.X.X;
            data[k + 1] = t.Basis.Y.X;
            data[k + 2] = t.Basis.Z.X;
            data[k + 3] = t.Origin.X;
            data[k + 4] = t.Basis.X.Y;
            data[k + 5] = t.Basis.Y.Y;
            data[k + 6] = t.Basis.Z.Y;
            data[k + 7] = t.Origin.Y;
            data[k + 8] = t.Basis.X.Z;
            data[k + 9] = t.Basis.Y.Z;
            data[k + 10] = t.Basis.Z.Z;
            data[k + 11] = t.Origin.Z;
            data[k + 12] = c.R;
            data[k + 13] = c.G;
            data[k + 14] = c.B;
            data[k + 15] = c.A;
        }
        return data;
    }

    public IEnumerable<Drawn> Nodes(Material buildings, Material trees, float upm)
    {
        if (_mesh.Count > 0)
        {
            var mesh = new ArrayMesh();
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, _mesh.Arrays(), RaisedMesh.Format);
            yield return new Drawn(new MeshInstance3D { Mesh = mesh, Position = _origin, MaterialOverride = buildings, Name = "Built" }, 0, float.PositiveInfinity);
        }
        foreach (var (kind, list) in _trees)
            foreach (bool near in new[] { true, false })
            {
                var many = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = RaisedTrees.Mesh(kind, near), InstanceCount = list.Count };
                many.Buffer = Packed(list);
                var node = new MultiMeshInstance3D
                {
                    Multimesh = many, Position = _origin, MaterialOverride = trees, Name = "Trees",
                    CastShadow = near ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                yield return near ? new Drawn(node, 0, TreeNearM) : new Drawn(node, TreeNearM, TreeFarM);
            }
    }
}
