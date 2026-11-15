using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltProps
{
    const float AqueductM = 2.6f, ArchM = 4.2f, AqueductFromPx = 2.4f, TramGaugeM = 1, TramFromPx = 3, QanatM = 2.4f, SphinxM = 4.2f, SphinxWideM = 1.4f, PaifangM = 10;
    const float FireTowerM = 2.6f, LanternM = 0.8f, HitchM = 3.2f, AdColumnM = 0.7f, TheatreTier = 0.16f, OrchestraShare = 0.28f, StageShare = 0.16f, BedShare = 0.26f;
    const float PavingM = 6, SurfacePx = 5, GardenWallM = 0.6f, BoardwalkM = 1.6f, MemorialM = 1.8f, ShelterM = 3.2f, TractorM = 3;
    const int TheatreTiers = 4, Aisles = 5;

    protected void Theatre(BuiltHand hand, Vector2[] px)
    {
        Vector2 centre = (px[0] + px[^1]) / 2, back = (px[px.Length / 2] - centre).Normalized(), across = (px[^1] - px[0]) / 2;
        float r = across.Length();
        if (hand.Shaded) Plain(px.Select(p => p + Vector2.One * Mathf.Min(0.03f * r, 6)).ToArray(), hand.Ink with { A = 0.5f });
        for (int tier = 0; tier < TheatreTiers; tier++)
        {
            float reach = 1 - tier * TheatreTier;
            Plain(px.Select(p => centre + (p - centre) * reach).ToArray(), tier % 2 == 0 ? hand.Stone : hand.Stone.Lightened(0.12f));
            Ringed(px.Select(p => centre + (p - centre) * reach).ToArray(), hand, 0.35f);
        }
        Plain(px.Select(p => centre + (p - centre) * OrchestraShare).ToArray(), hand.Mud.Lightened(0.2f));
        for (int i = 1; i < Aisles; i++)
        {
            var way = across.Normalized().Rotated(Mathf.Pi * i / Aisles * (back.Dot(across.Normalized().Rotated(Mathf.Pi / 2)) > 0 ? 1 : -1));
            Edge(centre + way * r * OrchestraShare, centre + way * r, hand, 0.5f);
        }
        Vector2 stage = -back * r * StageShare;
        Vector2[] scaena = [centre - across * 1.05f, centre + across * 1.05f, centre + across * 1.05f + stage, centre - across * 1.05f + stage];
        Plain(scaena, hand.Stone.Darkened(0.15f));
        Ringed(scaena, hand, 0.8f);
    }

    protected void Parterre(BuiltHand hand, Vector2[] px)
    {
        Plain(px, hand.Stone.Lerp(hand.Earth, 0.4f).Lightened(0.1f));
        Ringed(px, hand, 0.5f);
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 2; r++)
            {
                if (c == 1) continue;
                var mid = px[0] + along * ((c + 0.5f) / 3) + across * ((r + 0.5f) / 2);
                Vector2 a = along * (BedShare / 2), b = across * (BedShare * 1.4f / 2);
                Vector2[] bed = [mid - a - b, mid + a - b, mid + a + b, mid - a + b];
                Plain(bed, hand.Hedging);
                DrawPolyline([.. bed.Select(p => mid + (p - mid) * 0.62f), mid + (bed[0] - mid) * 0.62f], hand.Hedging.Lightened(0.25f), Mathf.Max(BuiltHand.LeastPx, 0.4f * _m), true);
                DrawCircle(mid, Mathf.Max(0.8f * _m, 1), hand.Tree);
            }
        var basin = Mid(px);
        DrawCircle(basin, Mathf.Max(along.Length() * 0.08f, 2), hand.Stone);
        DrawCircle(basin, Mathf.Max(along.Length() * 0.065f, 1.5f), hand.Wet);
    }

    protected bool Run(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased)
    {
        switch (s.Class)
        {
            case Hallmarks.Aqueduct:
                if (!cased) return true;
                if (AqueductM * _m < AqueductFromPx)
                {
                    DrawPolyline(px, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
                    return true;
                }
                float wide = AqueductM * _m;
                if (hand.Shaded)
                    foreach (var (at, along) in Stepped(px, ArchM * _m))
                        DrawLine(at + Vector2.One * wide * 1.4f - along * ArchM * 0.3f * _m, at + Vector2.One * wide * 1.4f + along * ArchM * 0.3f * _m, hand.Ink with { A = 0.35f }, wide, true);
                DrawPolyline(px, hand.Ink, wide + 2 * BuiltHand.LeastPx, true);
                DrawPolyline(px, hand.Stone, wide, true);
                DrawPolyline(px, hand.Wet with { A = 0.7f }, Mathf.Max(BuiltHand.LeastPx, wide * 0.3f), true);
                return true;
            case Settlements.GardenWall:
                if (!cased || GardenWallM * _m < BuiltHand.LeastPx) return true;
                DrawPolyline(px, hand.Ink with { A = 0.85f }, GardenWallM * _m + 2 * BuiltHand.LeastPx, true);
                DrawPolyline(px, hand.Stone, GardenWallM * _m, true);
                return true;
            case Settlements.Paved:
                if (!cased || PavingM * _m < SurfacePx) return true;
                var stone = hand.Line("street").Fill.Lerp(hand.Stone, 0.5f) with { A = 1 };
                DrawPolyline(px, stone, PavingM * _m, true);
                Strip(px, PavingM * _m, stone, Surface.Street);
                return true;
            case Hallmarks.Boardwalk:
                if (cased && BoardwalkM * _m >= SurfacePx) Strip(px, BoardwalkM * _m, hand.Wood.Lightened(0.12f), Surface.Planks);
                return true;
            case Hallmarks.Tramway:
                if (!cased || TramGaugeM * _m < TramFromPx) return true;
                foreach (int side in new[] { 1, -1 }) DrawPolyline(Sideways(px, side * TramGaugeM / 2 * _m), hand.Rails, Mathf.Max(BuiltHand.LeastPx, 0.12f * _m), true);
                return true;
            default:
                return false;
        }
    }

    static Vector2[] Sideways(Vector2[] px, float off)
    {
        var moved = new Vector2[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var way = (px[Mathf.Min(i + 1, px.Length - 1)] - px[Mathf.Max(i - 1, 0)]).Normalized();
            moved[i] = px[i] + way.Orthogonal() * off;
        }
        return moved;
    }

    protected bool Hallmark(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case Hallmarks.Qanat when Seen(QanatM):
                DrawCircle(at, QanatM * _m, hand.Mud.Lightened(0.15f));
                DrawArc(at, QanatM * _m, 0, Mathf.Tau, 14, hand.Ink with { A = 0.5f }, BuiltHand.LeastPx, true);
                DrawCircle(at, QanatM * 0.3f * _m, hand.Ink);
                return true;
            case Hallmarks.Sphinx when Seen(SphinxM):
                Boxed(hand, Box(at - across * 0.3f * _m, across, along, SphinxM, SphinxWideM), hand.Lime.Darkened(0.06f), Surface.Hewn);
                Tub(hand, at + across * (SphinxM / 2 - 0.2f) * _m, SphinxWideM * 0.45f, hand.Lime, Surface.Hewn);
                return true;
            case Hallmarks.Paifang when Seen(PaifangM / 2):
                Boxed(hand, Box(at, across, along, PaifangM, 1.6f), hand.Kawara, Surface.Barrel);
                for (int i = 0; i < 4; i++) DrawCircle(at + across * ((i - 1.5f) / 3 * PaifangM * 0.9f) * _m, Mathf.Max(0.35f * _m, 1), hand.Signal);
                return true;
            case Hallmarks.FireTower when Seen(FireTowerM):
                var frame = Box(at, across, along, FireTowerM, FireTowerM);
                if (hand.Shaded) DrawLine(at, at + Vector2.One * FireTowerM * 2.5f * _m, hand.Ink with { A = 0.3f }, FireTowerM * 0.6f * _m, true);
                Boxed(hand, frame, hand.Wood, Surface.Planks, shadow: false);
                DrawLine(frame[0], frame[2], hand.Ink with { A = 0.6f }, BuiltHand.LeastPx, true);
                DrawLine(frame[1], frame[3], hand.Ink with { A = 0.6f }, BuiltHand.LeastPx, true);
                DrawCircle(at, Mathf.Max(0.35f * _m, 1), hand.Copper);
                return true;
            case Hallmarks.Lantern when Seen(LanternM):
                Boxed(hand, Box(at, across, along, LanternM, LanternM), hand.Stone, Surface.Hewn);
                DrawCircle(at, LanternM * 0.28f * _m, hand.Stone.Lightened(0.15f));
                return true;
            case Hallmarks.Hitch when Seen(HitchM):
                DrawLine(at - across * HitchM / 2 * _m, at + across * HitchM / 2 * _m, hand.Wood, Mathf.Max(0.15f * _m, 1), true);
                foreach (int end in new[] { 1, -1 }) DrawCircle(at + across * end * HitchM / 2 * _m, Mathf.Max(0.18f * _m, 1), hand.Ink);
                return true;
            case Hallmarks.AdColumn when Seen(AdColumnM * 2):
                Tub(hand, at, AdColumnM, hand.Cloth.Lerp(hand.Straw, 0.25f), Surface.Cloth);
                DrawCircle(at, AdColumnM * 0.6f * _m, hand.Copper.Darkened(0.1f));
                return true;
            case Hallmarks.Memorial when Seen(MemorialM):
                Boxed(hand, Box(at, across, along, MemorialM, MemorialM), hand.Stone, Surface.Hewn);
                Boxed(hand, Box(at, across, along, MemorialM * 0.45f, MemorialM * 0.45f), hand.Stone.Lightened(0.15f), Surface.Hewn, shadow: false);
                return true;
            case Hallmarks.Shelter when Seen(ShelterM):
                var mound = Enumerable.Range(0, 10).Select(i => at + across.Rotated(i * Mathf.Tau / 10) * ShelterM * _m * (i % 5 == 0 ? 1 : 0.7f)).ToArray();
                Boxed(hand, mound, hand.Turf.Lerp(hand.Mud, 0.4f), Surface.Turf);
                Boxed(hand, Box(at + across * ShelterM * 0.55f * _m, across, along, 1.2f, 1.4f), hand.Ink.Lightened(0.1f), Surface.None, shadow: false);
                return true;
            case Hallmarks.FuelPump when Seen(0.6f):
                Boxed(hand, Box(at, across, along, 0.7f, 0.5f), hand.Signal, Surface.None, shadow: false);
                return true;
            case Hallmarks.Tractor when Seen(TractorM):
                Wheels(hand, at, across, along, TractorM, 1.4f);
                Boxed(hand, Box(at, across, along, TractorM, 1.3f), hand.Copper.Lerp(hand.Signal, Chance(9.1f)), Surface.None);
                DrawCircle(at - across * TractorM * 0.3f * _m, 0.35f * _m, hand.Ink);
                return true;
            case Hallmarks.Qanat or Hallmarks.Sphinx or Hallmarks.Paifang or Hallmarks.FireTower or Hallmarks.Lantern or Hallmarks.Hitch or Hallmarks.AdColumn or Hallmarks.Memorial
                or Hallmarks.Shelter or Hallmarks.FuelPump or Hallmarks.Tractor:
                return true;
        }
        return false;
    }
}
