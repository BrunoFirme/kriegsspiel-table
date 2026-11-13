using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float ShipLongM = 24, ShipBeamM = 6.4f, GalleyLongM = 30, GalleyBeamM = 4.6f, CraneM = 3, JibM = 6, BeaconM = 2.6f, ShipFromM = 8;
    const int Oars = 4, PanCols = 4;

    void Ship(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        bool galley = Manners.Old(_manner);
        Vector2 bow = across * ((galley ? GalleyLongM : ShipLongM) / 2 * _m), beam = along * ((galley ? GalleyBeamM : ShipBeamM) / 2 * _m);
        var hull = Hull(at, bow, beam);
        if (hand.Shaded) Plain(hull.Select(p => p + Vector2.One * Mathf.Min(0.8f * _m, 4)).ToArray(), hand.Ink with { A = 0.3f });
        for (int i = -Oars; galley && i <= Oars; i++)
            foreach (int side in new[] { 1, -1 }) Edge(at + bow * (i * 0.14f) + beam * side, at + bow * (i * 0.14f - 0.06f) + beam * (side * 2.1f), hand, 0.7f);
        Plain(hull, hand.Wood.Darkened(0.12f));
        Covered(Hull(at, bow * 0.9f, beam * 0.74f), hand.Wood.Lightened(0.22f), Surface.Planks, at, along);
        Ringed(hull, hand, 0.9f);
        Boxed(hand, Box(at - bow * 0.45f, across, along, 2.4f, 1.8f), hand.Wood, Surface.Planks, shadow: false);
        foreach (float mast in galley ? new[] { 0.1f } : new[] { 0.42f, -0.2f })
        {
            var foot = at + bow * mast;
            if (!galley) DrawLine(foot - beam * 1.1f, foot + beam * 1.1f, hand.Cloth, Mathf.Max(0.45f * _m, 1.2f), true);
            Edge(foot - beam * 1.25f, foot + beam * 1.25f, hand, 0.8f);
            DrawCircle(foot, Mathf.Max(0.28f * _m, 1), hand.Ink);
        }
        if (!galley) Edge(at + bow, at + bow * 1.28f, hand, 0.9f);
    }

    void Worked(BuiltHand hand, string cls, Vector2[] px)
    {
        var look = hand.Area(cls);
        var centre = Mid(px);
        if (cls == "quarry")
        {
            for (int tier = 0; tier < 3; tier++)
            {
                var ring = px.Select(p => centre + (p - centre) * (1 - tier * 0.24f) + Sunless * (tier * 0.5f * _m)).ToArray();
                Stuffed(ring, look.Fill.Darkened(tier * 0.1f), Surface.Hewn);
                Ringed(ring, hand, tier == 0 ? 0.8f : 0.45f);
            }
            return;
        }
        Stuffed(px, look.Fill, Surface.Hewn);
        for (int i = 0; i < PanCols * 2; i++)
        {
            float from = (i % PanCols + 0.08f) / PanCols, to = (i % PanCols + 0.92f) / PanCols, near = i < PanCols ? 0.08f : 0.54f;
            Vector2[] pan = [px[0].Lerp(px[1], from).Lerp(px[3].Lerp(px[2], from), near), px[0].Lerp(px[1], to).Lerp(px[3].Lerp(px[2], to), near),
                px[0].Lerp(px[1], to).Lerp(px[3].Lerp(px[2], to), near + 0.38f), px[0].Lerp(px[1], from).Lerp(px[3].Lerp(px[2], from), near + 0.38f)];
            Stuffed(pan, hand.Wet.Lerp(hand.Lime, 0.45f + 0.12f * (i % 3)), Surface.Water);
            if (i % 3 == 1) Disc(pan[2].Lerp(pan[0], 0.25f), 0.7f * _m, hand.Lime.Lightened(0.3f), Surface.Rounded);
        }
        Ringed(px, hand, 0.7f);
    }

    bool Afloat(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case "ship" when Seen(ShipFromM):
                Ship(hand, at, across, along);
                return true;
            case "crane" when Seen(CraneM):
                Boxed(hand, Box(at, across, along, CraneM, CraneM), hand.Wood, Surface.Planks);
                DrawCircle(at, CraneM * 0.32f * _m, hand.Wood.Lightened(0.15f));
                DrawLine(at, at + across * (JibM * _m), hand.Ink, Mathf.Max(0.3f * _m, 1.2f), true);
                DrawCircle(at + across * (JibM * _m), Mathf.Max(0.25f * _m, 1), hand.Ink);
                return true;
            case "beacon" when Seen(BeaconM):
                float r = BeaconM * _m;
                if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.4f, r, hand.Ink with { A = 0.35f });
                DrawCircle(at, r, hand.Stone);
                DrawArc(at, r, 0, Mathf.Tau, 16, hand.Ink, BuiltHand.LeastPx, true);
                DrawCircle(at, r * 0.58f, hand.Stone.Lightened(0.16f));
                DrawCircle(at, r * 0.28f, hand.Signal);
                return true;
            case "sawpit" when Seen(3):
                DrawColoredPolygon(Box(at, across, along, 4.2f, 1.3f), hand.Ink with { A = 0.75f });
                foreach (float end in new[] { -1.5f, 1.5f }) DrawLine(at + across * (end * _m) - along * 1.1f * _m, at + across * (end * _m)
                    + along * 1.1f * _m, hand.Wood, Mathf.Max(0.2f * _m, 1), true);
                Boxed(hand, Box(at, across, along, 5.6f, 0.5f), hand.Wood.Lightened(0.2f), Surface.Planks, shadow: false);
                Disc(at + along * 1.6f * _m, 0.8f * _m, hand.Straw.Lightened(0.15f), Surface.Rounded);
                return true;
            case "clamp" when Seen(2.6f):
                Dome(hand, at, 1.9f * _m, hand.Ink.Lerp(hand.Wood, 0.55f));
                for (int i = 1; hand.Shaded && i <= 5; i++) DrawCircle(at + Downwind * (i * 0.9f * _m), (0.5f + 0.2f * i) * _m, hand.Stone.Lightened(0.3f) with { A = SmokeInk * (6 - i) });
                return true;
            case "peat" when Seen(3):
                for (int i = 0; i < 3; i++) DrawColoredPolygon(Box(at + along * ((i - 1) * 1.5f * _m), across, along, 5, 0.9f), hand.Ink.Lerp(hand.Wood, 0.45f));
                for (int i = 0; i < 6; i++) Boxed(hand, Box(at + across * ((3.4f + i % 3 * 0.7f) * _m) + along * ((i / 3 - 0.5f) * 0.8f * _m), across, along, 0.6f, 0.5f), hand.Wood.Darkened(0.2f),
                    Surface.Hewn);
                return true;
            case "reeds" when Seen(1.2f):
                for (int i = 0; i < 3; i++) Tub(hand, at + across * ((i - 1) * 1.1f * _m) + along * ((i % 2) * 0.5f * _m), 0.5f, hand.Straw.Darkened(0.12f), Surface.Thatch);
                return true;
            case "blocks" when Seen(1.2f):
                for (int i = 0; i < 4; i++)
                    Boxed(hand, Box(at + across * ((i % 2 - 0.5f) * 1.6f * _m) + along * ((i / 2 - 0.5f) * 1.2f * _m), across.Rotated(i * 0.3f), along.Rotated(i * 0.3f), 1.2f, 0.75f),
                        hand.Stone.Lightened(0.1f), Surface.Hewn);
                return true;
            case "ship" or "crane" or "beacon" or "sawpit" or "clamp" or "peat" or "reeds" or "blocks":
                DrawCircle(at, True(1.2f, 1.4f), hand.Ink);
                return true;
            default:
                return false;
        }
    }
}
