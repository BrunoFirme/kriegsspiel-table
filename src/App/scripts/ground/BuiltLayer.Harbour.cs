using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float ShipLongM = 24, ShipBeamM = 6.4f, GalleyLongM = 30, GalleyBeamM = 4.6f, CraneM = 3, JibM = 6, BeaconM = 2.6f, ShipFromM = 8;
    const int Oars = 4;

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
            case "ship" or "crane" or "beacon":
                DrawCircle(at, True(1.2f, 1.4f), hand.Ink);
                return true;
            default:
                return false;
        }
    }
}
