using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float NearFromPx = 9, FacetDark = 0.26f, FacetLight = 0.1f, CourseInk = 0.13f, ParapetM = 0.5f, LeastFacet = 0.5f, ToneWide = 0.14f, Weathered = 0.3f;
    const float MansardIn = 0.27f, HalfHipIn = 0.16f, ConeSides = 14;
    const float RingLeastM = 3.5f, RingMostM = 7, RingShare = 0.3f;
    const float ColumnM = 0.55f, ColumnApartM = 2.8f, ColumnFromPx = 1.6f;
    static readonly Vector2 Sunless = new Vector2(1, 1).Normalized();

    int _manner;

    void Facet(Color cover, Vector2 centre, bool inward, params Vector2[] facet)
    {
        if (AreaOf(facet) < LeastFacet) return;
        float lean = (Mid(facet) - centre).Normalized().Dot(Sunless) * (inward ? -1 : 1);
        var shaded = lean > 0 ? cover.Darkened(FacetDark * lean) : cover.Lightened(FacetLight * -lean);
        if (_roof == Surface.None) Filled(facet, shaded);
        else Sloped(facet, shaded, centre, inward);
    }

    void Plain(Vector2[] ring, Color fill)
    {
        if (AreaOf(ring) >= LeastFacet) Filled(ring, fill);
    }

    void Edge(Vector2 a, Vector2 b, BuiltHand hand, float ink = RidgeInk) => DrawLine(a, b, hand.Ink with { A = ink }, BuiltHand.LeastPx, true);

    void Ringed(Vector2[] ring, BuiltHand hand, float ink = RidgeInk) => DrawPolyline(ring.Append(ring[0]).ToArray(), hand.Ink with { A = ink }, BuiltHand.LeastPx, true);

    Color Covering(BuiltHand hand, Cover cover)
    {
        var colour = cover switch
        {
            Cover.Slate => hand.Slate, Cover.Thatch => hand.Thatch, Cover.Shingle => hand.Shingle, Cover.Lead => hand.Lead, Cover.Mud => hand.Mud, Cover.Lime => hand.Lime,
            Cover.Turf => hand.Turf, Cover.Stone => hand.Stone, Cover.Copper => hand.Copper, _ => hand.Tile.Lerp(hand.Brick, Chance(6.1f) * Weathered),
        };
        float tone = (Chance(4.4f) - 0.5f) * ToneWide;
        return tone > 0 ? colour.Lightened(tone) : colour.Darkened(-tone);
    }

    void Decked(Roofed f, Vector2[] ring, Color fill)
    {
        if (AreaOf(ring) < LeastFacet) return;
        if (_roof == Surface.None) Filled(ring, fill);
        else Covered(ring, fill, _roof, f.A[0], f.Along);
    }

    void Slopes(BuiltHand hand, Roofed f, Form form, Color cover)
    {
        var a = f.A;
        Vector2 centre = f.Centre, from = f.From, to = f.To;
        switch (form)
        {
            case Form.Hip:
                float far = Mathf.Min(0.5f, 0.5f * f.ShortPx / Mathf.Max(f.LongPx, 1));
                Vector2 h0 = from.Lerp(to, far), h1 = to.Lerp(from, far);
                Facet(cover, centre, false, a[0], a[1], h1, h0);
                Facet(cover, centre, false, a[2], a[3], h0, h1);
                Facet(cover, centre, false, a[3], a[0], h0);
                Facet(cover, centre, false, a[1], a[2], h1);
                Edge(h0, h1, hand);
                foreach (var (corner, end) in new[] { (a[0], h0), (a[3], h0), (a[1], h1), (a[2], h1) }) Edge(corner, end, hand);
                break;
            case Form.Pyramid:
                for (int i = 0; i < 4; i++)
                {
                    Facet(cover, centre, false, a[i], a[(i + 1) % 4], centre);
                    Edge(a[i], centre, hand);
                }
                break;
            case Form.Flat:
                Plain(a, cover.Darkened(0.1f));
                Decked(f, f.Inset(ParapetM * _m), cover);
                Ringed(f.Inset(ParapetM * _m), hand, CourseInk * 2);
                break;
            case Form.Lean:
                Decked(f, a, cover);
                Decked(f, f.Part(0, 1, 0, 0.2f), cover.Darkened(0.14f));
                Edge(a[3], a[2], hand);
                break;
            case Form.Mansard:
                var top = f.Inset(MansardIn * f.ShortPx);
                for (int i = 0; i < 4; i++)
                {
                    Facet(cover.Darkened(0.08f), centre, false, a[i], a[(i + 1) % 4], top[(i + 1) % 4], top[i]);
                    Edge(a[i], top[i], hand);
                }
                Decked(f, top, cover.Lightened(0.08f));
                Ringed(top, hand);
                break;
            case Form.Valley:
                for (int i = 0; i < 4; i++) Decked(f, f.Part(0, 1, i * 0.25f, (i + 1) * 0.25f), i % 2 == 0 ? cover.Lightened(FacetLight) : cover.Darkened(FacetDark * 0.8f));
                Edge(f.At(0, 0.25f), f.At(1, 0.25f), hand);
                Edge(f.At(0, 0.75f), f.At(1, 0.75f), hand);
                Edge(f.At(0, 0.5f), f.At(1, 0.5f), hand, 0.8f);
                break;
            default:
                Facet(cover, centre, false, a[0], a[1], to, from);
                Facet(cover, centre, false, from, to, a[2], a[3]);
                if (form == Form.HalfHip)
                    foreach (var (end, inner, one, two) in new[] { (from, from.Lerp(to, HalfHipIn), a[0], a[3]), (to, to.Lerp(from, HalfHipIn), a[1], a[2]) })
                    {
                        Facet(cover, centre, false, inner, end.Lerp(one, 0.42f), end.Lerp(two, 0.42f));
                        Edge(inner, end.Lerp(one, 0.42f), hand);
                        Edge(inner, end.Lerp(two, 0.42f), hand);
                    }
                Edge(form == Form.HalfHip ? from.Lerp(to, HalfHipIn) : from.Lerp(to, 0.02f), form == Form.HalfHip ? to.Lerp(from, HalfHipIn) : to.Lerp(from, 0.02f), hand);
                break;
        }
    }

    void Cone(BuiltHand hand, Vector2 centre, float r, Color cover, bool smokeHole)
    {
        for (int i = 0; i < ConeSides; i++)
        {
            Vector2 one = centre + Vector2.Right.Rotated(i * Mathf.Tau / ConeSides) * r, two = centre + Vector2.Right.Rotated((i + 1) * Mathf.Tau / ConeSides) * r;
            Facet(cover, centre, false, centre, one, two);
            if (r >= NearFromPx) Edge(centre, one, hand, CourseInk);
        }
        DrawArc(centre, r, 0, Mathf.Tau, 24, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
        if (smokeHole) DrawCircle(centre, Mathf.Max(r * 0.09f, 1), hand.Ink with { A = 0.7f });
    }

    void Dome(BuiltHand hand, Vector2 centre, float r, Color cover)
    {
        DrawCircle(centre, r, cover.Darkened(FacetDark * 0.7f));
        DrawCircle(centre - Sunless * r * 0.14f, r * 0.84f, cover);
        DrawCircle(centre - Sunless * r * 0.34f, r * 0.42f, cover.Lightened(0.16f));
        DrawArc(centre, r, 0, Mathf.Tau, 24, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
    }

    void Domes(BuiltHand hand, Roofed f, Color cover, Color walls)
    {
        Plain(f.A, walls);
        int count = Mathf.Max(1, Mathf.RoundToInt(f.LongPx / Mathf.Max(f.ShortPx, 1)));
        for (int i = 0; i < count; i++) Dome(hand, f.At((i + 0.5f) / count, 0.5f), Mathf.Min(f.ShortPx, f.LongPx / count) * 0.46f, cover);
    }

    void Opening(BuiltHand hand, Roofed f, Vector2[] open, Color cover, float reach)
    {
        var eave = open.Select(p => f.Centre + (p - Mid(open)) * reach + (Mid(open) - f.Centre)).ToArray();
        for (int i = 0; i < 4; i++) Facet(cover, Mid(open), true, eave[i], eave[(i + 1) % 4], open[(i + 1) % 4], open[i]);
        Ringed(eave, hand, CourseInk * 2);
    }

    void Columns(BuiltHand hand, Vector2[] ring, float insetPx)
    {
        float r = ColumnM * _m / 2;
        if (r * 2 < ColumnFromPx) return;
        var middle = Mid(ring);
        var inner = ring.Select(p => p + (middle - p).Normalized() * insetPx).ToArray();
        foreach (var (at, _) in Stepped(inner.Append(inner[0]).ToArray(), ColumnApartM * _m))
        {
            DrawCircle(at, r, hand.Lime);
            DrawArc(at, r, 0, Mathf.Tau, 8, hand.Ink with { A = 0.7f }, BuiltHand.LeastPx, true);
        }
    }

    void Atrium(BuiltHand hand, Roofed f, Color cover)
    {
        Plain(f.A, cover);
        bool deep = f.LongPx >= f.ShortPx * 1.45f;
        var atrium = deep ? f.Part(0.2f, 0.4f, 0.34f, 0.66f) : f.Part(0.38f, 0.62f, 0.34f, 0.66f);
        Opening(hand, f, atrium, cover, 2.1f);
        Plain(atrium, hand.Stone);
        Plain(atrium.Select(p => Mid(atrium) + (p - Mid(atrium)) * 0.55f).ToArray(), hand.Wet);
        Ringed(atrium, hand);
        if (!deep) return;
        var garden = f.Part(0.58f, 0.9f, 0.2f, 0.8f);
        Opening(hand, f, garden, cover, 1.25f);
        Plain(garden, hand.Area("garden").Fill with { A = 1 });
        Ringed(garden, hand);
        Columns(hand, garden, ColumnM * _m);
        if (Chance(31.3f) < 0.6f) Crown(hand, Mid(garden), True(1.5f, TreeLeastPx));
    }

    void Court(BuiltHand hand, Roofed f, Color cover, bool flat)
    {
        float ring = Mathf.Clamp(f.ShortPx * RingShare, RingLeastM * _m, RingMostM * _m);
        if (ring * 2.4f > f.ShortPx)
        {
            Slopes(hand, f, flat ? Form.Flat : Form.Hip, cover);
            return;
        }
        var open = f.Inset(ring);
        if (flat)
        {
            Plain(f.A, cover.Darkened(0.1f));
            Decked(f, f.Inset(ParapetM * _m), cover);
        }
        else
        {
            var crest = f.Inset(ring / 2);
            for (int i = 0; i < 4; i++)
            {
                Facet(cover, f.Centre, false, f.A[i], f.A[(i + 1) % 4], crest[(i + 1) % 4], crest[i]);
                Facet(cover, f.Centre, true, crest[i], crest[(i + 1) % 4], open[(i + 1) % 4], open[i]);
                Edge(f.A[i], open[i], hand);
            }
            Ringed(crest, hand);
        }
        Plain(open, hand.Area("yard").Fill with { A = 1 });
        Ringed(open, hand);
        if (_manner is Manners.Roman or Manners.Southern or Manners.Egyptian) Columns(hand, open, ColumnM * _m);
        float mark = Chance(41.9f);
        if (mark < 0.35f) Tub(hand, Mid(open), 0.6f, hand.Wet, Surface.Water);
        else if (mark < 0.7f) Crown(hand, Mid(open), True(1.6f, TreeLeastPx));
    }

    void Roofs(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, Color cover)
    {
        _roof = SurfaceBook.Roofing(kind, _manner);
        Topped(hand, cls, f, form, kind, cover);
        _roof = Surface.None;
    }

    void Topped(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, Color cover)
    {
        switch (form)
        {
            case Form.Cone:
                Cone(hand, f.Centre, Mathf.Min(f.ShortPx, f.LongPx) * 0.5f, cover, kind == Cover.Thatch);
                break;
            case Form.Dome when cls is "steeple":
                Plain(f.A, hand.Stone);
                Dome(hand, f.Centre, f.ShortPx * 0.44f, cover);
                break;
            case Form.Dome when cls is "silo" or "kiln" or "dovecote":
                Dome(hand, f.Centre, Mathf.Min(f.ShortPx, f.LongPx) * 0.5f, cover);
                break;
            case Form.Dome:
                Domes(hand, f, cover, hand.Lime.Darkened(0.08f));
                break;
            case Form.Atrium:
                Atrium(hand, f, cover);
                break;
            case Form.Court:
                Court(hand, f, cover, kind is Cover.Mud or Cover.Lime);
                break;
            default:
                Slopes(hand, f, form, cover);
                break;
        }
    }
}
