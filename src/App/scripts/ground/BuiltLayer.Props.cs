using System.Linq;
using Godot;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float PropFromPx = 1.6f, DoorM = 1.2f, DoorFromPx = 2.5f, StepM = 0.55f, HipShare = 0.35f, HipInset = 0.26f, CobbleM = 1.15f, CobbleFromPx = 4, CobbleInk = 0.15f;
    const float MerlonM = 1.9f, MerlonFromPx = 6, PlankM = 0.6f, PlankFromPx = 3, StoneM = 0.45f;
    const int MostCobbles = 5000;

    bool Seen(float metres) => metres * _m >= PropFromPx;

    Vector2[] Box(Vector2 at, Vector2 across, Vector2 along, float longM, float wideM)
    {
        Vector2 run = across * (longM * _m / 2), side = along * (wideM * _m / 2);
        return [at - run - side, at + run - side, at + run + side, at - run + side];
    }

    void Boxed(BuiltHand hand, Vector2[] box, Color fill, bool shadow = true)
    {
        if (shadow && hand.Shaded) DrawColoredPolygon(box.Select(p => p + Vector2.One * Mathf.Min(0.35f * _m, 3)).ToArray(), hand.Ink with { A = 0.3f });
        DrawColoredPolygon(box, fill);
        DrawPolyline(box.Append(box[0]).ToArray(), hand.Ink, BuiltHand.LeastPx, true);
    }

    void Tub(BuiltHand hand, Vector2 at, float metres, Color fill)
    {
        float r = metres * _m;
        if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.3f, r, hand.Ink with { A = 0.3f });
        DrawCircle(at, r, fill);
        DrawArc(at, r, 0, Mathf.Tau, 12, hand.Ink, BuiltHand.LeastPx, true);
    }

    void Door(BuiltHand hand, string cls, Vector2[] px)
    {
        float wide = (cls is "barn" ? 2.2f : cls is "inn" or "public" or "church" or "keep" ? 1.5f : 1) * DoorM * _m;
        if (wide < DoorFromPx || cls is "shed" or "privy" or "steeple" or "tower" or "barracks" or "magazine" or "mill" or "gatehouse") return;
        Vector2 a = cls == "church" ? px[1] : px[0], b = cls == "church" ? px[2] : px[1];
        Vector2 middle = px.Aggregate(Vector2.Zero, (sum, p) => sum + p) / px.Length, at = a.Lerp(b, cls == "church" ? 0.5f : 0.5f + (Chance(1.3f) - 0.5f) * 0.4f);
        Vector2 along = (b - a).Normalized(), outward = along.Orthogonal();
        if (outward.Dot(at - middle) < 0) outward = -outward;
        float step = StepM * _m;
        DrawColoredPolygon([at - along * wide / 2, at + along * wide / 2, at + along * wide / 2 + outward * step, at - along * wide / 2 + outward * step], hand.Stone);
        DrawLine(at - along * wide / 2, at + along * wide / 2, hand.Ink, Mathf.Max(0.22f * _m, 1.2f), true);
    }

    void Hips(BuiltHand hand, Vector2[] px, Vector2 from, Vector2 to, bool longFirst)
    {
        Vector2 a = from.Lerp(to, HipInset), b = to.Lerp(from, HipInset);
        var ink = hand.Ink with { A = RidgeInk };
        DrawLine(a, b, ink, BuiltHand.LeastPx, true);
        foreach (var corner in longFirst ? new[] { px[0], px[3] } : new[] { px[0], px[1] }) DrawLine(a, corner, ink, BuiltHand.LeastPx, true);
        foreach (var corner in longFirst ? new[] { px[1], px[2] } : new[] { px[2], px[3] }) DrawLine(b, corner, ink, BuiltHand.LeastPx, true);
    }

    void Cobbles(BuiltHand hand, Vector2[] px)
    {
        float pitch = CobbleM * _m;
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        int cols = (int)(along.Length() / pitch), rows = (int)(across.Length() / pitch);
        if (pitch < CobbleFromPx || cols < 1 || rows < 1 || cols * rows > MostCobbles) return;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                DrawCircle(px[0] + along * ((c + 0.5f + (r % 2) * 0.5f) / (cols + 0.5f)) + across * ((r + 0.5f) / rows), pitch * 0.2f, hand.Ink with { A = CobbleInk });
    }

    void Merlons(BuiltHand hand, Vector2[] px, float wide)
    {
        if (wide < MerlonFromPx) return;
        foreach (var (at, _) in Stepped(px, MerlonM * _m)) DrawRect(new Rect2(at - Vector2.One * wide * 0.2f, Vector2.One * wide * 0.4f), hand.Ink with { A = 0.5f });
    }

    void Planks(BuiltHand hand, Vector2[] px, float wide)
    {
        if (PlankM * _m < PlankFromPx) return;
        foreach (var (at, along) in Stepped(px, PlankM * _m)) DrawLine(at - along.Orthogonal() * wide / 2, at + along.Orthogonal() * wide / 2, hand.Ink with { A = 0.35f }, BuiltHand.LeastPx, true);
    }

    void Stones(BuiltHand hand, Vector2 at, Vector2 across)
    {
        for (int i = -3; i <= 3; i++) Tub(hand, at + across * (i * 1.2f * _m) + across.Orthogonal() * ((Chance(i) - 0.5f) * 0.5f * _m), StoneM, hand.Stone);
    }

    void Wheels(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along, float longM, float wideM)
    {
        foreach (int end in new[] { 1, -1 })
            foreach (int side in new[] { 1, -1 })
            {
                var hub = at + across * (end * longM * 0.3f * _m) + along * (side * wideM * 0.58f * _m);
                DrawLine(hub - across * 0.45f * _m, hub + across * 0.45f * _m, hand.Ink, Mathf.Max(0.18f * _m, 1), true);
            }
    }

    bool Prop(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case "stall" when Seen(3.2f):
                var awning = Box(at, across, along, 3.2f, 2.2f);
                Boxed(hand, awning, hand.Cloth);
                for (float from = 0; from < 1; from += 0.5f)
                {
                    Vector2 a = awning[0].Lerp(awning[1], from), b = awning[0].Lerp(awning[1], from + 0.25f), c = awning[3].Lerp(awning[2], from + 0.25f), d = awning[3].Lerp(awning[2], from);
                    DrawColoredPolygon([a, b, c, d], hand.Signal with { A = 0.8f });
                }
                DrawPolyline(awning.Append(awning[0]).ToArray(), hand.Ink, BuiltHand.LeastPx, true);
                return true;
            case "cart" when Seen(3.2f):
                Wheels(hand, at, across, along, 3.2f, 1.5f);
                Boxed(hand, Box(at, across, along, 3.2f, 1.5f), hand.Wood);
                DrawLine(at + across * 1.6f * _m, at + across * 3.4f * _m, hand.Ink, Mathf.Max(0.12f * _m, 1), true);
                return true;
            case "barrels" when Seen(1.1f):
                foreach (var off in new[] { across * 0.5f, across * -0.45f + along * 0.3f, along * -0.55f })
                {
                    Tub(hand, at + off * _m, 0.45f, hand.Wood);
                    DrawCircle(at + off * _m, 0.26f * _m, hand.Wood.Lightened(0.2f));
                }
                return true;
            case "crates" when Seen(1.1f):
                Boxed(hand, Box(at - across * 0.5f * _m, across, along, 0.95f, 0.95f), hand.Wood.Lightened(0.15f));
                Boxed(hand, Box(at + across * 0.55f * _m + along * 0.2f * _m, across.Rotated(0.4f), along.Rotated(0.4f), 0.8f, 0.8f), hand.Wood.Lightened(0.25f));
                return true;
            case "trough" when Seen(2):
                Boxed(hand, Box(at, across, along, 2.2f, 0.75f), hand.Stone);
                DrawColoredPolygon(Box(at, across, along, 1.8f, 0.4f), hand.Wet);
                return true;
            case "fountain" when Seen(3):
                Tub(hand, at, 1.8f, hand.Stone);
                DrawCircle(at, 1.35f * _m, hand.Wet);
                Tub(hand, at, 0.35f, hand.Stone);
                return true;
            case "stocks" when Seen(1.8f):
                Boxed(hand, Box(at, across, along, 1.9f, 0.3f), hand.Wood, false);
                foreach (int end in new[] { 1, -1 }) DrawCircle(at + across * (end * 0.95f * _m), Mathf.Max(0.16f * _m, 1), hand.Ink);
                return true;
            case "woodpile" when Seen(2):
                Boxed(hand, Box(at, across, along, 2.2f, 0.8f), hand.Wood.Lightened(0.1f));
                for (int i = -2; i <= 2; i++) DrawCircle(at + across * (i * 0.42f * _m), 0.17f * _m, hand.Wood.Darkened(0.25f));
                return true;
            case "hive" when Seen(0.7f):
                Boxed(hand, Box(at, across, along, 0.7f, 0.7f), hand.Straw);
                return true;
            case "boat" when Seen(4):
                Vector2 bow = across * 2.2f * _m, beam = along * 0.75f * _m;
                Vector2[] hull = [at - bow, at - bow * 0.5f - beam, at + bow * 0.4f - beam, at + bow, at + bow * 0.4f + beam, at - bow * 0.5f + beam];
                Boxed(hand, hull, hand.Wood.Lightened(0.12f), false);
                foreach (float seat in new[] { -0.3f, 0.15f }) DrawLine(at + bow * seat - beam * 0.9f, at + bow * seat + beam * 0.9f, hand.Ink with { A = 0.7f }, BuiltHand.LeastPx, true);
                return true;
            case "gallows" when Seen(2.6f):
                DrawLine(at, at + across * 2.6f * _m, hand.Ink, Mathf.Max(0.2f * _m, 1.2f), true);
                DrawCircle(at, Mathf.Max(0.25f * _m, 1.4f), hand.Ink);
                DrawLine(at + across * 2.2f * _m, at + across * 2.2f * _m + along * 0.7f * _m, hand.Ink, BuiltHand.LeastPx, true);
                return true;
            case "rock" when Seen(1.6f):
                float girth = (0.8f + 0.7f * Chance(3.9f)) * _m;
                var blob = Enumerable.Range(0, 7).Select(i => at + across.Rotated(i * Mathf.Tau / 7) * (girth * (0.75f + 0.4f * Chance(i * 1.7f)))).ToArray();
                Boxed(hand, blob, hand.Stone);
                DrawLine(blob[5], blob[1].Lerp(at, 0.5f), hand.Ink with { A = 0.4f }, BuiltHand.LeastPx, true);
                return true;
            case "gun" when Seen(2.4f):
                Wheels(hand, at - across * 0.2f * _m, across, along, 0, 1.2f);
                Boxed(hand, Box(at - across * 0.5f * _m, across, along, 1.5f, 0.7f), hand.Wood, false);
                DrawLine(at - across * 0.3f * _m, at + across * 1.9f * _m, hand.Ink, Mathf.Max(0.26f * _m, 1.4f), true);
                return true;
            case "anvil" when Seen(0.8f):
                Boxed(hand, Box(at, across, along, 0.8f, 0.35f), hand.Ink, false);
                Tub(hand, at + along * 1.1f * _m, 0.4f, hand.Wood);
                return true;
            case "signboard" when Seen(1):
                DrawCircle(at, Mathf.Max(0.14f * _m, 1), hand.Ink);
                Boxed(hand, Box(at + across * 0.55f * _m, across, along, 1, 0.16f), hand.Signal, false);
                return true;
            case "stall" or "cart" or "barrels" or "crates" or "trough" or "fountain" or "stocks" or "woodpile" or "hive" or "boat" or "gallows" or "rock" or "gun" or "anvil" or "signboard":
                return true;
        }
        return false;
    }
}
