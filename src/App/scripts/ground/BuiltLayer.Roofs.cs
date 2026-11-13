using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class BuiltLayer
{
    enum Form { Gable, Hip, HalfHip, Pyramid, Flat, Lean, Mansard, Valley, Dome, Cone, Atrium, Court }

    enum Cover { Tile, Slate, Thatch, Shingle, Lead, Mud, Lime, Turf, Stone, Copper }

    const float NearFromPx = 9, FacetDark = 0.26f, FacetLight = 0.1f, CourseInk = 0.13f, ParapetM = 0.5f, LeastFacet = 0.5f, ToneWide = 0.14f, Weathered = 0.3f;
    const float TileRunM = 1.4f, SlateRunM = 0.8f, LeadRunM = 2.4f, CoursesFromPx = 5, MansardIn = 0.27f, HalfHipIn = 0.16f, ConeSides = 14;
    const float RingLeastM = 3.5f, RingMostM = 7, RingShare = 0.3f;
    const float ColumnM = 0.55f, ColumnApartM = 2.8f, ColumnFromPx = 1.6f;
    static readonly Vector2 Sunless = new Vector2(1, 1).Normalized();

    int _manner;

    readonly record struct Roofed(Vector2[] A, Vector2 From, Vector2 To, float LongPx, float ShortPx, Vector2 FrontA, Vector2 FrontB)
    {
        public Vector2 Centre => (A[0] + A[2]) / 2;
        public Vector2 Along => (A[1] - A[0]) / Mathf.Max(LongPx, 0.001f);
        public Vector2 Across => (A[3] - A[0]) / Mathf.Max(ShortPx, 0.001f);
        public Vector2 At(float along, float across) => A[0] + (A[1] - A[0]) * along + (A[3] - A[0]) * across;
        public Vector2[] Part(float t0, float t1, float s0, float s1) => [At(t0, s0), At(t1, s0), At(t1, s1), At(t0, s1)];
        public Vector2[] Inset(float px) => Part(px / Mathf.Max(LongPx, 0.001f), 1 - px / Mathf.Max(LongPx, 0.001f), px / Mathf.Max(ShortPx, 0.001f), 1 - px / Mathf.Max(ShortPx, 0.001f));
    }

    static Vector2 Mid(Vector2[] of) => of.Aggregate(Vector2.Zero, (sum, p) => sum + p) / of.Length;

    static Roofed Frame(Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2[] a = longFirst ? px : [px[1], px[2], px[3], px[0]];
        return new Roofed(a, (a[0] + a[3]) / 2, (a[1] + a[2]) / 2, (a[1] - a[0]).Length(), (a[3] - a[0]).Length(), px[0], px[1]);
    }

    static float Area(Vector2[] ring)
    {
        float twice = 0;
        for (int i = 0; i < ring.Length; i++) twice += ring[i].Cross(ring[(i + 1) % ring.Length]);
        return Mathf.Abs(twice) / 2;
    }

    void Facet(Color cover, Vector2 centre, bool inward, params Vector2[] facet)
    {
        if (Area(facet) < LeastFacet) return;
        float lean = (Mid(facet) - centre).Normalized().Dot(Sunless) * (inward ? -1 : 1);
        Filled(facet, lean > 0 ? cover.Darkened(FacetDark * lean) : cover.Lightened(FacetLight * -lean));
    }

    void Plain(Vector2[] ring, Color fill)
    {
        if (Area(ring) >= LeastFacet) Filled(ring, fill);
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

    Cover Main(float pick) => _manner switch
    {
        Manners.English => pick < 0.55f ? Cover.Slate : pick < 0.85f ? Cover.Tile : Cover.Thatch,
        Manners.Northern => pick < 0.45f ? Cover.Thatch : pick < 0.88f ? Cover.Shingle : Cover.Turf,
        Manners.Southern => pick < 0.68f ? Cover.Tile : Cover.Lime,
        Manners.Roman => Cover.Tile,
        Manners.Egyptian => pick < 0.72f ? Cover.Mud : Cover.Lime,
        Manners.Tribal => Cover.Thatch,
        _ => pick < 0.68f ? Cover.Tile : pick < 0.8f ? Cover.Slate : Cover.Thatch,
    };

    Cover Humble(float pick) => _manner switch
    {
        Manners.English or Manners.Continental => pick < 0.45f ? Cover.Thatch : pick < 0.9f ? Cover.Tile : Cover.Shingle,
        Manners.Northern => pick < 0.5f ? Cover.Thatch : Cover.Shingle,
        Manners.Egyptian => Cover.Mud,
        Manners.Tribal => Cover.Thatch,
        _ => Cover.Tile,
    };

    Form Shaped(Cover cover, float pick, float shortM, bool grand)
    {
        if (cover is Cover.Mud or Cover.Lime) return Form.Flat;
        if (cover is Cover.Thatch or Cover.Turf) return pick < 0.55f ? Form.Hip : pick < 0.8f ? Form.HalfHip : Form.Gable;
        return _manner switch
        {
            Manners.English => pick < 0.42f ? Form.Gable : pick < 0.66f ? Form.Hip : shortM >= 8 && pick < 0.9f ? Form.Valley : Form.HalfHip,
            Manners.Roman or Manners.Southern => pick < 0.5f ? Form.Hip : Form.Gable,
            Manners.Northern => pick < 0.75f ? Form.Gable : Form.Hip,
            _ => grand ? pick < 0.6f ? Form.Mansard : Form.Hip : pick < 0.5f ? Form.Gable : pick < 0.72f ? Form.Hip : pick < 0.9f || shortM < 8 ? Form.HalfHip : Form.Mansard,
        };
    }

    (Form Form, Cover Cover) Roofing(string cls, float shortM, float longM)
    {
        float pick = Chance(11.7f), other = Chance(23.1f);
        bool square = shortM / Mathf.Max(longM, 0.01f) > 0.85f, flat = _manner is Manners.Egyptian;
        switch (cls)
        {
            case "steeple":
                if (_manner is Manners.English or Manners.Southern) return (Form.Flat, Cover.Lead);
                return (_manner == Manners.Northern ? pick < 0.6f ? Form.Dome : Form.Pyramid : pick < 0.25f ? Form.Dome : Form.Pyramid, _manner == Manners.Northern ? Cover.Shingle : other < 0.5f
                    ? Cover.Slate : Cover.Copper);
            case "tower" or "keep" or "gatehouse" or "watchtower":
                return (Form.Flat, Cover.Lead);
            case "privy" or "shed" or "coop" or "pigsty" or "kennel" or "outshut" or "cartshed" or "taberna":
                return (flat ? Form.Flat : Form.Lean, Humble(pick));
            case "dovecote":
                return (flat ? Form.Dome : square && pick < 0.6f ? Form.Cone : Form.Pyramid, flat ? Cover.Mud : pick < 0.5f ? Cover.Tile : Cover.Slate);
            case "roundhouse" or "greathouse":
                return (Form.Cone, Cover.Thatch);
            case "silo" or "kiln":
                return (Form.Dome, cls == "kiln" ? Cover.Stone : Cover.Mud);
            case "domus":
                return (Form.Atrium, Cover.Tile);
            case "insula" or "cloister" or "villa":
                return (Form.Court, flat ? other < 0.5f ? Cover.Lime : Cover.Mud : _manner == Manners.Southern && other < 0.3f ? Cover.Lime : Cover.Tile);
            case "baths":
                return (Form.Dome, Cover.Tile);
            case "mill" when square:
                return (_manner == Manners.Northern ? Form.Pyramid : Form.Cone, _manner == Manners.Northern ? Cover.Shingle : _manner == Manners.Southern ? Cover.Tile : Cover.Slate);
            case "magazine":
                return (Form.Gable, Cover.Stone);
            case "warehouse" or "arsenal" or "markethall" or "basilica":
                return (flat ? Form.Flat : shortM >= 10 && cls != "markethall" && pick < 0.6f ? Form.Valley : pick < 0.5f ? Form.Hip : Form.Gable, cls == "basilica" ? Cover.Tile : Main(other));
            case "barn" or "stable" or "byre" or "granary" or "mill" or "smithy" or "longhouse":
                var humble = Humble(pick);
                return (flat ? Form.Flat : humble == Cover.Thatch ? other < 0.6f ? Form.HalfHip : Form.Hip : other < 0.6f ? Form.Gable : Form.HalfHip, humble);
            case "fisher":
                var poor = Humble(pick);
                return (Shaped(poor, other, shortM, false), poor);
            case "public":
                var grand = _manner == Manners.Continental ? Cover.Slate : Main(pick);
                return (Shaped(grand, other, shortM, true), grand);
        }
        var cover = Main(pick);
        return (Shaped(cover, other, shortM, false), cover);
    }

    void Courses(BuiltHand hand, Roofed f, Cover cover)
    {
        float run = (cover == Cover.Tile ? TileRunM : cover == Cover.Lead ? LeadRunM : SlateRunM) * _m;
        if (run < CoursesFromPx || cover is Cover.Thatch or Cover.Mud or Cover.Lime or Cover.Turf or Cover.Stone or Cover.Copper) return;
        bool acrossRidge = cover is Cover.Tile or Cover.Lead;
        int lines = (int)((acrossRidge ? f.LongPx : f.ShortPx) / run);
        for (int i = 1; i < lines; i++)
        {
            float at = i / (float)lines;
            if (acrossRidge) Edge(f.At(at, 0), f.At(at, 1), hand, CourseInk);
            else Edge(f.At(0, at), f.At(1, at), hand, CourseInk);
        }
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
                Plain(f.Inset(ParapetM * _m), cover);
                Ringed(f.Inset(ParapetM * _m), hand, CourseInk * 2);
                break;
            case Form.Lean:
                Plain(a, cover);
                Plain(f.Part(0, 1, 0, 0.2f), cover.Darkened(0.14f));
                Edge(a[3], a[2], hand);
                break;
            case Form.Mansard:
                var top = f.Inset(MansardIn * f.ShortPx);
                for (int i = 0; i < 4; i++)
                {
                    Facet(cover.Darkened(0.08f), centre, false, a[i], a[(i + 1) % 4], top[(i + 1) % 4], top[i]);
                    Edge(a[i], top[i], hand);
                }
                Plain(top, cover.Lightened(0.08f));
                Ringed(top, hand);
                break;
            case Form.Valley:
                for (int i = 0; i < 4; i++) Plain(f.Part(0, 1, i * 0.25f, (i + 1) * 0.25f), i % 2 == 0 ? cover.Lightened(FacetLight) : cover.Darkened(FacetDark * 0.8f));
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
            Plain(f.Inset(ParapetM * _m), cover);
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
        if (mark < 0.35f) Tub(hand, Mid(open), 0.6f, hand.Wet);
        else if (mark < 0.7f) Crown(hand, Mid(open), True(1.6f, TreeLeastPx));
    }

    void Roofs(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, Color cover)
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
                Courses(hand, f, kind);
                break;
        }
    }
}
