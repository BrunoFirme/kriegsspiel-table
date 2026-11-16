using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class RideSuspension
{
    public const float SettleSeconds = 0.45f;
    const float AtRest = 0.002f, TurnsPerSecond = 6f, TurnAtRest = 0.001f;

    public bool Off { get; set; }

    sealed class Spring
    {
        public required PieceNode Node;
        public float Asked, Height, Speed, Shown;
        public Quaternion AskedTurn, Turn;
        public Vector3 Scale;
        public Basis ShownBasis;
        public bool Ridden;
    }

    readonly Dictionary<ulong, Spring> _springs = new();

    public int Count => _springs.Count;

    public void Tick(double delta, IEnumerable<PieceNode> ridden)
    {
        foreach (var spring in _springs.Values) spring.Ridden = false;
        if (!Off)
            foreach (var node in ridden)
            {
                if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) continue;
                ulong id = node.GetInstanceId();
                if (!_springs.TryGetValue(id, out var spring))
                {
                    float y = node.Position.Y;
                    var turn = node.Basis.Orthonormalized().GetRotationQuaternion();
                    _springs[id] = spring = new Spring
                    {
                        Node = node, Asked = y, Height = y, Shown = y, AskedTurn = turn, Turn = turn, Scale = node.Basis.Scale, ShownBasis = node.Basis,
                    };
                }
                spring.Ridden = true;
            }
        float dt = (float)Math.Min(delta, 0.1);
        foreach (var (id, spring) in _springs.ToList())
        {
            var node = spring.Node;
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            {
                _springs.Remove(id);
                continue;
            }
            float now = node.Position.Y;
            if (!Mathf.IsEqualApprox(now, spring.Shown)) spring.Asked = now;
            var basis = node.Basis;
            if (!basis.IsEqualApprox(spring.ShownBasis))
            {
                spring.AskedTurn = basis.Orthonormalized().GetRotationQuaternion();
                spring.Scale = basis.Scale;
            }
            Damp(spring, dt);
            spring.Turn = spring.Turn.Slerp(spring.AskedTurn, 1 - Mathf.Exp(-dt * TurnsPerSecond)).Normalized();
            bool level = spring.Turn.AngleTo(spring.AskedTurn) < TurnAtRest;
            if (level) spring.Turn = spring.AskedTurn;
            bool rests = level && Mathf.Abs(spring.Height - spring.Asked) < AtRest && Mathf.Abs(spring.Speed) < AtRest;
            if (rests)
            {
                spring.Height = spring.Asked;
                spring.Speed = 0;
            }
            spring.Shown = spring.Height;
            spring.ShownBasis = new Basis(spring.Turn).Scaled(spring.Scale);
            node.Transform = new Transform3D(spring.ShownBasis, node.Position with { Y = spring.Height });
            if (rests && !spring.Ridden) _springs.Remove(id);
        }
    }

    static void Damp(Spring spring, float dt)
    {
        float stiff = 2f / SettleSeconds, x = stiff * dt, fade = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float away = spring.Height - spring.Asked, push = (spring.Speed + stiff * away) * dt;
        spring.Speed = (spring.Speed - stiff * push) * fade;
        spring.Height = spring.Asked + (away + push) * fade;
    }
}
