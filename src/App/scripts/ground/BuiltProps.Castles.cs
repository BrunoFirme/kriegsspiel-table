using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltProps
{
    protected const int RoundFrom = 8;
    const float MerlonGapM = 1.6f, MerlonSideM = 0.7f, RimShare = 0.78f, ConeShare = 0.82f, DrumShadowM = 2.5f, DrumShadowMostPx = 9;

    protected bool Drum(BuiltHand hand, Vector2[] px)
    {
        var centre = Mid(px);
        float r = px.Average(p => (p - centre).Length());
        if (r < 2) return false;
        if (hand.Shaded) DrawColoredPolygon(px.Select(p => p + Vector2.One * Mathf.Min(DrumShadowM * _m, DrumShadowMostPx)).ToArray(), hand.Ink with { A = 0.5f });
        Filled(px, hand.Stone);
        DrawPolyline(px.Append(px[0]).ToArray(), hand.Ink, BuiltHand.LeastPx, true);
        bool coned = _culture is Manners.Gothic or Manners.Continental or Manners.Rus or Manners.Northern && Chance(17.7f) < 0.5f;
        if (coned)
        {
            var roof = _culture is Manners.Rus or Manners.Northern ? hand.Plank : Chance(18.8f) < 0.5f ? hand.Slate : hand.Tile;
            var ring = px.Select(p => centre + (p - centre) * ConeShare).ToArray();
            for (int i = 0; i < ring.Length; i++)
            {
                float lean = (ring[i] - centre).Normalized().Dot(new Vector2(-0.7071f, -0.7071f));
                Filled([centre, ring[i], ring[(i + 1) % ring.Length]], lean > 0 ? roof.Lightened(0.12f * lean) : roof.Darkened(-0.25f * lean));
            }
            return true;
        }
        var walk = px.Select(p => centre + (p - centre) * RimShare).ToArray();
        Filled(walk, hand.Lead.Darkened(0.1f));
        DrawPolyline(walk.Append(walk[0]).ToArray(), hand.Ink with { A = 0.5f }, BuiltHand.LeastPx, true);
        if (MerlonSideM * _m < 1.2f) return true;
        foreach (var (at, _) in Stepped(px.Append(px[0]).ToArray(), MerlonGapM * _m))
        {
            var inward = (centre - at).Normalized() * MerlonSideM * _m * 0.6f;
            DrawRect(new Rect2(at + inward - Vector2.One * MerlonSideM * _m / 2, Vector2.One * MerlonSideM * _m), hand.Ink with { A = 0.45f });
        }
        return true;
    }
}
