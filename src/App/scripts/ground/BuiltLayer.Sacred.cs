using System.Linq;
using Godot;
using KTable.Core.Model;

using static KTable.App.BuiltShapes;

namespace KTable.App;

sealed partial class BuiltLayer
{
    const float ApseAt = 0.86f, AisleFromM = 13, TranseptFromM = 26, CrossingAt = 0.6f, StandingStones = 9, FrondM = 2.6f;
    const int ApseSides = 7, Fronds = 9, Spikes = 9;

    void Sacred(BuiltHand hand, Roofed f, float shortM, float longM)
    {
        if (_manner == Manners.Roman) Temple(hand, f);
        else if (_manner == Manners.Egyptian) Pylons(hand, f);
        else Church(hand, f, shortM, longM);
    }

    void Apse(BuiltHand hand, Roofed f, Color cover)
    {
        Vector2 centre = f.At(ApseAt, 0.5f);
        float r = f.ShortPx * 0.4f, bulge = Mathf.Min(r, (1 - ApseAt) * f.LongPx);
        var rim = Enumerable.Range(0, ApseSides + 1).Select(i => centre + f.Across * (Mathf.Cos(i * Mathf.Pi / ApseSides) * r) + f.Along * (Mathf.Sin(i * Mathf.Pi / ApseSides) * bulge)).ToArray();
        for (int i = 0; i < ApseSides; i++)
        {
            Facet(cover, centre, false, centre, rim[i], rim[i + 1]);
            Edge(rim[i], rim[i + 1], hand, 0.9f);
        }
    }

    void Church(BuiltHand hand, Roofed f, float shortM, float longM)
    {
        float pick = Chance(13.9f);
        var kind = _manner switch { Manners.English => Cover.Lead, Manners.Northern => Cover.Shingle, Manners.Southern => Cover.Tile, _ => pick < 0.6f ? Cover.Tile : Cover.Slate };
        var cover = Covering(hand, kind);
        bool aisled = shortM >= AisleFromM, crossed = longM >= TranseptFromM || Chance(17.3f) < 0.3f && shortM >= 9;
        var nave = Frame(aisled ? f.Part(0, ApseAt, 0.2f, 0.8f) : f.Part(0, ApseAt, 0, 1));
        if (aisled)
            foreach (var (from, to) in new[] { (0f, 0.2f), (0.8f, 1f) })
            {
                Plain(f.Part(0, ApseAt, from, to), cover.Darkened(from < 0.5f ? 0.02f : 0.2f));
                Ringed(f.Part(0, ApseAt, from, to), hand, 0.8f);
            }
        _roof = SurfaceBook.Roofing(kind, _manner);
        Slopes(hand, nave, _manner == Manners.Northern ? Form.Hip : Form.Gable, cover);
        Ringed(nave.A, hand, 0.9f);
        Apse(hand, f, cover);
        if (crossed)
        {
            var arms = Frame(f.Part(CrossingAt - 0.08f, CrossingAt + 0.08f, -0.14f, 1.14f));
            if (hand.Shaded) Plain(arms.A.Select(p => p + Vector2.One * Mathf.Min(ShadowM * _m, ShadowMostPx)).ToArray(), hand.Ink with { A = 0.5f });
            Slopes(hand, arms, Form.Gable, cover);
            Ringed(arms.A, hand, 0.9f);
        }
        _roof = Surface.None;
        if (ButtressM * _m >= 2 && !crossed)
            foreach (float side in new[] { 0f, 1f })
                foreach (var (at, _) in Stepped([f.At(0.06f, side), f.At(ApseAt - 0.04f, side)], ButtressApartM * _m))
                    DrawRect(new Rect2(at + f.Across * ((side - 0.5f) * ButtressM * _m) - Vector2.One * ButtressM * _m * 0.35f, Vector2.One * ButtressM * _m * 0.7f), hand.Stone);
        if (_manner == Manners.Northern)
        {
            Dome(hand, f.At(0.5f, 0.5f), f.ShortPx * 0.3f, hand.Copper);
            foreach (var (t, s) in new[] { (0.3f, 0.22f), (0.3f, 0.78f), (0.7f, 0.22f), (0.7f, 0.78f) }) Dome(hand, f.At(t, s), f.ShortPx * 0.13f, hand.Copper);
        }
        else if (_manner == Manners.Southern && crossed || _manner == Manners.Continental && crossed
            && pick > 0.7f) Dome(hand, f.At(CrossingAt, 0.5f), f.ShortPx * 0.3f, _manner == Manners.Southern ? hand.Tile : hand.Copper);
        Threshold(hand, f.At(0.16f, 1), f.Along, f.Across, 1.5f);
    }

    void Threshold(BuiltHand hand, Vector2 at, Vector2 along, Vector2 outward, float wideM)
    {
        float wide = wideM * DoorM * _m, step = StepM * _m;
        if (wide < DoorFromPx) return;
        Plain([at - along * wide / 2, at + along * wide / 2, at + along * wide / 2 + outward * step, at - along * wide / 2 + outward * step], hand.Stone);
        DrawLine(at - along * wide / 2, at + along * wide / 2, hand.Ink, Mathf.Max(0.22f * _m, 1.2f), true);
    }

    void Temple(BuiltHand hand, Roofed f)
    {
        Plain(f.A, hand.Stone.Lightened(0.12f));
        Ringed(f.A, hand, 0.9f);
        for (int i = 1; i <= 3; i++) Edge(f.At(1 - 0.03f * i, 0.08f), f.At(1 - 0.03f * i, 0.92f), hand, 0.45f);
        var cella = Frame(f.Part(0.07f, 0.72f, 0.2f, 0.8f));
        if (hand.Shaded) Plain(cella.A.Select(p => p + Vector2.One * Mathf.Min(ShadowM * _m, ShadowMostPx)).ToArray(), hand.Ink with { A = 0.45f });
        var cover = Covering(hand, Cover.Tile);
        _roof = SurfaceBook.Roofing(Cover.Tile, _manner);
        Slopes(hand, cella, Form.Gable, cover);
        _roof = Surface.None;
        Ringed(cella.A, hand, 0.9f);
        Columns(hand, f.Part(0.02f, 0.9f, 0, 1), ColumnM * _m * 1.1f);
        Threshold(hand, f.At(0.72f, 0.5f), f.Across, f.Along, 1.6f);
    }

    void Pylons(BuiltHand hand, Roofed f)
    {
        var floor = hand.Area("yard").Fill with { A = 1 };
        Plain(f.A, floor);
        var hall = f.Part(0.24f, 0.56f, 0.04f, 0.96f);
        Plain(hall, hand.Lime.Darkened(0.06f));
        Plain(f.Part(0.24f, 0.56f, 0.38f, 0.62f), hand.Lime.Lightened(0.06f));
        Ringed(hall, hand, 0.9f);
        Ringed(f.Part(0.24f, 0.56f, 0.38f, 0.62f), hand, 0.4f);
        var shrine = f.Part(0.02f, 0.24f, 0.16f, 0.84f);
        Plain(shrine, hand.Lime.Darkened(0.14f));
        Ringed(shrine, hand, 0.9f);
        Ringed(f.Part(0.06f, 0.18f, 0.38f, 0.62f), hand, 0.5f);
        Columns(hand, f.Part(0.58f, 0.88f, 0.04f, 0.96f), ColumnM * _m * 1.6f);
        foreach (var (from, to) in new[] { (0f, 0.43f), (0.57f, 1f) })
        {
            var tower = f.Part(0.9f, 1, from, to);
            if (hand.Shaded) Plain(tower.Select(p => p + Vector2.One * Mathf.Min(ShadowM * 1.5f * _m, ShadowMostPx * 1.5f)).ToArray(), hand.Ink with { A = 0.6f });
            Plain(tower, hand.Lime.Darkened(0.1f));
            Plain(tower.Select(p => Mid(tower) + (p - Mid(tower)) * 0.72f).ToArray(), hand.Lime.Lightened(0.05f));
            Ringed(tower, hand, 0.9f);
            foreach (float mast in new[] { 0.3f, 0.7f }) Edge(tower[1].Lerp(tower[2], mast), tower[1].Lerp(tower[2], mast) + f.Along * 2.2f * _m, hand, 0.9f);
        }
        Ringed(f.A, hand, 0.9f);
    }

    void Grove(BuiltHand hand, Roofed f)
    {
        float r = Mathf.Min(f.ShortPx, f.LongPx) * 0.42f;
        for (int i = 0; i < StandingStones; i++)
        {
            var at = f.Centre + Vector2.Right.Rotated(i * Mathf.Tau / StandingStones + Chance(i) * 0.3f) * r;
            Tub(hand, at, 0.5f + 0.4f * Chance(i * 2.3f), hand.Stone, Surface.Hewn);
        }
        Tub(hand, f.Centre, 1.1f, hand.Stone.Darkened(0.1f), Surface.Hewn);
    }

    void Palm(BuiltHand hand, Vector2 at, float r)
    {
        if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.3f, r * 0.75f, hand.Ink with { A = 0.22f });
        float turn = Chance(0.7f) * Mathf.Tau, pen = Mathf.Max(r * 0.2f, 1);
        for (int i = 0; i < Fronds; i++)
        {
            var way = Vector2.Right.Rotated(turn + i * Mathf.Tau / Fronds);
            DrawLine(at, at + way * r, i % 2 == 0 ? hand.Tree : hand.Tree.Lightened(0.14f), pen, true);
        }
        DrawCircle(at, Mathf.Max(r * 0.16f, 1), hand.Wood);
    }

    void Conifer(BuiltHand hand, Vector2 at, float r)
    {
        if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.3f, r * 0.8f, hand.Ink with { A = 0.28f });
        float turn = Chance(0.7f) * Mathf.Tau;
        var star = Enumerable.Range(0, Spikes * 2).Select(i => at + Vector2.Right.Rotated(turn + i * Mathf.Pi / Spikes) * (i % 2 == 0 ? r : r * 0.55f)).ToArray();
        Plain(star, hand.Tree.Darkened(0.22f));
        DrawCircle(at, r * 0.3f, hand.Tree.Darkened(0.05f));
    }

    void Tree(BuiltHand hand, Vector2 at, float r)
    {
        float pick = Chance(15.5f);
        if (_manner == Manners.Egyptian && pick < 0.8f) Palm(hand, at, Mathf.Max(r, FrondM * _m * 0.8f));
        else if (_manner == Manners.Northern && pick < 0.5f) Conifer(hand, at, r);
        else if (_manner is Manners.Roman or Manners.Southern && pick < 0.35f)
        {
            if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.5f, r * 0.5f, hand.Ink with { A = 0.3f });
            DrawCircle(at, r * 0.5f, hand.Tree.Darkened(0.3f));
            DrawCircle(at - Vector2.One * r * 0.12f, r * 0.22f, hand.Tree.Darkened(0.12f));
        }
        else Crown(hand, at, r);
    }
}
