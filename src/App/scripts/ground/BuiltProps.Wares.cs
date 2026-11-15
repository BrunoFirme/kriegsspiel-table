using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

abstract partial class BuiltProps
{
    const float StallM = 3.6f, StallDeepM = 2.4f, ValanceM = 0.35f, BoothDeepM = 2.2f, TableM = 2.6f, TableDeepM = 1, HeapM = 0.32f, RugM = 2.3f, RugDeepM = 1.5f;
    const float BasketM = 0.36f, ParasolM = 1.5f, PenM = 5.6f, PenDeepM = 3.8f, KioskM = 1.3f, BarrowM = 1.8f, BarrowDeepM = 0.8f, BorderShare = 0.14f;
    const int Ribs = 8, KioskSides = 8;

    bool Eastern => _culture is Manners.Saracen or Manners.Persian or Manners.Ottoman or Manners.NorthAfrican;

    Color[] Dyes(BuiltHand hand) => _culture switch
    {
        Manners.Saracen or Manners.Persian or Manners.Ottoman or Manners.NorthAfrican => [hand.Brick, hand.Slate.Lerp(hand.Wet, 0.5f), hand.Straw, hand.Copper, hand.Cloth, hand.Tile.Darkened(0.2f)],
        Manners.Roman or Manners.Byzantine => [hand.Cloth, hand.Brick.Lightened(0.2f), hand.Pantile, hand.Cloth.Lerp(hand.Tile, 0.5f)],
        Manners.Egyptian or Manners.Pacific or Manners.Tribal => [hand.Cloth, hand.Straw, hand.Palm, hand.Cloth.Lerp(hand.Mud, 0.3f)],
        Manners.Chinese or Manners.Japanese or Manners.Nippon => [hand.Slate.Lerp(hand.Wet, 0.4f), hand.Cloth, hand.Kawara.Lightened(0.2f), hand.Pantile.Lerp(hand.Straw, 0.5f)],
        Manners.Western or Manners.American => [hand.Cloth, hand.Signal.Lerp(hand.Cloth, 0.2f), hand.Wet.Lerp(hand.Cloth, 0.3f), hand.Turf, hand.Straw],
        Manners.Soviet => [hand.Cloth, hand.Tin, hand.Cloth.Darkened(0.15f)],
        _ => [hand.Cloth, hand.Cloth.Lerp(hand.Brick, 0.35f), hand.Cloth.Lerp(hand.Slate, 0.35f), hand.Cloth.Lerp(hand.Straw, 0.4f), hand.Cloth.Darkened(0.12f)],
    };

    Color Dye(BuiltHand hand, float salt)
    {
        var dyes = Dyes(hand);
        return dyes[(int)(Chance(salt) * dyes.Length) % dyes.Length];
    }

    float Striped => _culture switch
    {
        Manners.Western => 0.7f,
        Manners.Continental or Manners.American or Manners.Ottoman => 0.3f,
        Manners.Byzantine or Manners.Roman => 0.12f,
        _ => 0,
    };

    void Bands(Vector2[] box, Color band, int count, float share)
    {
        for (int i = 0; i < count; i++)
        {
            float from = (i + 0.5f - share / 2) / count, to = from + share / count;
            Stuffed([box[0].Lerp(box[1], from), box[0].Lerp(box[1], to), box[3].Lerp(box[2], to), box[3].Lerp(box[2], from)], band, Surface.Cloth);
        }
    }

    void Heaps(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along, float longM, int count, float salt)
    {
        Color[] crops = [hand.Tree, hand.Straw, hand.Brick, hand.Pantile, hand.Turf, hand.Mud.Lightened(0.15f)];
        for (int i = 0; i < count; i++)
        {
            var spot = at + across * ((i + 0.5f) / count - 0.5f) * longM * _m + along * (Chance(salt + i) - 0.5f) * 0.3f * _m;
            DrawCircle(spot, HeapM * _m * (0.8f + 0.4f * Chance(salt + i * 1.3f)), crops[(int)(Chance(salt + i * 2.1f) * crops.Length) % crops.Length]);
        }
    }

    void Stall(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        var awning = Box(at, across, along, StallM, StallDeepM);
        bool reed = Eastern || _culture is Manners.Egyptian or Manners.Pacific;
        if (reed && Chance(4.4f) < 0.5f)
        {
            Boxed(hand, awning, hand.Straw, Surface.Thatch);
            return;
        }
        var cloth = Dye(hand, 3.1f);
        Boxed(hand, awning, cloth, Surface.Cloth);
        if (Chance(6.2f) < Striped)
        {
            var band = _culture == Manners.Western ? Dye(hand, 7.7f) : cloth.Darkened(0.35f);
            Bands(awning, band == cloth ? cloth.Darkened(0.3f) : band, 3 + (int)(Chance(8.8f) * 3), 0.5f);
        }
        else if (Chance(2.7f) < 0.35f) Stuffed([awning[0], awning[1], awning[1].Lerp(awning[2], 0.5f), awning[0].Lerp(awning[3], 0.5f)], Dye(hand, 9.9f), Surface.Cloth);
        Stuffed([awning[2], awning[3], awning[3] - along * ValanceM * _m, awning[2] - along * ValanceM * _m], cloth.Darkened(0.2f), Surface.Cloth);
        DrawPolyline(awning.Append(awning[0]).ToArray(), hand.Ink, BuiltHand.LeastPx, true);
    }

    void Booth(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        var shed = Box(at, across, along, StallM, BoothDeepM);
        var roof = _culture is Manners.Rus or Manners.Northern ? hand.Plank : Chance(1.9f) < 0.5f ? hand.Shingle : hand.Thatch;
        Boxed(hand, shed, roof, roof == hand.Thatch ? Surface.Thatch : Surface.Shingle);
        DrawLine(shed[0].Lerp(shed[3], 0.62f), shed[1].Lerp(shed[2], 0.62f), hand.Ink with { A = 0.55f }, BuiltHand.LeastPx, true);
        var flap = Box(at + along * (BoothDeepM / 2 + 0.3f) * _m, across, along, StallM * 0.8f, 0.55f);
        Boxed(hand, flap, hand.Wood.Lightened(0.12f), Surface.Planks, shadow: false);
        Heaps(hand, at + along * (BoothDeepM / 2 + 0.3f) * _m, across, along, StallM * 0.7f, 3, 12.5f);
    }

    void Trestle(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        Boxed(hand, Box(at, across, along, TableM, TableDeepM), hand.Wood.Lightened(0.08f), Surface.Planks);
        Heaps(hand, at, across, along, TableM * 0.85f, 3 + (int)(Chance(5.3f) * 3), 21.7f);
        if (Chance(3.3f) < 0.4f) Boxed(hand, Box(at - along * 0.9f * _m, across, along, TableM * 0.9f, 0.5f), Dye(hand, 11.1f), Surface.Cloth, shadow: false);
    }

    void Rug(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along, float salt)
    {
        var rug = Box(at, across, along, RugM, RugDeepM);
        bool mat = !Eastern;
        var field = mat ? hand.Straw.Lerp(hand.Palm, Chance(salt) * 0.5f) : Dye(hand, salt);
        Boxed(hand, rug, field, mat ? Surface.Thatch : Surface.Cloth, shadow: false);
        if (mat) return;
        var inner = rug.Select(p => at + (p - at) * (1 - BorderShare * 2)).ToArray();
        DrawPolyline(inner.Append(inner[0]).ToArray(), Dye(hand, salt + 1.7f), Mathf.Max(BuiltHand.LeastPx, 0.12f * _m), true);
        float r = RugDeepM * 0.22f * _m;
        DrawColoredPolygon([at - across * r * 1.4f, at - along * r, at + across * r * 1.4f, at + along * r], Dye(hand, salt + 3.3f));
    }

    void Basket(BuiltHand hand, Vector2 at, float salt)
    {
        Color[] filled = [hand.Straw.Lightened(0.15f), hand.Brick, hand.Pantile, hand.Copper, hand.Mud, hand.Tree, hand.Stone];
        float r = BasketM * _m * (0.85f + 0.3f * Chance(salt));
        Tub(hand, at, BasketM * (0.85f + 0.3f * Chance(salt)), hand.Straw.Darkened(0.12f), Surface.Thatch);
        DrawCircle(at, r * 0.7f, filled[(int)(Chance(salt + 0.9f) * filled.Length) % filled.Length]);
    }

    void Parasol(BuiltHand hand, Vector2 at, Vector2 across)
    {
        float r = ParasolM * _m;
        var thatched = _culture is Manners.Pacific or Manners.Tribal;
        var canopy = thatched ? hand.Palm : Dye(hand, 13.3f);
        var ring = Enumerable.Range(0, Ribs).Select(i => at + across.Rotated(i * Mathf.Tau / Ribs) * r).ToArray();
        if (hand.Shaded) DrawColoredPolygon(ring.Select(p => p + Vector2.One * Mathf.Min(0.4f * _m, 3)).ToArray(), hand.Ink with { A = 0.3f });
        Stuffed(ring, canopy, thatched ? Surface.Thatch : Surface.Cloth);
        foreach (var rib in ring) DrawLine(at, rib, canopy.Darkened(0.3f), BuiltHand.LeastPx, true);
        DrawPolyline(ring.Append(ring[0]).ToArray(), hand.Ink, BuiltHand.LeastPx, true);
    }

    void Hurdles(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        var pen = Box(at, across, along, PenM, PenDeepM);
        Filled(pen, hand.Mud with { A = 0.35f });
        DrawPolyline(pen.Append(pen[0]).ToArray(), hand.Wood.Darkened(0.2f), Mathf.Max(BuiltHand.LeastPx, 0.15f * _m), true);
        bool horses = _culture is Manners.Mongol or Manners.American;
        Color[] coats = horses ? [hand.Wood, hand.Mud, hand.Ink.Lightened(0.2f)] : _culture is Manners.Rus or Manners.Tribal ? [hand.Mud, hand.Cloth, hand.Wood]
            : [hand.Cloth, hand.Cloth.Darkened(0.1f)];
        Herd(hand, at, across, 3 + (int)(Chance(4.9f) * 4), PenDeepM * 0.3f, horses ? 2.2f : 1.3f, horses ? 0.7f : 0.7f, coats, hand.Ink.Lightened(0.15f));
    }

    void Kiosk(BuiltHand hand, Vector2 at, Vector2 across)
    {
        float r = KioskM * _m;
        var ring = Enumerable.Range(0, KioskSides).Select(i => at + across.Rotated(i * Mathf.Tau / KioskSides) * r).ToArray();
        var roof = _culture == Manners.Western ? hand.Copper : hand.Tin;
        if (hand.Shaded) DrawColoredPolygon(ring.Select(p => p + Vector2.One * Mathf.Min(0.5f * _m, 3)).ToArray(), hand.Ink with { A = 0.3f });
        Stuffed(ring, roof, Surface.Lead);
        foreach (var corner in ring) DrawLine(at, corner, roof.Darkened(0.25f), BuiltHand.LeastPx, true);
        DrawPolyline(ring.Append(ring[0]).ToArray(), hand.Ink, BuiltHand.LeastPx, true);
        DrawCircle(at, Mathf.Max(0.2f * _m, 1), hand.Ink);
    }

    void Barrow(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        Boxed(hand, Box(at, across, along, BarrowM, BarrowDeepM), hand.Wood, Surface.Planks);
        DrawLine(at + across * (BarrowM / 2) * _m + along * 0.3f * _m, at + across * (BarrowM / 2 + 0.9f) * _m + along * 0.3f * _m, hand.Ink, BuiltHand.LeastPx, true);
        DrawLine(at + across * (BarrowM / 2) * _m - along * 0.3f * _m, at + across * (BarrowM / 2 + 0.9f) * _m - along * 0.3f * _m, hand.Ink, BuiltHand.LeastPx, true);
        DrawLine(at - across * (BarrowM / 2 + 0.1f) * _m - along * 0.5f * _m, at - across * (BarrowM / 2 + 0.1f) * _m + along * 0.5f * _m, hand.Ink, Mathf.Max(0.16f * _m, 1), true);
        Heaps(hand, at, across, along, BarrowM * 0.7f, 2, 31.1f);
    }

    protected const float AwningM = 1.3f;

    void Fronted(BuiltHand hand, Vector2[] front, Color fill, Surface stuff)
    {
        Stuffed(front, fill, stuff);
        DrawPolyline(front.Append(front[0]).ToArray(), hand.Ink with { A = 0.7f }, BuiltHand.LeastPx, true);
    }

    protected void ShopFront(BuiltHand hand, Vector2 mid, Vector2 along, Vector2 outward, float wide)
    {
        Vector2 a = mid - along * wide * 0.36f, b = mid + along * wide * 0.36f, reach = outward * AwningM * _m;
        Vector2[] Strip(float share) => [a, b, b + reach * share, a + reach * share];
        switch (_culture)
        {
            case Manners.Gothic or Manners.Rus or Manners.Northern or Manners.Tribal:
                Fronted(hand, Strip(0.75f), hand.Wood.Lightened(0.1f), Surface.Planks);
                Heaps(hand, (a + b) / 2 + reach * 0.4f, along, outward, wide / _m * 0.5f, 3, 61.1f);
                return;
            case Manners.Roman:
                Fronted(hand, [a, a.Lerp(b, 0.65f), a.Lerp(b, 0.65f) + reach * 0.45f, a + reach * 0.45f], hand.Stone.Lightened(0.06f), Surface.Hewn);
                foreach (float at in new[] { 0.18f, 0.42f }) DrawCircle(a.Lerp(b, at) + reach * 0.22f, 0.22f * _m, hand.Ink with { A = 0.6f });
                return;
            case Manners.Byzantine or Manners.Soviet:
                Fronted(hand, Strip(0.6f), hand.Plank, Surface.Shingle);
                return;
            case Manners.Saracen or Manners.Persian or Manners.Egyptian or Manners.NorthAfrican or Manners.Pacific:
                Fronted(hand, Strip(1), Chance(44.4f) < 0.6f ? hand.Straw : hand.Palm, Surface.Thatch);
                return;
            case Manners.Chinese or Manners.Japanese or Manners.Nippon:
                Fronted(hand, Strip(0.35f), Chance(45.5f) < 0.7f ? hand.Slate.Lerp(hand.Wet, 0.4f) : hand.Cloth, Surface.Cloth);
                if (Chance(46.6f) < 0.5f) DrawCircle(a + reach * 0.5f, 0.3f * _m, hand.Signal);
                return;
        }
        var cloth = Dye(hand, 47.7f);
        Fronted(hand, Strip(1), cloth, Surface.Cloth);
        if (Chance(48.8f) < Striped) Bands(Strip(1), cloth == hand.Cloth ? Dye(hand, 49.9f).Darkened(0.1f) : hand.Cloth, 4, 0.5f);
    }

    protected bool Ware(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case Markets.Stall when Seen(StallM):
                Stall(hand, at, across, along);
                return true;
            case Markets.Booth when Seen(StallM):
                Booth(hand, at, across, along);
                return true;
            case Markets.Trestle when Seen(TableM):
                Trestle(hand, at, across, along);
                return true;
            case Markets.Rugs when Seen(RugM):
                int rugs = 2 + (int)(Chance(14.4f) * 2);
                for (int i = 0; i < rugs; i++) Rug(hand, at + across * (i - (rugs - 1) / 2f) * 1.1f * _m
                    + along * (Chance(i + 0.5f) - 0.5f) * 0.6f * _m, across.Rotated((Chance(i * 2.2f) - 0.5f) * 0.4f),
                    along.Rotated((Chance(i * 2.2f) - 0.5f) * 0.4f), 17 + i * 4.1f);
                return true;
            case Markets.Baskets when Seen(BasketM * 4):
                int baskets = 4 + (int)(Chance(15.5f) * 4);
                for (int i = 0; i < baskets; i++) Basket(hand, at + across * ((i % 4) - 1.5f) * BasketM * 2.3f * _m + along * ((i / 4) - 0.5f) * BasketM * 2.4f * _m, 40 + i * 1.9f);
                return true;
            case Markets.Parasol when Seen(ParasolM * 2):
                Parasol(hand, at, across);
                return true;
            case Markets.Hurdles when Seen(PenM):
                Hurdles(hand, at, across, along);
                return true;
            case Markets.Kiosk when Seen(KioskM * 2):
                Kiosk(hand, at, across);
                return true;
            case Markets.Barrow when Seen(BarrowM):
                Barrow(hand, at, across, along);
                return true;
            case Markets.Stall or Markets.Booth or Markets.Trestle or Markets.Rugs or Markets.Baskets or Markets.Parasol or Markets.Hurdles or Markets.Kiosk or Markets.Barrow:
                return true;
        }
        return false;
    }
}
