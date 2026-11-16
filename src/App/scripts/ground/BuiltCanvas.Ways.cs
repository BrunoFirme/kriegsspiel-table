using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    const float StairsM = 3.2f, RampM = 3.4f, TreadM = 0.36f, RampTreadM = 1.4f, TreadSignPx = 2.6f, TreadsFromPx = 2.2f, CheekM = 0.3f, StepSignPx = 2.6f;
    const float RiserShade = 0.3f, RiserShadePx = 2, RiserInk = 0.25f;
    const float HollowLaneM = 1.6f, HollowBankM = 3, FormationM = 4, StrokeM = 2.2f, StrokeSignPx = 3, StrokeShort = 0.5f, StrokeReach = 0.85f, TaperM = 0.6f, TaperPx = 1.6f;
    const float MouthM = 6, PortalWingM = 2.6f, PortalSignPx = 4, TubeM = 3.2f, TubeDashM = 9, TubeDashPx = 4, DeckM = 7, DeckSignPx = 3, PierApartM = 18, PierM = 2.4f, PierOutM = 1.2f, WingM = 3;
    const float BankWash = 0.35f, HollowWash = 0.4f, DottedTube = 0.55f, FineInk = 0.75f;

    enum Hatch { Tapered, Stroked, Alternate, Hanging, Banded }

    static int Style(BuiltHand hand) => Enumerable.Range(0, MapStyles.ShaderOrder.Count).FirstOrDefault(i => ReferenceEquals(BuiltHand.Of(i), hand));

    static bool Is(BuiltHand hand, string style) => Style(hand) == MapStyles.Index(style);

    static bool Topographic(BuiltHand hand) => Is(hand, MapStyles.Modern) || Is(hand, MapStyles.Staff);

    protected bool Climb(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased)
    {
        switch (s.Class)
        {
            case Grades.Stairs or Grades.Ramp:
                if (cased) Steps(hand, px, s.Class == Grades.Ramp);
                return true;
            case Grades.Holloway:
                if (cased) Sunken(hand, px);
                return true;
            case Grades.Tunnel:
                if (cased) Bore(hand, px);
                return true;
            case Grades.Viaduct:
                if (cased) Spans(hand, px);
                return true;
            default:
                return false;
        }
    }

    static Vector2[] Aside(Vector2[] px, float off)
    {
        var moved = new Vector2[px.Length];
        for (int i = 0; i < px.Length; i++) moved[i] = px[i] + (px[Mathf.Min(i + 1, px.Length - 1)] - px[Mathf.Max(i - 1, 0)]).Normalized().Orthogonal() * off;
        return moved;
    }

    void Steps(BuiltHand hand, Vector2[] px, bool ramp)
    {
        float wide = Mathf.Max((ramp ? RampM : StairsM) * _m, StepSignPx), tread = (ramp ? RampTreadM : TreadM) * _m;
        bool close = tread >= TreadsFromPx;
        if (close || !Is(hand, MapStyles.Modern)) DrawPolyline(px, (ramp ? hand.Stone.Lerp(hand.Earth, 0.3f) : hand.Stone).Lightened(0.08f), wide, true);
        if (close)
        {
            float shade = Mathf.Min(tread * RiserShade, RiserShadePx);
            foreach (var (at, along) in Stepped(px, tread))
            {
                var across = along.Orthogonal() * (wide / 2);
                if (hand.Shaded) DrawLine(at - across - along * shade, at + across - along * shade, hand.Ink with { A = RiserInk }, shade, true);
                DrawLine(at - across, at + across, hand.Ink with { A = FineInk }, BuiltHand.LeastPx, true);
            }
            Sides(px, wide / 2, hand.Ink, Mathf.Max(CheekM * _m, BuiltHand.LeastPx));
            return;
        }
        float every = ramp ? TreadSignPx * 2 : TreadSignPx;
        bool ladder = Is(hand, MapStyles.Survey) || Is(hand, MapStyles.Staff);
        if (ladder) Sides(px, wide / 2, hand.Ink with { A = FineInk }, BuiltHand.LeastPx);
        foreach (var (at, along) in Stepped(px, every))
        {
            var across = along.Orthogonal() * (wide / 2 * (Is(hand, MapStyles.Modern) ? 0.6f : 1));
            if (Is(hand, MapStyles.Ancient)) DrawLine(at - across, at + across, hand.Ink with { A = 0.8f }, Mathf.Max(every * 0.4f, BuiltHand.LeastPx), true);
            else DrawLine(at - across, at + across, hand.Ink with { A = FineInk }, BuiltHand.LeastPx, true);
        }
    }

    Hatch Hatched(BuiltHand hand) =>
        Is(hand, MapStyles.Survey) ? Hatch.Tapered
        : Is(hand, MapStyles.Modern) ? Hatch.Alternate
        : Is(hand, MapStyles.Staff) ? Hatch.Hanging
        : Is(hand, MapStyles.Medieval) ? Hatch.Stroked : Hatch.Banded;

    void Sunken(BuiltHand hand, Vector2[] px)
    {
        if (Is(hand, MapStyles.Ancient)) return;
        float lane = HollowLaneM * _m, bank = Mathf.Max(HollowBankM * _m, StrokeSignPx);
        foreach (int side in new[] { 1, -1 })
        {
            Vector2[] top = Aside(px, side * (lane + bank)), foot = Aside(px, side * lane);
            if (Is(hand, MapStyles.Medieval))
            {
                DrawPolyline(Aside(px, side * (lane + bank / 2)), hand.Earth.Darkened(0.25f) with { A = HollowWash }, bank, true);
                continue;
            }
            if (Topographic(hand)) DrawPolyline(top, hand.Ink with { A = FineInk }, BuiltHand.LeastPx, true);
            Strokes(hand, top, foot, Hatched(hand));
        }
    }

    void Strokes(BuiltHand hand, Vector2[] top, Vector2[] foot, Hatch hatch)
    {
        float every = Mathf.Max(StrokeM * _m, StrokeSignPx), taper = Mathf.Max(TaperM * _m, TaperPx), carried = every / 2;
        int stroke = 0;
        var ink = hand.Ink with { A = hatch == Hatch.Tapered ? 0.6f : FineInk };
        for (int k = 0; k + 1 < top.Length; k++)
        {
            float length = (top[k + 1] - top[k]).Length();
            if (length < 0.001f) continue;
            for (; carried < length; carried += every)
            {
                float t = carried / length;
                Vector2 from = top[k].Lerp(top[k + 1], t), to = foot[k].Lerp(foot[k + 1], t);
                if (hatch == Hatch.Alternate) to = from.Lerp(to, stroke++ % 2 == 0 ? StrokeReach : StrokeReach * StrokeShort);
                if (hatch == Hatch.Tapered)
                {
                    var half = (to - from).Normalized().Orthogonal() * (taper / 2);
                    DrawColoredPolygon([from + half, from - half, to], ink);
                }
                else DrawLine(from, to, ink, BuiltHand.LeastPx, true);
            }
            carried -= length;
        }
    }

    protected bool Banks(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (s.Class is not (Grades.Cutting or Grades.Embankment)) return false;
        if (px.Length < 4 || px.Length % 2 != 0) return true;
        int n = px.Length / 2;
        bool cut = s.Class == Grades.Cutting;
        var hatch = Hatched(hand);
        if (hatch == Hatch.Banded || hatch == Hatch.Stroked) Filled(px, hand.Earth with { A = BankWash });
        foreach (int side in new[] { 0, 1 })
        {
            var edge = Enumerable.Range(0, n).Select(i => side == 0 ? px[i] : px[px.Length - 1 - i]).ToArray();
            var formation = Enumerable.Range(0, n).Select(i => Formed((px[i] + px[px.Length - 1 - i]) / 2, edge[i])).ToArray();
            Vector2[] top = cut ? edge : formation, foot = cut ? formation : edge;
            if (hatch == Hatch.Banded)
            {
                DrawPolyline(edge, hand.Ink with { A = FineInk }, BuiltHand.LeastPx, true);
                continue;
            }
            if (hatch == Hatch.Alternate || hatch == Hatch.Hanging && cut) DrawPolyline(top, hand.Ink with { A = FineInk }, BuiltHand.LeastPx, true);
            Strokes(hand, top, foot, hatch);
        }
        return true;
    }

    Vector2 Formed(Vector2 middle, Vector2 edge)
    {
        var out_ = edge - middle;
        float formation = FormationM * _m;
        return out_.Length() <= formation ? edge : middle + out_.Normalized() * formation;
    }

    void Portal(BuiltHand hand, Vector2 at, Vector2 inward)
    {
        float wide = Mathf.Max(MouthM * _m, PortalSignPx), wing = Mathf.Max(PortalWingM * _m, PortalSignPx * 0.5f);
        var across = inward.Orthogonal() * (wide / 2);
        DrawLine(at - across, at + across, hand.Ink, Mathf.Max(Fine * 1.5f, BuiltHand.LeastPx), true);
        foreach (int side in new[] { 1, -1 }) DrawLine(at + across * side, at + across * side - inward * wing + across.Normalized() * (side * wing * 0.6f), hand.Ink, BuiltHand.LeastPx, true);
    }

    void Bore(BuiltHand hand, Vector2[] px)
    {
        float tube = Mathf.Max(TubeM * _m, PortalSignPx * 0.6f), dash = Mathf.Max(TubeDashM * _m, TubeDashPx);
        if (Topographic(hand))
            foreach (int side in new[] { 1, -1 })
            {
                var line = Aside(px, side * tube / 2);
                for (int i = 0; i + 1 < line.Length; i++) DrawDashedLine(line[i], line[i + 1], hand.Ink with { A = FineInk }, BuiltHand.LeastPx, dash, true, true);
            }
        else
            for (int i = 0; i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], hand.Ink with { A = DottedTube }, BuiltHand.LeastPx, Mathf.Max(dash * 0.4f, 2), true, true);
        Portal(hand, px[0], (px[1] - px[0]).Normalized());
        Portal(hand, px[^1], (px[^2] - px[^1]).Normalized());
    }

    void Spans(BuiltHand hand, Vector2[] px)
    {
        float deck = Mathf.Max(DeckM * _m, DeckSignPx), pier = PierM * _m, wing = Mathf.Max(WingM * _m, DeckSignPx * 0.6f);
        bool close = pier >= DeckSignPx;
        if (close)
            foreach (var (at, along) in Stepped(px, PierApartM * _m))
            {
                var pierRing = Quad(at, along * (pier / 2), along.Orthogonal() * (deck / 2 + PierOutM * _m));
                Filled(pierRing, hand.Stone.Darkened(0.15f));
                DrawPolyline([.. pierRing, pierRing[0]], hand.Ink with { A = FineInk }, BuiltHand.LeastPx, true);
            }
        if (close || !Topographic(hand)) DrawPolyline(px, hand.Stone, deck, true);
        Sides(px, deck / 2, hand.Ink, Mathf.Max(Fine, BuiltHand.LeastPx));
        foreach (var (end, inward) in new[] { (px[0], (px[1] - px[0]).Normalized()), (px[^1], (px[^2] - px[^1]).Normalized()) })
            foreach (int side in new[] { 1, -1 })
            {
                var corner = end + inward.Orthogonal() * (side * deck / 2);
                DrawLine(corner, corner + (inward.Orthogonal() * side - inward).Normalized() * wing, hand.Ink, Mathf.Max(Fine, BuiltHand.LeastPx), true);
            }
    }
}
