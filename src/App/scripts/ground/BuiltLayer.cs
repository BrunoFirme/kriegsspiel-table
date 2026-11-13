using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer : BuiltCanvas
{
    const float BedM = 1.6f, BedFromPx = 3, BedInk = 0.2f, GraveM = 0.9f, GraveAlongM = 3.4f, GraveAcrossM = 2.6f, GraveFromPx = 2.2f, GraveKeep = 0.7f, GraveEdgeM = 1.6f;
    const float RidgeInk = 0.55f, ShadowM = 1.3f, ShadowMostPx = 5;
    const float SurfaceFromPx = 5, DitchOffM = 1.7f, AvenueM = 9, AvenueCrownM = 2.2f, AvenueFromPx = 1.1f, WallFromPx = 4;
    const float StakeM = 0.6f, StakeFromPx = 3, BushM = 1.25f, BushApartM = 1.8f;
    const float PostM = 2.5f, PostFromPx = 6, CrestInk = 0.35f, BedMostPx = 12;
    const int MostBeds = 80, MostGraves = 120;

    readonly List<BuiltPainter.Shape> _shapes;
    readonly Rect2 _uv;
    readonly float _pixels, _perUnit, _squash;
    readonly int _style;

    public BuiltLayer(List<BuiltPainter.Shape> shapes, Rect2 uv, float pixels, float perUnit, float squash, int style)
    {
        _shapes = shapes;
        _uv = uv;
        _pixels = pixels;
        _perUnit = perUnit;
        _squash = squash;
        _style = style;
        Scale = new Vector2(1, squash);
    }

    Vector2[] Px(Vector2[] uv) => uv.Select(p => (p - _uv.Position) / _uv.Size * _pixels * new Vector2(1, 1 / _squash)).ToArray();

    float True(float metres, float leastPx = 0) => Mathf.Max(metres * _m, leastPx);

    float Pen(float weight, float mostPx) => Mathf.Clamp(weight * _perUnit, BuiltHand.LeastPx, mostPx);

    float Fine => Pen(BuiltHand.Fine, BuiltHand.FineMostPx);

    float Medium => Pen(BuiltHand.Medium, BuiltHand.MediumMostPx);

    const float KeyScale = 4096;

    public override void _Draw()
    {
        var hand = BuiltHand.Of(_style);
        var roads = _shapes.Where(BuiltHand.IsRoad).ToList();
        bool paved = false;
        foreach (var s in _shapes)
        {
            var px = Px(s.Uv);
            _key = s.Uv[0] * KeyScale;
            _m = _perUnit * s.PerMetre;
            _manner = s.Manner;
            if (s.Kind == PartKinds.Area) Area(hand, s, px);
            else if (BuiltHand.IsRoad(s))
            {
                if (paved) continue;
                paved = true;
                foreach (var road in roads)
                {
                    _m = _perUnit * road.PerMetre;
                    Line(hand, road, Px(road.Uv), true, false);
                }
                foreach (var road in roads)
                {
                    _key = road.Uv[0] * KeyScale;
                    _m = _perUnit * road.PerMetre;
                    _manner = road.Manner;
                    Line(hand, road, Px(road.Uv), false, true);
                }
            }
            else if (s.Kind == PartKinds.Line) Line(hand, s, px, true, true);
            else if (s.Kind == PartKinds.Block) Block(hand, s, px);
            else if (s.Kind == PartKinds.Sign) Sign(hand, s, px[0]);
            _key = default;
        }
    }

    void Area(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        if (s.Class is "arena" or "quarry" or "saltpans")
        {
            if (s.Class == "arena") Arena(hand, px);
            else Worked(hand, s.Class, px);
            return;
        }
        var look = hand.Area(s.Class);
        var ground = SurfaceBook.Ground(s.Class, _manner);
        if (ground != Surface.None && look.Fill.A > 0) Surfaced(px, look.Fill with { A = 1 }, ground, look.Fill.A < 1);
        else if (look.Fill.A > 0) Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
        if (s.Class == "garden" && px.Length == 4) Beds(hand, px);
        if (s.Class == "churchyard" && px.Length == 4 && !Manners.Old(_manner)) Graves(hand, px);
        if (s.Class == "forum" && px.Length == 4) Slabs(hand, px);
        if (s.Class is "vineyard" or "osiers" && px.Length == 4) Vines(hand, px);
        if (s.Class == "threshing") Threshed(hand, px);
    }

    void Beds(BuiltHand hand, Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2 a = px[0], along = longFirst ? px[1] - px[0] : px[3] - px[0], across = longFirst ? px[3] - px[0] : px[1] - px[0];
        float bed = Mathf.Max(BedM * _m, BedFromPx * 1.5f);
        if (across.Length() < BedFromPx * 3 || BedM * _m > BedMostPx) return;
        int beds = Mathf.Clamp((int)(across.Length() / bed), 2, MostBeds);
        for (int i = 1; i < beds; i++) DrawLine(a + across * (i / (float)beds), a + across * (i / (float)beds) + along, hand.Ink with { A = BedInk }, BuiltHand.LeastPx, true);
    }

    void Grave(BuiltHand hand, Vector2 at, float mark)
    {
        if (!hand.Crosses)
        {
            DrawCircle(at, mark * 0.45f, hand.Ink with { A = 0.7f });
            return;
        }
        DrawLine(at - Vector2.Down * mark, at + Vector2.Down * mark * 0.6f, hand.Ink with { A = 0.75f }, BuiltHand.LeastPx, true);
        DrawLine(at - Vector2.Right * mark * 0.5f - Vector2.Down * mark * 0.3f, at + Vector2.Right * mark * 0.5f - Vector2.Down * mark * 0.3f, hand.Ink with { A = 0.75f }, BuiltHand.LeastPx, true);
    }

    void Graves(BuiltHand hand, Vector2[] px)
    {
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        float mark = GraveM * _m;
        if (mark < GraveFromPx || along.Length() < 1 || across.Length() < 1) return;
        float edge = GraveEdgeM * _m;
        int cols = (int)((along.Length() - 2 * edge) / (GraveAlongM * _m)), rows = (int)((across.Length() - 2 * edge) / (GraveAcrossM * _m)), laid = 0;
        Vector2 u = along.Normalized(), v = across.Normalized(), middle = px[0] + along / 2 + across / 2;
        for (int r = 0; r <= rows && laid < MostGraves; r++)
            for (int c = 0; c <= cols && laid < MostGraves; c++)
            {
                var at = px[0] + u * (edge + c * GraveAlongM * _m) + v * (edge + r * GraveAcrossM * _m);
                var off = (at - middle) / new Vector2(along.Length(), across.Length()).Length();
                if (Mathf.Abs(off.Dot(u)) < 0.3f && Mathf.Abs(off.Dot(v)) < 0.22f || Chance(c * 7.1f + r * 13.3f) > GraveKeep) continue;
                Grave(hand, at, mark);
                laid++;
            }
    }

    void Line(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased, bool filled)
    {
        if (px.Length < 2) return;
        var look = hand.Line(s.Class);
        float wide = True(look.Metres, look.LeastPx);
        bool surface = look.Metres * _m >= (s.Class is "wall" ? WallFromPx : SurfaceFromPx);
        if (s.Class == "hedge" && surface) { if (cased) Hedge(hand, px); return; }
        if (s.Class == "palisade" && StakeM * _m >= StakeFromPx) { if (cased) Stakes(hand, px); return; }
        if (look.Dash > 0 && !surface)
        {
            float dash = Mathf.Max(look.Dash * _perUnit, wide * 2);
            for (int i = 0; cased && i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], look.Ink, wide, dash, true, true);
            return;
        }
        bool bare = look.Fill.A <= 0 && !(surface && look.Close.A > 0);
        if (cased) DrawPolyline(px, bare ? look.Ink : look.Ink with { A = 1 }, bare ? wide : wide + 2 * Fine, true);
        if (cased && s.Class == "fence") Posts(hand, px);
        if (!filled || bare) return;
        var fill = look.Fill.A > 0 ? look.Fill : look.Close;
        DrawPolyline(px, fill, wide, true);
        if (!surface) return;
        if (SurfaceBook.Lining(s.Class, _manner) is var lining && lining != Surface.None) Strip(px, wide, fill with { A = 1 }, lining);
        if (s.Class == "rampart") DrawPolyline(px, hand.Ink with { A = CrestInk }, Fine, true);
        if (s.Class == "wall") Merlons(hand, px, wide);
        if (s.Class != "chaussee") return;
        Sides(px, wide / 2 + DitchOffM * _m, hand.Wet with { A = 0.7f }, Fine);
        Avenue(hand, px, wide);
    }

    void Sides(Vector2[] px, float off, Color ink, float width)
    {
        foreach (int way in new[] { 1, -1 })
        {
            var side = new Vector2[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var along = (px[Mathf.Min(px.Length - 1, i + 1)] - px[Mathf.Max(0, i - 1)]).Normalized();
                side[i] = px[i] + along.Orthogonal() * (off * way);
            }
            DrawPolyline(side, ink, width, true);
        }
    }

    void Avenue(BuiltHand hand, Vector2[] px, float wide)
    {
        float r = AvenueCrownM * _m;
        if (r < AvenueFromPx) return;
        int tree = 0;
        foreach (var (at, along) in Stepped(px, AvenueM * _m))
            foreach (int way in new[] { 1, -1 }) Crown(hand, at + along.Orthogonal() * ((wide / 2 + DitchOffM * _m + r * 1.3f) * way), r, ++tree);
    }

    void Hedge(BuiltHand hand, Vector2[] px)
    {
        float r = BushM * _m;
        int n = 0;
        foreach (var (at, along) in Stepped(px, BushApartM * _m))
        {
            var bush = at + along.Orthogonal() * ((Chance(++n * 1.7f) - 0.5f) * r * 0.6f);
            if (hand.Shaded) DrawCircle(bush + Vector2.One * r * 0.25f, r, hand.Ink with { A = 0.25f });
            Leafed(bush, r * (0.85f + 0.3f * Chance(n * 4.2f)), hand.Hedging, n);
        }
    }

    void Stakes(BuiltHand hand, Vector2[] px)
    {
        foreach (var (at, _) in Stepped(px, StakeM * _m)) DrawCircle(at, StakeM * _m * 0.36f, hand.Ink);
    }

    void Posts(BuiltHand hand, Vector2[] px)
    {
        if (PostM * _m < PostFromPx) return;
        foreach (var (at, _) in Stepped(px, PostM * _m)) DrawCircle(at, Mathf.Max(0.18f * _m, 0.9f), hand.Ink);
    }
}
