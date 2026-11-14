using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

partial class ToolIcon
{
    public static readonly IReadOnlyDictionary<string, Action<ToolIcon>> Choices = new Dictionary<string, Action<ToolIcon>>
    {
        ["walls-none"] = icon => icon.Hamlet(),
        ["walls-wall"] = icon => icon.Ringwall(),
        ["walls-bastions"] = icon => icon.StarFort(),
        ["walls-" + Defences.Lines] = icon => icon.Entrenched(),
        ["plan-" + Plans.Maze] = icon => icon.Alleys(),
        ["plan-" + Plans.Wards] = icon => icon.Wards(),
        ["plan-" + Plans.Rows] = icon => icon.Rows(),
        ["plan-" + Plans.Shore] = icon => icon.Strand(),
        ["plan-" + Plans.Scatter] = icon => icon.Steadings(),
        ["outworks-none"] = icon => icon.StarFort(),
        ["outworks-glacis"] = icon => icon.Outworks(false),
        ["outworks-ravelins"] = icon => icon.Outworks(true),
        ["plan-" + Plans.Grown] = icon => icon.Lanes(),
        ["plan-" + Plans.Street] = icon => icon.Plots(),
        ["plan-" + Plans.Green] = icon => icon.Common(),
        ["plan-" + Plans.Round] = icon => icon.Horseshoe(),
        ["plan-" + Plans.Grid] = icon => icon.Lattice(),
        ["fields-enclosed"] = icon => icon.Closes(),
        ["fields-shares"] = icon => icon.Strips(),
        ["name-none"] = icon => icon.Lettered(false),
        ["name-typed"] = icon => icon.Lettered(true),
        ["sweep-" + Brushed.Fill] = icon => icon.More(true),
        ["sweep-" + Brushed.Thin] = icon => icon.More(false),
        ["sweep-" + BuildTool.Gate] = icon => icon.Breach(),
    };

    bool Chosen(string kind)
    {
        if (!Choices.TryGetValue(kind, out var draw)) return false;
        draw(this);
        return true;
    }

    void Square(float x, float y, float side) => Solid(x, y, x + side, y, x + side, y + side, x, y + side);

    void Hamlet()
    {
        Square(5.6f, 5.6f, 3.8f);
        Square(10.8f, 7.8f, 3.8f);
        Square(6.6f, 11, 3.2f);
    }

    void Ringwall()
    {
        Hamlet();
        Trace(_c, Fine, 10, 2.5f, 16, 5, 17.5f, 11, 14, 16.5f, 6, 16.5f, 2.5f, 11, 4, 5, 10, 2.5f);
        foreach (var (x, y) in new[] { (10f, 2.5f), (17.5f, 11f), (14f, 16.5f), (6f, 16.5f), (2.5f, 11f) }) DrawCircle(P(x, y), 1.3f * _k, _c);
    }

    void Entrenched()
    {
        Hamlet();
        Trace(_c, Fine, 3, 6, 5, 3.5f, 7, 5, 9, 2.5f, 11, 4, 13, 2.5f, 15, 5, 17, 6.5f);
        Trace(_c, Fine, 3, 14, 5, 16.5f, 7, 15, 9, 17.5f, 11, 16, 13, 17.5f, 15, 15, 17, 13.5f);
        Barbs(1.5f, 10);
        Barbs(18.5f, 10);
    }

    void Barbs(float x, float y)
    {
        Line(x - 1, y - 1, x + 1, y + 1, Fine);
        Line(x - 1, y + 1, x + 1, y - 1, Fine);
    }

    void Alleys()
    {
        Trace(_c, Bold, 2, 12, 6, 9, 9, 12, 13, 9, 18, 11);
        Trace(_c, Fine, 6, 9, 5, 5, 8, 3.5f);
        Trace(_c, Fine, 9, 12, 8, 16, 11, 17.5f);
        Trace(_c, Fine, 13, 9, 14.5f, 5, 12.5f, 3);
        Trace(_c, Fine, 14.5f, 10, 16, 14, 14, 16.5f);
    }

    void Wards()
    {
        Line(10, 2, 10, 18, Bold);
        Line(2, 10, 18, 10, Bold);
        foreach (var (x, y) in new[] { (3.2f, 3.2f), (11.8f, 3.2f), (3.2f, 11.8f), (11.8f, 11.8f) }) Trace(_c, Fine, x, y, x + 5, y, x + 5, y + 5, x, y + 5, x, y);
    }

    void Rows()
    {
        Trace(_c, Fine, 2.5f, 3, 17.5f, 3, 17.5f, 17, 2.5f, 17, 2.5f, 3);
        for (float y = 5.5f; y < 16; y += 2.6f) Line(4, y, 16, y, Bold);
    }

    void Strand()
    {
        Trace(Blue, Bold, 1, 15, 5, 13.5f, 9, 15, 13, 13.5f, 19, 15);
        for (float x = 3; x < 18; x += 3.4f) Square(x, 8.4f - 0.6f * Mathf.Sin(x), 2.2f);
    }

    void Steadings()
    {
        foreach (var (x, y, r) in new[] { (6f, 6f, 3f), (14f, 5.5f, 2.4f), (10.5f, 12f, 2.8f), (4.5f, 14.5f, 2.2f), (16f, 14f, 2.6f) })
        {
            DrawArc(P(x, y), r * _k, 0.5f, Mathf.Tau, 18, _c, Fine * _k, true);
            DrawCircle(P(x, y), r * 0.38f * _k, _c);
        }
    }

    void Outworks(bool ravelins)
    {
        StarFort();
        DrawArc(P(10, 10), 9.4f * _k, 0, Mathf.Tau, 28, Soft, Fine * _k, true);
        if (!ravelins) return;
        Solid(10, 0.4f, 11.6f, 2.6f, 8.4f, 2.6f);
        Solid(10, 19.6f, 11.6f, 17.4f, 8.4f, 17.4f);
        Solid(0.4f, 10, 2.6f, 8.4f, 2.6f, 11.6f);
        Solid(19.6f, 10, 17.4f, 8.4f, 17.4f, 11.6f);
    }

    void Lanes()
    {
        Trace(_c, Bold, 2, 14, 8, 11, 13, 12, 18, 8);
        Trace(_c, Fine, 8, 11, 6, 4.5f);
        Trace(_c, Fine, 13, 12, 15, 17);
        Trace(_c, Fine, 10.5f, 11.5f, 12, 5);
    }

    void Plots()
    {
        Line(2, 10, 18, 10, Bold);
        for (float x = 3.5f; x < 18; x += 3.5f)
        {
            Line(x, 8.6f, x, 3.5f, Fine);
            Line(x, 11.4f, x, 16.5f, Fine);
        }
    }

    void Common()
    {
        DrawColoredPolygon(Pts(2, 10, 6, 7, 10, 6, 14, 7, 18, 10, 14, 13, 10, 14, 6, 13), Soft);
        Trace(_c, Bold, 2, 10, 6, 7, 10, 6, 14, 7, 18, 10, 14, 13, 10, 14, 6, 13, 2, 10);
    }

    void Lattice()
    {
        foreach (float at in new[] { 5f, 10f, 15f })
        {
            Line(at, 3, at, 17, Fine);
            Line(3, at, 17, at, Fine);
        }
    }

    void Horseshoe()
    {
        DrawArc(P(10, 10), 4.6f * _k, 0.6f, Mathf.Tau - 0.6f, 24, _c, Bold * _k, true);
        for (int i = 1; i < 8; i++) DrawCircle(P(10, 10) + Vector2.Right.Rotated(i * Mathf.Tau / 8) * 7.6f * _k, 1.1f * _k, _c);
    }

    void Closes()
    {
        Trace(_c, Fine, 2, 4, 9, 3, 11, 9, 3, 11, 2, 4);
        Trace(_c, Fine, 11, 9, 18, 7, 17, 16, 9, 17, 3, 11);
    }

    void Strips()
    {
        Line(2, 16, 18, 16, Bold);
        for (float x = 4; x <= 16; x += 3) Line(x, 16, x, 4, Fine);
    }

    void Lettered(bool typed)
    {
        Square(2, 7, 6);
        if (typed) Line(10.5f, 8.5f, 17, 8.5f, Bold);
        else Trace(Soft, Bold, 10.5f, 8.5f, 17, 8.5f);
        if (typed) Line(10.5f, 12, 15, 12, Fine);
        else Trace(Soft, Fine, 10.5f, 12, 15, 12);
        if (typed) Line(18.4f, 6, 18.4f, 14, Fine);
    }

    void Breach()
    {
        Line(2, 10, 7.5f, 10, Bold);
        Line(12.5f, 10, 18, 10, Bold);
        Square(6.3f, 8.3f, 2.4f);
        Square(11.3f, 8.3f, 2.4f);
        Line(10, 4, 10, 16, Fine);
    }

    void More(bool houses)
    {
        Square(3, 8, 3.4f);
        Square(7.6f, 8, 3.4f);
        if (houses) Square(12.2f, 8, 3.4f);
        else Trace(Soft, Fine, 12.2f, 8, 15.6f, 8, 15.6f, 11.4f, 12.2f, 11.4f, 12.2f, 8);
        Line(13, 15.5f, 17, 15.5f, Bold);
        if (houses) Line(15, 13.5f, 15, 17.5f, Bold);
    }
}
