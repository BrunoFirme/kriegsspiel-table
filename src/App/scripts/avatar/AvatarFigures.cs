using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class AvatarFigures
{
    public const long Self = -1;
    const string ModelPath = "res://avatar/Knight.glb";
    const double GoneAfterSeconds = 3;
    const float Follow = 14f, MovingHeights = 0.15f, RunningHeights = 3f, NameAbove = 1.12f, NamePixel = 0.004f;
    static readonly string[] Idle = ["Idle"], Walk = ["Walking_A", "Walking_B"], Run = ["Running_A", "Running_B"];

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Dictionary<long, Figure> _figures = new();
    Node3D? _root;
    PackedScene? _model;
    bool _looked;

    sealed class Figure
    {
        public required Node3D Node;
        public required Node3D Body;
        public required Label3D Name;
        public AnimationPlayer? Moves;
        public float Tall = 1;
        public AvatarPose Goal;
        public Vector3 At;
        public float Yaw;
        public double Unseen;
        public string Playing = "";
    }

    public AvatarFigures(TableContext c, PieceLayer pieces)
    {
        _c = c;
        _pieces = pieces;
    }

    public int Count => _figures.Count;
    public IEnumerable<long> Peers() => _figures.Keys;
    public Vector3? At(long peer) => _figures.TryGetValue(peer, out var f) ? f.Node.GlobalPosition : null;

    public void Show(long peer, string name, AvatarPose pose, bool snap = false)
    {
        bool fresh = !_figures.TryGetValue(peer, out var f);
        if (fresh)
        {
            f = Made(name);
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
        if (fresh || snap) Place(f, 0, pose.At);
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
        float dt = (float)delta, ease = 1 - Mathf.Exp(-dt * Follow);
        foreach (var (peer, f) in _figures.ToList())
        {
            f.Unseen += delta;
            if (f.Unseen > GoneAfterSeconds)
            {
                Forget(peer);
                continue;
            }
            var was = f.At;
            f.At = was.Lerp(f.Goal.At, ease);
            f.Yaw = Mathf.LerpAngle(f.Yaw, f.Goal.Yaw, ease);
            Place(f, dt, was);
        }
    }

    void Place(Figure f, float dt, Vector3 was)
    {
        var space = Space(f.Goal.Ride);
        f.Node.Position = space * f.At;
        f.Node.Rotation = new Vector3(0, f.Yaw + space.Basis.GetEuler().Y, 0);
        f.Body.Scale = Vector3.One * (f.Goal.Height / f.Tall);
        f.Name.Position = Vector3.Up * (f.Goal.Height * NameAbove);
        f.Name.PixelSize = NamePixel * f.Goal.Height;
        float speed = dt > 0 ? (f.At - was).Length() / dt / f.Goal.Height : 0;
        Play(f, speed > RunningHeights ? Run : speed > MovingHeights ? Walk : Idle);
    }

    Transform3D Space(string? ride) =>
        ride != null && _pieces.NodeOf(Layers.Umpire, ride) is { } node && AvatarSolids.Where(node) is { } at ? at : Transform3D.Identity;

    static void Play(Figure f, string[] names)
    {
        if (f.Moves is not { } moves || names.FirstOrDefault(n => moves.HasAnimation(n)) is not { } name || f.Playing == name) return;
        f.Playing = name;
        moves.Play(name, 0.2);
    }

    Figure Made(string name)
    {
        if (_root == null)
        {
            _root = new Node3D { Name = "Avatars" };
            _c.Scene.AddChild(_root);
        }
        if (!_looked)
        {
            _looked = true;
            _model = ResourceLoader.Exists(ModelPath) ? GD.Load<PackedScene>(ModelPath) : null;
        }
        var node = new Node3D();
        var (body, tall) = _model?.Instantiate() is Node3D made ? (made, Tallness(made)) : StandIn();
        node.AddChild(body);
        var label = new Label3D
        {
            Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 48, OutlineSize = 12,
            Layers = Table.LiveOnlyLayer,
        };
        node.AddChild(label);
        _root.AddChild(node);
        var moves = body.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        if (moves != null)
            foreach (var loop in Idle.Concat(Walk).Concat(Run).Where(n => moves.HasAnimation(n))) moves.GetAnimation(loop).LoopMode = Animation.LoopModeEnum.Linear;
        return new Figure { Node = node, Body = body, Name = label, Moves = moves, Tall = tall };
    }

    static float Tallness(Node3D body)
    {
        float top = 0;
        foreach (var mesh in body.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
            top = Mathf.Max(top, mesh.GetAabb().End.Y * mesh.Scale.Y + mesh.Position.Y);
        return top > 0 ? top : 1;
    }

    static (Node3D, float) StandIn()
    {
        var body = new Node3D();
        body.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.2f, Height = 1f }, Position = Vector3.Up * 0.5f });
        return (body, 1f);
    }
}
