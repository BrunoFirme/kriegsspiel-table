using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    const float PorchDeep = 0.12f, SpireM = 1.2f, PostDotM = 0.45f, LanternM = 0.6f, FinialM = 1.6f, BellM = 0.7f, ZigzagM = 3.2f, ZigzagSwingM = 0.7f, ZigzagFromPx = 4;

    static Vector2[] Square(Vector2 at, Vector2 along, Vector2 across, float side) => Quad(at, along * side / 2, across * side / 2);

    void Tower(BuiltHand hand, Vector2 at, Vector2 along, Vector2 across, float side, Color fill, int tiers)
    {
        var body = Square(at, along, across, side);
        if (hand.Shaded) Filled(Cast(body, Mathf.Min(side * 0.5f, ShadeMostPx * 1.5f)), hand.Ink with { A = 0.5f });
        for (int i = 0; i < tiers; i++)
        {
            Filled(Shrunk(body, i * (0.7f / tiers)), i % 2 == 0 ? fill.Darkened(0.1f) : fill.Lightened(0.08f));
            Lined(Shrunk(body, i * (0.7f / tiers)), hand, 0.85f);
        }
    }

    void Spire(BuiltHand hand, Vector2 at, Color fill)
    {
        float r = Mathf.Max(SpireM * _m, BuiltHand.LeastPx * 1.5f);
        if (hand.Shaded) DrawLine(at, at + Sunless * (r * 6), hand.Ink with { A = 0.35f }, Mathf.Max(r * 0.8f, 1), true);
        Bulb(hand, at, r, fill);
        DrawCircle(at, r * 0.35f, hand.Ink with { A = 0.7f });
    }

    void Posts(BuiltHand hand, Vector2 from, Vector2 to, int count, Color fill)
    {
        float r = Mathf.Max(PostDotM * _m, BuiltHand.LeastPx);
        for (int i = 0; i < count; i++) DrawCircle(from.Lerp(to, count == 1 ? 0.5f : i / (count - 1f)), r, fill);
    }

    void Gate(BuiltHand hand, Vector2 at, Vector2 across, float half)
    {
        float r = Mathf.Max(PostDotM * _m, BuiltHand.LeastPx), pen = Mathf.Max(0.35f * _m, 1.2f);
        var along = across.Orthogonal();
        DrawLine(at - across * half * 1.3f, at + across * half * 1.3f, hand.Signal, pen, true);
        DrawLine(at - across * half * 1.1f + along * pen * 1.6f, at + across * half * 1.1f + along * pen * 1.6f, hand.Signal, pen * 0.7f, true);
        foreach (int side in new[] { 1, -1 }) DrawCircle(at + across * (half * side), r, hand.Signal.Darkened(0.2f));
    }

    protected void Adorned(BuiltHand hand, Vector2[] q, bool church)
    {
        if (_adorners == _culture || q.Length != 4) return;
        Vector2 along = (q[1] - q[0]).Normalized(), across = (q[3] - q[0]).Normalized();
        float wide = (q[3] - q[0]).Length(), pick = Chance(61.3f);
        switch (_adorners)
        {
            case Manners.Ottoman:
                Bulb(hand, Within(q, 0.45f, 0.5f), wide * 0.3f, hand.Lead);
                for (int i = 0; i < 3; i++) Bulb(hand, Within(q, 1 + PorchDeep / 2, 0.25f + 0.25f * i), wide * 0.08f, hand.Lead);
                (float U, float V)[] corners = [(1, 0), (1, 1), (0, 0), (0, 1)];
                foreach (var (u, v) in corners.Take(pick < 0.35f ? 1 : pick < 0.8f ? 2 : 4)) Spire(hand, Within(q, u, v), hand.Stone.Lightened(0.2f));
                break;
            case Manners.NorthAfrican:
                Bulb(hand, Within(q, 0.4f, 0.5f), wide * 0.24f, hand.Lime);
                Tower(hand, Within(q, 0.94f, pick < 0.5f ? 0.1f : 0.9f), along, across, wide * 0.26f, hand.Lime, 3);
                break;
            case Manners.Chinese:
                Tower(hand, Within(q, 0.5f, 0.5f), along, across, wide * 0.62f, hand.Kawara, 2);
                foreach (float v in new[] { 0.16f, 0.84f }) Tower(hand, Within(q, 1 + PorchDeep * 1.6f, v), along, across, wide * 0.16f, hand.Kawara, 2);
                break;
            case Manners.Japanese or Manners.Nippon:
                Tower(hand, Within(q, 0.22f, pick < 0.5f ? -0.3f : 1.3f), along, across, wide * 0.34f, hand.Kawara, 3);
                Gate(hand, Within(q, 1 + PorchDeep * 2.4f, 0.5f), across, wide * 0.22f);
                foreach (float v in new[] { 0.3f, 0.7f }) DrawCircle(Within(q, 1 + PorchDeep, v), Mathf.Max(LanternM * _m / 2, 1), hand.Stone);
                break;
            case Manners.Persian:
                foreach (float u in new[] { 1.03f, 1.09f }) Posts(hand, Within(q, u, 0.08f), Within(q, u, 0.92f), 6, hand.Lime);
                Filled(Band(q, 0.9f, 1, 0.38f, 0.62f), hand.Ink with { A = 0.45f });
                foreach (var (u, v) in new[] { (0f, 0f), (0f, 1f), (1f, 0f), (1f, 1f) }) Tower(hand, Within(q, u, v), along, across, wide * 0.14f, hand.Lime.Darkened(0.1f), 1);
                break;
            case Manners.American:
                Filled(Band(q, 1, 1 + PorchDeep, 0.18f, 0.82f), hand.Lime.Lightened(0.1f));
                Lined(Band(q, 1, 1 + PorchDeep, 0.18f, 0.82f), hand, 0.6f);
                Posts(hand, Within(q, 1 + PorchDeep, 0.2f), Within(q, 1 + PorchDeep, 0.8f), 4, hand.Lime.Lightened(0.2f));
                Tower(hand, Within(q, 0.72f, 0.5f), along, across, wide * 0.24f, hand.Lime, 1);
                Bulb(hand, Within(q, 0.72f, 0.5f), wide * 0.07f, hand.Lead);
                break;
            case Manners.Pacific:
                foreach (float u in new[] { -0.02f, 1.02f })
                    foreach (int side in new[] { 1, -1 })
                        DrawLine(Within(q, u, 0.5f) - (along + across * side) * FinialM * _m * 0.5f, Within(q, u, 0.5f)
                            + (along + across * side) * FinialM * _m * 0.5f, hand.Wood, Mathf.Max(0.2f * _m, 1), true);
                Vector2 bell = Within(q, 0.5f, 1.35f);
                Posts(hand, bell - along * BellM * 2 * _m, bell + along * BellM * 2 * _m, 2, hand.Wood);
                DrawLine(bell - along * BellM * 2 * _m, bell + along * BellM * 2 * _m, hand.Wood, Mathf.Max(0.2f * _m, 1), true);
                DrawCircle(bell, Mathf.Max(BellM * _m / 2, 1), hand.Straw);
                break;
            case Manners.Northern or Manners.Soviet when !church:
                Bulb(hand, Within(q, 0.5f, 0.5f), wide * 0.3f, hand.Copper);
                foreach (var (u, v) in new[] { (0.3f, 0.22f), (0.3f, 0.78f), (0.7f, 0.22f), (0.7f, 0.78f) }) Bulb(hand, Within(q, u, v), wide * 0.13f, hand.Copper);
                break;
            case Manners.Continental or Manners.Western when !church:
                Tower(hand, Within(q, 0.55f, 0.5f), along, across, wide * 0.3f, hand.Slate, 1);
                Bulb(hand, Within(q, 0.55f, 0.5f), wide * 0.1f, hand.Copper);
                break;
            case Manners.Egyptian:
                foreach (var (near, far) in new[] { (0f, 0.43f), (0.57f, 1f) })
                {
                    var pylon = Band(q, 1, 1 + PorchDeep, near, far);
                    if (hand.Shaded) Filled(Cast(pylon, Mathf.Min(wide * 0.1f, ShadeMostPx * 1.5f)), hand.Ink with { A = 0.6f });
                    Filled(pylon, hand.Lime.Darkened(0.1f));
                    Filled(Shrunk(pylon, 0.28f), hand.Lime.Lightened(0.05f));
                    Lined(pylon, hand, 0.9f);
                }
                foreach (float v in new[] { 0.3f, 0.7f }) Spire(hand, Within(q, 1 + PorchDeep * 2.2f, v), hand.Stone.Lightened(0.1f));
                break;
            case Manners.Roman:
                Posts(hand, Within(q, 1 + PorchDeep / 2, 0.1f), Within(q, 1 + PorchDeep / 2, 0.9f), 6, hand.Lime);
                Inked(Within(q, 1, 0.06f), Within(q, 1, 0.94f), hand, 0.8f);
                foreach (float v in new[] { 0.05f, 0.95f }) DrawCircle(Within(q, 1 + PorchDeep, v), Mathf.Max(PostDotM * _m * 0.8f, 1), hand.Stone.Darkened(0.2f));
                break;
            case Manners.Tribal:
                foreach (float v in new[] { 0.15f, 0.85f }) Bulb(hand, Within(q, 1 + PorchDeep / 2, v), Mathf.Max(0.7f * _m, 1.2f), hand.Stone);
                Posts(hand, Within(q, 1 + PorchDeep * 1.6f, 0.3f), Within(q, 1 + PorchDeep * 1.6f, 0.7f), 3, hand.Wood);
                break;
        }
    }

    protected bool Crested(BuiltHand hand, Vector2[] q)
    {
        if (q.Length != 4) return false;
        Vector2 along = (q[1] - q[0]).Normalized(), across = (q[3] - q[0]).Normalized(), middle = Within(q, 0.5f, 0.5f);
        float wide = (q[3] - q[0]).Length();
        switch (_adorners)
        {
            case Manners.Ottoman:
                Bulb(hand, middle, wide * 0.3f, hand.Lead);
                return true;
            case Manners.NorthAfrican:
                Bulb(hand, middle, wide * 0.26f, hand.Lime);
                return true;
            case Manners.Chinese or Manners.Japanese or Manners.Nippon:
                Tower(hand, middle, along, across, wide * 0.6f, hand.Kawara, 2);
                return true;
            case Manners.American:
                Tower(hand, middle, along, across, wide * 0.3f, hand.Lime, 1);
                Bulb(hand, middle, wide * 0.09f, hand.Lead);
                return true;
            case Manners.Northern or Manners.Soviet:
                Bulb(hand, middle, wide * 0.24f, hand.Copper);
                return true;
            case Manners.Persian:
                foreach (var (u, v) in new[] { (0f, 0f), (0f, 1f), (1f, 0f), (1f, 1f) }) Tower(hand, Within(q, u, v), along, across, wide * 0.18f, hand.Lime.Darkened(0.1f), 1);
                return true;
            default:
                return false;
        }
    }

    protected bool Railed(BuiltHand hand, Vector2[] px)
    {
        if (_culture != Manners.American || ZigzagM * _m < ZigzagFromPx) return false;
        var zigzag = Stepped(px, ZigzagM * _m).Select((step, i) => step.At + step.Along.Orthogonal() * (ZigzagSwingM * _m * (i % 2 == 0 ? 1 : -1))).ToArray();
        if (zigzag.Length > 1) DrawPolyline(zigzag, hand.Wood, Mathf.Max(0.25f * _m, 1), true);
        return true;
    }

    protected bool Monument(BuiltHand hand, string cls, Vector2 at, Vector2 across)
    {
        switch (cls)
        {
            case "mound":
                float mound = True(6.5f, 2.4f);
                if (hand.Shaded) DrawCircle(at + Sunless * mound * 0.4f, mound, hand.Ink with { A = 0.3f });
                DrawCircle(at, mound, hand.Earth.Darkened(0.18f));
                DrawCircle(at - Sunless * mound * 0.18f, mound * 0.72f, hand.Earth);
                DrawCircle(at - Sunless * mound * 0.34f, mound * 0.34f, hand.Earth.Lightened(0.12f));
                DrawArc(at, mound, 0, Mathf.Tau, 22, hand.Ink with { A = 0.6f }, BuiltHand.LeastPx, true);
                return true;
            case "ovoo":
                float cairn = True(2.4f, 1.8f);
                if (hand.Shaded) DrawCircle(at + Sunless * cairn * 0.4f, cairn, hand.Ink with { A = 0.3f });
                DrawCircle(at, cairn, hand.Stone.Darkened(0.16f));
                DrawCircle(at - Sunless * cairn * 0.25f, cairn * 0.62f, hand.Stone);
                DrawArc(at, cairn, 0, Mathf.Tau, 14, hand.Ink with { A = 0.6f }, BuiltHand.LeastPx, true);
                DrawLine(at, at - Sunless * cairn * 1.7f, hand.Wood, Mathf.Max(BuiltHand.LeastPx, cairn * 0.16f), true);
                DrawCircle(at - Sunless * cairn * 1.7f, cairn * 0.3f, hand.Wet.Lightened(0.5f));
                return true;
            case "torii":
                Gate(hand, at, across, True(2.6f, 2.4f));
                return true;
            default:
                return false;
        }
    }
}
