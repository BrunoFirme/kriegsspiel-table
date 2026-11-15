using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class AvatarFigures
{
    public const long Self = -1;
    const double GoneAfterSeconds = 3;
    const float Follow = 14f, NameAbove = 1.12f, NamePixel = 0.0007f, StandInRadius = 0.13f;
    const string BodyPieceId = "walker-body";

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Dictionary<long, Figure> _figures = new();
    Node3D? _root;

    sealed class Figure
    {
        public required Node3D Node;
        public required Node3D Body;
        public required Label3D Name;
        public AvatarPose Goal;
        public Vector3 At;
        public float Yaw;
        public double Unseen;
        public int Slot;
        public string? Shape;
        public float Native;
    }

    public AvatarFigures(TableContext c, PieceLayer pieces)
    {
        _c = c;
        _pieces = pieces;
    }

    public int Count => _figures.Count;
    public IEnumerable<long> Peers() => _figures.Keys;
    public Vector3? At(long peer) => _figures.TryGetValue(peer, out var f) ? f.Node.GlobalPosition : null;
    public float HeightOf(long peer) => _figures.TryGetValue(peer, out var f) ? f.Goal.Height : AvatarPose.Usual;

    public IEnumerable<(long Peer, string Name)> Named() =>
        _figures.Where(p => p.Key != Self).Select(p => (p.Key, p.Value.Name.Text)).OrderBy(p => p.Text).ToList();

    public void Show(long peer, string name, AvatarPose pose, int slot, bool snap = false)
    {
        bool fresh = !_figures.TryGetValue(peer, out var f);
        if (!fresh && (f!.Slot != slot || f.Shape != pose.Body))
        {
            Forget(peer);
            fresh = true;
        }
        if (fresh)
        {
            f = Made(name, slot, pose.Body);
            _figures[peer] = f;
        }
        if (fresh || snap || f!.Goal.Ride != pose.Ride)
        {
            f!.At = pose.At;
            f.Yaw = pose.Yaw;
        }
        f.Goal = pose;
        f.Unseen = 0;
        if (f.Name.Text != name) f.Name.Text = name;
        if (fresh || snap) Place(f);
    }

    public IEnumerable<PieceNode> Rides()
    {
        foreach (var ride in _figures.Values.Select(f => f.Goal.Ride).OfType<string>().Distinct().ToList())
            if (_pieces.NodeOf(Layers.Umpire, ride) is { } node) yield return node;
    }

    public void Forget(long peer)
    {
        if (!_figures.Remove(peer, out var f)) return;
        f.Node.QueueFree();
    }

    public void Tick(double delta)
    {
        if (_figures.Count == 0) return;
        float ease = 1 - Mathf.Exp(-(float)delta * Follow);
        foreach (var (peer, f) in _figures.ToList())
        {
            f.Unseen += delta;
            if (f.Unseen > GoneAfterSeconds)
            {
                Forget(peer);
                continue;
            }
            f.At = f.At.Lerp(f.Goal.At, ease);
            f.Yaw = Mathf.LerpAngle(f.Yaw, f.Goal.Yaw, ease);
            Place(f);
        }
    }

    void Place(Figure f)
    {
        var space = Space(f.Goal.Ride);
        float h = f.Goal.Height;
        f.Node.Position = space * f.At;
        f.Node.Rotation = new Vector3(0, f.Yaw + space.Basis.GetEuler().Y, 0);
        if (f.Native <= 0) Measure(f);
        f.Body.Scale = Vector3.One * (f.Native > 0 ? h / f.Native : h);
        f.Name.Position = Vector3.Up * (h * NameAbove);
    }

    static void Measure(Figure f)
    {
        if (f.Body.GetChildCount() == 0 || f.Body.GetChild(0) is not Node3D shape || !shape.IsInsideTree()) return;
        var into = f.Body.GlobalTransform.AffineInverse();
        Aabb? box = null;
        foreach (var mesh in Meshes(shape))
        {
            var b = into * mesh.GlobalTransform * mesh.GetAabb();
            box = box is { } had ? had.Merge(b) : b;
        }
        if (box is not { Size.Y: > 1e-6f } found) return;
        f.Native = Mathf.Max(found.Size.Y, Mathf.Max(found.Size.X, found.Size.Z));
        var c = found.GetCenter();
        shape.Position -= new Vector3(c.X, found.Position.Y, c.Z);
    }

    static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if (node is MeshInstance3D { Mesh: not null, Visible: true } m) yield return m;
        foreach (var child in node.GetChildren())
            foreach (var inner in Meshes(child)) yield return inner;
    }

    Transform3D Space(string? ride) =>
        ride != null && _pieces.NodeOf(Layers.Umpire, ride) is { } node && AvatarSolids.Where(node) is { } at ? at : Transform3D.Identity;

    Figure Made(string name, int slot, string? shape)
    {
        if (_root == null)
        {
            _root = new Node3D { Name = "Avatars" };
            _c.Scene.AddChild(_root);
        }
        var colour = UmpireHue.OfSlot(slot);
        var node = new Node3D();
        var body = new Node3D { Name = "Body" };
        body.AddChild(Shape(shape, colour));
        node.AddChild(body);
        var label = new Label3D
        {
            Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 32, OutlineSize = 10, FixedSize = true, PixelSize = NamePixel,
            Modulate = colour.Lightened(0.35f), Layers = Table.LiveOnlyLayer,
        };
        node.AddChild(label);
        _root.AddChild(node);
        return new Figure { Node = node, Body = body, Name = label, Slot = slot, Shape = shape };
    }

    Node3D Shape(string? shape, Color colour)
    {
        if (AvatarBody.Piece(shape) is { } def && _c.S.Def(def) != null)
        {
            var piece = PieceNode.Create(_c.S, Layers.Umpire, new PieceInstance { Id = BodyPieceId, Def = def }, ghost: true, lettered: false);
            var turned = new Node3D();
            piece.Rotation = new Vector3(0, Mathf.Pi, 0);
            turned.AddChild(piece);
            return turned;
        }
        if (AvatarBody.Model(shape) is var (meshFile, textureFile))
            return Loaded(meshFile, textureFile);
        return new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = StandInRadius, Height = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.7f },
        };
    }

    Node3D Loaded(string meshFile, string? textureFile)
    {
        var assets = _c.S.Assets;
        var pack = UploadAsset.ReliefPackage;
        var material = new StandardMaterial3D { Roughness = 0.75f, VertexColorUseAsAlbedo = textureFile == null };
        var model = new MeshInstance3D { MaterialOverride = material };
        if (textureFile != null && assets.Texture(pack, textureFile, t => material.AlbedoTexture = t) is { } tex) material.AlbedoTexture = tex;
        assets.EnsureFile(pack, meshFile, () =>
        {
            if (GodotObject.IsInstanceValid(model)) model.Mesh = ModelCache.Get(assets.Abs(pack, meshFile));
        });
        return model;
    }
}
