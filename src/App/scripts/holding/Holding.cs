using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;
using Held = KTable.Client.Hand.HeldPiece<KTable.App.PieceNode>;

namespace KTable.App;

sealed partial class Carry : IHeldPieces
{
    readonly TableContext _c;
    readonly PointerPress _press;
    readonly PieceLayer _pieces;
    readonly Selection _selection;
    readonly PendingPoses _poses;
    readonly Footing<PieceNode> _footing;
    readonly Sticking<PieceNode> _sticking;

    public Carry(TableContext c, PointerPress press, PieceLayer pieces, Selection selection, PendingPoses poses)
    {
        _c = c;
        _press = press;
        _pieces = pieces;
        _selection = selection;
        _poses = poses;
        _footing = new Footing<PieceNode>(p => _pieces.Grid.Near(p), n => _heldNodes.Contains(n), () => _c.S.Ground);
        _sticking = new Sticking<PieceNode>(p => _pieces.Grid.Near(p), _pieces.NodeOf, l => _c.S.View.Layer(l), _footing);
    }

    Dice _dice = null!;
    public void Connect(Dice dice) => _dice = dice;

    readonly List<Held> _held = new();
    readonly HashSet<PieceNode> _heldNodes = new();
    Vec2 _cursor;
    float _floor = -1;
    public const float LiftHeight = 2.4f;

    public bool Holding => _held.Count > 0;
    public int Count => _held.Count;
    public IEnumerable<string> HeldIds => _held.Select(h => h.Id);
    public bool Has(PieceNode n) => _heldNodes.Contains(n);
    public HandAt At => new((float)_cursor.X, (float)_cursor.Z, Math.Max(0, _floor) + _lift);

    public IReadOnlyList<CarriedPiece> Shared() =>
        _held.Where(h => GodotObject.IsInstanceValid(h.Node)).Select(h => new CarriedPiece(h.Layer, h.Id, (float)h.Offset.X, (float)h.Offset.Z, (float)h.Rot, h.Lift)).ToList();

    public void Start(Vec2 grab) => Take(grab, _selection.Movable.ToList());

    void Take(Vec2 grab, List<PieceNode> nodes)
    {
        EndMarch();
        _held.Clear();
        _heldNodes.Clear();
        _trail.Clear();
        StopShake();
        _floor = -1;
        foreach (var n in nodes)
        {
            var c = Drawn(n);
            n.Held = true;
            n.TakenInHand();
            _held.Add(new Held { Node = n, Layer = n.Layer, Id = n.Piece.Id, Offset = c.pos - grab, Rot = c.rot });
            _heldNodes.Add(n);
        }
        TakeRiders(grab);
        _c.Sound.Lift(_heldNodes);
        var s = _c.S;
        foreach (var h in _held.ToList())
            if (_held.Contains(h) && s.CanTouch(h.Layer, h.Node.Piece) && (s.View.Script != null || h.Node.Piece.Script != null || h.Node.Def?.Script != null))
                s.Do(new HoldPiece { Layer = h.Layer, Id = h.Id, Held = true });
        _poses.Flush();
        _cursor = grab;
        Move(grab);
    }

    void TakeRiders(Vec2 grab)
    {
        void Ride(Held under, PieceNode cn)
        {
            var cc = Drawn(cn);
            cn.Held = true;
            cn.TakenInHand();
            _held.Add(new Held { Node = cn, Layer = cn.Layer, Id = cn.Piece.Id, Offset = cc.pos - grab, Rot = cc.rot, Lift = Math.Max(0, cn.RestY - under.Node.RestY), Parent = under });
            _heldNodes.Add(cn);
        }
        for (int i = 0; i < _held.Count; i++)
        {
            var h = _held[i];
            foreach (var cid in h.Node.Piece.Attached)
                if (_pieces.NodeOf(h.Node.Layer, cid) is { } cn && !_heldNodes.Contains(cn) && _c.LayerInteractive(cn.Layer)) Ride(h, cn);
            if (h.Node.Piece.NotSticky) continue;
            foreach (var cn in RestingOn(h.Node)) if (!_heldNodes.Contains(cn)) Ride(h, cn);
        }
    }

    List<PieceNode> RestingOn(PieceNode under)
    {
        float reach = Mathf.Max(under.Footprint.X, under.Footprint.Y) * 0.75f + 1;
        return _footing.RestingOn(under, _pieces.Grid.NearAny(new[] { under.Position with { Y = 0 } }, reach), _c.LayerInteractive);
    }

    (Vec2 pos, double rot) Drawn(PieceNode n)
    {
        if (n.Rolling) return (Coords.FromWorld(n.ModelWorldCenter), Table.Norm360(180 - Mathf.RadToDeg(n.Rotation.Y)));
        if (!n.Placed) { var c = _poses.Current(n); return (c.pos, c.rot); }
        return (Coords.FromWorld(n.Position), Table.Norm360(180 - Mathf.RadToDeg(n.Heading)));
    }

    public void Move(Vec2 cursor)
    {
        bool moved = _floor < 0 || (cursor - _cursor).Length > 1e-9;
        _cursor = cursor;
        if (moved)
        {
            TrailAdd(cursor);
            _floor = _held.Select(h => SurfaceY(cursor + h.Offset, h.Node.Layer, h.Node)).DefaultIfEmpty(0).Max();
        }
        PlaceHeld();
    }

    readonly HashSet<string> _measured = new();

    public void DrawMeasures(ToolOverlay overlay)
    {
        foreach (var id in _measured) _c.Labels.Remove($"moved:{id}");
        _measured.Clear();
        if (Marching) return;
        var mat = new StandardMaterial3D { AlbedoColor = MeasureColour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        var cal = _c.S.View.Calibration;
        foreach (var h in _held.Where(h => h.Parent == null && GodotObject.IsInstanceValid(h.Node) && h.Node.Piece.MeasuresMoves))
        {
            Vec2 from = h.Node.Piece.Pos, to = _cursor + h.Offset;
            const float Above = 0.3f;
            overlay.SegPx(overlay.Mesh, mat, from, to, Above);
            var mid = new Vec2((from.X + to.X) / 2, (from.Z + to.Z) / 2);
            _c.Labels.Set($"moved:{h.Id}", Coords.ToWorld(mid, Above + (float)_c.S.Ground.At(mid)), Fmt.Distance((to - from).Length, cal), MeasureText, 14,
                new Vector2(0, -16), centered: true);
            _measured.Add(h.Id);
        }
    }

    static readonly Color MeasureColour = new(1f, 0.42f, 0.28f), MeasureText = new(1f, 0.95f, 0.85f);

    void PlaceHeld()
    {
        foreach (var h in _held)
            if (GodotObject.IsInstanceValid(h.Node))
            {
                var (pos, scale, y) = Pulled(_cursor + h.Offset, h.Node.Piece.Scale, Math.Max(0, _floor) + _lift + h.Lift);
                h.Node.Preview(pos, h.Rot, scale, y);
            }
    }

    public void Tick(bool overUi)
    {
        if (_held.Count == 0) return;
        if (Marching) { MarchOn(); return; }
        var m = Pointer.In(_c.Scene.GetViewport());
        bool down = _fromDrawer != null ? Input.IsMouseButtonPressed(MouseButton.Left) : _press.LeftDown;
        bool over = _fromDrawer != null ? _fromDrawer(m) : overUi;
        if (down && !over) ShakeAt(Time.GetTicksMsec() / 1000.0, new System.Numerics.Vector2(m.X, m.Y));
        if (down && !over && _c.Ground(m) is { } g)
        {
            WatchStow(g);
            Move(g);
        }
        else
        {
            if (over) LeaveStow();
            PlaceHeld();
        }
    }

    Func<Vector2, bool>? _fromDrawer;

    public void StartFromDrawer(Vec2 grab, Func<Vector2, bool> overDrawer)
    {
        Start(grab);
        if (Holding) _fromDrawer = overDrawer;
    }

    public void PutBack()
    {
        EndMarch();
        Rehome();
        var nodes = _held.Select(h => h.Node).Where(GodotObject.IsInstanceValid).ToList();
        _c.Sound.Lift(nodes);
        foreach (var n in nodes) n.Held = false;
        var cmds = _held.Where(h => _c.S.CanTouch(h.Layer, h.Node.Piece)).Select(h => (Command)new DeletePiece { Layer = h.Layer, Id = h.Id }).ToList();
        _held.Clear();
        _heldNodes.Clear();
        _floor = -1;
        StopShake();
        _c.DoAll(cmds);
    }

    void ReturnHome()
    {
        foreach (var n in _held.Select(h => h.Node).Where(GodotObject.IsInstanceValid))
        {
            n.Held = false;
            if (n.IsDie) n.TakeFace(n.Piece.State);
            n.Refresh(n.Piece);
        }
        _held.Clear();
        _heldNodes.Clear();
        _floor = -1;
        StopShake();
    }

    public void Rehome()
    {
        if (_held.Count == 0) return;
        bool changed = false;
        foreach (var h in _held.ToList())
        {
            var now = _pieces.NodeOf(h.Layer, h.Id);
            if (now == h.Node) continue;
            changed = true;
            if (now != null)
            {
                h.Node = now;
                now.Held = true;
                now.TakenInHand();
                continue;
            }
            _held.Remove(h);
            foreach (var x in _held) if (x.Parent == h) x.Parent = null;
        }
        if (!changed) return;
        _heldNodes.Clear();
        foreach (var h in _held) _heldNodes.Add(h.Node);
        _floor = -1;
    }

    public void Rotate(double deg)
    {
        FinishClump();
        Formation.Turn(_held, deg);
        _c.Sound.Turn(_heldNodes);
        _floor = -1;
        Move(_cursor);
    }

    public void FormRows(int rows)
    {
        if (Holding)
        {
            FormHeldRows(rows);
            return;
        }
        var nodes = _selection.Nodes.ToList();
        if (nodes.Count < 2) return;
        var cur = nodes.Select(n => _poses.Current(n)).ToList();
        var center = new Vec2(cur.Average(c => c.pos.X), cur.Average(c => c.pos.Z));
        var pos = Formation.Rows(nodes.Select((n, i) => (n, cur[i].pos - center)).ToList(), cur[0].rot, rows, center);
        for (int i = 0; i < nodes.Count; i++) _poses.Set(nodes[i], pos[i], cur[0].rot, cur[i].scale);
        _c.Say($"Formed {rows} row{(rows > 1 ? "s" : "")}");
    }

    void FormHeldRows(int rows)
    {
        FinishClump();
        Formation.FormHeld(_held, rows);
        _floor = -1;
        Move(_cursor);
        _c.Say($"{rows} row{(rows > 1 ? "s" : "")} — release to drop");
    }

    public float SurfaceY(Vec2 p, string layer, PieceNode? except = null, HashSet<PieceNode>? skip = null) => _footing.SurfaceY(p, layer, except, skip);

    public double StoredY(Vec2 p, string layer) => _footing.StoredAt(p, SurfaceY(p, layer));

    public double RestingY(PieceNode n, Vec2 pos, double rot, double y) => _footing.Stored(n, pos, rot, y);

    public double StoredAt(Vec2 p, double y) => _footing.StoredAt(p, y);

    void SettleDrop(List<Held> held)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var landing = new Landing<PieceNode>(_footing, held, _cursor, SlideAtMost);
        var moved = new List<(PieceNode n, MovePiece m)>();
        var landed = new List<(PieceNode n, bool onPiece, double after)>();
        float drop = 0;
        foreach (var h in held)
        {
            if (!GodotObject.IsInstanceValid(h.Node)) continue;
            var m = landing.Move(h);
            var hung = h.Node.Position;
            var turn = h.Node.RestingTurn(m.Pos, m.RotDeg, m.Y);
            h.Node.Preview(m.Pos, h.Rot, h.Node.Piece.Scale, h.Node.LandingY(m.Pos, m.RotDeg, m.Y));
            drop = Mathf.Max(drop, hung.Y - h.Node.Position.Y);
            landed.Add((h.Node, m.Y > 0.01, Fall(h.Node, hung, turn)));
            moved.Add((h.Node, m));
        }
        if (moved.Count == 0) return;
        _c.Sound.LandMany(landed, drop / LiftHeight);
        var cmds = Landing<PieceNode>.ParentsFirst(moved, _c.S.View.Layer(moved[0].n.Layer));
        var nodes = moved.Select(x => x.n).ToList();
        var sticky = _sticking.Stickiness(nodes, moved, nodes.ToHashSet());
        cmds.AddRange(sticky.cmds);
        if (sticky.stuck > 0) _c.Say(sticky.stuck == 1 ? $"Stuck to {sticky.to}" : $"{sticky.stuck} pieces stuck to {sticky.to}");
        else if (sticky.freed > 0) _c.Say("Dropped; no longer stuck");
        GD.Print($"[drop] {moved.Count} set down on what is under them, nothing shoved, {clock.ElapsedMilliseconds} ms");
        _c.DoAll(cmds);
    }

    const float FallPull = 5 * 9.8f;

    float Fall(PieceNode n, Vector3 from, Quaternion turnTo)
    {
        var to = n.Position;
        var turnFrom = n.Quaternion;
        float t = Mathf.Clamp(Mathf.Sqrt(2 * Mathf.Max(0, from.Y - to.Y) / FallPull), 0.06f, 0.4f);
        n.Position = from;
        n.BeginSettle();
        _c.Moving(n);
        var tw = n.CreateTween();
        tw.TweenMethod(Callable.From<float>(k =>
        {
            if (n.Held) return;
            n.Position = from.Lerp(to, k * k);
            n.Quaternion = turnFrom.Slerp(turnTo, k * k);
            _c.Moving(n);
        }), 0f, 1f, t);
        tw.Finished += () => { n.EndSettle(); if (!n.Placed) n.Refresh(n.Piece); };
        return t;
    }

    public const int SimulateAtMost = 32;

    public const int SlideAtMost = 64;

    public const int ShoveAtMost = 48;

    public const int SettleBudgetMs = 1200;

    bool CanPush(PieceNode n)
    {
        var s = _c.S;
        return s.CanTouch(n.Layer, n.Piece);
    }

    static RigidBody3D MakeBody(PieceNode n, PhysicsMaterial mat, bool frozen)
    {
        var rb = new RigidBody3D
        {
            Mass = 1, GravityScale = 5, LinearDamp = 0.6f, AngularDamp = 5, PhysicsMaterialOverride = mat,
            AxisLockAngularX = true, AxisLockAngularZ = true, ContinuousCd = true, CanSleep = true,
            ContactMonitor = true, MaxContactsReported = 16,
            Freeze = frozen, FreezeMode = RigidBody3D.FreezeModeEnum.Static,
        };
        rb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(n.Footprint.X * 0.98f, Mathf.Max(0.02f, n.Thickness), n.Footprint.Y * 0.98f) } });
        return rb;
    }

    public async void Drop()
    {
        try { await DropAsync(); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    async Task DropAsync()
    {
        Rehome();
        EndMarch();
        if (HolderWindow.Under(Pointer.In(_c.Scene.GetViewport())) is { } w && PutInto(w.Layer, w.Holder)) return;
        if (DropIntoHolder()) return;
        if (_held.Any(h => !_c.S.CanTouch(h.Layer, _cursor + h.Offset)))
        {
            ReturnHome();
            _c.Say("Outside your zone");
            return;
        }
        foreach (var h in _held) h.Node.Held = false;
        var held = _held.Where(h => GodotObject.IsInstanceValid(h.Node)).ToList();
        _held.Clear();
        _heldNodes.Clear();
        _floor = -1;
        StopShake();
        var dice = held.Where(h => h.Node.IsDie).Select(h => h.Node).ToList();
        if (dice.Count > 0) _dice.Roll(dice, DiceThrowVelocity(), hop: false);
        held = held.Where(h => !h.Node.IsDie).ToList();
        var passing = held.Where(h => h.Node.Piece.NoCollision).ToList();
        if (passing.Count > 0) { SettleDrop(passing); held = held.Where(h => !h.Node.Piece.NoCollision).ToList(); }
        if (held.Count == 0) return;
        var flick = ThrowVelocity();
        if (held.Count > SimulateAtMost || new Vector3(flick.X, 0, flick.Z).Length() < FlingFrom) { SettleDrop(held); return; }
        await Throw(held);
    }

    sealed class DropScene
    {
        public readonly Node3D World = new() { Name = "DropPhysics" };
        public PhysicsMaterial Mat = null!;
        public readonly List<(PieceNode n, RigidBody3D b, bool neighbour)> Sim = new();
        readonly Dictionary<ulong, RigidBody3D> _byBody = new();

        public void Sleeper(RigidBody3D rb) => _byBody[rb.GetInstanceId()] = rb;

        public void Wake(Node other)
        {
            if (other is RigidBody3D rb && rb.Freeze && _byBody.ContainsKey(rb.GetInstanceId())) rb.SetDeferred(RigidBody3D.PropertyName.Freeze, false);
        }
    }

    async Task Throw(List<Held> held)
    {
        var scene = new DropScene();
        var world = scene.World;
        _c.Scene.AddChild(world);
        var settling = held.Select(h => h.Node).Where(GodotObject.IsInstanceValid).ToList();
        foreach (var n in settling) n.BeginSettle();
        void DoneSettling() { foreach (var n in settling) if (GodotObject.IsInstanceValid(n)) n.EndSettle(); settling.Clear(); }
        try
        {
            TerrainCollider.AddGround(world, _c.S.Ground, Coords.ToWorld(_cursor));
            scene.Mat = new PhysicsMaterial { Friction = 0.8f, Bounce = 0f };
            var layer = held[0].Node.Layer;
            var heldSet = held.Select(h => h.Node).ToHashSet();
            AddNeighbours(scene, layer, heldSet, held.Select(h => Coords.ToWorld(_cursor + h.Offset)).ToList());
            AddThrown(scene, held);
            var start = scene.Sim.ToDictionary(x => x.n, x => (pos: x.n.Piece.Pos, rot: x.n.Piece.RotDeg, y: x.n.Piece.Y));
            var dropWatch = System.Diagnostics.Stopwatch.StartNew();
            int framesRun = await RunUntilSettled(scene, dropWatch);
            DoneSettling();
            int taken = scene.Sim.Count(x => GodotObject.IsInstanceValid(x.n) && x.n.Held);
            GD.Print($"[drop] {held.Count} dropped, {scene.Sim.Count - held.Count} neighbours simulated, {taken} taken back into a hand, settled after {framesRun} physics frames in {dropWatch.ElapsedMilliseconds} ms");
            CommitThrow(scene, held, heldSet, start, layer);
        }
        finally
        {
            world.QueueFree();
            DoneSettling();
        }
    }

    void AddNeighbours(DropScene scene, string layer, HashSet<PieceNode> heldSet, List<Vector3> area)
    {
        var world = scene.World;
        var mat = scene.Mat;
        var round = new List<(PieceNode n, float d)>();
        foreach (var n in _pieces.Grid.NearAny(area, 20))
        {
            if (heldSet.Contains(n) || n.Layer != layer || n.Piece.NoCollision) continue;
            float d = float.MaxValue;
            foreach (var a in area) d = Mathf.Min(d, (a - n.Position with { Y = 0 }).Length());
            if (d < 20) round.Add((n, d));
        }
        round.Sort((x, y) => x.d.CompareTo(y.d));
        foreach (var (n, _) in round.Count > ShoveAtMost ? round.GetRange(0, ShoveAtMost) : round)
        {
            if (n.IsEffect) continue;
            if (n.Def?.Kind == "model" && !n.IsDie && n.WorldFaces() is { Length: > 2 } faces)
            {
                var sb = new StaticBody3D { PhysicsMaterialOverride = mat };
                sb.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true } });
                world.AddChild(sb);
                continue;
            }
            if (CanPush(n))
            {
                var rb = MakeBody(n, mat, frozen: true);
                world.AddChild(rb);
                rb.GlobalTransform = new Transform3D(new Basis(Vector3.Up, n.Heading), n.SlabCentre);
                rb.BodyEntered += scene.Wake;
                scene.Sleeper(rb);
                scene.Sim.Add((n, rb, true));
            }
            else
            {
                var sb = new StaticBody3D { PhysicsMaterialOverride = mat };
                sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(n.Footprint.X, n.Thickness, n.Footprint.Y) } });
                world.AddChild(sb);
                sb.GlobalTransform = new Transform3D(new Basis(Vector3.Up, n.Heading), n.SlabCentre);
            }
        }
    }

    const float FlingFrom = 8f;

    void AddThrown(DropScene scene, List<Held> held)
    {
        var fling = ThrowVelocity();
        foreach (var h in held)
        {
            var rb = MakeBody(h.Node, scene.Mat, frozen: false);
            scene.World.AddChild(rb);
            rb.GlobalTransform = new Transform3D(new Basis(Vector3.Up, h.Node.Heading), h.Node.SlabCentre);
            rb.BodyEntered += scene.Wake;
            rb.LinearVelocity = fling;
            scene.Sim.Add((h.Node, rb, false));
        }
    }

    async Task<int> RunUntilSettled(DropScene scene, System.Diagnostics.Stopwatch dropWatch)
    {
        int framesRun = 0;
        var knocks = new Knocks();
        for (int frame = 0; frame < 150; frame++)
        {
            framesRun = frame + 1;
            await _c.Scene.ToSignal(_c.Scene.GetTree(), SceneTree.SignalName.PhysicsFrame);
            bool settled = frame > 8;
            float dt = (float)_c.Scene.GetPhysicsProcessDeltaTime();
            foreach (var (n, b, _) in scene.Sim)
            {
                if (b.Freeze || !GodotObject.IsInstanceValid(n)) continue;
                if (n.Held) { b.SetDeferred(RigidBody3D.PropertyName.Freeze, true); continue; }
                n.Position = b.GlobalPosition - Vector3.Up * n.Thickness / 2 + Vector3.Up * n.LiftY;
                n.Quaternion = n.PoseTurn(b.GlobalRotation.Y);
                _c.Moving(n);
                knocks.Frame(b, dt, jolt => _c.Sound.Knock(n, b.GlobalPosition, jolt));
                if (!b.Sleeping && b.LinearVelocity.Length() > 0.05f) settled = false;
            }
            if (settled) break;
            if (dropWatch.ElapsedMilliseconds > SettleBudgetMs) break;
        }
        return framesRun;
    }

    void CommitThrow(DropScene scene, List<Held> held, HashSet<PieceNode> heldSet, Dictionary<PieceNode, (Vec2 pos, double rot, double y)> start, string layer)
    {
        var moved = new List<(PieceNode n, MovePiece m)>();
        foreach (var (n, b, neighbour) in scene.Sim)
        {
            if (b.Freeze || !GodotObject.IsInstanceValid(n) || n.Held) continue;
            var pos = Coords.FromWorld(b.GlobalPosition);
            double rot = Table.Norm360(180 - Mathf.RadToDeg(b.GlobalRotation.Y));
            double y = _footing.Stored(n, pos, rot, Math.Max(0, b.GlobalPosition.Y - n.Thickness / 2 - 0.03));
            var s0 = start[n];
            if (neighbour && (pos - s0.pos).Length < 0.01 && Math.Abs(Mathf.Wrap((float)(rot - s0.rot), -180, 180)) < 0.3 && Math.Abs(y - s0.y) < 0.02)
            {
                n.Refresh(n.Piece);
                continue;
            }
            moved.Add((n, new MovePiece { Layer = n.Layer, Id = n.Piece.Id, Pos = pos, RotDeg = rot, Stack = 0, Y = y }));
        }
        var cmds = Landing<PieceNode>.ParentsFirst(moved, _c.S.View.Layer(layer));
        int pushed = moved.Count(x => !heldSet.Contains(x.n));
        var sticky =
            _sticking.Stickiness(held.Select(h => h.Node).Where(n => GodotObject.IsInstanceValid(n) && !n.Held).ToList(), moved, scene.Sim.Where(x => !x.b.Freeze).Select(x => x.n).ToHashSet());
        cmds.AddRange(sticky.cmds);
        if (sticky.stuck > 0) _c.Say(sticky.stuck == 1 ? $"Stuck to {sticky.to}" : $"{sticky.stuck} pieces stuck to {sticky.to}");
        else if (sticky.freed > 0) _c.Say("Dropped; no longer stuck");
        else if (pushed > 0) _c.Say($"Dropped; pushed {pushed} piece{(pushed > 1 ? "s" : "")}");
        _c.DoAll(cmds);
    }

    readonly Shake _shake = new();
    static readonly Random ShakeRng = new();
    const double ClumpTime = 0.12;
    Dictionary<Held, (Vec2 from, double fromRot, float fromLift, Vec2 to, double toRot, float toLift)>? _clumping;
    Dictionary<PieceNode, (Quaternion from, Quaternion to)>? _tumbling;
    double _clumpStart;

    internal void ShakeAt(double now, System.Numerics.Vector2 pointer)
    {
        if (_shake.Add(now, pointer)) Clump(now);
        if (_clumping == null) return;
        double t = Math.Clamp((now - _clumpStart) / ClumpTime, 0, 1);
        float k = (float)(t * (2 - t));
        foreach (var (h, c) in _clumping)
        {
            h.Offset = new Vec2(c.from.X + (c.to.X - c.from.X) * k, c.from.Z + (c.to.Z - c.from.Z) * k);
            h.Rot = c.fromRot + Mathf.Wrap((float)(c.toRot - c.fromRot), -180, 180) * k;
            h.Lift = c.fromLift + (c.toLift - c.fromLift) * k;
        }
        if (_tumbling != null)
            foreach (var (n, q) in _tumbling)
                if (GodotObject.IsInstanceValid(n)) n.TurnInHandTo(q.from.Slerp(q.to, k));
        if (t >= 1) FinishClump();
    }

    void Clump(double now)
    {
        FinishClump();
        var from = _held.ToDictionary(h => h, h => (h.Offset, h.Rot, h.Lift));
        Formation.Gather(_held, ShakeRng);
        _clumping = _held.ToDictionary(h => h, h => (from[h].Offset, from[h].Rot, from[h].Lift, h.Offset, h.Rot, h.Lift));
        _tumbling = _heldNodes.Where(n => n.IsDie).ToDictionary(n => n, n => (n.TurnInHand, PieceNode.AnyTurn(ShakeRng)));
        _clumpStart = now;
        _c.Sound.Shaking(_heldNodes);
    }

    void FinishClump()
    {
        if (_clumping == null) return;
        foreach (var (h, c) in _clumping)
        {
            h.Offset = c.to;
            h.Rot = c.toRot;
            h.Lift = c.toLift;
        }
        _clumping = null;
        foreach (var (n, q) in _tumbling ?? []) if (GodotObject.IsInstanceValid(n)) n.TurnInHandTo(q.to);
        _tumbling = null;
        _floor = -1;
        _c.Sound.Gathered(_heldNodes);
    }

    void StopShake()
    {
        LeaveStow();
        _shake.Clear();
        _clumping = null;
        _tumbling = null;
        _fromDrawer = null;
    }

    readonly ThrowTrail _trail = new();

    void TrailAdd(Vec2 at) => _trail.Add(Time.GetTicksMsec() / 1000.0, at);

    public Vector3 ThrowVelocity()
    {
        var v = _trail.Velocity(Time.GetTicksMsec() / 1000.0);
        return new Vector3(v.X, v.Y, v.Z);
    }

    Vector3 DiceThrowVelocity()
    {
        var v = _trail.Velocity(Time.GetTicksMsec() / 1000.0, cap: 40, liftPer: 0.3f, liftMax: 12);
        return new Vector3(v.X, v.Y, v.Z);
    }
}
