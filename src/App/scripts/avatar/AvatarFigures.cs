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
    const float LeanShare = 0.35f, LeanMost = 0.08f, LeanEase = 4f;
    const int NameFontPx = 32, NameEdgePx = 6;
    static readonly Color NameEdge = new(0, 0, 0, 0.75f);
    const float Follow = 14f, NameAbove = 1.08f, NamePixel = 0.0005f, NameLight = 0.4f, SpeedSeconds = 0.15f, CalmShare = 0.7f;

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Func<PieceNode, bool> _busy;
    public Func<Vector3, bool> CutAway { get; set; } = _ => false;
    readonly Dictionary<long, Figure> _figures = new();
    readonly Dictionary<string, Livery> _liveries = new();
    readonly ActedBodies _acted;
    Node3D? _root;

    sealed class Figure
    {
        public required Node3D Node;
        public required Label3D Name;
        public MeshInstance3D? StandIn;
        public PieceNode? Driven;
        public IFigureMotion? Gait;
        public AvatarPose Goal;
        public Vector3 At;
        public float Yaw;
        public double Unseen;
        public int Slot;
        public bool Hidden, Burst, Armed = true;
        public float Speed;
        public double SeenAt;
        public Vector3 Along, ShiftWas, LeanFrom;
        public float Lean;
        public GpuParticles3D? Trail;
    }

    public AvatarFigures(TableContext c, PieceLayer pieces, Func<PieceNode, bool> busy)
    {
        _c = c;
        _pieces = pieces;
        _busy = busy;
        _acted = new ActedBodies(c, pieces, Drives);
    }

    public IReadOnlyList<BodyOf> Bodies { get; set; } = [];
    public int Count => _figures.Count;
    public int Acted => _acted.Count;
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
        _acted.Tick(delta);
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
        f.Name.Visible = _c.Settings.NameTags && !CutAway(f.Node.GlobalPosition);
        var body = f.Goal.Body is { } id && _pieces.NodeOf(Layers.Umpire, id) is { } n && GodotObject.IsInstanceValid(n) ? n : null;
        if (body != f.Driven) Release(f);
        if (body != null && !_busy(body))
        {
            if (f.Driven == null)
            {
                body.BeginSettle();
                f.Driven = body;
                f.Gait = Rigged.Wants(body.Def) ? new Rigged(body, _c) : new Gait(body, _c);
            }
            body.ShowPose(Leaned(body.HandPose(Coords.FromWorld(feet), AvatarBody.RotOf(yaw), feet.Y), yaw, Lean(f, feet, h, delta)));
            body.Visible = !f.Hidden && !CutAway(body.GlobalPosition);
            if (f.Gait is Rigged rigged) (rigged.Style, rigged.Act) = (f.Goal.Gait, f.Goal.Act ?? body.Piece.Act);
            f.Gait?.Step(feet, h, delta);
        }
        else if (body != null) Release(f);
        StandIn(f, body == null, h);
        Fly(f, feet, h);
    }

    static float Lean(Figure f, Vector3 feet, float h, float delta)
    {
        float flat = new Vector2(feet.X - f.LeanFrom.X, feet.Z - f.LeanFrom.Z).Length();
        float grade = flat > h * 0.001f ? (feet.Y - f.LeanFrom.Y) / flat : 0, pace = delta > 0 ? flat / delta / h : 0;
        f.LeanFrom = feet;
        float want = flat < h ? Mathf.Clamp(grade * LeanShare, -LeanMost, LeanMost) * Mathf.Clamp(pace / Walker.RunHeights, 0, 1) : 0;
        f.Lean = Mathf.Lerp(f.Lean, want, 1 - Mathf.Exp(-delta * LeanEase));
        return f.Lean;
    }

    static KTable.Client.Hand.Pose Leaned(KTable.Client.Hand.Pose pose, float yaw, float lean)
    {
        if (Mathf.Abs(lean) < 1e-4f) return pose;
        var facing = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        var tilt = new Quaternion(Vector3.Up.Cross(facing).Normalized(), lean);
        var turned = tilt * new Quaternion(pose.Turn.X, pose.Turn.Y, pose.Turn.Z, pose.Turn.W);
        return pose with { Turn = new System.Numerics.Quaternion(turned.X, turned.Y, turned.Z, turned.W) };
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
            f.Trail = FlightFx.Trail(h);
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
        if (f.Gait is Rigged rigged)
        {
            rigged.Aloft = aloft;
            if (f.Burst && aloft) rigged.Pulse();
        }
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
        float t = Time.GetTicksMsec() / 1000f;
        f.StandIn.Position = Vector3.Up * (h * (SoulFloat + SoulBob * Mathf.Sin(t * SoulPace + f.Slot)));
        for (int i = 0; i < f.StandIn.GetChildCount(); i++)
        {
            if (f.StandIn.GetChild(i) is not MeshInstance3D { MaterialOverride: StandardMaterial3D haze } wave) continue;
            float beat = Mathf.PosMod(t * SoulBeat + (float)i / f.StandIn.GetChildCount(), 1);
            wave.Scale = Vector3.One * Mathf.Lerp(1, SoulReach, beat);
            haze.AlbedoColor = haze.AlbedoColor with { A = SoulHaze * (1 - beat) * (1 - beat) };
        }
    }

    readonly SphereMesh _soulShape = new() { Radius = SoulRadius, Height = SoulRadius * 2, RadialSegments = 24, Rings = 12 };
    const float SoulRadius = 0.055f, SoulFloat = 0.72f, SoulBob = 0.035f, SoulPace = 1.7f, SoulBeat = 0.6f, SoulReach = 5f, SoulHaze = 0.3f;
    const int SoulWaves = 3;
    readonly Dictionary<int, StandardMaterial3D> _soulLooks = new();

    StandardMaterial3D SoulLook(int slot) => _soulLooks.TryGetValue(slot, out var look) ? look : _soulLooks[slot] = FlightFx.Glow(UmpireHue.OfSlot(slot).Lightened(0.2f));

    public Node3D Sample() => Soul(0);

    MeshInstance3D Soul(int slot)
    {
        var soul = new MeshInstance3D { Mesh = _soulShape, MaterialOverride = SoulLook(slot), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        for (int i = 0; i < SoulWaves; i++)
            soul.AddChild(new MeshInstance3D
            {
                Mesh = _soulShape, MaterialOverride = FlightFx.Glow(UmpireHue.OfSlot(slot) with { A = 0 }), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        return soul;
    }

    MeshInstance3D MadeStandIn(Figure f)
    {
        var standIn = Soul(f.Slot);
        f.Node.AddChild(standIn);
        return standIn;
    }

    public Vector3 FeetOf(AvatarPose pose) => Space(pose.Ride) * pose.At;

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
            Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = true, PixelSize = NamePixel, Layers = Table.LiveOnlyLayer,
            Font = Ui.TableSans(), FontSize = NameFontPx, OutlineSize = NameEdgePx, Modulate = colour.Lightened(NameLight), OutlineModulate = NameEdge,
        };
        node.AddChild(label);
        _root.AddChild(node);
        return new Figure { Node = node, Name = label, Slot = slot };
    }
}
