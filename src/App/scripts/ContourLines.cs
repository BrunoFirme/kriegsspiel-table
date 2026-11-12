using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class ContourLines
{
    const double SettleSeconds = 0.3;
    const int SamplesAcross = 240;
    const int MostLabels = 120;
    const double LabelEveryCells = 26, LabelApartCells = 7;
    const float TextPerExtent = 1 / 160f, ReachPerText = 4f;
    const int FontPixels = 96;
    static readonly Color[] Ink = { new(0.3f, 0.24f, 0.18f), new(0.36f, 0.22f, 0.12f), new(0.5f, 0.32f, 0.2f), new(0.2f, 0.19f, 0.18f) };
    static readonly string[] Serifs = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
    static readonly string[] Sans = { "Arial", "Liberation Sans", "Noto Sans", "DejaVu Sans" };
    static readonly Font[] Faces =
    {
        new SystemFont { FontNames = Serifs, FontItalic = true },
        new SystemFont { FontNames = Serifs },
        new SystemFont { FontNames = Sans, FontWeight = 600 },
        new SystemFont { FontNames = Serifs, FontItalic = true },
    };

    internal static int Styles => Math.Min(Ink.Length, Faces.Length);

    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly List<Decal> _labels = new();
    readonly MapLettering _letters;
    Lighting? _look;
    bool _due;
    double _wait;
    double _plannedFor = double.NaN;

    public readonly Node3D Root = new() { Name = "Contours" };

    public ContourLines(TableContext c, SheetLayer sheets, MapLettering letters)
    {
        _c = c;
        _sheets = sheets;
        _letters = letters;
    }

    public void Due(Lighting? look = null)
    {
        if (look != null) _look = look;
        _due = true;
    }

    public void Tick(double delta)
    {
        _letters.Develop();
        if ((_look ?? _c.S.View.Lighting).Contours && _c.S.Ground.Highest != _plannedFor) _due = true;
        if (!_due) return;
        _wait -= delta;
        if (_wait > 0) return;
        _due = false;
        _wait = SettleSeconds;
        Refresh();
    }

    public static ContourPlan? PlanFor(GameState view, Ground ground)
    {
        var reliefs = view.Sheets.Where(s => s.Relief is { Length: > 0 }).ToList();
        return reliefs.Count == 0 ? null : Contours.Plan(ground.Highest, reliefs.Max(s => s.Height), view.Calibration);
    }

    public static double SnowLineFor(GameState view, Lighting look)
    {
        if (look.SnowLine <= 0 || view.Calibration.UnitsPerMeter is not > 0) return 0;
        var reliefs = view.Sheets.Where(s => s.Relief is { Length: > 0 }).ToList();
        return reliefs.Count == 0 ? 0 : view.Calibration.ToTableUnits(look.SnowLine) * reliefs.Max(s => s.Height);
    }

    void Refresh()
    {
        var s = _c.S;
        var look = _look ?? s.View.Lighting;
        bool contoursChanged = ReliefSurface.ShowContours(look.Contours);
        if (ReliefSurface.ShowStreams(look.Streams) || contoursChanged) _sheets.RegroundAll();
        _plannedFor = s.Ground.Highest;
        var plan = look.Contours ? PlanFor(s.View, s.Ground) : null;
        ReliefSurface.ContourStep(plan?.Step ?? 0);
        ReliefSurface.SnowLine(SnowLineFor(s.View, look));
        Clear();
        if (plan is { Numbered: true }) Letter(plan, s.Ground, MapStyles.Index(look.MapStyle));
    }

    void Clear()
    {
        foreach (var l in _labels) l.QueueFree();
        _labels.Clear();
    }

    void Letter(ContourPlan plan, Ground ground, int style)
    {
        var reliefs = _c.S.View.Sheets.Where(sh => sh.Relief is { Length: > 0 }).ToList();
        if (reliefs.Count == 0) return;
        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
        foreach (var sh in reliefs)
        {
            double r = Math.Sqrt(sh.Size.W * sh.Size.W + sh.Size.H * sh.Size.H) / 2;
            x0 = Math.Min(x0, sh.Pos.X - r);
            x1 = Math.Max(x1, sh.Pos.X + r);
            z0 = Math.Min(z0, sh.Pos.Z - r);
            z1 = Math.Max(z1, sh.Pos.Z + r);
        }
        double extent = Math.Max(x1 - x0, z1 - z0);
        double cell = extent / SamplesAcross;
        int nx = (int)Math.Ceiling((x1 - x0) / cell) + 1, nz = (int)Math.Ceiling((z1 - z0) / cell) + 1;
        var h = new double[nx * nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var p = new Vec2(x0 + i * cell, z0 + j * cell);
                h[j * nx + i] = ground.ReliefAt(p) != null ? ground.At(p) : double.NaN;
            }
        double index = plan.Step * ContourPlan.IndexEvery;
        float text = (float)(extent * TextPerExtent);
        var placed = new List<(Vec2 at, int level)>();
        for (int level = 1; level * index < ground.Highest && placed.Count < MostLabels; level++)
        {
            double at = level * index;
            for (int j = 1; j < nz - 1 && placed.Count < MostLabels; j++)
                for (int i = 1; i < nx - 1 && placed.Count < MostLabels; i++)
                {
                    double a = h[j * nx + i], b = h[j * nx + i + 1], c = h[(j + 1) * nx + i];
                    if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) continue;
                    if (!((a - at) * (b - at) < 0 || (a - at) * (c - at) < 0)) continue;
                    double gx = (h[j * nx + i + 1] - h[j * nx + i - 1]) / (2 * cell), gz = (h[(j + 1) * nx + i] - h[(j - 1) * nx + i]) / (2 * cell);
                    if (double.IsNaN(gx) || double.IsNaN(gz)) continue;
                    double g = Math.Sqrt(gx * gx + gz * gz);
                    if (g < 1e-9 || plan.Step / g < text * 0.45) continue;
                    var p = new Vec2(x0 + (i + 0.5) * cell, z0 + (j + 0.5) * cell);
                    if (placed.Any(q => Near(q.at, p, cell * (q.level == level ? LabelEveryCells : LabelApartCells)))) continue;
                    placed.Add((p, level));
                    Paint(p, gx / g, gz / g, Words(level * ContourPlan.IndexEvery * plan.Every, style), text, ground.At(p), g, style);
                }
        }
    }

    static string Words(double value, int style) =>
        (style == MapStyles.Index(MapStyles.Ancient) ? Roman.Of(value) : null) ?? value.ToString("0.##", CultureInfo.InvariantCulture);

    static bool Near(Vec2 a, Vec2 b, double within) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) < within * within;

    void Paint(Vec2 p, double upX, double upZ, string words, float text, double groundY, double slope, int style)
    {
        var lettered = _letters.Bake($"contour/{style}:{words}", words, new MapLettering.Hand($"contour/{style}", Faces[style], FontPixels, Ink[style], MapLettering.Paper[style]));
        var up = (Coords.ToWorld(new Vec2(p.X + upX, p.Z + upZ)) - Coords.ToWorld(p)).Normalized();
        _labels.Add(_letters.Place(lettered, Coords.ToWorld(p, (float)groundY), up, text, (float)slope));
    }
}
