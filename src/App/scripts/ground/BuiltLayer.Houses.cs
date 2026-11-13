using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float StackM = 0.9f, StackFromPx = 3, PotM = 0.17f, SmokeM = 0.3f, SmokeShare = 0.14f, SmokeInk = 0.055f, DormerM = 1.15f, DormerFromPx = 3.2f, AwningM = 1.3f, OvenM = 1.25f;
    const float ButtressApartM = 4.2f, ButtressM = 0.9f, PillarApartM = 3, PillarM = 0.35f, StaddleM = 0.4f, TurretM = 2.6f, VentM = 0.6f, RoofStairM = 2.4f, PorchM = 3.6f;
    static readonly Vector2 Downwind = new Vector2(0.8f, -0.6f).Normalized();

    static bool Smokeless(string cls) => cls is "barn" or "shed" or "privy" or "stable" or "byre" or "granary" or "cartshed" or "warehouse" or "steeple" or "keep" or "gatehouse" or "tower"
        or "mill" or "coop" or "pigsty" or "dovecote" or "kennel" or "magazine" or "arsenal" or "markethall" or "outshut" or "silo" or "kiln" or "watchtower" or "baths" or "basilica";

    static Vector2[][] Wings(Vector2[] px)
    {
        if ((px[2] - px[1]).Length() > (px[5] - px[0]).Length())
        {
            var corner = px[1] + px[5] - px[0];
            return [[px[0], px[1], corner, px[5]], [px[4], corner, px[2], px[3]]];
        }
        var inner = px[0] + px[2] - px[1];
        return [[px[0], px[1], px[2], inner], [inner, px[3], px[4], px[5]]];
    }

    void Block(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        var look = hand.Block(s.Class);
        Vector2[][] parts = px.Length == 6 ? Wings(px) : px.Length == 4 ? [px] : [];
        if (parts.Length > 0 && parts.All(p => Mathf.Min((p[1] - p[0]).Length(), (p[2] - p[1]).Length()) >= NearFromPx))
        {
            for (int i = 0; i < parts.Length; i++) Building(hand, s.Class, parts[i], i == 0);
            return;
        }
        float shade = Mathf.Clamp(ShadowM * _m, BuiltHand.LeastPx, ShadowMostPx);
        if (look.Shadow.A > 0) Filled(px.Select(p => p + new Vector2(shade, shade)).ToArray(), look.Shadow with { A = 0.85f });
        Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
    }

    void Building(BuiltHand hand, string cls, Vector2[] px, bool main)
    {
        var f = Frame(px);
        float shortM = f.ShortPx / _m, longM = f.LongPx / _m, shade = Mathf.Clamp(ShadowM * _m, BuiltHand.LeastPx, ShadowMostPx);
        var (form, kind) = Roofing(cls, shortM, longM);
        bool round = form == Form.Cone || form == Form.Dome && cls is "silo" or "kiln" or "dovecote", sacred = cls == "church";
        if (sacred && _manner == Manners.Tribal)
        {
            Grove(hand, f);
            return;
        }
        if (hand.Shaded && round) DrawCircle(f.Centre + new Vector2(shade, shade), Mathf.Min(f.ShortPx, f.LongPx) * 0.5f, hand.Ink with { A = 0.85f });
        else if (hand.Shaded) Filled(px.Select(p => p + new Vector2(shade, shade)).ToArray(), hand.Ink with { A = 0.85f });
        var cover = Covering(hand, kind);
        if (sacred)
        {
            Sacred(hand, f, shortM, longM);
            return;
        }
        Roofs(hand, cls, f, form, kind, cover);
        if (!round) Ringed(px, hand, 0.9f);
        if (!main) return;
        Trim(hand, cls, f, form, kind, cover, shortM, longM);
        if (!Smokeless(cls) && !Manners.Old(_manner) && kind is not (Cover.Mud or Cover.Lime) && form is not (Form.Flat or Form.Court or Form.Cone or Form.Dome))
            Stacks(hand, cls, f, form, kind, longM);
        if (form == Form.Cone && cls != "mill" && cls != "dovecote") Porch(hand, f, cover);
        else if (!round) Door(hand, cls, px);
    }

    static Vector2 Ridge(Roofed f, Form form, float t)
    {
        float far = Mathf.Min(0.5f, 0.5f * f.ShortPx / Mathf.Max(f.LongPx, 1));
        return form switch
        {
            Form.Hip => f.From.Lerp(f.To, far).Lerp(f.To.Lerp(f.From, far), t),
            Form.Pyramid => f.Centre,
            Form.Mansard => f.At(Mathf.Lerp(0.3f, 0.7f, t), 0.5f),
            Form.Valley => f.At(t, 0.25f),
            Form.Lean => f.At(t, 0.82f),
            _ => f.From.Lerp(f.To, t),
        };
    }

    void Stack(BuiltHand hand, Roofed f, Vector2 at, float acrossM, float alongM, int pots, bool smoking)
    {
        var box = Box(at, f.Across, f.Along, acrossM, alongM);
        if (hand.Shaded) Plain(box.Select(p => p + Vector2.One * Mathf.Min(0.5f * _m, 3)).ToArray(), hand.Ink with { A = 0.4f });
        Plain(box, hand.Brick.Lerp(hand.Stone, Chance(51.3f) * 0.6f));
        Ringed(box, hand, 0.8f);
        for (int i = 0; i < pots; i++) DrawCircle(at + f.Across * ((i - (pots - 1) / 2f) * PotM * 2.6f * _m), Mathf.Max(PotM * _m, 0.8f), hand.Tile.Darkened(0.25f));
        if (pots == 0 && alongM * _m >= StackFromPx * 2) DrawRect(new Rect2(at - Vector2.One * alongM * _m * 0.2f, Vector2.One * alongM * _m * 0.4f), hand.Ink with { A = 0.6f });
        if (!smoking || !hand.Shaded) return;
        for (int i = 1; i <= 5; i++) DrawCircle(at + Downwind * (i * 0.75f * _m)
            + Downwind.Orthogonal() * ((Chance(i * 3.3f) - 0.5f) * 0.5f * _m), (SmokeM + 0.16f * i) * _m, hand.Stone.Lightened(0.3f) with { A = SmokeInk * (6 - i) });
    }

    void Stacks(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, float longM)
    {
        if (StackM * _m < StackFromPx) return;
        bool forge = cls is "smithy" or "bakery", smoking = forge || Chance(77.7f) < SmokeShare;
        if (kind is Cover.Thatch or Cover.Turf && _manner is Manners.Northern or Manners.Tribal)
        {
            Plain(Box(Ridge(f, form, 0.5f), f.Across, f.Along, 0.55f, 0.8f), hand.Ink with { A = 0.65f });
            return;
        }
        switch (_manner)
        {
            case Manners.English:
                Stack(hand, f, Ridge(f, form, 0.05f), 1.5f, 0.75f, 2 + (int)(Chance(61.1f) * 2.99f), smoking);
                if (longM >= 9) Stack(hand, f, Ridge(f, form, 0.95f), 1.5f, 0.75f, 2, false);
                break;
            case Manners.Northern:
                Stack(hand, f, Ridge(f, form, 0.5f), 1.15f, 1.15f, 0, smoking);
                break;
            case Manners.Southern:
                Stack(hand, f, Ridge(f, form, 0.22f) + f.Across * (0.2f * f.ShortPx), 0.7f, 0.7f, 0, smoking);
                break;
            default:
                Stack(hand, f, Ridge(f, form, 0.3f), StackM, StackM, 0, smoking && !forge);
                if (longM >= 13) Stack(hand, f, Ridge(f, form, 0.76f), StackM, StackM, 0, false);
                break;
        }
        if (forge) Stack(hand, f, Ridge(f, form, 0.8f), 1.4f, 1.4f, 0, true);
    }

    (Vector2 Mid, Vector2 Along, Vector2 Out) Front(Roofed f)
    {
        Vector2 mid = (f.FrontA + f.FrontB) / 2, along = (f.FrontB - f.FrontA).Normalized(), outward = along.Orthogonal();
        return (mid, along, outward.Dot(mid - f.Centre) < 0 ? -outward : outward);
    }

    void Posts(BuiltHand hand, Vector2[] line, Color colour)
    {
        if (PillarM * _m < 1) return;
        foreach (var (at, _) in Stepped(line, PillarApartM * _m)) DrawCircle(at, PillarM * _m / 2 + 0.3f, colour);
    }

    void Porch(BuiltHand hand, Roofed f, Color cover)
    {
        var way = (Front(f).Mid - f.Centre).Normalized();
        float r = Mathf.Min(f.ShortPx, f.LongPx) * 0.5f;
        var porch = Box(f.Centre + way * (r + 0.5f * _m), way, way.Orthogonal(), 1.6f, 1.5f);
        Plain(porch, cover.Darkened(0.12f));
        Ringed(porch, hand, 0.7f);
    }

    void RoofLife(BuiltHand hand, Roofed f)
    {
        if (RoofStairM * _m < 6) return;
        float pick = Chance(83.1f);
        var stair = f.Part(0.06f, 0.06f + RoofStairM * _m / f.LongPx, 0.08f, 0.08f + 0.9f * _m / f.ShortPx);
        Plain(stair, hand.Ink with { A = 0.3f });
        for (int i = 1; i < 4; i++) Edge(stair[0].Lerp(stair[1], i / 4f), stair[3].Lerp(stair[2], i / 4f), hand, 0.5f);
        if (pick < 0.6f) DrawRect(new Rect2(f.At(0.7f, 0.3f) - Vector2.One * VentM * _m / 2, Vector2.One * VentM * _m), hand.Ink with { A = 0.55f });
        if (pick > 0.35f)
        {
            var shade = f.Part(0.5f, 0.86f, 0.55f, 0.9f);
            Plain(shade, hand.Cloth with { A = 0.85f });
            Ringed(shade, hand, 0.5f);
            for (int i = 1; i < 4; i++) Edge(shade[0].Lerp(shade[1], i / 4f), shade[3].Lerp(shade[2], i / 4f), hand, 0.25f);
        }
        if (pick > 0.2f && pick < 0.8f)
            for (int i = 0; i < 3; i++) Tub(hand, f.At(0.2f + 0.07f * i, 0.72f + 0.05f * (i % 2)), 0.28f, hand.Tile);
    }

    void Trim(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, Color cover, float shortM, float longM)
    {
        var (mid, along, outward) = Front(f);
        float wide = (f.FrontB - f.FrontA).Length();
        if (form == Form.Flat && kind is Cover.Lead or Cover.Stone)
            foreach (var (at, _) in Stepped(f.A.Append(f.A[0]).ToArray(), MerlonM * _m))
                if (0.7f * _m >= 1.5f) DrawRect(new Rect2(at - Vector2.One * 0.35f * _m, Vector2.One * 0.7f * _m), hand.Ink with { A = 0.5f });
        if (form == Form.Flat && kind is Cover.Mud or Cover.Lime && cls is not ("privy" or "shed" or "coop" or "pigsty")) RoofLife(hand, f);
        if (form == Form.Mansard && DormerM * _m >= DormerFromPx)
            for (int i = 0; i < Mathf.Clamp((int)(longM / 4.5f), 1, 5); i++)
            {
                var dormer = Box(f.At((i + 0.5f) / Mathf.Clamp((int)(longM / 4.5f), 1, 5), 0.13f), f.Along, f.Across, DormerM, DormerM * 0.9f);
                Plain(dormer, cover.Lightened(0.14f));
                Ringed(dormer, hand, 0.7f);
            }
        switch (cls)
        {
            case "shop" or "taberna" when AwningM * _m >= 2.5f:
                Vector2 a = mid - along * wide * 0.36f, b = mid + along * wide * 0.36f, reach = outward * AwningM * _m;
                Plain([a, b, b + reach, a + reach], hand.Cloth);
                for (float from = 0; from < 1; from += 0.25f) Plain([a.Lerp(b, from), a.Lerp(b, from + 0.125f), a.Lerp(b, from + 0.125f) + reach, a.Lerp(b, from)
                    + reach], hand.Signal with { A = 0.75f });
                Ringed([a, b, b + reach, a + reach], hand, 0.7f);
                break;
            case "bakery" when OvenM * _m >= 2.5f:
                Dome(hand, f.Centre - (mid - f.Centre) * 1.02f, OvenM * _m, hand.Brick);
                break;
            case "cartshed" or "markethall":
                if (cls == "cartshed") Plain([f.FrontA, f.FrontB, f.FrontB - outward * 1.1f * _m, f.FrontA - outward * 1.1f * _m], hand.Ink with { A = 0.35f });
                Posts(hand, cls == "cartshed" ? [f.FrontA, f.FrontB]
                    : f.A.Select(p => p + (p - f.Centre).Normalized() * 0.8f * _m).Append(f.A[0] + (f.A[0] - f.Centre).Normalized() * 0.8f * _m).ToArray(), hand.Stone);
                break;
            case "granary":
                foreach (var corner in f.A) Tub(hand, corner + (f.Centre - corner).Normalized() * 0.2f * _m, StaddleM, hand.Stone);
                break;
            case "barn" when PorchM * _m >= 8 && Chance(91.7f) < 0.45f && !Manners.Old(_manner):
                var porch = Box(mid + outward * 0.9f * _m, outward, along, 2.6f, PorchM);
                Slopes(hand, Frame(porch), Form.Gable, cover);
                Ringed(porch, hand, 0.8f);
                break;
            case "public" when !Manners.Old(_manner) && TurretM * _m >= 5:
                if (_manner == Manners.English) Dome(hand, f.Centre, TurretM * _m * 0.42f, hand.Copper);
                else
                {
                    var turret = Box(f.Centre, f.Along, f.Across, TurretM, TurretM);
                    Slopes(hand, Frame(turret), Form.Pyramid, hand.Slate);
                    Ringed(turret, hand, 0.8f);
                }
                break;
            case "smithy":
                DrawCircle(Ridge(f, form, 0.8f), Mathf.Max(0.35f * _m, 1.2f), hand.Signal);
                break;
            case "steeple" when form == Form.Flat:
                Tub(hand, f.Centre, 0.5f, hand.Lead.Darkened(0.2f));
                break;
        }
    }
}
