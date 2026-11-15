using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RaisedChunk
{
    public const float TreeNearM = 700, TreeFarM = 3500;
    const float TreePick = 15.5f, TintSpread = 0.22f, CrownGrown = 1.3f;

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

    public void Add(BuiltPainter.Shape shape, Vector3[] world, string? style, Ground ground, float top, float upm)
    {
        _mesh.PerMetre = shape.PerMetre > 0 ? shape.PerMetre : upm;
        var flat = world.Select(p => p with { Y = 0 }).ToArray();
        if (shape.Kind == PartKinds.Block)
        {
            var floor = TableRaised.Footing(world, _mesh.PerMetre, out float bottom);
            RaisedBuildings.Add(_mesh, shape, new RaisedBuildings.Site(flat, floor.Y, bottom, style));
            Buildings++;
        }
        else if (shape.Kind == PartKinds.Line) RaisedLines.Add(_mesh, shape, world, p => top + (float)ground.DrawnAt(Coords.FromWorld(p)), style);
        else if (shape.Kind == PartKinds.Sign && shape.Class == "tree")
        {
            float pick = BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, TreePick);
            var kind = RaisedForest.TownKind(RaisedLooks.Aged(style, shape.Manner), pick);
            Tree(world[0], kind, RaisedForest.TownCrownM(BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, 5.5f)) * _mesh.PerMetre, pick);
        }
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
                for (int i = 0; i < list.Count; i++)
                {
                    many.SetInstanceTransform(i, list[i].At);
                    many.SetInstanceColor(i, list[i].Tint);
                }
                var node = new MultiMeshInstance3D
                {
                    Multimesh = many, Position = _origin, MaterialOverride = trees, Name = "Trees",
                    CastShadow = near ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                yield return near ? new Drawn(node, 0, TreeNearM) : new Drawn(node, TreeNearM, TreeFarM);
            }
    }
}
