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
    const int FontPixels = 96, Margin = 12, HaloPixels = 10;
    static readonly Color[] Ink = { new(0.42f, 0.27f, 0.17f), new(0.36f, 0.22f, 0.12f), new(0.6f, 0.33f, 0.13f) };
    static readonly Color[] Paper = { new(0.87f, 0.82f, 0.7f, 0.55f), new(0.83f, 0.72f, 0.5f, 0.55f), new(0.95f, 0.945f, 0.9f, 0.7f) };
    static readonly string[] Serifs = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
    static readonly string[] Sans = { "Arial", "Liberation Sans", "Noto Sans", "DejaVu Sans" };
    static readonly Font[] Faces =
    {
        new SystemFont { FontNames = Serifs, FontItalic = true },
        new SystemFont { FontNames = Serifs },
        new SystemFont { FontNames = Sans, FontWeight = 600 },
    };

    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly List<Decal> _labels = new();
    readonly Dictionary<(string, int), Lettered> _lettering = new();
    Lighting? _look;
    bool _due;
    double _wait;
    double _plannedFor = double.NaN;

    public readonly Node3D Root = new() { Name = "Contours" };

    sealed class Lettered
    {
        public SubViewport? View;
        public ImageTexture? Texture;
        public Vector2 Size;
        public int FramesLeft = FramesToRender;
        public readonly List<Decal> Waiting = new();
    }

    const int FramesToRender = 2;

    public ContourLines(TableContext c, SheetLayer sheets) { _c = c; _sheets = sheets; }

    public void Due(Lighting? look = null)
    {
        if (look != null) _look = look;
        _due = true;
    }

    public void Tick(double delta)
    {
        Develop();
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

    void Develop()
    {
        foreach (var l in _lettering.Values)
        {
            if (l.View == null || --l.FramesLeft > 0) continue;
            l.Texture = ImageTexture.CreateFromImage(l.View.GetTexture().GetImage());
            l.View.QueueFree();
            l.View = null;
            foreach (var d in l.Waiting) if (GodotObject.IsInstanceValid(d)) d.TextureAlbedo = l.Texture;
            l.Waiting.Clear();
        }
    }

    Lettered Lettering(string words, int style)
    {
        if (_lettering.TryGetValue((words, style), out var made)) return made;
        var font = Faces[style];
        var size = font.GetStringSize(words, HorizontalAlignment.Left, -1, FontPixels) + new Vector2(Margin * 2, Margin * 2);
        var view = new SubViewport
        {
            Size = new Vector2I((int)Math.Ceiling(size.X), (int)Math.Ceiling(size.Y)),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        var label = new Label { Text = words, Position = new Vector2(Margin, Margin) };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", FontPixels);
        label.AddThemeColorOverride("font_color", Ink[style]);
        label.AddThemeColorOverride("font_outline_color", Paper[style]);
        label.AddThemeConstantOverride("outline_size", HaloPixels);
        view.AddChild(label);
        Root.AddChild(view);
        return _lettering[(words, style)] = new Lettered { View = view, Size = size };
    }

    void Paint(Vec2 p, double upX, double upZ, string words, float text, double groundY, double slope, int style)
    {
        var lettered = Lettering(words, style);
        var size = lettered.Size;
        var up = (Coords.ToWorld(new Vec2(p.X + upX, p.Z + upZ)) - Coords.ToWorld(p)).Normalized();
        var right = new Vector3(-up.Z, 0, up.X);
        float width = text * size.X / size.Y;
        float reach = text * ReachPerText * (1 + (float)slope);
        var decal = new Decal
        {
            Size = new Vector3(width, reach, text),
            CullMask = ReliefSurface.GroundLayer,
            NormalFade = 0,
            Transform = new Transform3D(new Basis(right, Vector3.Up, -up), Coords.ToWorld(p, (float)groundY)),
        };
        Root.AddChild(decal);
        if (lettered.Texture != null) decal.TextureAlbedo = lettered.Texture;
        else lettered.Waiting.Add(decal);
        _labels.Add(decal);
    }
}
