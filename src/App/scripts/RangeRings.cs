using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RangeRings
{
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly Selection _selection;
    public RangeRings(TableContext c, ToolOverlay overlay, Selection selection)
    {
        _c = c;
        _overlay = overlay;
        _selection = selection;
    }

    double? _standingReach;

    public double RangeMetres
    {
        get => _standingReach ?? (Scaled ? 500 : 10);
        set => _standingReach = value;
    }

    bool Scaled => _c.S.View.Calibration.UnitsPerMeter is { } u && u > 0;

    public double RangeOf(PieceNode n)
    {
        foreach (var (k, v) in n.Piece.Counters)
            if (string.Equals(k, "range", StringComparison.OrdinalIgnoreCase)) return v;
        foreach (var (k, v) in n.Piece.Text)
            if (string.Equals(k, "range", StringComparison.OrdinalIgnoreCase) && double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture,
                out var t)) return t;
        return RangeMetres;
    }

    double RangeToUnits(double reach) => _c.S.View.Calibration.UnitsPerMeter is { } upm && upm > 0 ? reach * upm : reach;

    public string Hint()
    {
        if (_selection.Ids.Count == 0) return "Ranging · click a piece to see how far it reaches.";
        bool scale = _c.S.View.Calibration.UnitsPerMeter is { } u && u > 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var reach = scale ? string.Format(inv, "{0:#,0} m", RangeMetres) : string.Format(inv, "{0:0.##} u", RangeMetres);
        return $"Ranging · click a piece to measure from it · wheel over it = {reach} · a piece with a \"range\" counter uses its own · Esc to finish.";
    }

    public void Wheel(float f)
    {
        RangeMetres = Math.Clamp(RangeMetres * (f < 1 ? 1.1 : 1 / 1.1), 1, 1_000_000);
        _c.Say(Hint());
    }

    readonly HashSet<string> _labels = new();

    public int Readings => _labels.Count;

    public void ClearReadings()
    {
        foreach (var id in _labels) _c.Labels.Remove($"range:{id}");
        _labels.Clear();
    }

    public void Draw()
    {
        ClearReadings();
        if (_c.Tool != Tool.Range) return;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.55f, 0.15f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        var faint =
            new StandardMaterial3D { AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        var wash =
            new StandardMaterial3D { AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.08f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        int which = 0;
        foreach (var n in _selection.Nodes)
        {
            double r = RangeToUnits(RangeOf(n));
            if (r <= 0.01) continue;
            DrawRing(n, r, which, mat, faint, wash);
            which++;
        }
    }

    void DrawRing(PieceNode n, double r, int which, StandardMaterial3D mat, StandardMaterial3D faint, StandardMaterial3D wash)
    {
        var overlay = _overlay;
        var mesh = overlay.Mesh;
        const float y = 0.12f;
        const int steps = 120;
        var ring = new List<Vec2>();
        for (int i = 0; i <= steps; i++)
        {
            double a = i * Math.Tau / steps;
            ring.Add(n.Piece.Pos + new Vec2(Math.Cos(a) * r, Math.Sin(a) * r));
        }
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, wash);
        for (int i = 0; i < steps; i++)
        {
            mesh.SurfaceAddVertex(Coords.ToWorld(n.Piece.Pos, y));
            mesh.SurfaceAddVertex(Coords.ToWorld(ring[i], y));
            mesh.SurfaceAddVertex(Coords.ToWorld(ring[i + 1], y));
        }
        mesh.SurfaceEnd();
        Ribbon.Draw(mesh, mat, ring, overlay.Px(3), y);
        double ang = Math.PI / 2 - which * 0.42;
        var edge = n.Piece.Pos + new Vec2(Math.Cos(ang) * r, Math.Sin(ang) * r);
        void Seg(Vec2 p, Vec2 q) => overlay.SegPx(mesh, faint, p, q, y, 2f);
        Seg(n.Piece.Pos, edge);
        for (int q = 0; q < 4; q++)
        {
            double a = q * Math.Tau / 4;
            var u = new Vec2(Math.Cos(a), Math.Sin(a));
            Seg(n.Piece.Pos + new Vec2(u.X * r, u.Z * r), n.Piece.Pos + new Vec2(u.X * (r + overlay.Px(6)), u.Z * (r + overlay.Px(6))));
        }
        _c.Labels.Set($"range:{n.Piece.Id}", Coords.ToWorld(edge, 0.3f),
            Fmt.Distance(r, _c.S.View.Calibration), new Color(1f, 0.78f, 0.5f), 13, new Vector2(0, -12), centered: true);
        _labels.Add(n.Piece.Id);
    }
}
