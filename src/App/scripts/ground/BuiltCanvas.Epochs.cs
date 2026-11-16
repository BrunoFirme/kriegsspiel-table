using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    const float GaugeM = 1.435f, BallastM = 4.6f, SleeperM = 2.5f, SleepersApartM = 0.7f, SleeperFromPx = 2.4f, TieSignPx = 9, TieSignHalfPx = 2.6f, RailFromPx = 4;
    const float LaneDashM = 6, LaneGapM = 9, VergeM = 0.5f, PoleApartM = 48, PoleOffM = 1.6f, PoleFromPx = 0.9f, PoleArmM = 1.1f;
    const float BankM = 1.4f, TrenchFromPx = 3.2f, BarbApartM = 4.5f, BarbM = 0.9f, BarbSignPx = 7, BarbFromPx = 2;
    const float TickApartM = 1.8f, TickM = 1.1f, TickSignPx = 3.5f, TickLeastPx = 1.4f;
    const float HachureApartM = 3.2f, HachureFromPx = 3, HachureInk = 0.22f, GardenWallM = 0.5f, GutterM = 0.5f, GutterFromPx = 1.4f;
    const float PlotM = 9, MineApartM = 7, MineM = 0.7f, MineFromPx = 1.4f, TankM = 3.6f, PitM = 4.2f, TeethM = 1.1f, NearPx = 9, CrestLine = 0.35f, CastM = 1.3f, CastMostPx = 5, EmblemLeastPx = 1.6f;
    const int MostPlots = 40, MostMines = 160;

    protected int _manner, _builds, _adorns, _culture, _builders, _adorners;
    protected float _perUnit;

    protected float True(float metres, float leastPx = 0) => Mathf.Max(metres * _m, leastPx);

    float Pen(float weight, float mostPx) => Mathf.Clamp(weight * _perUnit, BuiltHand.LeastPx, mostPx);

    protected float Fine => Pen(BuiltHand.Fine, BuiltHand.FineMostPx);

    protected float Medium => Pen(BuiltHand.Medium, BuiltHand.MediumMostPx);

    protected void Sides(Vector2[] px, float off, Color ink, float width)
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

    protected bool Modern => Manners.Named(_culture).Epoch == Manners.Modern;

    protected bool Metalled => Modern && _culture is Manners.Western or Manners.Nippon or Manners.NorthAfrican;

    protected bool Tarred(string cls) => cls == "highway" || Modern && cls == "boulevard" || Metalled && (cls is "road" or "chaussee" || cls == "street" && _culture == Manners.Western);

    protected bool Ground(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (s.Class is not ("allotments" or "pitch" or "minefield")) return false;
        var look = hand.Area(s.Class);
        if (look.Fill.A > 0) Filled(px, look.Fill);
        if (s.Class == "allotments" && px.Length == 4) Plots(hand, px);
        else if (s.Class == "pitch" && px.Length == 4) Pitch(hand, px);
        else if (s.Class == "minefield") Mines(hand, px);
        if (look.Edge.A > 0 && s.Class != "minefield") DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Fine, true);
        return true;
    }

    void Plots(BuiltHand hand, Vector2[] px)
    {
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        float plot = Mathf.Max(PlotM * _m, 4);
        int cols = Mathf.Clamp((int)(along.Length() / plot), 1, MostPlots), rows = Mathf.Clamp((int)(across.Length() / (plot * 2)), 1, MostPlots);
        for (int i = 1; i < cols; i++) DrawLine(px[0] + along * (i / (float)cols), px[3] + along * (i / (float)cols), hand.Ink with { A = 0.3f }, BuiltHand.LeastPx, true);
        for (int j = 1; j < rows; j++) DrawLine(px[0] + across * (j / (float)rows), px[1] + across * (j / (float)rows), hand.Ink with { A = 0.45f }, BuiltHand.LeastPx, true);
        if (plot < NearPx) return;
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                var corner = px[0] + along * ((i + 0.12f) / cols) + across * ((j + 0.08f) / rows);
                if (Chance(i * 3.1f + j * 7.7f) < 0.55f) Filled(Quad(corner + (along.Normalized() + across.Normalized()) * 1.2f * _m, along.Normalized() * 1.1f * _m, across.Normalized() * 0.9f * _m),
                    hand.Wood);
                for (int bed = 1; bed < 4; bed++)
                    DrawLine(px[0] + along * ((i + 0.3f) / cols) + across * ((j + bed / 4f) / rows), px[0] + along * ((i + 0.94f) / cols)
                        + across * ((j + bed / 4f) / rows), hand.Tree with { A = 0.5f },
                        Mathf.Max(0.5f * _m, 1), true);
            }
    }

    void Pitch(BuiltHand hand, Vector2[] px)
    {
        var paint = hand.Paint with { A = 0.85f };
        var line = px.Select(p => Mid(px) + (p - Mid(px)) * 0.88f).ToArray();
        float pen = Mathf.Max(0.25f * _m, BuiltHand.LeastPx);
        DrawPolyline(line.Append(line[0]).ToArray(), paint, pen, true);
        bool longFirst = (line[1] - line[0]).Length() >= (line[2] - line[1]).Length();
        Vector2 a = longFirst ? (line[0] + line[1]) / 2 : (line[1] + line[2]) / 2, b = longFirst ? (line[3] + line[2]) / 2 : (line[0] + line[3]) / 2;
        DrawLine(a, b, paint, pen, true);
        DrawArc(Mid(px), (a - b).Length() * 0.14f, 0, Mathf.Tau, 20, paint, pen, true);
    }

    void Mines(BuiltHand hand, Vector2[] px)
    {
        var ring = px.Append(px[0]).ToArray();
        for (int i = 0; i + 1 < ring.Length; i++) DrawDashedLine(ring[i], ring[i + 1], hand.Signal, Fine, Mathf.Max(Fine * 4, 2 * _m), true, true);
        if (px.Length != 4 || MineM * _m < MineFromPx) return;
        Vector2 along = px[1] - px[0], across = px[3] - px[0];
        int cols = Mathf.Max(1, (int)(along.Length() / (MineApartM * _m))), rows = Mathf.Max(1, (int)(across.Length() / (MineApartM * _m)));
        for (int i = 0, laid = 0; i < cols * rows && laid < MostMines; i++)
        {
            if (Chance(i * 2.3f) < 0.3f) continue;
            var at = px[0] + along * ((i % cols + 0.3f + 0.4f * Chance(i * 5.1f)) / cols) + across * ((i / cols + 0.3f + 0.4f * Chance(i * 9.4f)) / rows);
            DrawCircle(at, MineM * _m / 2, hand.Ink with { A = 0.75f });
            laid++;
        }
    }

    protected bool Way(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased)
    {
        switch (s.Class)
        {
            case "railway":
                if (cased) Railway(hand, px);
                return true;
            case "trench" when BankM * _m >= TrenchFromPx:
                if (cased) Trench(hand, px);
                return true;
            case "wire":
                if (cased) Wire(hand, px);
                return true;
            case Streetlife.Gutter:
                if (cased && GutterM * _m >= GutterFromPx)
                {
                    DrawPolyline(px, hand.Wet.Lerp(hand.Mud, 0.45f) with { A = 0.85f }, GutterM * _m, true);
                    DrawPolyline(px, hand.Wet.Lightened(0.25f) with { A = 0.35f }, GutterM * 0.3f * _m, true);
                }
                return true;
            case "fence" when cased && Railed(hand, px):
                return true;
            case "fence" when cased && Ways.Of(_culture).Walled && GardenWallM * _m >= BuiltHand.LeastPx:
                DrawPolyline(px, hand.Ink with { A = 0.85f }, GardenWallM * _m + Fine, true);
                DrawPolyline(px, _culture is Manners.NorthAfrican ? hand.Lime : hand.Mud, GardenWallM * _m, true);
                return true;
            default:
                return false;
        }
    }

    void Railway(BuiltHand hand, Vector2[] px)
    {
        float gauge = GaugeM * _m;
        if (gauge < RailFromPx)
        {
            DrawPolyline(px, hand.Ink, Mathf.Max(Medium, 1.5f), true);
            foreach (var (at, along) in Stepped(px, TieSignPx)) DrawLine(at - along.Orthogonal() * TieSignHalfPx, at + along.Orthogonal() * TieSignHalfPx, hand.Ink, Mathf.Max(Fine, 1), true);
            return;
        }
        DrawPolyline(px, hand.Stone.Darkened(0.12f), BallastM * _m, true);
        if (SleepersApartM * _m >= SleeperFromPx)
            foreach (var (at, along) in Stepped(px, SleepersApartM * _m))
                DrawLine(at - along.Orthogonal() * (SleeperM / 2 * _m), at + along.Orthogonal() * (SleeperM / 2 * _m), hand.Wood, Mathf.Max(0.24f * _m, 1), true);
        Sides(px, gauge / 2, hand.Rails, Mathf.Max(0.12f * _m, 1));
        Poles(hand, px, BallastM / 2 * _m);
    }

    protected void Poles(BuiltHand hand, Vector2[] px, float half)
    {
        if (0.3f * _m < PoleFromPx) return;
        foreach (var (at, along) in Stepped(px, PoleApartM * _m))
        {
            var foot = at + along.Orthogonal() * (half + PoleOffM * _m);
            if (hand.Shaded) DrawLine(foot, foot + Vector2.One * 2.2f * _m, hand.Ink with { A = 0.25f }, Mathf.Max(0.2f * _m, 1), true);
            DrawLine(foot - along * (PoleArmM * _m), foot + along * (PoleArmM * _m), hand.Ink with { A = 0.8f }, Mathf.Max(0.14f * _m, 1), true);
            DrawCircle(foot, Mathf.Max(0.16f * _m, 1), hand.Wood);
        }
    }

    protected void Marked(BuiltHand hand, Vector2[] px, float wide)
    {
        if (LaneDashM * _m < HachureFromPx * 2) return;
        Sides(px, wide / 2 - VergeM * _m, hand.Paint with { A = 0.7f }, Mathf.Max(0.14f * _m, 1));
        bool dash = true;
        foreach (var (at, along) in Stepped(px, (LaneDashM + LaneGapM) / 2 * _m))
        {
            if (dash) DrawLine(at - along * (LaneDashM / 2 * _m), at + along * (LaneDashM / 2 * _m), hand.Paint with { A = 0.85f }, Mathf.Max(0.16f * _m, 1), true);
            dash = !dash;
        }
    }

    void Trench(BuiltHand hand, Vector2[] px)
    {
        var look = hand.Line("trench");
        float wide = look.Metres * _m;
        DrawPolyline(px, hand.Earth.Lerp(hand.Mud, 0.6f), wide + 2 * BankM * _m, true);
        DrawPolyline(px, look.Ink, wide + 2 * Fine, true);
        DrawPolyline(px, look.Close, wide, true);
    }

    void Wire(BuiltHand hand, Vector2[] px)
    {
        var look = hand.Line("wire");
        bool near = BarbM * _m >= BarbFromPx;
        DrawPolyline(px, look.Ink, Fine, true);
        float barb = near ? BarbM * _m : BarbFromPx;
        foreach (var (at, along) in Stepped(px, near ? BarbApartM * _m : BarbSignPx))
        {
            var slant = (along + along.Orthogonal()).Normalized() * barb;
            DrawLine(at - slant, at + slant, look.Ink, BuiltHand.LeastPx, true);
            DrawLine(at - slant.Orthogonal(), at + slant.Orthogonal(), look.Ink, BuiltHand.LeastPx, true);
        }
    }

    protected void Revetted(BuiltHand hand, Vector2[] px)
    {
        float apart = Mathf.Max(TickApartM * _m, TickSignPx), tick = Mathf.Max(TickM * _m, TickLeastPx);
        foreach (var (at, along) in Stepped(px, apart)) DrawLine(at, at + along.Orthogonal() * tick, hand.Ink with { A = HachureInk * 2 }, BuiltHand.LeastPx, true);
    }

    protected void Hachured(BuiltHand hand, Vector2[] px, float wide)
    {
        if (HachureApartM * _m < HachureFromPx) return;
        foreach (var (at, along) in Stepped(px, HachureApartM * _m))
            DrawLine(at - along.Orthogonal() * (wide * 0.46f), at - along.Orthogonal() * (wide * 0.12f), hand.Ink with { A = HachureInk }, BuiltHand.LeastPx, true);
        Sides(px, wide * 0.1f, hand.Ink with { A = CrestLine }, Fine);
    }

    protected bool Military(BuiltHand hand, string cls, Vector2[] q)
    {
        if (cls is not ("bunker" or "elevator")) return false;
        var f = Frame(q);
        float shade = Mathf.Clamp(CastM * _m, BuiltHand.LeastPx, CastMostPx);
        if (hand.Shaded) Filled(q.Select(p => p + new Vector2(shade, shade)).ToArray(), hand.Ink with { A = 0.7f });
        if (cls == "elevator")
        {
            Filled(f.Part(0, 0.3f, 0, 1), hand.Concrete.Darkened(0.12f));
            Lined(f.Part(0, 0.3f, 0, 1), hand, 0.9f);
            int bins = Mathf.Max(2, Mathf.RoundToInt(f.LongPx * 0.7f / Mathf.Max(f.ShortPx, 1) * 2));
            for (int i = 0; i < bins; i++)
                foreach (float row in new[] { 0.27f, 0.73f }) Bulb(hand, f.At(0.3f + 0.7f * (i + 0.5f) / bins, row), f.ShortPx * 0.24f, hand.Concrete);
            return true;
        }
        var slab = Enumerable.Range(0, 6).Select(i => f.Centre + f.Along * (Mathf.Cos(i * Mathf.Tau / 6) * f.LongPx * 0.56f) + f.Across * (Mathf.Sin(i * Mathf.Tau / 6) * f.ShortPx * 0.56f)).ToArray();
        bool logs = _culture is Manners.Pacific or Manners.Nippon;
        Filled(slab, logs ? hand.Wood.Lerp(hand.Palm, 0.4f) : hand.Concrete.Darkened(0.08f));
        Filled(slab.Select(p => f.Centre + (p - f.Centre) * 0.7f).ToArray(), logs ? hand.Palm.Darkened(0.1f) : hand.Concrete.Lightened(0.08f));
        DrawPolyline(slab.Append(slab[0]).ToArray(), hand.Ink, Fine, true);
        DrawLine(f.Centre + f.Across * (f.ShortPx * 0.3f), f.Centre + f.Across * (f.ShortPx * 0.56f), hand.Ink, Mathf.Max(0.5f * _m, 1), true);
        return true;
    }

    protected bool Emblem(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        switch (cls)
        {
            case "watertower":
                float tank = True(TankM, EmblemLeastPx);
                if (hand.Shaded) DrawCircle(at + Vector2.One * tank * 0.6f, tank, hand.Ink with { A = 0.35f });
                Bulb(hand, at, tank, hand.Tin.Lerp(hand.Rust, 0.25f));
                return true;
            case "gunpit":
                float pit = True(PitM, EmblemLeastPx * 1.4f);
                DrawArc(at, pit, across.Angle() - 2.3f, across.Angle() + 2.3f, 18, hand.Earth.Lerp(hand.Mud, 0.6f), Mathf.Max(pit * 0.42f, 1.4f), true);
                DrawArc(at, pit * 1.2f, across.Angle() - 2.3f, across.Angle() + 2.3f, 18, hand.Ink with { A = 0.8f }, BuiltHand.LeastPx, true);
                DrawLine(at - across * pit * 0.3f, at + across * pit * 0.9f, hand.Ink, Mathf.Max(0.3f * _m, 1.2f), true);
                DrawCircle(at - across * pit * 0.2f, Mathf.Max(0.5f * _m, 1.2f), hand.Ink);
                return true;
            case "roadblock":
                float tooth = True(TeethM, 1.4f);
                for (int i = -2; i <= 2; i++)
                    foreach (int row in new[] { 0, 1 })
                        DrawRect(new Rect2(at + across * (i * tooth * 1.9f + row * tooth * 0.9f) + along * (row * tooth * 1.8f) - Vector2.One * tooth / 2, Vector2.One * tooth), hand.Concrete);
                return true;
            default:
                return Monument(hand, cls, at, across);
        }
    }
}
