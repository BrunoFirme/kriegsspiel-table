using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

abstract partial class BuiltProps
{
    const float PuddleM = 1.3f, PuddleRimM = 0.3f, DonkeyM = 1.5f, DonkeyWideM = 0.5f, CamelM = 2.6f, CamelWideM = 0.75f, SteppingM = 0.45f, SteppingApartM = 1.25f;
    const int PuddleSides = 9;

    void Puddle(BuiltHand hand, Vector2 at, Vector2 across)
    {
        float reach = PuddleM * (0.6f + 0.9f * Chance(2.2f)) * _m, stretch = 1 + Chance(3.3f);
        var ring = Enumerable.Range(0, PuddleSides).Select(i => across.Rotated(i * Mathf.Tau / PuddleSides) * reach * (0.7f + 0.5f * Chance(i * 1.9f + 4)))
            .Select(p => at + new Vector2(p.X * stretch, p.Y)
            .Rotated(Chance(5.5f) * Mathf.Pi)).ToArray();
        var rim = ring.Select(p => at + (p - at) * (1 + PuddleRimM * _m / reach)).ToArray();
        Filled(rim, hand.Mud.Darkened(0.25f) with { A = 0.7f });
        Stuffed(ring, hand.Pool.Lerp(hand.Mud, 0.55f).Darkened(0.12f), Surface.Water);
    }

    void Laden(BuiltHand hand, Vector2 at, Vector2 across, int count, float longM, float wideM, Color[] coats, bool packs)
    {
        for (int i = 0; i < count; i++)
        {
            var spot = at + across * (i * longM * 1.15f * _m) + across.Orthogonal() * (Chance(i * 2.7f) - 0.5f) * wideM * _m;
            var heading = across.Rotated((Chance(i * 3.1f) - 0.5f) * 0.5f);
            Beast(hand, spot, heading, longM, wideM, coats[i % coats.Length], hand.Ink.Lightened(0.15f));
            if (packs && Chance(i + 6.6f) < 0.7f)
                foreach (int side in new[] { 1, -1 }) DrawCircle(spot + heading.Orthogonal() * side * wideM * 0.55f * _m, wideM * 0.35f * _m, hand.Straw.Darkened(0.1f));
        }
    }

    void Stepping(BuiltHand hand, Vector2 at, Vector2 across)
    {
        for (int i = -1; i <= 1; i++)
        {
            var stone = at + across * i * SteppingApartM * _m;
            var oval = Enumerable.Range(0, 8).Select(k => stone + across.Rotated(k * Mathf.Tau / 8) * SteppingM * _m * (k % 2 == 0 ? 1 : 0.75f)).ToArray();
            Boxed(hand, oval, hand.Stone.Lightened(0.08f), Surface.Hewn);
        }
    }

    protected bool Street(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case Streetlife.Puddle when Seen(PuddleM * 1.4f):
                Puddle(hand, at, across);
                return true;
            case Streetlife.Donkeys when Seen(DonkeyM) && DonkeyM * _m >= BeastFromPx * 2:
                Laden(hand, at, across, 1 + (int)(Chance(7.1f) * 3), DonkeyM, DonkeyWideM, [hand.Stone.Darkened(0.25f), hand.Mud, hand.Stone.Darkened(0.1f)], true);
                return true;
            case Streetlife.Camels when Seen(CamelM) && CamelM * _m >= BeastFromPx * 2:
                Laden(hand, at, across, 1 + (int)(Chance(8.3f) * 4), CamelM, CamelWideM, [hand.Straw.Lerp(hand.Mud, 0.4f), hand.Straw.Lerp(hand.Mud, 0.2f)], true);
                return true;
            case Streetlife.SteppingStones when Seen(SteppingM * 2):
                Stepping(hand, at, across);
                return true;
            case Streetlife.Puddle or Streetlife.Donkeys or Streetlife.Camels or Streetlife.SteppingStones:
                return true;
        }
        return false;
    }
}
