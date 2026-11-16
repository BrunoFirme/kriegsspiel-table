using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    const float TerraceWallM = 0.6f, TerraceTickApartM = 2.4f, TerraceTickM = 1.4f, TerraceTickSignPx = 3, TerraceTickLeastPx = 1.1f, TerraceFootM = 1.6f, TerraceStepLeastPx = 1.4f;
    const float TerraceShadeM = 1.2f, TerraceStoneM = 0.42f, TerraceStoneFromPx = 2.2f, TerraceBankM = 1.8f, TerraceHachureApartM = 0.9f, TerraceTreadWash = 0.22f;
    const float PaddyBundM = 0.5f, PaddyWater = 0.72f, PaddyTuftApartM = 0.9f, PaddyTuftM = 0.12f, PaddyTuftFromPx = 1.6f, PoolRimM = 0.6f;
    const float RillLeatM = 1.8f, RillChannelM = 0.9f, RillDotApartPx = 5, WeirM = 1.4f, WeirFoamM = 0.9f, ArcadeWideM = 3, ArcadeArchM = 4.2f, ArcadePierShare = 0.3f, ArcadeFromPx = 2.2f;
    const float VineSignApartPx = 7, VineSignHighPx = 2.6f, VineSignDotPx = 0.8f, VineSignInk = 0.75f, GalleryDashPx = 3, GalleryInk = 0.4f;
    const int MostTufts = 900, MostStones = 600, MostVineSigns = 400;

    static string StyleOf(int style) => MapStyles.ShaderOrder[Mathf.Clamp(style, 0, MapStyles.ShaderOrder.Count - 1)];

    Terracing Fields => Slopes.Of(_culture).Fields;

    protected bool Terraced(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, int style)
    {
        switch (s.Class)
        {
            case Terraces.Terrace:
                Tread(hand, px, StyleOf(style));
                return true;
            case Terraces.Paddy:
                Paddy(hand, px);
                return true;
            case Terraces.Pool:
                Filled(px, hand.Stone);
                var inner = px.Select(p => Mid(px) + (p - Mid(px)) * Mathf.Max(0.5f, 1 - PoolRimM * _m / Mathf.Max(px.Max(q => (q - Mid(px)).Length()), 0.001f))).ToArray();
                if (PoolRimM * _m >= BuiltHand.LeastPx) Surfaced(inner, hand.Pool, Surface.Water, false);
                else Filled(px, hand.Pool);
                DrawPolyline(px.Append(px[0]).ToArray(), hand.Ink with { A = 0.7f }, Fine, true);
                return true;
            default:
                return false;
        }
    }

    void Tread(BuiltHand hand, Vector2[] px, string style)
    {
        if (px.Length < 4) return;
        var top = px.Take(px.Length / 2).ToArray();
        if (style == MapStyles.Ancient) Filled(px, hand.Stone.Lightened(0.2f) with { A = TerraceTreadWash });
        if (style == MapStyles.Medieval) Filled(px, hand.Turf with { A = TerraceTreadWash });
        float shade = TerraceShadeM * _m;
        if (shade >= TerraceStepLeastPx && style != MapStyles.Modern && style != MapStyles.Staff) DrawPolyline(top, hand.Ink with { A = 0.12f }, shade, true);
    }

    void Paddy(BuiltHand hand, Vector2[] px)
    {
        Filled(px, hand.Pool with { A = PaddyWater });
        float bund = True(PaddyBundM, BuiltHand.LeastPx);
        DrawPolyline(px.Append(px[0]).ToArray(), (Fields == Terracing.Paddy ? hand.Earth : hand.Mud).Darkened(0.15f), bund, true);
        if (PaddyTuftM * _m < PaddyTuftFromPx / 4) return;
        var frame = Frame(px.Length == 4 ? px : [px[0], px[px.Length / 2 - 1], px[px.Length / 2], px[^1]]);
        int rows = Mathf.Clamp((int)(frame.ShortPx / (PaddyTuftApartM * _m)), 1, 60), cols = Mathf.Clamp((int)(frame.LongPx / (PaddyTuftApartM * _m)), 1, 60), laid = 0;
        for (int r = 0; r < rows && laid < MostTufts; r++)
            for (int c = 0; c < cols && laid < MostTufts; c++, laid++)
            {
                var at = frame.At((c + 0.5f) / cols, (r + 0.5f) / rows);
                if (Geometry2D.IsPointInPolygon(at, px)) DrawCircle(at, Mathf.Max(PaddyTuftM * _m * 2, 0.6f), hand.Tree.Lightened(0.15f));
            }
    }

    protected bool Watered(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased, int style)
    {
        switch (s.Class)
        {
            case Terraces.Wall:
                if (cased) Riser(hand, px, StyleOf(style));
                return true;
            case Watercourses.Leat or Watercourses.Channel:
                if (cased) Rill(hand, px, s.Class == Watercourses.Leat ? RillLeatM : RillChannelM, StyleOf(style));
                return true;
            case Watercourses.Weir:
                if (cased) Weir(hand, px, StyleOf(style));
                return true;
            case Watercourses.Arcade:
                if (cased) Arcade(hand, px);
                return true;
            case Hallmarks.Qanat:
                for (int i = 0; cased && i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], hand.Ink with { A = GalleryInk }, BuiltHand.LeastPx, GalleryDashPx, true, true);
                return true;
            default:
                return false;
        }
    }

    void Riser(BuiltHand hand, Vector2[] px, string style)
    {
        bool earth = Fields is Terracing.Lynchet or Terracing.EarthBank or Terracing.Paddy;
        if (TerraceStoneM * _m >= TerraceStoneFromPx)
        {
            if (earth) Bank(hand, px);
            else Coursed(hand, px);
            return;
        }
        float tick = Mathf.Max(TerraceTickM * _m, TerraceTickLeastPx), apart = Mathf.Max(TerraceTickApartM * _m, TerraceTickSignPx);
        switch (style)
        {
            case MapStyles.Modern:
                DrawPolyline(px, hand.Ink with { A = 0.9f }, Fine, true);
                return;
            case MapStyles.Staff:
                DrawPolyline(px, hand.Ink, Fine, true);
                DrawPolyline(Offset(px, Mathf.Max(TerraceFootM * _m, TerraceStepLeastPx)), hand.Ink with { A = 0.45f }, BuiltHand.LeastPx, true);
                return;
            case MapStyles.Ancient:
                DrawPolyline(px, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
                DrawPolyline(Offset(px, Mathf.Max(TerraceWallM * _m, BuiltHand.LeastPx * 2)), hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
                return;
            case MapStyles.Medieval:
                DrawPolyline(Offset(px, tick * 0.5f), hand.Ink with { A = 0.25f }, tick, true);
                DrawPolyline(px, earth ? hand.Turf.Darkened(0.2f) : hand.Stone, Mathf.Max(TerraceWallM * _m, Medium), true);
                return;
        }
        DrawPolyline(px, hand.Ink with { A = earth ? 0.55f : 0.85f }, Fine, true);
        foreach (var (at, along) in Stepped(px, apart)) DrawLine(at, at + along.Orthogonal() * tick, hand.Ink with { A = earth ? 0.3f : 0.5f }, BuiltHand.LeastPx, true);
    }

    static Vector2[] Offset(Vector2[] px, float by)
    {
        var moved = new Vector2[px.Length];
        for (int i = 0; i < px.Length; i++) moved[i] = px[i] + (px[Mathf.Min(px.Length - 1, i + 1)] - px[Mathf.Max(0, i - 1)]).Normalized().Orthogonal() * by;
        return moved;
    }

    void Coursed(BuiltHand hand, Vector2[] px)
    {
        float stone = TerraceStoneM * _m, wall = TerraceWallM * _m;
        DrawPolyline(Offset(px, wall + TerraceShadeM * _m / 2), hand.Ink with { A = 0.18f }, TerraceShadeM * _m, true);
        DrawPolyline(Offset(px, wall / 2), hand.Ink, wall + Fine, true);
        int laid = 0;
        foreach (var (at, along) in Stepped(px, stone))
        {
            if (++laid > MostStones) break;
            var across = along.Orthogonal();
            var middle = at + across * (wall / 2) + along * ((Chance(laid * 1.3f) - 0.5f) * stone * 0.3f);
            var tone = hand.Stone.Lerp(hand.Lime, Chance(laid * 2.7f) * 0.35f).Darkened(Chance(laid * 4.1f) * 0.2f);
            Filled(Quad(middle, along * (stone * 0.46f), across * (wall * 0.45f)), tone);
        }
    }

    void Bank(BuiltHand hand, Vector2[] px)
    {
        float bank = TerraceBankM * _m;
        DrawPolyline(Offset(px, bank / 2), hand.Turf.Darkened(0.12f), bank, true);
        foreach (var (at, along) in Stepped(px, TerraceHachureApartM * _m)) DrawLine(at, at + along.Orthogonal() * bank, hand.Ink with { A = 0.28f }, BuiltHand.LeastPx, true);
        DrawPolyline(px, hand.Turf.Lightened(0.1f), Fine, true);
    }

    void Rill(BuiltHand hand, Vector2[] px, float metres, string style)
    {
        float wide = metres * _m;
        if (wide < BuiltHand.LeastPx * 2)
        {
            if (style == MapStyles.Modern || style == MapStyles.Staff) DrawPolyline(px, hand.Wet, BuiltHand.LeastPx, true);
            else for (int i = 0; i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], hand.Wet, BuiltHand.LeastPx, RillDotApartPx, true, true);
            return;
        }
        bool lined = _culture is Manners.Persian or Manners.Saracen or Manners.Roman;
        DrawPolyline(px, lined ? hand.Stone : hand.Mud.Darkened(0.1f), wide + 2 * Mathf.Max(0.25f * _m, BuiltHand.LeastPx), true);
        DrawPolyline(px, hand.Pool, wide, true);
        DrawPolyline(px, hand.Wet with { A = 0.5f }, Mathf.Max(BuiltHand.LeastPx, wide * 0.2f), true);
    }

    void Weir(BuiltHand hand, Vector2[] px, string style)
    {
        float bar = style == MapStyles.Modern ? True(WeirM, BuiltHand.LeastPx * 2) : style == MapStyles.Staff ? Mathf.Max(True(WeirM), Medium * 2) : Mathf.Max(True(WeirM), Medium);
        DrawPolyline(px, hand.Ink, bar + 2 * BuiltHand.LeastPx, true);
        DrawPolyline(px, hand.Stone.Lightened(0.1f), bar, true);
        if (WeirFoamM * _m < BuiltHand.LeastPx * 2) return;
        foreach (int side in new[] { 1, -1 }) DrawPolyline(Offset(px, side * (bar / 2 + WeirFoamM * _m / 2)), Colors.White with { A = 0.35f }, WeirFoamM * _m, true);
    }

    void Arcade(BuiltHand hand, Vector2[] px)
    {
        float wide = ArcadeWideM * _m;
        if (wide < ArcadeFromPx)
        {
            DrawPolyline(px, hand.Ink, Mathf.Max(Medium, BuiltHand.LeastPx * 2), true);
            return;
        }
        if (hand.Shaded) DrawPolyline(px.Select(p => p + Vector2.One * wide * 1.6f).ToArray(), hand.Ink with { A = 0.3f }, wide, true);
        DrawPolyline(px, hand.Ink, wide + 2 * BuiltHand.LeastPx, true);
        DrawPolyline(px, hand.Stone, wide, true);
        foreach (var (at, along) in Stepped(px, ArcadeArchM * _m))
        {
            var across = along.Orthogonal() * (wide / 2);
            DrawLine(at - across, at + across, hand.Ink with { A = 0.6f }, Mathf.Max(BuiltHand.LeastPx, ArcadeArchM * ArcadePierShare * _m * 0.3f), true);
        }
        DrawPolyline(px, hand.Wet with { A = 0.7f }, Mathf.Max(BuiltHand.LeastPx, wide * 0.3f), true);
    }

    protected void VineSigns(BuiltHand hand, Vector2[] px, int style)
    {
        string named = StyleOf(style);
        if (named is not (MapStyles.Survey or MapStyles.Modern or MapStyles.Staff) || VineSignHighPx > VineSignApartPx) return;
        float x0 = px.Min(p => p.X), x1 = px.Max(p => p.X), y0 = px.Min(p => p.Y), y1 = px.Max(p => p.Y);
        int laid = 0;
        for (float y = y0 + VineSignApartPx / 2; y < y1 && laid < MostVineSigns; y += VineSignApartPx)
            for (float x = x0 + VineSignApartPx / 2 + (Mathf.RoundToInt((y - y0) / VineSignApartPx) % 2) * VineSignApartPx / 2; x < x1 && laid < MostVineSigns; x += VineSignApartPx)
            {
                var at = new Vector2(x, y);
                if (!Geometry2D.IsPointInPolygon(at, px)) continue;
                laid++;
                DrawLine(at - Vector2.Down * VineSignHighPx / 2, at + Vector2.Down * VineSignHighPx / 2, hand.Ink with { A = VineSignInk }, BuiltHand.LeastPx, true);
                if (named == MapStyles.Survey) DrawCircle(at - Vector2.Down * VineSignHighPx / 2, VineSignDotPx, hand.Ink with { A = VineSignInk });
            }
    }
}
