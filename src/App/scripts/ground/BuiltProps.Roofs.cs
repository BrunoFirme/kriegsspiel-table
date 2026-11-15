using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

abstract partial class BuiltProps
{
    const float VentM = 0.6f, RoofStairM = 2.4f, CatcherM = 1.7f, CatcherDeepM = 0.9f, BinM = 0.75f, DomeletM = 1.5f, BadgirM = 1.8f, DomedFromM = 9;
    static readonly Vector2 Light = new(-0.7071f, -0.7071f);

    protected void Domelet(BuiltHand hand, Vector2 at, float r, Color fill)
    {
        if (hand.Shaded) DrawCircle(at + Vector2.One * r * 0.3f, r, hand.Ink with { A = 0.3f });
        DrawCircle(at, r, fill.Darkened(0.18f));
        DrawCircle(at + Light * r * 0.18f, r * 0.72f, fill);
        DrawCircle(at + Light * r * 0.38f, r * 0.3f, fill.Lightened(0.14f));
    }

    void Stair(BuiltHand hand, Roofed f)
    {
        var stair = f.Part(0.06f, 0.06f + RoofStairM * _m / f.LongPx, 0.08f, 0.08f + 0.9f * _m / f.ShortPx);
        Plain(stair, hand.Ink with { A = 0.3f });
        for (int i = 1; i < 4; i++) Edge(stair[0].Lerp(stair[1], i / 4f), stair[3].Lerp(stair[2], i / 4f), hand, 0.5f);
    }

    void Shade(BuiltHand hand, Roofed f, Color cloth, float u, float v)
    {
        var shade = f.Part(u, u + 0.36f, v, v + 0.35f);
        Plain(shade, cloth with { A = 0.85f });
        Ringed(shade, hand, 0.5f);
        for (int i = 1; i < 4; i++) Edge(shade[0].Lerp(shade[1], i / 4f), shade[3].Lerp(shade[2], i / 4f), hand, 0.25f);
    }

    void Parapet(BuiltHand hand, Roofed f, Color roof)
    {
        var inner = f.Part(0.04f, 0.96f, 0.06f, 0.94f);
        Ringed(inner, hand, 0.35f);
        DrawPolyline([.. inner, inner[0]], roof.Lightened(0.12f) with { A = 0.6f }, BuiltHand.LeastPx, true);
    }

    void Washing(BuiltHand hand, Roofed f, float salt, float v = 0.2f)
    {
        Vector2 a = f.At(0.55f, v), b = f.At(0.9f, v + 0.05f);
        DrawLine(a, b, hand.Ink with { A = 0.5f }, BuiltHand.LeastPx, true);
        for (int i = 1; i < 5; i++)
        {
            var hung = a.Lerp(b, i / 5f);
            DrawRect(new Rect2(hung - Vector2.One * 0.2f * _m, Vector2.One * 0.4f * _m), Chance(salt + i) < 0.5f ? hand.Cloth : hand.Cloth.Lerp(hand.Slate, 0.4f));
        }
    }

    void Catcher(BuiltHand hand, Roofed f, Color roof)
    {
        var box = f.Part(0.78f, 0.78f + CatcherM * _m / f.LongPx, 0.15f, 0.15f + CatcherDeepM * _m / f.ShortPx);
        Plain(box, roof.Lightened(0.08f));
        Ringed(box, hand, 0.7f);
        Plain([box[0], box[1], box[1].Lerp(box[2], 0.45f), box[0].Lerp(box[3], 0.45f)], hand.Ink with { A = 0.55f });
    }

    void Badgir(BuiltHand hand, Roofed f, Color roof)
    {
        var tower = f.Part(0.72f, 0.72f + BadgirM * _m / f.LongPx, 0.6f, 0.6f + BadgirM * _m / f.ShortPx);
        if (hand.Shaded) Plain([.. tower.Select(p => p + Vector2.One * BadgirM * 0.5f * _m)], hand.Ink with { A = 0.3f });
        Plain(tower, roof.Lightened(0.1f));
        Ringed(tower, hand, 0.8f);
        for (int i = 1; i < 4; i++) Edge(tower[0].Lerp(tower[1], i / 4f), tower[3].Lerp(tower[2], i / 4f), hand, 0.6f);
    }

    void Vent(BuiltHand hand, Roofed f, float u, float v) => DrawRect(new Rect2(f.At(u, v) - Vector2.One * VentM * _m / 2, Vector2.One * VentM * _m), hand.Ink with { A = 0.55f });

    void Jars(BuiltHand hand, Roofed f, float u, float v)
    {
        for (int i = 0; i < 3; i++) Tub(hand, f.At(u + 0.07f * i, v + 0.05f * (i % 2)), 0.28f, hand.Tile, Surface.Rounded);
    }

    protected void RoofLife(BuiltHand hand, Roofed f, Color roof)
    {
        if (RoofStairM * _m < 6) return;
        float pick = Chance(83.1f), longM = f.LongPx / _m;
        switch (_culture)
        {
            case Manners.Egyptian:
                Stair(hand, f);
                int bins = 2 + (int)(pick * 3);
                for (int i = 0; i < bins; i++) Domelet(hand, f.At(0.55f + 0.12f * i, 0.3f + 0.25f * (i % 2)), BinM * _m, hand.Silt.Lightened(0.12f));
                if (pick > 0.5f)
                {
                    for (int i = 0; i < 4; i++) DrawRect(new Rect2(f.At(0.2f + 0.06f * i, 0.75f) - Vector2.One * 0.25f * _m, Vector2.One * 0.5f * _m), hand.Mud.Darkened(0.3f));
                    Jars(hand, f, 0.2f, 0.48f);
                }
                else Shade(hand, f, hand.Straw, 0.15f, 0.42f);
                if (Chance(88.8f) < 0.5f) Vent(hand, f, 0.88f, 0.85f);
                return;
            case Manners.Saracen:
                Parapet(hand, f, roof);
                if (pick < 0.7f) Catcher(hand, f, roof);
                bool domed = longM >= DomedFromM && Chance(84.4f) < 0.35f;
                if (domed) Domelet(hand, f.At(0.35f, 0.55f), DomeletM * _m, roof.Lightened(0.06f));
                else Stair(hand, f);
                if (pick > 0.6f) Washing(hand, f, 85.5f, 0.38f);
                if (!domed && Chance(89.9f) < 0.45f) Shade(hand, f, hand.Cloth, 0.5f, 0.55f);
                if (pick is > 0.2f and < 0.7f) Jars(hand, f, 0.2f, 0.72f);
                if (pick < 0.4f) Vent(hand, f, 0.15f, 0.82f);
                return;
            case Manners.Persian:
                Stair(hand, f);
                if (pick < 0.6f) Badgir(hand, f, roof);
                if (longM >= DomedFromM)
                    for (int i = 0; i < 1 + (int)(Chance(86.6f) * 3); i++) Domelet(hand, f.At(0.3f + 0.18f * i, 0.4f), DomeletM * _m, roof);
                else if (Chance(90.1f) < 0.5f) Shade(hand, f, hand.Straw, 0.38f, 0.12f);
                if (pick is > 0.2f and < 0.7f) Jars(hand, f, 0.12f, 0.78f);
                if (pick > 0.5f) Vent(hand, f, 0.88f, 0.25f);
                return;
            case Manners.NorthAfrican:
                Parapet(hand, f, roof);
                Stair(hand, f);
                if (pick < 0.55f) Washing(hand, f, 87.7f);
                if (pick > 0.45f)
                    for (int i = 0; i < 2; i++) Tub(hand, f.At(0.2f + 0.08f * i, 0.75f), 0.35f, hand.Silt.Lightened(0.1f), Surface.Rounded);
                if (pick > 0.7f) Shade(hand, f, hand.Cloth, 0.5f, 0.55f);
                if (pick < 0.3f) Vent(hand, f, 0.85f, 0.32f);
                return;
        }
        Stair(hand, f);
        if (pick < 0.6f) Vent(hand, f, 0.7f, 0.3f);
        if (pick > 0.35f) Shade(hand, f, hand.Cloth, 0.5f, 0.55f);
        if (pick is > 0.2f and < 0.8f) Jars(hand, f, 0.2f, 0.72f);
    }
}
