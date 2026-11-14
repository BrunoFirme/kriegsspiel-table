using System.Linq;
using Godot;

using static KTable.App.BuiltShapes;

namespace KTable.App;

abstract partial class BuiltCanvas
{
    protected static readonly Vector2 Sunless = new Vector2(1, 1).Normalized();
    static readonly Vector2 Drift = new Vector2(0.8f, -0.6f).Normalized();

    const float GlazedShare = 0.32f, StackM = 2.3f, StackShadeM = 15, ApronShare = 0.45f, WorksFromPx = 1.6f, WorksSeenM = 8;
    const float SteamerLongM = 46, SteamerBeamM = 8.4f, JibM = 15, PortalM = 5.5f, CraneRunM = 11, CounterM = 3.2f;
    const float ThresholdM = 30, DashM = 30, DashGapM = 20, RunwayEndM = 6, MarksFromPx = 5, CentreWide = 0.03f;
    const int Sheds = 6, Ribs = 9, Guides = 8, Keys = 8, Lights = 5, Laces = 5, Puffs = 5, PanCols = 4;

    static Vector2[] Quad(Vector2 at, Vector2 run, Vector2 side) => [at - run - side, at + run - side, at + run + side, at - run + side];

    static Vector2[] LongFirst(Vector2[] q) => (q[1] - q[0]).Length() >= (q[2] - q[1]).Length() ? q : [q[1], q[2], q[3], q[0]];

    void Inked(Vector2 a, Vector2 b, BuiltHand hand, float ink) => DrawLine(a, b, hand.Ink with { A = ink }, BuiltHand.LeastPx, true);

    protected bool Industry(BuiltHand hand, string cls, Vector2[] q)
    {
        if (cls is not ("factory" or "gasholder" or "hangar" or "station" or "dockshed")) return false;
        var body = LongFirst(q);
        float deep = (body[2] - body[1]).Length();
        if (cls == "gasholder")
        {
            Gasholder(hand, Mid(q), deep * 0.5f);
            return true;
        }
        if (cls == "hangar") Filled(Band(body, 1, 1 + ApronShare, -0.12f, 1.12f), hand.Stone.Lightened(0.24f));
        if (cls == "station") Filled(Band(body, -0.06f, 1.06f, 1, 1.5f), hand.Stone.Lightened(0.22f));
        if (hand.Shaded) Filled(Cast(body, Mathf.Min(deep * 0.1f, ShadeMostPx)), hand.Ink with { A = 0.6f });
        if (cls == "factory") Factory(hand, body, deep);
        else if (cls == "hangar") Hangar(hand, body);
        else if (cls == "station") Station(hand, body);
        else Shed(hand, body);
        Lined(body, hand, 0.9f);
        return true;
    }

    void Factory(BuiltHand hand, Vector2[] body, float deep)
    {
        Color glass = hand.Wet.Lerp(hand.Lime, 0.6f), slope = hand.Slate.Lerp(hand.Brick, 0.2f);
        Filled(body, hand.Brick.Darkened(0.2f));
        for (int i = 0; i < Sheds; i++)
        {
            float from = i / (float)Sheds, to = (i + 1f) / Sheds, lit = from + (to - from) * GlazedShare;
            Filled(Band(body, from, lit, 0.05f, 0.95f), glass);
            Filled(Band(body, lit, to, 0.05f, 0.95f), slope.Lightened(0.1f));
            Filled(Band(body, lit + (to - lit) * 0.55f, to, 0.05f, 0.95f), slope.Darkened(0.14f));
            Inked(Within(body, from, 0.05f), Within(body, from, 0.95f), hand, 0.9f);
            Inked(Within(body, lit, 0.05f), Within(body, lit, 0.95f), hand, 0.35f);
        }
        var boilers = Band(body, 0.02f, 0.24f, 1, 1.28f);
        if (hand.Shaded) Filled(Cast(boilers, Mathf.Min(deep * 0.06f, ShadeMostPx)), hand.Ink with { A = 0.5f });
        Filled(boilers, hand.Brick);
        Lined(boilers, hand, 0.9f);
        Stack(hand, Within(body, 0.13f, 1.42f), Mathf.Max(StackM * _m, BuiltHand.LeastPx * 1.5f));
    }

    void Stack(BuiltHand hand, Vector2 at, float r)
    {
        if (hand.Shaded) DrawLine(at, at + Sunless * (StackShadeM * _m), hand.Ink with { A = 0.4f }, r * 1.5f, true);
        for (int i = 1; hand.Shaded && i <= Puffs; i++) DrawCircle(at + Drift * (i * 1.6f * r), r * (0.8f + 0.3f * i), hand.Stone.Lightened(0.3f) with { A = 0.07f * (Puffs + 1 - i) });
        DrawCircle(at, r, hand.Brick.Darkened(0.12f));
        DrawCircle(at, r * 0.5f, hand.Ink);
        DrawArc(at, r, 0, Mathf.Tau, 14, hand.Ink, BuiltHand.LeastPx, true);
    }

    void Gasholder(BuiltHand hand, Vector2 at, float r)
    {
        Bulb(hand, at, r * 0.84f, hand.Lead.Lerp(hand.Brick, 0.35f));
        for (int i = 0; i < Guides; i++)
        {
            var spoke = Vector2.Right.Rotated(i * Mathf.Tau / Guides + 0.2f);
            Inked(at + spoke * r * 0.2f, at + spoke * r * 0.84f, hand, 0.3f);
            DrawCircle(at + spoke * r, Mathf.Max(r * 0.07f, BuiltHand.LeastPx), hand.Ink);
        }
        DrawArc(at, r, 0, Mathf.Tau, 24, hand.Ink with { A = 0.7f }, BuiltHand.LeastPx, true);
        DrawCircle(at, r * 0.18f, hand.Lead.Lightened(0.2f));
    }

    void Hangar(BuiltHand hand, Vector2[] body)
    {
        var drab = hand.Lead.Lerp(hand.Tree, 0.45f);
        Filled(body, drab.Darkened(0.22f));
        Filled(Band(body, 0, 1, 0.14f, 0.86f), drab.Darkened(0.06f));
        Filled(Band(body, 0, 1, 0.34f, 0.66f), drab.Lightened(0.16f));
        for (int i = 1; i < Ribs; i++) Inked(Within(body, i / (float)Ribs, 0), Within(body, i / (float)Ribs, 1), hand, 0.4f);
        DrawLine(Within(body, 1, 0.1f), Within(body, 1, 0.9f), hand.Ink, BuiltHand.LeastPx * 2.5f, true);
    }

    void Station(BuiltHand hand, Vector2[] body)
    {
        Filled(Band(body, 0, 1, 0, 0.5f), hand.Slate.Lightened(0.12f));
        Filled(Band(body, 0, 1, 0.5f, 1), hand.Slate.Darkened(0.1f));
        Inked(Within(body, 0.03f, 0.5f), Within(body, 0.97f, 0.5f), hand, 0.7f);
        var canopy = Band(body, 0.05f, 0.95f, 1, 1.34f);
        Filled(canopy, hand.Lead.Lightened(0.16f));
        for (int i = 0; i <= Ribs; i++) Inked(Within(canopy, i / (float)Ribs, 0), Within(canopy, i / (float)Ribs, 1), hand, 0.35f);
        Lined(canopy, hand, 0.7f);
        var hall = Band(body, 0.38f, 0.62f, -0.22f, 1);
        Filled(Band(hall, 0, 0.5f, 0, 1), hand.Tile.Lightened(0.1f));
        Filled(Band(hall, 0.5f, 1, 0, 1), hand.Tile.Darkened(0.12f));
        Inked(Within(hall, 0.5f, 0.04f), Within(hall, 0.5f, 0.96f), hand, 0.7f);
        Lined(hall, hand, 0.9f);
    }

    void Shed(BuiltHand hand, Vector2[] body)
    {
        var rust = hand.Lead.Lerp(hand.Brick, 0.5f);
        Filled(Band(body, 0, 1, 0, 0.5f), rust.Lightened(0.1f));
        Filled(Band(body, 0, 1, 0.5f, 1), rust.Darkened(0.14f));
        for (int i = 1; i < Ribs * 2; i++) Inked(Within(body, i / (Ribs * 2f), 0), Within(body, i / (Ribs * 2f), 1), hand, 0.22f);
        for (int i = 0; i < Lights; i++) Filled(Band(body, (i + 0.3f) / Lights, (i + 0.7f) / Lights, 0.4f, 0.6f), hand.Wet.Lerp(hand.Lime, 0.6f));
        Inked(Within(body, 0, 0.5f), Within(body, 1, 0.5f), hand, 0.75f);
    }

    protected void Runway(BuiltHand hand, Vector2[] q)
    {
        var strip = LongFirst(q);
        float longPx = (strip[1] - strip[0]).Length(), widePx = (strip[2] - strip[1]).Length();
        if (widePx < MarksFromPx || longPx < 1) return;
        var paint = hand.Lime.Lightened(0.3f);
        float end = RunwayEndM * _m / longPx, key = ThresholdM * _m / longPx, dash = DashM * _m / longPx, gap = DashGapM * _m / longPx;
        foreach (float from in new[] { end, 1 - end - key })
            for (int i = 0; i < Keys; i++)
                if (i != Keys / 2 - 1 && i != Keys / 2) Filled(Band(strip, from, from + key, (i + 0.25f) / Keys, (i + 0.75f) / Keys), paint);
        for (float u = end + key + gap; u + dash <= 1 - end - key - gap; u += dash + gap) Filled(Band(strip, u, u + dash, 0.5f - CentreWide / 2, 0.5f + CentreWide / 2), paint);
        foreach (float v in new[] { 0.05f, 0.95f }) DrawLine(Within(strip, end, v), Within(strip, 1 - end, v), paint with { A = 0.8f }, BuiltHand.LeastPx, true);
    }

    protected bool Docks(BuiltHand hand, string cls, Vector2 at, Vector2 across, Vector2 along)
    {
        if (cls is not ("steamer" or "dockcrane") || WorksSeenM * _m < WorksFromPx) return false;
        if (cls == "steamer") Steamer(hand, at, across, along);
        else DockCrane(hand, at, across, along);
        return true;
    }

    void Steamer(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        Vector2 bow = across * (SteamerLongM / 2 * _m), beam = along * (SteamerBeamM / 2 * _m);
        var hull = Hull(at, bow, beam);
        if (hand.Shaded) Filled(Cast(hull, Mathf.Min(0.9f * _m, 4)), hand.Ink with { A = 0.3f });
        Filled(hull, hand.Ink.Lerp(hand.Lead, 0.3f));
        Filled(Hull(at, bow * 0.93f, beam * 0.78f), hand.Wood.Lerp(hand.Lime, 0.45f));
        foreach (float hold in new[] { 0.56f, 0.3f, -0.42f }) Filled(Quad(at + bow * hold, bow * 0.085f, beam * 0.42f), hand.Ink with { A = 0.7f });
        var house = Quad(at - bow * 0.07f, bow * 0.15f, beam * 0.7f);
        Filled(house, hand.Lime);
        Lined(house, hand, 0.9f);
        float funnel = Mathf.Max(beam.Length() * 0.36f, BuiltHand.LeastPx);
        DrawCircle(at - bow * 0.1f, funnel, hand.Signal);
        DrawCircle(at - bow * 0.1f, funnel * 0.5f, hand.Ink);
        foreach (float mast in new[] { 0.43f, -0.28f }) DrawCircle(at + bow * mast, Mathf.Max(0.3f * _m, 1), hand.Ink);
        Lined(hull, hand, 0.9f);
    }

    void DockCrane(BuiltHand hand, Vector2 at, Vector2 across, Vector2 along)
    {
        float half = PortalM / 2 * _m, line = Mathf.Max(0.3f * _m, BuiltHand.LeastPx);
        foreach (int side in new[] { 1, -1 }) Inked(at + across * (side * half) - along * (CraneRunM * _m), at + across * (side * half) + along * (CraneRunM * _m), hand, 0.6f);
        Vector2 reach = (across * 0.92f + along * 0.38f).Normalized(), tip = at + reach * (JibM * _m), wide = reach.Orthogonal() * (1.1f * _m);
        if (hand.Shaded) DrawLine(at + Sunless * _m, tip + Sunless * (2.4f * _m), hand.Ink with { A = 0.3f }, line * 1.5f, true);
        var portal = Quad(at, across * half, along * half);
        Filled(portal, hand.Lead.Darkened(0.28f));
        Lined(portal, hand, 0.9f);
        Filled(Quad(at - reach * (CounterM * 0.6f * _m), reach * (CounterM / 2 * _m), wide * 1.2f), hand.Ink with { A = 0.85f });
        DrawLine(at - wide, tip, hand.Ink, line, true);
        DrawLine(at + wide, tip, hand.Ink, line, true);
        for (int i = 1; i < Laces; i++) Inked((at - wide).Lerp(tip, i / (float)Laces), (at + wide).Lerp(tip, (i + 0.5f) / Laces), hand, 0.7f);
        DrawCircle(at, 1.5f * _m, hand.Signal);
        DrawCircle(tip, Mathf.Max(0.4f * _m, 1), hand.Ink);
    }

    protected void Worked(BuiltHand hand, string cls, Vector2[] px)
    {
        var look = hand.Area(cls);
        var centre = Mid(px);
        if (cls == "quarry")
        {
            for (int tier = 0; tier < 3; tier++)
            {
                var ring = px.Select(p => centre + (p - centre) * (1 - tier * 0.24f) + Sunless * (tier * 0.5f * _m)).ToArray();
                Stuffed(ring, look.Fill.Darkened(tier * 0.1f), Surface.Hewn);
                Lined(ring, hand, tier == 0 ? 0.8f : 0.45f);
            }
            return;
        }
        Stuffed(px, look.Fill, Surface.Hewn);
        for (int i = 0; i < PanCols * 2; i++)
        {
            float from = (i % PanCols + 0.08f) / PanCols, to = (i % PanCols + 0.92f) / PanCols, near = i < PanCols ? 0.08f : 0.54f;
            var pan = Band(px, from, to, near, near + 0.38f);
            Stuffed(pan, hand.Wet.Lerp(hand.Lime, 0.45f + 0.12f * (i % 3)), Surface.Water);
            if (i % 3 == 1) Disc(pan[2].Lerp(pan[0], 0.25f), 0.7f * _m, hand.Lime.Lightened(0.3f), Surface.Rounded);
        }
        Lined(px, hand, 0.7f);
    }
}
