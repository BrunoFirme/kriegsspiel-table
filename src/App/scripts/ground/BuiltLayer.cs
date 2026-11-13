using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class BuiltLayer : Node2D
{
    const float BedM = 1.6f, BedFromPx = 3, BedInk = 0.2f, GraveM = 0.9f, GraveAlongM = 3.4f, GraveAcrossM = 2.6f, GraveFromPx = 2.2f, GraveKeep = 0.7f, GraveEdgeM = 1.6f;
    const float RidgeFromPx = 9, RidgeInset = 0.1f, RidgeInk = 0.55f, RoofShade = 0.2f, ShadowM = 1.3f, ShadowMostPx = 5;
    const float ChimneyM = 0.9f, ChimneyFromPx = 3, TilesM = 1.4f, TilesFromPx = 5, TilesInk = 0.12f;
    const float SurfaceFromPx = 5, RutM = 0.75f, DitchOffM = 1.7f, AvenueM = 9, AvenueCrownM = 2.2f, AvenueFromPx = 1.1f, WallFromPx = 4;
    const float StakeM = 0.6f, StakeFromPx = 3, BushM = 1.25f, BushApartM = 1.8f;
    const float PostM = 2.5f, PostFromPx = 6, CrestInk = 0.35f, RutsFromPx = 12, RutInk = 0.16f;
    const int MostBeds = 80, MostGraves = 120;

    readonly List<BuiltPainter.Shape> _shapes;
    readonly Rect2 _uv;
    readonly float _pixels, _perUnit, _squash;
    float _m;
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
    Vector2 _key;

    float Chance(float salt) => Mathf.PosMod(Mathf.Sin(_key.X * 12.9898f + _key.Y * 78.233f + salt) * 43758.547f, 1);

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
                    Line(hand, road, Px(road.Uv), false, true);
                }
            }
            else if (s.Kind == PartKinds.Line) Line(hand, s, px, true, true);
            else if (s.Kind == PartKinds.Block) Block(hand, s, px);
            else if (s.Kind == PartKinds.Sign) Sign(hand, s, px[0]);
            _key = default;
        }
    }

    void Filled(Vector2[] px, Color fill)
    {
        DrawColoredPolygon(px, fill);
        DrawPolyline(px.Append(px[0]).ToArray(), fill, 1, true);
    }

    void Area(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        var look = hand.Area(s.Class);
        if (look.Fill.A > 0) Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
        if (s.Class == "garden" && px.Length == 4) Beds(hand, px);
        if (s.Class == "square" && px.Length == 4) Cobbles(hand, px);
        if (s.Class == "churchyard" && px.Length == 4) Graves(hand, px);
    }

    void Beds(BuiltHand hand, Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2 a = px[0], along = longFirst ? px[1] - px[0] : px[3] - px[0], across = longFirst ? px[3] - px[0] : px[1] - px[0];
        float bed = Mathf.Max(BedM * _m, BedFromPx * 1.5f);
        if (across.Length() < BedFromPx * 3) return;
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

    void Block(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        var look = hand.Block(s.Class);
        float shade = Mathf.Clamp(ShadowM * _m, BuiltHand.LeastPx, ShadowMostPx);
        if (look.Shadow.A > 0) Filled(px.Select(p => p + new Vector2(shade, shade)).ToArray(), look.Shadow with { A = 0.85f });
        Filled(px, look.Fill);
        if (px.Length == 4 && hand.Shaded) Roof(hand, s.Class, px);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
        Door(hand, s.Class, px);
    }

    void Roof(BuiltHand hand, string cls, Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2 from = longFirst ? (px[0] + px[3]) / 2 : (px[0] + px[1]) / 2, to = longFirst ? (px[1] + px[2]) / 2 : (px[2] + px[3]) / 2;
        float length = (to - from).Length();
        if (length < RidgeFromPx) return;
        if (cls is "steeple" or "tower" or "keep" or "gatehouse")
        {
            DrawLine(px[0], px[2], hand.Ink with { A = RidgeInk }, BuiltHand.LeastPx, true);
            DrawLine(px[1], px[3], hand.Ink with { A = RidgeInk }, BuiltHand.LeastPx, true);
            return;
        }
        Vector2[] one = longFirst ? [px[0], px[1], to, from] : [px[1], px[2], to, from], other = longFirst ? [from, to, px[2], px[3]] : [from, to, px[3], px[0]];
        Vector2 lean(Vector2[] half) => half.Aggregate(Vector2.Zero, (sum, p) => sum + p) / half.Length;
        var dark = lean(one).X + lean(one).Y > lean(other).X + lean(other).Y ? one : other;
        DrawColoredPolygon(dark, hand.Ink with { A = RoofShade });
        Tiles(hand, px, from, to, longFirst);
        if (cls is "house" or "inn" or "public" && Chance(9.1f) < HipShare) Hips(hand, px, from, to, longFirst);
        else DrawLine(from.Lerp(to, RidgeInset), to.Lerp(from, RidgeInset), hand.Ink with { A = RidgeInk }, BuiltHand.LeastPx, true);
        if (cls == "smithy") DrawCircle(from.Lerp(to, 0.7f), Mathf.Max(0.45f * _m, 1.2f), hand.Signal);
        float chimney = ChimneyM * _m;
        if (chimney < ChimneyFromPx || cls is "barn" or "shed") return;
        var at = from.Lerp(to, 0.3f) + (to - from).Orthogonal().Normalized() * chimney;
        DrawRect(new Rect2(at - Vector2.One * chimney / 2, Vector2.One * chimney), hand.Stone);
        DrawRect(new Rect2(at - Vector2.One * chimney / 2, Vector2.One * chimney), hand.Ink, false, BuiltHand.LeastPx);
    }

    void Tiles(BuiltHand hand, Vector2[] px, Vector2 from, Vector2 to, bool longFirst)
    {
        float pitch = TilesM * _m;
        if (pitch < TilesFromPx) return;
        Vector2 a = longFirst ? px[0] : px[1], d = longFirst ? px[3] : px[0], along = to - from;
        int courses = (int)(along.Length() / pitch);
        for (int i = 1; i < courses; i++) DrawLine(a + along * (i / (float)courses), d + along * (i / (float)courses), hand.Ink with { A = TilesInk }, BuiltHand.LeastPx, true);
    }

    static IEnumerable<(Vector2 At, Vector2 Along)> Stepped(Vector2[] px, float every)
    {
        float carried = every / 2;
        for (int i = 0; i + 1 < px.Length; i++)
        {
            var leg = px[i + 1] - px[i];
            float length = leg.Length();
            if (length < 0.001f) continue;
            for (; carried < length; carried += every) yield return (px[i] + leg * (carried / length), leg / length);
            carried -= length;
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
        DrawPolyline(px, look.Fill.A > 0 ? look.Fill : look.Close, wide, true);
        if (!surface) return;
        if (s.Class == "track") Sides(px, RutM * _m, hand.Ink with { A = 0.45f }, BuiltHand.LeastPx);
        if (s.Class is "lane" or "street" or "road" && wide >= RutsFromPx) Sides(px, RutM * _m, hand.Ink with { A = RutInk }, BuiltHand.LeastPx);
        if (s.Class == "rampart") DrawPolyline(px, hand.Ink with { A = CrestInk }, Fine, true);
        if (s.Class == "wall") Merlons(hand, px, wide);
        if (s.Class == "jetty") Planks(hand, px, wide);
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
            DrawCircle(bush, r * (0.85f + 0.3f * Chance(n * 4.2f)), hand.Hedging);
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
