using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RaisedChunk
{
    public const float TreeNearM = 700, TreeFarM = 3500, FineM = 650, PlainFromM = Settings.RaisedDetailUsual;
    const float BlocksFromM = 2.5f;
    const float DrawbridgeM = 12;
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

    public Vector3 Origin => _origin;

    public IReadOnlyDictionary<TreeKind, List<(Transform3D At, Color Tint)>> Planted => _trees;

    public readonly record struct Drawn(GeometryInstance3D Node, float BeginM, float EndM, bool Handed = false, bool Stands = false);

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

    public void Add(BuiltPainter.Shape shape, Vector3[] world, string? style, Ground ground, float top, float upm, Vector3 across, float[]? gaps = null, float[]? opens = null,
        System.Func<Vector3, bool>? stands = null)
    {
        Prepare(shape, upm);
        float Height(Vector3 p) => top + (float)ground.DrawnAt(Coords.FromWorld(p));
        var flat = world.Select(p => p with { Y = 0 }).ToArray();
        if (shape.Kind == PartKinds.Block)
        {
            var floor = TableRaised.Footing(world, _mesh.PerMetre, out float bottom, shape.Foot);
            if (shape.Foot == (int)Footing.Stilts)
                foreach (var m in new[] { _mesh, _plain }) RaisedLines.Stilts(m, world, floor.Y);
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
            RaisedLines.Add(_mesh, shape, world, Height, style, stands);
            if (RaisedLines.HighM(shape, style) is >= BlocksFromM and var high && flat.Length >= 2)
                Blockers.Add((flat.Concat(flat.Reverse()).ToArray(), world.Max(Height) + high * _mesh.PerMetre));
            RaisedLines.Add(_plain, shape, world, Height, style);
        }
        else if (shape.Kind == PartKinds.Area)
        {
            RaisedAreas.Add(this, _mesh, shape, world, Height, style);
            if (shape.Class is Hallmarks.Theatre or "arena") RaisedAreas.Add(this, _plain, shape, world, Height, style);
        }
        else if (shape.Kind == PartKinds.Sign && shape.Class is "bridge" or BuiltShapes.Drawbridge) Bridge(shape, world, across, Height, style);
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

    void Bridge(BuiltPainter.Shape shape, Vector3[] world, Vector3 across, System.Func<Vector3, float> height, string? style)
    {
        var at = world[0];
        float upm = _mesh.PerMetre, span = world.Length >= 3 ? ((world[2] - world[1]) with { Y = 0 }).Length() / upm : 0;
        var axis = world.Length >= 3 ? world[2] - world[1] : across;
        if (shape.Class == BuiltShapes.Drawbridge)
        {
            var wood = new RaisedMesh.Paint(RaisedColours.Timber, RaisedCodes.Timber);
            RaisedArcades.Drawbridge(_mesh, at, axis, span > 0 ? span : DrawbridgeM, height, wood, new RaisedMesh.Paint(new Color(0.2f, 0.2f, 0.21f), RaisedCodes.Iron));
            return;
        }
        if (span <= 0)
        {
            RaisedProps.Add(_mesh, shape.Class, at, across, 0);
            RaisedProps.Add(_plain, shape.Class, at, across, 0);
            return;
        }
        int builders = RaisedLooks.Aged(style, shape.Built);
        var stone = new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Stone, builders, BuiltCanvas.Chanced(shape.Uv[0] * BuiltCanvas.KeyScale, 3.9f)), (int)Fabric.Stone);
        RaisedArcades.Bridge(_mesh, at, axis, span, height, stone);
        RaisedArcades.Bridge(_plain, at, axis, span, height, stone);
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

    public IEnumerable<Drawn> Nodes(Material buildings, Material plain, float upm)
    {
        if (_mesh.Count > 0)
        {
            var mesh = new ArrayMesh();
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, _mesh.Arrays(), RaisedMesh.Format);
            yield return new Drawn(new MeshInstance3D { Mesh = mesh, Position = _origin, MaterialOverride = buildings, Name = "Built" }, 0, PlainFromM, true, true);
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
    }
}
