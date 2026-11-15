using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class AvatarFigures
{
    public const long Self = -1;
    const double GoneAfterSeconds = 3;
    const float Follow = 14f, NameAbove = 1.12f, NamePixel = 0.0007f, StandInRadius = 0.13f, StandInShare = 1f, SpeedSeconds = 0.15f, CalmShare = 0.7f;

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Func<PieceNode, bool> _busy;
    public Func<Vector3, bool> CutAway { get; set; } = _ => false;
    readonly Dictionary<long, Figure> _figures = new();
    readonly Dictionary<string, Livery> _liveries = new();
    Node3D? _root;

    sealed class Figure
    {
        public required Node3D Node;
        public required Label3D Name;
        public MeshInstance3D? StandIn;
        public PieceNode? Driven;
        public Gait? Gait;
        public AvatarPose Goal;
        public Vector3 At;
        public float Yaw;
        public double Unseen;
        public int Slot;
        public bool Hidden, Burst, Armed = true;
        public float Speed;
        public double SeenAt;
        public Vector3 Along, ShiftWas;
        public GpuParticles3D? Trail;
    }

    public AvatarFigures(TableContext c, PieceLayer pieces, Func<PieceNode, bool> busy)
    {
        _c = c;
        _pieces = pieces;
        _busy = busy;
    }

    public IReadOnlyList<BodyOf> Bodies { get; set; } = [];
    public int Count => _figures.Count;
    public IEnumerable<long> Peers() => _figures.Keys;
    public Vector3? At(long peer) => _figures.TryGetValue(peer, out var f) ? f.Node.TablePosition() : null;
    public float HeightOf(long peer) => _figures.TryGetValue(peer, out var f) ? f.Goal.Height : AvatarPose.Usual;
    public bool Drives(PieceNode n) => _figures.Values.Any(f => f.Driven == n);

    public BodyOf? OwnerOf(string pieceId) => Bodies.FirstOrDefault(b => b.Piece == pieceId) is { Piece: not null } b ? b : null;

    public IEnumerable<(long Peer, string Name)> Named() =>
        _figures.Where(p => p.Key != Self).Select(p => (p.Key, p.Value.Name.Text)).OrderBy(p => p.Text).ToList();

    public void Burst(long peer)
    {
        if (_figures.TryGetValue(peer, out var f)) f.Burst = true;
    }

    public void Show(long peer, string name, AvatarPose pose, int slot, bool snap = false, bool hidden = false, float? speed = null)
    {
        bool fresh = !_figures.TryGetValue(peer, out var f);
        if (!fresh && f!.Slot != slot)
        {
            Forget(peer);
            fresh = true;
        }
        if (fresh)
        {
            f = Made(name, slot);
            _figures[peer] = f;
        }
        if (fresh || snap || f!.Goal.Ride != pose.Ride)
        {
            f!.At = pose.At;
            f.Yaw = pose.Yaw;
        }
        if (speed is { } known) f.Speed = known;
        else Clock(f, pose, fresh);
        f.Goal = pose;
        f.Unseen = 0;
        f.Hidden = hidden;
        if (f.Name.Text != name) f.Name.Text = name;
        if (fresh || snap) Place(f, 0);
    }

    public IEnumerable<PieceNode> Rides()
    {
        foreach (var ride in _figures.Values.Select(f => f.Goal.Ride).OfType<string>().Distinct().ToList())
            if (_pieces.NodeOf(Layers.Umpire, ride) is { } node) yield return node;
    }

    public void Forget(long peer)
    {
        if (!_figures.Remove(peer, out var f)) return;
        Release(f);
        f.Node.QueueFree();
    }

    public void Tick(double delta)
    {
        Dress();
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
            Place(f, (float)delta);
        }
    }

    void Dress()
    {
        foreach (var bare in _liveries.Keys.Where(id => !Bodies.Any(b => b.Piece == id && b.Slot >= 0)).ToList())
        {
            _liveries[bare].Restore();
            _liveries.Remove(bare);
        }
        foreach (var b in Bodies)
        {
            if (b.Slot < 0 || _pieces.NodeOf(Layers.Umpire, b.Piece) is not { } node || !GodotObject.IsInstanceValid(node)) continue;
            if (!_liveries.TryGetValue(b.Piece, out var dye) || !dye.On(node)) _liveries[b.Piece] = dye = new Livery(node, _c);
            dye.Wear(UmpireHue.OfSlot(b.Slot));
        }
    }

    void Place(Figure f, float delta)
    {
        var space = Space(f.Goal.Ride);
        float h = f.Goal.Height, yaw = f.Yaw + space.Basis.GetEuler().Y;
        var feet = space * f.At;
        f.Node.Position = feet;
        f.Node.Rotation = new Vector3(0, yaw, 0);
        f.Name.Position = Vector3.Up * (h * NameAbove);
        f.Name.Visible = !CutAway(f.Node.GlobalPosition);
        var body = f.Goal.Body is { } id && _pieces.NodeOf(Layers.Umpire, id) is { } n && GodotObject.IsInstanceValid(n) ? n : null;
        if (body != f.Driven) Release(f);
        if (body != null && !_busy(body))
        {
            if (f.Driven == null)
            {
                body.BeginSettle();
                f.Driven = body;
                f.Gait = new Gait(body, _c);
            }
            body.ShowPose(body.HandPose(Coords.FromWorld(feet), AvatarBody.RotOf(yaw), feet.Y));
            body.Visible = !f.Hidden && !CutAway(body.GlobalPosition);
            f.Gait?.Step(feet, h, delta);
        }
        else if (body != null) Release(f);
        StandIn(f, body == null, h);
        Fly(f, feet, h);
    }

    static void Clock(Figure f, AvatarPose pose, bool fresh)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (!fresh && now > f.SeenAt && f.Goal.Ride == pose.Ride)
        {
            var moved = pose.At - f.Goal.At;
            float fast = FlightFx.BurstFrom * pose.Height;
            bool calm = f.Speed < fast * CalmShare;
            float seconds = (float)(now - f.SeenAt);
            f.Speed = Mathf.Lerp(f.Speed, moved.Length() / seconds, 1 - Mathf.Exp(-seconds / SpeedSeconds));
            f.Along = moved;
            if (calm) f.Armed = true;
            if (f.Armed && f.Speed > fast) (f.Burst, f.Armed) = (true, false);
        }
        f.SeenAt = now;
    }

    void Fly(Figure f, Vector3 feet, float h)
    {
        bool aloft = feet.Y - (float)_c.S.Ground.At(Coords.FromWorld(feet)) > h * FlightFx.AirShare;
        bool streams = !f.Hidden && aloft && f.Speed > h * FlightFx.TrailFrom;
        if (streams && f.Trail == null)
        {
            f.Trail = FlightFx.Trail(UmpireHue.OfSlot(f.Slot), h);
            f.Node.AddChild(f.Trail);
            f.Trail.Position = Vector3.Up * (h / 2);
        }
        if (f.Trail != null)
        {
            if (WorldShift.By != f.ShiftWas)
            {
                f.ShiftWas = WorldShift.By;
                f.Trail.Restart();
            }
            f.Trail.Emitting = streams;
        }
        if (f.Burst && !f.Hidden && aloft && _root != null) FlightFx.Ring(_root, feet + Vector3.Up * (h / 2), f.Along, UmpireHue.OfSlot(f.Slot), h);
        f.Burst = false;
    }

    void Release(Figure f)
    {
        if (f.Driven is not { } was) return;
        f.Driven = null;
        f.Gait?.Stop();
        f.Gait = null;
        if (!GodotObject.IsInstanceValid(was)) return;
        was.Visible = true;
        was.EndSettle();
        was.Refresh(was.Piece);
    }

    void StandIn(Figure f, bool wanted, float h)
    {
        if (!wanted)
        {
            if (f.StandIn != null) f.StandIn.Visible = false;
            return;
        }
        f.StandIn ??= MadeStandIn(f);
        f.StandIn.Visible = !f.Hidden && !CutAway(f.Node.GlobalPosition);
        f.StandIn.Scale = Vector3.One * h;
        f.StandIn.Position = Vector3.Up * (h * StandInShare / 2);
    }

    MeshInstance3D MadeStandIn(Figure f)
    {
        var standIn = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = StandInRadius, Height = StandInShare },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = UmpireHue.OfSlot(f.Slot), Roughness = 0.7f },
        };
        f.Node.AddChild(standIn);
        return standIn;
    }

    Transform3D Space(string? ride) =>
        ride != null && _pieces.NodeOf(Layers.Umpire, ride) is { } node && AvatarSolids.Where(node) is { } at ? at : Transform3D.Identity;

    Figure Made(string name, int slot)
    {
        if (_root == null)
        {
            _root = new Node3D { Name = "Avatars" };
            _c.Scene.AddChild(_root);
        }
        var colour = UmpireHue.OfSlot(slot);
        var node = new Node3D();
        var label = new Label3D
        {
            Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 32, OutlineSize = 10, FixedSize = true, PixelSize = NamePixel,
            Modulate = colour.Lightened(0.35f), Layers = Table.LiveOnlyLayer,
        };
        node.AddChild(label);
        _root.AddChild(node);
        return new Figure { Node = node, Name = label, Slot = slot };
    }
}
