using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class Dice
{
    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Selection _selection;
    readonly Carry _hand;
    public Dice(TableContext c, PieceLayer pieces, Selection selection, Carry hand)
    {
        _c = c;
        _pieces = pieces;
        _selection = selection;
        _hand = hand;
    }

    Hover _hover = null!;
    public void Connect(Hover hover) => _hover = hover;

    public static readonly Random Rng = new();
    static float Rnd(float lo, float hi) => lo + (float)Rng.NextDouble() * (hi - lo);

    public bool RollAtHand()
    {
        var nodes = _selection.Nodes.Where(n => n.IsDie).ToList();
        var hover = _hover.Node;
        if (nodes.Count == 0 && hover != null && hover.IsDie && _c.LayerInteractive(hover.Layer)) nodes.Add(hover);
        if (nodes.Count == 0) return false;
        Roll(nodes, Vector3.Zero, hop: true);
        return true;
    }

    public async void Roll(List<PieceNode> dice, Vector3 throwVel, bool hop)
    {
        try { await RollAsync(dice, throwVel, hop); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    async System.Threading.Tasks.Task RollAsync(List<PieceNode> dice, Vector3 throwVel, bool hop)
    {
        dice = dice.Where(n => GodotObject.IsInstanceValid(n) && n.IsDie && !n.Rolling).ToList();
        if (dice.Count == 0) return;
        foreach (var n in dice) n.BeginRoll();
        var gens = dice.ToDictionary(n => n, n => n.RollGen);
        bool Ours(PieceNode n) => GodotObject.IsInstanceValid(n) && n.InRoll(gens[n]);
        foreach (var g in dice.Where(n => !n.Piece.PhysicalRoll).GroupBy(n => n.Layer))
        {
            var list = g.ToList();
            _c.S.Do(new RollDice { Layer = g.Key, PieceIds = list.Select(n => n.Piece.Id).ToList(), Count = list.Count, Sides = list[0].Def!.States.Count,
                Label = list.Count == 1 ? list[0].Def!.Name : $"{list.Count} dice" });
        }

        var world = new Node3D { Name = "DicePhysics" };
        _c.Scene.AddChild(world);
        var rest = new Dictionary<PieceNode, (Basis b, Vector3 c)>();
        try
        {
            var bodies = Bodies(world, dice, throwVel, hop);
            var knocks = new Knocks();
            int calm = 0;
            for (int frame = 0; frame < 240; frame++)
            {
                await _c.Scene.ToSignal(_c.Scene.GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (!GodotObject.IsInstanceValid(world)) return;
                bodies.RemoveAll(x =>
                {
                    if (Ours(x.n)) return false;
                    x.b.QueueFree();
                    return true;
                });
                if (bodies.Count == 0) break;
                bool allCalm = frame > 10;
                float dt = (float)_c.Scene.GetPhysicsProcessDeltaTime();
                foreach (var (n, b) in bodies)
                {
                    n.ShowTumble(b.GlobalBasis.Orthonormalized(), b.GlobalPosition);
                    _c.Moving(n);
                    knocks.Frame(b, dt, jolt => _c.Sound.Knock(n, b.GlobalPosition, jolt, onDie: b.GetCollidingBodies().Any(o => o is RigidBody3D)));
                    if (!(b.Sleeping || (b.LinearVelocity.Length() < 0.08f && b.AngularVelocity.Length() < 0.3f))) allCalm = false;
                }
                calm = allCalm ? calm + 1 : 0;
                if (calm >= 6) break;
            }
            foreach (var (n, b) in bodies) if (Ours(n)) rest[n] = (b.GlobalBasis.Orthonormalized(), b.GlobalPosition);
        }
        finally { world.QueueFree(); }
        await Land(rest.Keys.ToList(), rest, Ours);
    }

    List<(PieceNode n, RigidBody3D b)> Bodies(Node3D world, List<PieceNode> dice, Vector3 throwVel, bool hop)
    {
        var mat = new PhysicsMaterial { Friction = 0.85f, Bounce = 0.25f };
        var set = dice.ToHashSet();
        var centers = dice.Select(n => n.ModelWorldCenter).ToList();
        var middle = centers.Aggregate(Vector3.Zero, (a, c) => a + c) / centers.Count;
        float spread = centers.Max(c => (c - middle with { Y = c.Y }).Length()) * 2;
        TerrainCollider.AddGround(world, _c.S.Ground, middle, TerrainCollider.Span + spread);
        foreach (var n in _pieces.Nodes)
        {
            if (set.Contains(n) || n.Layer != dice[0].Layer || n.Rolling || n.Piece.NoCollision) continue;
            if (!centers.Any(c => (c - n.Position with { Y = 0 }).Length() < 30)) continue;
            var sb = new StaticBody3D { PhysicsMaterialOverride = mat };
            sb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(n.Footprint.X, n.Thickness, n.Footprint.Y) } });
            world.AddChild(sb);
            sb.GlobalTransform = new Transform3D(new Basis(Vector3.Up, n.Rotation.Y), n.Position + Vector3.Up * n.Thickness / 2);
        }
        var bodies = new List<(PieceNode n, RigidBody3D b)>();
        var flat = new Vector3(throwVel.X, 0, throwVel.Z);
        foreach (var n in dice)
        {
            var rb =
                new RigidBody3D { Mass = 1, GravityScale = 5, LinearDamp = 1.2f, AngularDamp = 2.2f, PhysicsMaterialOverride = mat, ContinuousCd = true, CanSleep = true, ContactMonitor = true,
                    MaxContactsReported = 4 };
            Shape3D shape = n.DiceHull is { Length: >= 4 } hull
                ? new ConvexPolygonShape3D { Points = hull }
                : new BoxShape3D { Size = new Vector3(n.Footprint.X, n.Thickness, n.Footprint.Y) };
            rb.AddChild(new CollisionShape3D { Shape = shape });
            world.AddChild(rb);
            rb.GlobalTransform = new Transform3D(n.ModelWorldBasis, n.ModelWorldCenter + (hop ? Vector3.Up * 0.05f : Vector3.Zero));
            rb.LinearVelocity = throwVel + (hop ? new Vector3(Rnd(-1.5f, 1.5f), 9f, Rnd(-1.5f, 1.5f)) : Vector3.Zero);
            var wobble = new Vector3(Rnd(-1, 1), Rnd(-1, 1), Rnd(-1, 1)).Normalized();
            float size = Mathf.Max(0.2f, n.Footprint.Length() * 0.5f);
            bool thrown = !hop && flat.Length() > 0.5f;
            var axis = thrown ? (Vector3.Up.Cross(flat).Normalized() + wobble * 0.4f).Normalized() : wobble;
            rb.AngularVelocity = axis * (thrown ? Mathf.Clamp(flat.Length() / size * 0.8f, 4, 45) : hop ? 14 : 9);
            bodies.Add((n, rb));
        }
        return bodies;
    }

    async System.Threading.Tasks.Task Land(List<PieceNode> dice, Dictionary<PieceNode, (Basis b, Vector3 c)> rest, Func<PieceNode, bool> ours)
    {
        double waited = 0;
        while (dice.Any(n => ours(n) && !n.Piece.PhysicalRoll && n.PendingState == null) && waited < 2.0)
        {
            await _c.Scene.ToSignal(_c.Scene.GetTree(), SceneTree.SignalName.ProcessFrame);
            waited += _c.Scene.GetProcessDeltaTime();
        }
        dice = dice.Where(ours).ToList();
        var cmds = new List<Command>();
        var real = new List<(PieceNode n, int face)>();
        foreach (var n in dice)
            if (rest.TryGetValue(n, out var r))
            {
                int face = n.Piece.PhysicalRoll ? n.FaceUp(r.b) : n.PendingState ?? n.Piece.State;
                if (n.Piece.PhysicalRoll) real.Add((n, face));
                var (rot, y) = n.SettleFromPhysics(face, r.b, r.c, (pos, rotDeg, top) => _hand.RestingY(n, pos, rotDeg, top));
                cmds.Add(new MovePiece { Layer = n.Layer, Id = n.Piece.Id, Pos = Coords.FromWorld(r.c), RotDeg = rot, Stack = 0, Y = y });
                _c.Moving(n);
            }
        foreach (var g in real.GroupBy(x => x.n.Layer))
        {
            var list = g.ToList();
            cmds.Insert(0, new RollDice
            {
                Layer = g.Key, PieceIds = list.Select(x => x.n.Piece.Id).ToList(), Results = list.Select(x => x.face + 1).ToList(), Physical = true,
                Count = list.Count, Sides = list[0].n.Def!.States.Count, Label = list.Count == 1 ? list[0].n.Def!.Name : $"{list.Count} dice",
            });
        }
        _c.DoAll(cmds);
        foreach (var n in dice) n.EndRoll();
    }

    public string Debug() => string.Join(" | ", _pieces.Nodes.Where(n => n.IsDie).Select(n => $"{n.Def!.Name}: centre {n.ModelWorldCenter.X:0.0},{n.ModelWorldCenter.Y:0.00},{n.ModelWorldCenter.Z:0.0} rolling={n.Rolling} face={n.Piece.State + 1}"));
}
