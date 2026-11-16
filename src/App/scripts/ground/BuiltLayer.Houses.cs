using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float StackM = 0.9f, StackFromPx = 3, PotM = 0.17f, SmokeM = 0.3f, SmokeShare = 0.14f, SmokeInk = 0.055f, DormerM = 1.15f, DormerFromPx = 3.2f, OvenM = 1.25f;
    const float ButtressApartM = 4.2f, ButtressM = 0.9f, PillarApartM = 3, PillarM = 0.35f, StaddleM = 0.4f, TurretM = 2.6f, PorchM = 3.6f;
    static readonly Vector2 Downwind = new Vector2(0.8f, -0.6f).Normalized();

    void Block(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (s.Class == Castles.RoundTower && px.Length >= RoundFrom && Drum(hand, px) || s.Class == Settlements.Rubble && px.Length >= 3 && Rubble(hand, px)) return;
        if (s.Class == Settlements.Ruin && px.Length >= 3) Ruin(hand, px);
        if (px.Length < 3 || s.Class == Settlements.Ruin || px.Length == 4 && (Industry(hand, s.Class, px) || Military(hand, s.Class, px) || Delved(hand, s.Class, px))) return;
        var look = hand.Block(s.Class);
        Vector2[][] parts = px.Length == 6 ? Wings(px) : px.Length == 4 ? [px] : [];
        if (parts.Length > 0 && parts.All(p => Mathf.Min((p[1] - p[0]).Length(), (p[2] - p[1]).Length()) >= NearFromPx))
        {
            for (int i = 0; i < parts.Length; i++) Building(hand, s.Class, parts[i], i == 0);
            return;
        }
        float shade = Mathf.Clamp(ShadowM * _m, BuiltHand.LeastPx, ShadowMostPx);
        if (look.Shadow.A > 0) Filled(px.Select(p => p + new Vector2(shade, shade)).ToArray(), look.Shadow with { A = 0.85f });
        Filled(px, parts.Length > 0 ? Afar(hand, s.Class, Frame(parts[0]), look.Fill) : look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
    }

    internal const float ChurchPick = 13.9f, FormPick = 11.7f, CoverPick = 23.1f;
    float _town = -1;

    Color Afar(BuiltHand hand, string cls, Roofed f, Color otherwise)
    {
        if (cls != "church") return Covering(hand, RoofBook.Of(cls, _builders, _adorners, f.ShortPx / _m, f.LongPx / _m, Chance(FormPick), Chance(CoverPick), _town).Cover);
        return _manner is Manners.Roman or Manners.Egyptian or Manners.Tribal ? otherwise : Covering(hand, RoofBook.Church(_builders, Chance(ChurchPick)));
    }

    void Building(BuiltHand hand, string cls, Vector2[] px, bool main)
    {
        var f = Frame(px);
        float shortM = f.ShortPx / _m, longM = f.LongPx / _m, shade = Mathf.Clamp(ShadowM * _m, BuiltHand.LeastPx, ShadowMostPx);
        var (form, kind) = RoofBook.Of(cls, _builders, _adorners, shortM, longM, Chance(FormPick), Chance(CoverPick), _town);
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
        if (!Smokeless(cls) && !Manners.Old(_builds) && RoofBook.Stacked(_builders) && !RoofBook.Level(kind) && form is not (Form.Flat or Form.Court or Form.Cone or Form.Dome or Form.Vault))
            Stacks(hand, cls, f, form, kind, longM);
        if (form == Form.Cone && cls != "mill" && cls != "dovecote") Porch(hand, f, cover);
        else if (!round) Door(hand, cls, px);
    }

    const float PorchDeepM = 2.2f, StiltM = 0.28f, PartyWallM = 5.2f;

    void Manner(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, Color cover, float longM)
    {
        var (mid, along, outward) = Front(f);
        float wide = (f.FrontB - f.FrontA).Length();
        if (_builders == Manners.American && cls is "house" or "farm" or "inn" or "tavern" && PorchDeepM * _m >= 2.5f)
        {
            var porch = new[] { mid - along * wide * 0.4f, mid + along * wide * 0.4f, mid + along * wide * 0.4f + outward * PorchDeepM * _m, mid - along * wide * 0.4f + outward * PorchDeepM * _m };
            Plain(porch, hand.Lime.Lightened(0.1f));
            Ringed(porch, hand, 0.6f);
        }
        if (_builders is Manners.American && form is not Form.Flat) DrawPolyline(f.A.Append(f.A[0]).ToArray(), hand.Lime with { A = 0.9f }, Mathf.Max(0.35f * _m, 1), true);
        if (_builders is Manners.Japanese or Manners.Nippon && cls == "shed") DrawPolyline(f.A.Append(f.A[0]).ToArray(), hand.Lime, Mathf.Max(0.5f * _m, 1.2f), true);
        if (_builders == Manners.Egyptian && form == Form.Flat) DrawPolyline(f.Inset(0.25f * _m).Append(f.Inset(0.25f * _m)[0]).ToArray(), hand.Lime with { A = 0.8f }, Mathf.Max(0.3f * _m, 1), true);
        if (_builders == Manners.Pacific && cls is "house" or "hut" or "fisher" && StiltM * _m >= 0.9f)
        {
            foreach (var corner in f.A) DrawCircle(corner + (corner - f.Centre).Normalized() * 0.5f * _m, StiltM * _m, hand.Wood);
            var ladder = new[] { mid - along * 0.5f * _m, mid + along * 0.5f * _m, mid + along * 0.5f * _m + outward * 1.8f * _m, mid - along * 0.5f * _m + outward * 1.8f * _m };
            Plain(ladder, hand.Wood.Lightened(0.15f));
            Ringed(ladder, hand, 0.5f);
        }
        if (cls != "terrace" || PartyWallM * _m < 4) return;
        int homes = Mathf.Max(2, (int)(longM / PartyWallM));
        for (int i = 1; i < homes; i++)
        {
            Edge(f.At(i / (float)homes, 0), f.At(i / (float)homes, 1), hand, 0.5f);
            Plain(Box(f.At(i / (float)homes, 0.5f), f.Along, f.Across, 0.8f, 1.6f), hand.Brick.Darkened(0.1f));
        }
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
        if (kind is Cover.Thatch or Cover.Turf && _builds is Manners.Northern or Manners.Tribal)
        {
            Plain(Box(Ridge(f, form, 0.5f), f.Across, f.Along, 0.55f, 0.8f), hand.Ink with { A = 0.65f });
            return;
        }
        switch (_builds)
        {
            case Manners.Northern:
                Stack(hand, f, Ridge(f, form, 0.5f), 1.15f, 1.15f, 0, smoking);
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

    void Trim(BuiltHand hand, string cls, Roofed f, Form form, Cover kind, Color cover, float shortM, float longM)
    {
        var (mid, along, outward) = Front(f);
        float wide = (f.FrontB - f.FrontA).Length();
        if (form == Form.Flat && kind is Cover.Lead or Cover.Stone)
            foreach (var (at, _) in Stepped(f.A.Append(f.A[0]).ToArray(), MerlonM * _m))
                if (0.7f * _m >= 1.5f) DrawRect(new Rect2(at - Vector2.One * 0.35f * _m, Vector2.One * 0.7f * _m), hand.Ink with { A = 0.5f });
        if (form == Form.Flat && kind is Cover.Mud or Cover.Lime or Cover.Silt && cls is not ("privy" or "shed" or "coop" or "pigsty")) RoofLife(hand, f, cover);
        Manner(hand, cls, f, form, kind, cover, longM);
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
                ShopFront(hand, mid, along, outward, wide);
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
                foreach (var corner in f.A) Tub(hand, corner + (f.Centre - corner).Normalized() * 0.2f * _m, StaddleM, hand.Stone, Surface.Hewn);
                break;
            case "barn" when PorchM * _m >= 8 && Chance(91.7f) < 0.45f && !Manners.Old(_builds):
                var porch = Box(mid + outward * 0.9f * _m, outward, along, 2.6f, PorchM);
                Slopes(hand, Frame(porch), Form.Gable, cover);
                Ringed(porch, hand, 0.8f);
                break;
            case "public" when TurretM * _m >= 5 && Crested(hand, f.Part(0, 1, 0, 1)):
                break;
            case "public" when !Manners.Old(_adorns) && TurretM * _m >= 5:
                var turret = Box(f.Centre, f.Along, f.Across, TurretM, TurretM);
                Slopes(hand, Frame(turret), Form.Pyramid, hand.Slate);
                Ringed(turret, hand, 0.8f);
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
