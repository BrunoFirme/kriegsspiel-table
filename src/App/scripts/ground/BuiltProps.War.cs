using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltProps
{
    const float CraterM = 2.4f, BombCraterM = 7, RimShareOut = 1.35f, ChunkM = 0.5f, BeamM = 2.2f;
    const int CraterSides = 14, Chunks = 26, Beams = 4;

    protected bool Rubble(BuiltHand hand, Vector2[] px)
    {
        var centre = Mid(px);
        var heap = px.Select((p, i) => centre + (p - centre) * (0.8f + 0.35f * Chance(i * 2.9f + 1))).ToArray();
        if (hand.Shaded) DrawColoredPolygon(heap.Select(p => p + Vector2.One * Mathf.Min(0.8f * _m, 4)).ToArray(), hand.Ink with { A = 0.35f });
        Filled(heap, hand.Stone.Lerp(hand.Mud, 0.35f));
        float reach = px.Max(p => (p - centre).Length());
        if (ChunkM * _m < 1) return true;
        for (int i = 0; i < Chunks; i++)
        {
            var at = centre + Vector2.Right.Rotated(Chance(i * 3.3f) * Mathf.Tau) * reach * Mathf.Sqrt(Chance(i * 5.1f + 2)) * 0.85f;
            DrawCircle(at, ChunkM * _m * (0.6f + Chance(i * 1.7f)), i % 3 == 0 ? hand.Stone.Lightened(0.15f) : i % 3 == 1 ? hand.Brick : hand.Stone.Darkened(0.2f));
        }
        for (int i = 0; i < Beams; i++)
        {
            var at = centre + Vector2.Right.Rotated(Chance(i * 7.7f) * Mathf.Tau) * reach * 0.5f * Chance(i * 8.8f);
            var way = Vector2.Right.Rotated(Chance(i * 9.9f) * Mathf.Pi) * BeamM * _m;
            DrawLine(at - way, at + way, hand.Wood.Darkened(0.35f), Mathf.Max(0.25f * _m, 1), true);
        }
        return true;
    }

    void Crater(BuiltHand hand, Vector2 at, float metres)
    {
        float r = metres * _m * (0.8f + 0.4f * Chance(3.7f));
        var ring = Enumerable.Range(0, CraterSides).Select(i => at + Vector2.Right.Rotated(i * Mathf.Tau / CraterSides) * r * (0.85f + 0.3f * Chance(i * 1.3f))).ToArray();
        Filled(ring.Select(p => at + (p - at) * RimShareOut).ToArray(), hand.Mud.Lightened(0.12f) with { A = 0.85f });
        Filled(ring, hand.Mud.Darkened(0.35f));
        Filled(ring.Select(p => at + (p - at) * 0.5f).ToArray(), Chance(4.4f) < 0.3f ? hand.Pool.Lerp(hand.Mud, 0.5f) : hand.Mud.Darkened(0.5f));
        DrawPolyline(ring.Append(ring[0]).ToArray(), hand.Ink with { A = 0.5f }, BuiltHand.LeastPx, true);
    }

    protected bool Shelled(BuiltHand hand, string cls, Vector2 at)
    {
        switch (cls)
        {
            case Settlements.Crater when Seen(CraterM):
                Crater(hand, at, CraterM);
                return true;
            case Settlements.BombCrater when Seen(BombCraterM / 2):
                Crater(hand, at, BombCraterM);
                return true;
            case Settlements.Crater or Settlements.BombCrater:
                return true;
        }
        return false;
    }
}
