using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RaisedChunk
{
    public const float TreeNearM = 700, TreeFarM = 3500, FineM = 650, PlainFromM = 800;
    const float MostSpanM = 160, DrawbridgeM = 12;
    const float TreePick = 15.5f, TintSpread = 0.22f, CrownGrown = 1.3f, PropPick = 61.3f, ShiftPick = 53.1f, ShiftM = 7, PropWeather = 0.5f;

    readonly Vector3 _origin;
    readonly RaisedMesh _mesh, _plain;
    readonly Dictionary<TreeKind, List<(Transform3D At, Color Tint)>> _trees = new();

    public int Buildings { get; private set; }

    public static float SnowY { get; set; } = float.PositiveInfinity;

    public List<(Vector3[] Ring, float Top)> Blockers { get; } = new();

    public int Trees => _trees.Values.Sum(t => t.Count);

    public int NearTriangles => _mesh.Triangles + (_mesh.Fine?.Triangles ?? 0);

    public int PlainTriangles => _plain.Triangles;

    public IEnumerable<Vector3> TreesAt => _trees.Values.SelectMany(list => list.Select(t => t.At.Origin + _origin));

    public IReadOnlyList<Vector3> Built => _mesh.Points;

    public bool Grows(TreeKind kind) => _trees.ContainsKey(kind);

    public readonly record struct Drawn(GeometryInstance3D Node, float BeginM, float EndM, bool Handed = false);

    public RaisedChunk(Vector3 origin)
    {
        _origin = origin;
        _mesh = new RaisedMesh { Origin = origin, Fine = new RaisedMesh { Origin = origin }, Smokes = new() };
        _plain = new RaisedMesh { Origin = origin, Plain = true };
    }

    void Prepare(BuiltPainter.Shape shape, float upm)
    {
        var key = shape.Uv[0] * BuiltCanvas.KeyScale;
        foreach (var m in new[] { _mesh, _plain })
        {
            m.PerMetre = shape.PerMetre > 0 ? shape.PerMetre : upm;
            m.Shift = new Vector2(BuiltCanvas.Chanced(key, ShiftPick), BuiltCanvas.Chanced(key, ShiftPick + 1)) * ShiftM;
            m.Weather = PropWeather;
            m.OpenFor = null;
            m.FloorY = float.NaN;
            m.Scorch = 0;
        }
    }

    public void Lamp(int kind, Vector3 at, float upm, float pick)
    {
        _mesh.PerMetre = upm;
        _mesh.Shift = Vector2.Zero;
        _mesh.Weather = PropWeather;
        _mesh.Scorch = 0;
        RaisedProps.Lamp(_mesh.Small, kind, at, pick);
    }

    static float Span(Ground ground, Vector3 at, Vector3 axis, float upm)
    {
        if (ground.KindAt(Coords.FromWorld(at)) != GroundKinds.Water) return 0;
        var flat = (axis with { Y = 0 }).Normalized();
        float Reach(int way)
        {
            for (float d = 1; d < MostSpanM; d++)
                if (ground.KindAt(Coords.FromWorld(at + flat * (way * d * upm))) != GroundKinds.Water) return d;
            return MostSpanM;
        }
        return Reach(1) + Reach(-1);
    }

    public void Add(BuiltPainter.Shape shape, Vector3[] world, string? style, Ground ground, float top, float upm, Vector3 across, float[]? gaps = null, float[]? opens = null,
        bool gated = false)
    {
        Prepare(shape, upm);
        float Height(Vector3 p) => top + (float)ground.DrawnAt(Coords.FromWorld(p));
        var flat = world.Select(p => p with { Y = 0 }).ToArray();
        if (shape.Kind == PartKinds.Block)
        {
            var floor = TableRaised.Footing(world, _mesh.PerMetre, out float bottom);
            int from = _mesh.Count;
            var site = new RaisedBuildings.Site(flat, floor.Y, bottom, style, gaps, opens);
            RaisedBuildings.Add(_mesh, shape, site);
            RaisedBuildings.Add(_plain, shape, site);
            float roof = float.NegativeInfinity;
            for (int i = from; i < _mesh.Count; i++) roof = Mathf.Max(roof, _mesh.Points[i].Y + _origin.Y);
            if (flat.Length >= 3 && roof > float.NegativeInfinity) Blockers.Add((flat, roof));
            Buildings++;
        }
        else if (shape.Kind == PartKinds.Line)
        {
            RaisedLines.Add(_mesh, shape, world, Height, style);
            RaisedLines.Add(_plain, shape, world, Height, style);
        }
        else if (shape.Kind == PartKinds.Area)
        {
            RaisedAreas.Add(this, _mesh, shape, world, Height, style);
            if (shape.Class is Hallmarks.Theatre or "arena") RaisedAreas.Add(this, _plain, shape, world, Height, style);
        }
        else if (shape.Kind == PartKinds.Sign && shape.Class == "bridge") Bridge(shape, world[0], across, ground, Height, style, gated);
        else if (shape.Kind == PartKinds.Sign && shape.Class == "tower")
        {
            int builders = RaisedLooks.Aged(style, shape.Built);
            float pick = BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, PropPick);
            RaisedTowers.Add(_mesh, builders, world[0], across, pick);
            RaisedTowers.Add(_plain, builders, world[0], across, pick);
        }
        else if (shape.Kind == PartKinds.Sign && shape.Class == "tree")
        {
            float pick = BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, TreePick);
            var kind = RaisedForest.TownKind(RaisedLooks.Aged(style, shape.Manner), pick);
            Tree(world[0], kind, RaisedForest.TownCrownM(BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, 5.5f)) * _mesh.PerMetre, pick);
        }
        else if (shape.Kind == PartKinds.Sign) RaisedProps.Add(_mesh.Small, shape.Class, world[0], across, BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, PropPick));
    }

    void Bridge(BuiltPainter.Shape shape, Vector3 at, Vector3 across, Ground ground, System.Func<Vector3, float> height, string? style, bool gated)
    {
        float span = Span(ground, at, across, _mesh.PerMetre);
        int builders = RaisedLooks.Aged(style, shape.Built);
        var stone = new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Stone, builders, BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, 3.9f)), (int)Fabric.Stone);
        if (gated)
        {
            var wood = new RaisedMesh.Paint(RaisedColours.Timber, RaisedCodes.Timber);
            RaisedArcades.Drawbridge(_mesh, at, across, span > 0 ? span : DrawbridgeM, height, wood, new RaisedMesh.Paint(new Color(0.2f, 0.2f, 0.21f), RaisedCodes.Iron));
            return;
        }
        if (span <= 0)
        {
            RaisedProps.Add(_mesh, shape.Class, at, across, 0);
            RaisedProps.Add(_plain, shape.Class, at, across, 0);
            return;
        }
        RaisedArcades.Bridge(_mesh, at, across, span, height, stone);
        RaisedArcades.Bridge(_plain, at, across, span, height, stone);
    }

    public void Tree(Vector3 at, TreeKind kind, float crown, float pick)
    {
        if (at.Y > SnowY && kind is TreeKind.Broad or TreeKind.Fruit or TreeKind.Birch) kind = TreeKind.Bare;
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

    public IEnumerable<Drawn> Nodes(Material buildings, Material plain, Material trees, float upm)
    {
        if (_mesh.Count > 0)
        {
            var mesh = new ArrayMesh();
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, _mesh.Arrays(), RaisedMesh.Format);
            yield return new Drawn(new MeshInstance3D { Mesh = mesh, Position = _origin, MaterialOverride = buildings, Name = "Built" }, 0, PlainFromM, true);
        }
        if (_plain.Count > 0)
        {
            var mesh = new ArrayMesh();
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, _plain.Arrays(), RaisedMesh.Format);
            yield return new Drawn(new MeshInstance3D { Mesh = mesh, Position = _origin, MaterialOverride = plain, Name = "Plain" }, PlainFromM, float.PositiveInfinity, true);
        }
        if (_mesh.Smokes is { Count: > 0 } smokes) yield return new Drawn(RaisedSmoke.Node(smokes, _origin, upm), 0, RaisedSmoke.ReachM);
        if (_mesh.Fine is { Count: > 0 } fine)
        {
            var mesh = new ArrayMesh();
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, fine.Arrays(), RaisedMesh.Format);
            var node = new MeshInstance3D { Mesh = mesh, Position = _origin, MaterialOverride = buildings, Name = "Fine", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            yield return new Drawn(node, 0, FineM);
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
