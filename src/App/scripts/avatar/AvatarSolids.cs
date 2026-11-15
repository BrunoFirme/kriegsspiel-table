using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

sealed class AvatarSolids
{
    public const uint Layer = 1u << 20;
    const float ReachMetres = 150f, UnderMetres = 10f;
    const double LookEvery = 0.25, BuildMsAFrame = 3;
    const float SameSizeWithin = 0.001f;
    const int KeptShapes = 16;

    readonly PieceLayer _pieces;
    WalkSpace _space;
    readonly Node3D _root = new() { Name = "AvatarSolids", TopLevel = true };
    readonly Dictionary<ulong, Solid> _made = new();
    readonly Dictionary<ulong, Solid> _byBody = new();
    static readonly Dictionary<string, List<Shape3D>> Kept = new();
    static readonly List<string> KeptOrder = new();
    double _lookIn;

    sealed class Solid
    {
        public required StaticBody3D Body;
        public required PieceNode Node;
        public required ulong Mesh;
        public required Vector3 Size;
        public Task<List<Vector3[]>?>? Reading;
        public List<Shape3D>? Shapes;
        public int Built;
        public string? Key;
        public bool Done;
    }

    public AvatarSolids(Node3D scene, PieceLayer pieces, WalkSpace space)
    {
        _pieces = pieces;
        _space = space;
        scene.AddChild(_root);
    }

    public int Count => _made.Count;
    public bool Building => _made.Values.Any(s => !s.Done);

    public void Tick(double delta, Vector3 around)
    {
        _lookIn -= delta;
        if (_made.Values.Any(s => !GodotObject.IsInstanceValid(s.Node))) _lookIn = 0;
        if (_lookIn <= 0)
        {
            _lookIn = LookEvery;
            Look(around, now: false);
        }
        Build(BuildMsAFrame);
        foreach (var solid in _made.Values)
            if (Where(solid.Node) is { } at && _space.In(at) is var placed && !placed.IsEqualApprox(solid.Body.GlobalTransform)) solid.Body.GlobalTransform = placed;
    }

    public string? Ignore { get; set; }

    public void Respace(WalkSpace space)
    {
        _space = space;
        foreach (var solid in _made.Values)
            if (Where(solid.Node) is { } at) solid.Body.GlobalTransform = _space.In(at);
    }

    public PieceNode? Owner(StaticBody3D body) => _byBody.TryGetValue(body.GetInstanceId(), out var solid) ? solid.Node : null;

    public static Transform3D? Where(PieceNode node)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return null;
        if (!node.ShowsModel) return node.TableTransform() * new Transform3D(Basis.Identity, new Vector3(node.FootprintCentre.X, node.Thickness / 2, node.FootprintCentre.Y));
        var (pose, mesh) = PieceLights.ModelPose(node);
        return mesh == 0 ? null : new Transform3D(pose.Basis, WorldShift.ToTable(pose.Origin));
    }

    public void Look(Vector3 around, bool now = true)
    {
        var seen = new HashSet<ulong>();
        foreach (var node in _pieces.Nodes.Where(n => n.Piece.Id != Ignore).SelectMany(WithParts))
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.IsEffect || node.Ghost || node.IsDie) continue;
            var at = node.TablePosition();
            if (new Vector2(at.X - around.X, at.Z - around.Z).Length() > ReachMetres / _space.Scale + node.Footprint.Length()) continue;
            ulong id = node.GetInstanceId();
            var (pose, mesh) = PieceLights.ModelPose(node);
            if (node.ShowsModel && mesh == 0) continue;
            var size = node.ShowsModel ? Vector3.One : new Vector3(node.Footprint.X, node.Thickness, node.Footprint.Y);
            seen.Add(id);
            if (_made.TryGetValue(id, out var had))
            {
                if (had.Mesh == mesh && (had.Size - size).Length() <= SameSizeWithin * size.Length()) continue;
                Drop(id);
            }
            var solid = Made(node, mesh, size);
            _made[id] = solid;
            _byBody[solid.Body.GetInstanceId()] = solid;
        }
        foreach (var gone in _made.Keys.Where(k => !seen.Contains(k)).ToList()) Drop(gone);
        if (now) Build(double.PositiveInfinity, around);
    }

    static IEnumerable<PieceNode> WithParts(PieceNode node) => node.Parts.Where(p => p.ShowsModel && !p.IsEffect).SelectMany(WithParts).Prepend(node);

    void Drop(ulong id)
    {
        if (!_made.Remove(id, out var solid)) return;
        _byBody.Remove(solid.Body.GetInstanceId());
        solid.Body.CollisionLayer = 0;
        solid.Body.QueueFree();
    }

    Solid Made(PieceNode node, ulong mesh, Vector3 size)
    {
        var body = new StaticBody3D { CollisionLayer = Layer, CollisionMask = 0 };
        _root.AddChild(body);
        var solid = new Solid { Body = body, Node = node, Mesh = mesh, Size = size };
        if (Where(node) is { } at) body.GlobalTransform = _space.In(at);
        if (!node.ShowsModel)
        {
            if (size.X > 0 && size.Y > 0 && size.Z > 0) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            solid.Done = true;
            return solid;
        }
        if (node.Model?.Mesh is not { } drawn || ModelCache.PathOf(drawn) is not { } file)
        {
            if (PieceLights.ModelFaces(node) is { Length: >= 3 } faces) body.AddChild(new CollisionShape3D { Shape = Concave(faces) });
            solid.Done = true;
            return solid;
        }
        solid.Key = file;
        if (Kept.TryGetValue(file, out var shapes))
        {
            foreach (var shape in shapes) body.AddChild(new CollisionShape3D { Shape = shape });
            solid.Done = true;
            return solid;
        }
        solid.Reading = ModelTriangles.Of(file);
        solid.Shapes = new();
        return solid;
    }

    bool Under(PieceNode node, Vector3 around) =>
        new Vector2(node.TablePosition().X - around.X, node.TablePosition().Z - around.Z).Length() <= node.Footprint.Length() + UnderMetres / _space.Scale;

    void Build(double budgetMs, Vector3? only = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var solid in _made.Values)
        {
            if (solid.Done || solid.Reading is not { } reading || only is { } around && !Under(solid.Node, around)) continue;
            if (!reading.IsCompleted)
            {
                if (double.IsPositiveInfinity(budgetMs)) reading.Wait();
                else continue;
            }
            if (reading.Result is not { } pieces)
            {
                solid.Done = true;
                continue;
            }
            while (solid.Built < pieces.Count)
            {
                if (clock.Elapsed.TotalMilliseconds >= budgetMs) return;
                var shape = Concave(pieces[solid.Built++]);
                solid.Shapes!.Add(shape);
                solid.Body.AddChild(new CollisionShape3D { Shape = shape });
            }
            solid.Done = true;
            Keep(solid.Key!, solid.Shapes!);
        }
    }

    static Shape3D Concave(Vector3[] faces)
    {
        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces);
        return shape;
    }

    static void Keep(string key, List<Shape3D> shapes)
    {
        if (Kept.ContainsKey(key)) return;
        Kept[key] = shapes;
        KeptOrder.Add(key);
        if (KeptOrder.Count <= KeptShapes) return;
        Kept.Remove(KeptOrder[0]);
        KeptOrder.RemoveAt(0);
    }

    public void Clear()
    {
        foreach (var solid in _made.Values) solid.Body.CollisionLayer = 0;
        _made.Clear();
        _byBody.Clear();
        _root.QueueFree();
    }
}
