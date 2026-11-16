using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class SlopeCanvas
{
    const int SurveyHand = 0, AncientHand = 1, ModernHand = 2, StaffHand = 3, MedievalHand = 4;
    const float CutApartM = 2.4f, CutFromPx = 2.2f, CutSignPx = 4, CutInk = 0.55f, CutTaper = 0.35f, StepInset = 0.3f, LongStroke = 0.8f, ShortStroke = 0.4f;
    const float ToothApartM = 6, ToothM = 1.8f, ToothSignPx = 3, EscarpM = 6, TunnelDashM = 4, TunnelSignPx = 5, TunnelInk = 0.6f;
    const float StoneBandM = 3, StoneApartM = 1.6f, StoneSignPx = 3, FlecheHachure = 0.46f, CaveSignPx = 3.2f, LookoutM = 5, LookoutLeastPx = 3.6f, PavedInk = 0.3f;
    static readonly float[] Courses = [0.94f, 0.88f, 0.82f];

    protected bool Dug(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased)
    {
        switch (s.Class)
        {
            case Castles.Cut:
                if (cased) Cutting(hand, px, True(hand.Line(s.Class).Metres, CutSignPx) / 2, true);
                return true;
            case Fieldworks.Escarp:
                if (cased) Escarped(hand, px);
                return true;
            case FortGround.Fleche:
                if (cased) Fleched(hand, px);
                return true;
            case Castles.Ishigaki:
                if (cased) Battered(hand, px);
                return true;
            case Fieldworks.Tunnel:
                if (cased) Tunnelled(hand, px);
                return true;
            default:
                return false;
        }
    }

    void Cutting(BuiltHand hand, Vector2[] px, float half, bool both)
    {
        int style = hand.Style;
        if (style != StaffHand) DrawPolyline(px, hand.Mud.Darkened(0.14f) with { A = 0.85f }, half * 2, true);
        foreach (int side in both ? new[] { 1, -1 } : new[] { -1 })
        {
            var edge = Offset(px, half * side);
            DrawPolyline(edge, hand.Ink with { A = style == SurveyHand ? CutInk : 0.85f }, Fine, true);
            if (style == AncientHand)
            {
                DrawPolyline(Offset(px, half * side * (1 - StepInset)), hand.Ink with { A = CutInk }, Fine, true);
                continue;
            }
            float apart = Mathf.Max(CutApartM * _m, CutSignPx), reach = half * (both ? 0.9f : 1.8f);
            if (half < CutFromPx) continue;
            int n = 0;
            foreach (var (at, along) in Stepped(edge, apart))
            {
                var inward = along.Orthogonal() * -side;
                float length = style == ModernHand ? reach * (n++ % 2 == 0 ? LongStroke : ShortStroke) : reach * (style == MedievalHand ? 0.7f : 1);
                if (style == SurveyHand) Filled([at - along * (apart * CutTaper / 2), at + along * (apart * CutTaper / 2), at + inward * length], hand.Ink with { A = CutInk });
                else DrawLine(at, at + inward * length, hand.Ink with { A = style == MedievalHand ? 0.75f : 0.85f }, style == MedievalHand ? Medium : Fine, true);
            }
        }
    }

    Vector2[] Offset(Vector2[] px, float off)
    {
        var side = new Vector2[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var along = (px[Mathf.Min(px.Length - 1, i + 1)] - px[Mathf.Max(0, i - 1)]).Normalized();
            side[i] = px[i] + along.Orthogonal() * off;
        }
        return side;
    }

    void Escarped(BuiltHand hand, Vector2[] px)
    {
        float half = True(EscarpM, CutSignPx) / 2;
        Cutting(hand, Offset(px, half), half, false);
        if (hand.Style is not (StaffHand or ModernHand)) return;
        float tooth = True(ToothM, ToothSignPx);
        foreach (var (at, along) in Stepped(px, Mathf.Max(ToothApartM * _m, tooth * 2.5f)))
            Filled([at - along * tooth * 0.6f, at + along * tooth * 0.6f, at + along.Orthogonal() * tooth], hand.Ink);
    }

    void Fleched(BuiltHand hand, Vector2[] px)
    {
        var look = hand.Line(FortGround.Fleche);
        float wide = True(look.Metres, look.LeastPx);
        DrawPolyline(px, hand.Ink, wide + 2 * Fine, true);
        DrawPolyline(px, look.Fill, wide, true);
        if (hand.Style == AncientHand) DrawPolyline(px, hand.Ink with { A = CutInk }, Fine, true);
        else Hachured(hand, px, wide);
    }

    void Battered(BuiltHand hand, Vector2[] px)
    {
        float band = True(StoneBandM, StoneSignPx);
        var foot = Offset(px, band);
        DrawPolyline(Offset(px, band / 2), hand.Stone.Darkened(0.1f), band, true);
        DrawPolyline(px, hand.Ink, Fine, true);
        if (hand.Style == AncientHand)
        {
            DrawPolyline(foot, hand.Ink with { A = CutInk }, Fine, true);
            return;
        }
        float apart = Mathf.Max(StoneApartM * _m, StoneSignPx);
        foreach (var (at, along) in Stepped(px, apart)) DrawLine(at, at + along.Orthogonal() * band, hand.Ink with { A = CutInk }, BuiltHand.LeastPx, true);
        if (hand.Style != StaffHand) DrawPolyline(foot, hand.Ink with { A = CutInk * 0.6f }, BuiltHand.LeastPx, true);
    }

    void Tunnelled(BuiltHand hand, Vector2[] px)
    {
        float dash = Mathf.Max(TunnelDashM * _m, TunnelSignPx);
        for (int i = 0; i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], hand.Ink with { A = TunnelInk }, Fine, hand.Style == SurveyHand ? dash * 0.4f : dash, true, true);
        if (hand.Style is not (StaffHand or ModernHand)) return;
        foreach (var (end, inner) in new[] { (px[0], px[1]), (px[^1], px[^2]) })
        {
            var across = (inner - end).Normalized().Orthogonal() * Mathf.Max(dash * 0.5f, Fine * 3);
            DrawLine(end - across, end + across, hand.Ink, Fine, true);
        }
    }

    protected bool Paved(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (s.Class != Castles.Talus) return false;
        var look = hand.Area(s.Class);
        Filled(px, look.Fill);
        var middle = Mid(px);
        if (hand.Style != AncientHand)
            foreach (float course in Courses) DrawPolyline(px.Append(px[0]).Select(p => middle + (p - middle) * course).ToArray(), hand.Ink with { A = PavedInk }, BuiltHand.LeastPx, true);
        DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
        return true;
    }

    protected bool Delved(BuiltHand hand, string cls, Vector2[] q)
    {
        if (cls != Fieldworks.Cave) return false;
        Vector2 mouth = (q[0] + q[1]) / 2, into = ((q[2] + q[3]) / 2 - mouth).Normalized();
        float r = Mathf.Max((q[1] - q[0]).Length() / 2, CaveSignPx);
        var arch = Enumerable.Range(0, 13).Select(i => mouth + into.Rotated(Mathf.Pi * (i / 12f - 0.5f)) * r).Prepend(mouth).ToArray();
        if (hand.Style is SurveyHand or MedievalHand)
            for (int i = -2; i <= 2; i++) DrawLine(mouth + into.Rotated(i * 0.45f) * r * 1.2f, mouth + into.Rotated(i * 0.45f) * r * 1.9f, hand.Ink with { A = CutInk }, BuiltHand.LeastPx, true);
        Filled(arch, hand.Ink);
        if (hand.Style == StaffHand) DrawPolyline(arch.Append(arch[0]).ToArray(), hand.Ink, Fine, true);
        return true;
    }

    protected bool Watch(BuiltHand hand, string cls, Vector2 at, Vector2 across)
    {
        if (cls != Fieldworks.Lookout) return false;
        float r = True(LookoutM, LookoutLeastPx);
        Vector2[] tri = [at + across * r, at + across.Rotated(Mathf.Tau / 3) * r, at + across.Rotated(-Mathf.Tau / 3) * r];
        if (hand.Rubricated) Filled(tri, hand.Signal with { A = 0.85f });
        DrawPolyline(tri.Append(tri[0]).ToArray(), hand.Ink, Fine, true);
        DrawCircle(at, Mathf.Max(r * 0.18f, 1), hand.Ink);
        return true;
    }
}
