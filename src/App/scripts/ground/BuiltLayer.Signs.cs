using Godot;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float TreeM = 3.4f, TreeLeastPx = 1.3f, LobedFromPx = 7, TrunkM = 0.35f, RickM = 2.4f, RickLeastPx = 1.1f, WellM = 1.2f, WellLeastPx = 1.4f, TowerM = 3.3f, TowerLeastPx = 1.6f;
    const float CrossM = 4, CrossLeastPx = 3, ShrineM = 1.8f, ShrineLeastPx = 2, FlagM = 7, FlagLeastPx = 4, WheelM = 2.4f, WheelLeastPx = 2, SailM = 8, SailLeastPx = 3.2f;
    const float DeckLongM = 7, DeckHalfM = 3.6f, DeckLeastPx = 2.6f, FordM = 8, RingFromPx = 5;
    const int Lobes = 6;

    void Crown(BuiltHand hand, Vector2 at, float r)
    {
        if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.28f, r, hand.Ink with { A = 0.3f });
        if (r < LobedFromPx)
        {
            DrawCircle(at, r, hand.Tree);
            if (r >= TreeLeastPx * 1.6f) DrawArc(at, r, 0, Mathf.Tau, 14, hand.Ink with { A = 0.7f }, BuiltHand.LeastPx * 0.8f, true);
            return;
        }
        float turn = Chance(at, 0.7f) * Mathf.Tau;
        for (int i = 0; i < Lobes; i++)
        {
            var lobe = at + Vector2.Right.Rotated(turn + i * Mathf.Tau / Lobes) * (r * 0.5f);
            DrawCircle(lobe, r * (0.5f + 0.12f * Chance(lobe, 2.2f)), hand.Tree);
        }
        DrawCircle(at, r * 0.62f, hand.Tree);
        DrawCircle(at - Vector2.One * r * 0.22f, r * 0.42f, hand.Tree.Lightened(0.14f));
        DrawCircle(at, Mathf.Max(TrunkM * _m, 1), hand.Ink with { A = 0.7f });
    }

    void Tower(BuiltHand hand, Vector2 at)
    {
        float r = True(TowerM, TowerLeastPx);
        if (r < RingFromPx)
        {
            DrawCircle(at, r, hand.Ink);
            return;
        }
        DrawCircle(at, r, hand.Ink);
        DrawCircle(at, r - Mathf.Max(0.5f * _m, 1), hand.Stone);
        DrawArc(at, r * 0.55f, 0, Mathf.Tau, 16, hand.Ink with { A = 0.6f }, BuiltHand.LeastPx, true);
    }

    void Deck(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        float reach = True(DeckLongM, DeckLeastPx * 1.4f), half = True(DeckHalfM, DeckLeastPx);
        Vector2 run = across * reach, side = along * half;
        if (half >= RingFromPx) DrawColoredPolygon([at - run - side, at + run - side, at + run + side, at - run + side], hand.Stone);
        foreach (int bank in new[] { 1, -1 })
        {
            Vector2 a = at - across * reach + along * (half * bank), b = at + across * reach + along * (half * bank);
            DrawLine(a, b, hand.Ink, Medium, true);
            DrawLine(a, a - across * (half * 0.5f) + along * (half * 0.5f * bank), hand.Ink, Medium, true);
            DrawLine(b, b + across * (half * 0.5f) + along * (half * 0.5f * bank), hand.Ink, Medium, true);
        }
    }

    void Cross(BuiltHand hand, Vector2 at, float r, float width)
    {
        DrawLine(at - Vector2.Down * r, at + Vector2.Down * r * 0.6f, hand.Ink, width, true);
        DrawLine(at - Vector2.Right * r * 0.5f - Vector2.Down * r * 0.35f, at + Vector2.Right * r * 0.5f - Vector2.Down * r * 0.35f, hand.Ink, width, true);
    }

    void Sign(BuiltHand hand, BuiltPainter.Shape s, Vector2 at)
    {
        var across = Vector2.Right.Rotated(Mathf.DegToRad(s.Turn / 10f));
        var along = across.Orthogonal();
        switch (s.Class)
        {
            case "tree":
                Crown(hand, at, True(TreeM * (0.8f + 0.4f * Chance(at, 5.5f)), TreeLeastPx));
                break;
            case "tower":
                Tower(hand, at);
                break;
            case "bridge":
                Deck(hand, at, across, along);
                break;
            case "ford":
                DrawDashedLine(at - across * True(FordM, DeckLeastPx), at + across * True(FordM, DeckLeastPx), hand.Ink, Medium, Medium * 2, true, true);
                break;
            case "well":
                float well = True(WellM, WellLeastPx);
                if (well >= RingFromPx) DrawCircle(at, well, hand.Wet);
                DrawArc(at, well, 0, Mathf.Tau, 16, hand.Ink, Mathf.Max(Medium, 0.35f * _m), true);
                break;
            case "rick":
                float rick = True(RickM, RickLeastPx);
                if (hand.Shaded) DrawCircle(at + Vector2.One * rick * 0.25f, rick, hand.Ink with { A = 0.28f });
                DrawCircle(at, rick, hand.Straw);
                if (rick >= RingFromPx) DrawCircle(at - Vector2.One * rick * 0.2f, rick * 0.45f, hand.Straw.Lightened(0.18f));
                DrawArc(at, rick, 0, Mathf.Tau, 12, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
                break;
            case "mill":
                float sail = True(SailM, SailLeastPx);
                DrawLine(at - new Vector2(sail, sail), at + new Vector2(sail, sail), hand.Ink, Medium, true);
                DrawLine(at - new Vector2(sail, -sail), at + new Vector2(sail, -sail), hand.Ink, Medium, true);
                DrawCircle(at, sail * 0.3f, hand.Signal);
                break;
            case "cross" when hand.Crosses:
                Cross(hand, at, True(CrossM, CrossLeastPx), Medium);
                break;
            case "cross":
                break;
            case "shrine" when hand.Crosses:
                Cross(hand, at, True(ShrineM, ShrineLeastPx), Fine);
                break;
            case "shrine":
                float stone = True(ShrineM, ShrineLeastPx);
                DrawRect(new Rect2(at - new Vector2(stone * 0.16f, stone * 0.5f), new Vector2(stone * 0.32f, stone * 0.62f)), hand.Ink);
                break;
            case "flag":
                float staff = True(FlagM, FlagLeastPx);
                DrawLine(at, at - Vector2.Down * staff, hand.Ink, Fine, true);
                DrawColoredPolygon([at - Vector2.Down * staff, at - Vector2.Down * staff * 0.5f, at + Vector2.Right * staff * 0.7f - Vector2.Down * staff * 0.75f], hand.Signal);
                break;
            case "wheel":
                float wheel = True(WheelM, WheelLeastPx);
                DrawArc(at, wheel, 0, Mathf.Tau, 14, hand.Wet, Medium, true);
                DrawLine(at - across * wheel, at + across * wheel, hand.Wet, Fine, true);
                DrawLine(at - along * wheel, at + along * wheel, hand.Wet, Fine, true);
                break;
            default:
                DrawCircle(at, True(1, 1.2f), hand.Ink);
                break;
        }
    }
}
