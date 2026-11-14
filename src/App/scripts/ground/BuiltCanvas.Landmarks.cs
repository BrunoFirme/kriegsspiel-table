using System.Linq;
using Godot;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    const float BulbShade = 0.35f, BulbLight = 0.16f, MinaretM = 1.3f, TierStep = 0.22f, EaveStep = 0.17f, ColumnDotM = 0.9f;
    const float ShadeMostPx = 6;
    const int Tiers = 4, Eaves = 3, Aisles = 5;

    static Vector2 Within(Vector2[] q, float u, float v) => q[0].Lerp(q[1], u).Lerp(q[3].Lerp(q[2], u), v);

    static Vector2[] Band(Vector2[] q, float u0, float u1, float v0, float v1) => [Within(q, u0, v0), Within(q, u1, v0), Within(q, u1, v1), Within(q, u0, v1)];

    static Vector2[] Shrunk(Vector2[] q, float share) => q.Select(p => Mid(q) + (p - Mid(q)) * (1 - share)).ToArray();

    static Vector2[] Cast(Vector2[] q, float by) => q.Select(p => p + Vector2.One * by).ToArray();

    void Lined(Vector2[] ring, BuiltHand hand, float ink) => DrawPolyline(ring.Append(ring[0]).ToArray(), hand.Ink with { A = ink }, BuiltHand.LeastPx, true);

    void Bulb(BuiltHand hand, Vector2 at, float r, Color fill)
    {
        if (hand.Shaded) DrawCircle(at + Vector2.One * r * BulbShade, r, hand.Ink with { A = 0.4f });
        DrawCircle(at, r, fill.Darkened(0.12f));
        DrawCircle(at - Vector2.One * r * 0.16f, r * 0.8f, fill);
        DrawCircle(at - Vector2.One * r * 0.3f, r * 0.35f, fill.Lightened(BulbLight));
        DrawArc(at, r, 0, Mathf.Tau, 20, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
    }

    protected void Mosque(BuiltHand hand, Vector2[] hall, Vector2[] court, float shortPx, bool whitewashed, Color? roofed = null)
    {
        Filled(court, hand.Area("yard").Fill with { A = 1 });
        Lined(court, hand, 0.9f);
        DrawCircle(Mid(court), shortPx * 0.07f, hand.Wet);
        if (hand.Shaded) Filled(Cast(hall, shortPx * 0.05f), hand.Ink with { A = 0.5f });
        Filled(hall, roofed ?? (whitewashed ? hand.Lime : hand.Stone.Lightened(0.14f)));
        Lined(hall, hand, 0.9f);
        var dome = whitewashed ? hand.Lime.Darkened(0.08f) : hand.Lead;
        Bulb(hand, Mid(hall), shortPx * 0.27f, dome);
        if (whitewashed)
        {
            var tower = Shrunk(Band(court, 0.72f, 1, 0, 0.28f), 0.1f);
            if (hand.Shaded) Filled(Cast(tower, shortPx * 0.12f), hand.Ink with { A = 0.55f });
            Filled(tower, hand.Lime.Darkened(0.05f));
            Filled(Shrunk(tower, 0.45f), hand.Lime.Lightened(0.05f));
            Lined(tower, hand, 0.9f);
            return;
        }
        foreach (var (u, v) in new[] { (0.18f, 0.18f), (0.18f, 0.82f), (0.82f, 0.18f), (0.82f, 0.82f) }) Bulb(hand, Within(hall, u, v), shortPx * 0.09f, dome);
        foreach (float v in new[] { 0.04f, 0.96f }) Bulb(hand, Within(court, 0.06f, v), Mathf.Max(MinaretM * _m, BuiltHand.LeastPx * 1.5f), hand.Stone.Lightened(0.2f));
    }

    protected void Pagoda(BuiltHand hand, Vector2[] hall, Vector2[] tower, Color roof)
    {
        float wide = (hall[1] - hall[0]).Length(), square = (tower[1] - tower[0]).Length();
        if (hand.Shaded) Filled(Cast(hall, wide * 0.03f), hand.Ink with { A = 0.5f });
        for (int i = 0; i < Eaves; i++)
        {
            Filled(Shrunk(hall, i * EaveStep), i % 2 == 0 ? roof : roof.Darkened(0.14f));
            Lined(Shrunk(hall, i * EaveStep), hand, 0.8f);
        }
        DrawLine(Within(hall, 0.3f, 0.5f), Within(hall, 0.7f, 0.5f), hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
        if (hand.Shaded) Filled(Cast(tower, square * 0.22f), hand.Ink with { A = 0.55f });
        for (int i = 0; i < Tiers; i++)
        {
            Filled(Shrunk(tower, i * TierStep), i % 2 == 0 ? roof.Darkened(0.1f) : roof.Lightened(0.06f));
            Lined(Shrunk(tower, i * TierStep), hand, 0.85f);
        }
        DrawCircle(Mid(tower), Mathf.Max(square * 0.06f, BuiltHand.LeastPx), hand.Copper);
    }

    protected void Terrace(BuiltHand hand, Vector2[] ground, Vector2[] hall)
    {
        float wide = (hall[1] - hall[0]).Length(), r = Mathf.Max(ColumnDotM * _m / 2, BuiltHand.LeastPx * 0.7f), half = wide * 0.09f;
        Filled(ground, hand.Stone.Lightened(0.16f));
        Lined(ground, hand, 0.9f);
        for (int i = 1; i <= 3; i++) DrawLine(Within(ground, 1 - 0.03f * i, 0.3f), Within(ground, 1 - 0.03f * i, 0.7f), hand.Ink with { A = 0.45f }, BuiltHand.LeastPx, true);
        if (hand.Shaded) Filled(Cast(hall, wide * 0.04f), hand.Ink with { A = 0.45f });
        Filled(hall, hand.Lime.Darkened(0.06f));
        Lined(hall, hand, 0.9f);
        for (int i = 0; i < Aisles * Aisles; i++) DrawCircle(Within(hall, (i % Aisles + 0.5f) / Aisles, (i / Aisles + 0.5f) / Aisles), r, hand.Stone.Darkened(0.28f));
        foreach (var (u, v) in new[] { (0f, 0f), (0f, 1f), (1f, 0f), (1f, 1f) })
        {
            var at = Within(hall, u, v);
            Filled([at + new Vector2(-half, -half), at + new Vector2(half, -half), at + new Vector2(half, half), at + new Vector2(-half, half)], hand.Lime.Darkened(0.14f));
        }
    }
}
