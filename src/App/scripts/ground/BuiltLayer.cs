using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer : BuiltProps
{
    const float BedM = 1.6f, BedFromPx = 3, BedInk = 0.2f, GraveM = 0.9f, GraveAlongM = 3.4f, GraveAcrossM = 2.6f, GraveFromPx = 2.2f, GraveKeep = 0.7f, GraveEdgeM = 1.6f;
    const float ShadowM = 1.3f, ShadowMostPx = 5;
    const float SurfaceFromPx = 5, DitchOffM = 1.7f, WallFromPx = 4;
    const float StakeM = 0.6f, StakeFromPx = 3, BushM = 1.25f, BushApartM = 1.8f;
    const float PostM = 2.5f, PostFromPx = 6, CrestInk = 0.35f, RubricShare = 0.3f, TarredConcrete = 0.45f;
    const int MostBeds = 80, MostGraves = 120;

    readonly List<BuiltPainter.Shape> _shapes;
    readonly Rect2 _uv;
    readonly float _pixels, _squash;
    readonly int _style;
    readonly Raising _raised;

    public readonly record struct Raising(float BarePx, bool Photo)
    {
        public static readonly Raising None = default;

        public bool Bare(float pixelsPerMetre) => BarePx > 0 && pixelsPerMetre >= BarePx;
    }

    public BuiltLayer(List<BuiltPainter.Shape> shapes, Rect2 uv, float pixels, float perUnit, float squash, int style, Raising raised = default)
    {
        _raised = raised;
        _shapes = shapes;
        _uv = uv;
        _pixels = pixels;
        _perUnit = perUnit;
        _squash = squash;
        _style = style;
        Scale = new Vector2(1, squash);
    }

    int Aged(int manner) => Manners.For(Manners.Age(MapStyles.ShaderOrder[Mathf.Clamp(_style, 0, MapStyles.ShaderOrder.Count - 1)]), manner);

    Vector2[] Px(Vector2[] uv) => uv.Select(p => (p - _uv.Position) / _uv.Size * _pixels * new Vector2(1, 1 / _squash)).ToArray();

    protected override void Shape()
    {
        var hand = BuiltHand.Of(_style);
        var roads = _shapes.Where(BuiltHand.IsRoad).ToList();
        bool paved = false;
        foreach (var s in _shapes)
        {
            var px = Px(Took(s).Uv);
            _town = s.Town;
            if (s.Kind == PartKinds.Area) Area(hand, s, px);
            else if (BuiltHand.IsRoad(s))
            {
                if (paved) continue;
                paved = true;
                foreach (var road in roads) Line(hand, Took(road), Px(road.Uv), true, false);
                foreach (var road in roads) Line(hand, Took(road), Px(road.Uv), false, true);
                var ways = roads.Select(r => Way(hand, r)).ToList();
                Opened(RoadMouths.Of(ways, SurfaceFromPx).ToList(), Fine, under => Patched(hand, roads[under.Through], under));
            }
            else if (s.Kind == PartKinds.Line && !(s.Class is Hallmarks.Aqueduct or Footings.Revetment && _raised.Bare(_m))) Line(hand, s, px, true, true);
            else if (s.Kind == PartKinds.Block && !_raised.Bare(_m)) Block(hand, s, px);
            else if (s.Kind == PartKinds.Sign) Sign(hand, s, px[0]);
            _key = default;
        }
    }

    void Area(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3 || s.Class == Traces.Block || Ground(hand, s, px) || Paved(hand, s, px) || Terraced(hand, s, px, _style) || Banks(hand, s, px)) return;
        if (s.Class is "arena" or "quarry" or "saltpans" or Hallmarks.Theatre or Hallmarks.Parterre or Traces.Circus)
        {
            if (s.Class == Hallmarks.Theatre) Theatre(hand, px);
            else if (s.Class == Traces.Circus) Circus(hand, px);
            else if (s.Class == Hallmarks.Parterre) Parterre(hand, px);
            else if (s.Class == "arena") Arena(hand, px);
            else Worked(hand, s.Class, px);
            return;
        }
        var look = hand.Area(s.Class);
        var ground = SurfaceBook.Ground(s.Class, _builds);
        if (_raised.Photo && ground == Surface.None && SurfaceBook.Grassed(s.Class)) ground = Surface.Lawn;
        if (ground != Surface.None && look.Fill.A > 0) Surfaced(px, look.Fill with { A = 1 }, ground, look.Fill.A < 1);
        else if (look.Fill.A > 0) Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
        if (s.Class == "meadow" && px.Length == 4) Beds(hand, px);
        if (s.Class == "churchyard" && px.Length == 4 && !Manners.Old(_manner)) Graves(hand, px);
        if (s.Class == "forum" && px.Length == 4) Slabs(hand, px);
        if (s.Class is "vineyard" or "osiers" && px.Length == 4) Vines(hand, px);
        if (s.Class == Terraces.Vineyard) VineSigns(hand, px, _style);
        if (s.Class == "threshing") Threshed(hand, px);
        if (s.Class == "airstrip" && px.Length == 4) Runway(hand, px);
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
        if (!hand.Crosses || !Manners.Crossed(_culture))
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

    BuiltPainter.Shape Took(BuiltPainter.Shape road)
    {
        (_key, _m) = (road.Uv[0] * KeyScale, _perUnit * road.PerMetre);
        (_culture, _builders, _adorners) = (Aged(road.Manner), Aged(road.Built), Aged(road.Adorned));
        (_manner, _builds, _adorns) = (Manners.Kin(_culture), Manners.Kin(_builders), Manners.Kin(_adorners));
        return road;
    }

    RoadMouths.Way Way(BuiltHand hand, BuiltPainter.Shape road)
    {
        var (fill, lining) = Paving(hand, Took(road).Class, true);
        return new(road, Px(road.Uv), hand.Line(road.Class), _m, fill, lining);
    }

    (Color Fill, Surface Lining) Paving(BuiltHand hand, string cls, bool surface)
    {
        if (surface && Tarred(cls)) return (cls == "highway" ? hand.Asphalt : hand.Asphalt.Lerp(hand.Concrete, TarredConcrete), Surface.None);
        var look = hand.Line(cls);
        return (look.Fill.A > 0 ? look.Fill : look.Close, SurfaceBook.Lining(cls, _builders));
    }

    void Patched(BuiltHand hand, BuiltPainter.Shape road, RoadMouths.Patch under)
    {
        if (under.Px.Length < 2) return;
        var look = hand.Line(Took(road).Class);
        var (fill, lining) = Paving(hand, road.Class, true);
        if (fill.A <= 0) return;
        float wide = True(look.Metres, look.LeastPx);
        DrawPolyline(under.Px, fill with { A = 1 }, wide, false);
        if (lining != Surface.None) Strip(under.Px, wide, fill with { A = 1 }, lining, under.FromPx);
    }

    void Line(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased, bool filled)
    {
        if (px.Length < 2 || Way(hand, s, px, cased) || Run(hand, s, px, cased) || Dug(hand, s, px, cased) || Watered(hand, s, px, cased, _style) || Climb(hand, s, px, cased)) return;
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
        if (cased && s.Class == "revetment") Revetted(hand, px);
        bool runs = s.Class is "river" or "stream";
        if (!filled || bare)
        {
            if (runs && cased) Flowing(px, wide, look.Ink);
            return;
        }
        var (fill, lining) = Paving(hand, s.Class, surface);
        DrawPolyline(px, fill, wide, true);
        if (runs && !surface) Flowing(px, wide, look.Ink);
        if (hand.Rubricated && !surface && s.Class is "road" or "chaussee") DrawPolyline(px, hand.Signal, Mathf.Max(BuiltHand.LeastPx, wide * RubricShare), true);
        if (!surface) return;
        if (lining != Surface.None) Strip(px, wide, fill with { A = 1 }, lining);
        if (runs) Flowing(px, wide, look.Ink);
        if (s.Class == "rampart") Hachured(hand, px, wide);
        if (s.Class == "highway") Marked(hand, px, wide);
        if (Modern && s.Class is "road" or "highway") Poles(hand, px, wide / 2);
        if (s.Class == "wall") Merlons(hand, px, wide);
        if (s.Class != "chaussee") return;
        Sides(px, wide / 2 + DitchOffM * _m, hand.Wet with { A = 0.7f }, Fine);
    }

    const float ArrowsApartPx = 56, ArrowLeastPx = 3.2f, ArrowMostPx = 11, ArrowWithinFrom = 1.6f, ArrowPen = 0.22f;
    static readonly Color FlowLight = new(0.88f, 0.93f, 0.97f, 0.85f);

    void Flowing(Vector2[] px, float wide, Color ink)
    {
        float apart = Mathf.Pow(2, Mathf.Ceil(Mathf.Log(ArrowsApartPx / _m) / Mathf.Log(2))) * _m;
        float barb = Mathf.Clamp(wide * 0.8f, ArrowLeastPx, ArrowMostPx);
        var colour = wide >= barb * ArrowWithinFrom ? FlowLight : ink with { A = 1 };
        foreach (var (at, along) in Stepped(px, apart))
        {
            var across = along.Orthogonal() * (barb * 0.6f);
            var tip = at + along * (barb * 0.5f);
            DrawPolyline([tip - along * barb + across, tip, tip - along * barb - across], colour, Mathf.Max(BuiltHand.LeastPx, barb * ArrowPen), true);
        }
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
