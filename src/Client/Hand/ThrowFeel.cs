using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Hand;

public readonly record struct Fling(Vector3 Velocity, float Turn)
{
    public bool Thrown => Velocity != Vector3.Zero;
}

public sealed record ThrowFeel(float From, float Span, float Most, float LiftPer, float LiftMost)
{
    const float TurnKept = 0.5f;

    public static readonly ThrowFeel Piece = new(From: 0.6f, Span: 12, Most: 50, LiftPer: 0.2f, LiftMost: 4);
    public static readonly ThrowFeel Die = new(From: 0.25f, Span: 6, Most: 20, LiftPer: 0.3f, LiftMost: 5);

    public Fling Release(Fling hand, float viewSpan)
    {
        float speed = hand.Velocity.Norm();
        if (speed <= 0 || viewSpan <= 0) return default;
        float takes = Mathf.Clamp((speed / viewSpan - From) / From, 0, 1);
        if (takes <= 0) return default;
        float kept = Mathf.Min(Most, speed * takes * Mathf.Min(1, Span / viewSpan));
        return new Fling(hand.Velocity * (kept / speed) + Axis.Up * Mathf.Min(LiftMost, kept * LiftPer), hand.Turn * takes * TurnKept);
    }
}

public static class Heft
{
    const float ReferenceVolume = 0.3f, Lightest = 0.5f, Heaviest = 4, ThinnestSlab = 0.02f;

    public static float Of(float width, float depth, float thickness) =>
        Mathf.Clamp(width * depth * Mathf.Max(ThinnestSlab, thickness) / ReferenceVolume, Lightest, Heaviest);
}
