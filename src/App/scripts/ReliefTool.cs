using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class ReliefTool
{
    const ulong DraftEveryMs = 70;
    const int RingSteps = 48;
    const float SmallestBrush = 0.2f, LargestBrush = 400f;

    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly SheetLayer _sheets;
    SheetDef? _sheet;
    ReliefGrid? _draft;
    Vec2? _last;
    Rect2? _dirty;
    ulong _pushed;
    double? _level;

    public ReliefTool(TableContext c, ToolOverlay overlay, SheetLayer sheets) { _c = c; _overlay = overlay; _sheets = sheets; }

    public event Action<string, Command>? Shaped;

    Settings Cfg => _c.Settings;

    public static Brush BrushOf(string mode, double strength) => mode switch
    {
        "lower" => new Brush(BrushMode.Lower, 0, strength),
        "smooth" => new Brush(BrushMode.Smooth, 0, strength),
        "flatten" => new Brush(BrushMode.Flatten, 0, strength),
        _ when mode.StartsWith("paint:", StringComparison.Ordinal) => new Brush(BrushMode.Paint, 0, strength, GroundKinds.Of(mode[6..]) ?? GroundKinds.None),
        _ => new Brush(BrushMode.Raise, 0, strength),
    };

    public static SheetDef? Target(Session s, SheetLayer sheets, Vec2 p) => s.Ground.ReliefAt(p) ?? sheets.TopAt(p)?.Def;

    public void Press(Vec2 g)
    {
        var s = _c.S;
        if (!s.CanEdit(Layers.Umpire)) { _c.Say("Only umpires shape the ground, and not while a past turn is shown."); return; }
        if (Target(s, _sheets, g) is not { } target) { _c.Say("Brush over a map sheet."); return; }
        ReliefGrid? existing = null;
        if (target.Relief is { Length: > 0 } && (existing = s.Relief.Grid(target)) == null) { _c.Say("The relief is still loading."); return; }
        _sheet = Json.Clone(target);
        var c = target.Crop ?? SheetGeometry.Whole;
        _draft = existing?.Copy() ?? ReliefGrid.Flat(Ground.FullWidth(target), target.Size.H / Math.Max(1e-6, c.H));
        _level = null;
        _last = null;
        Dab(g);
    }

    public void Motion(Vec2 g) { if (_draft != null) Dab(g); }

    void Dab(Vec2 g)
    {
        if (_sheet == null || _draft == null) return;
        var from = _last ?? g;
        _last = g;
        if (Ground.Uv(_sheet, g) is not var (u1, v1) || Ground.Uv(_sheet, from) is not var (u0, v0)) return;
        double cell = Ground.FullWidth(_sheet) / Math.Max(1, _draft.Cols - 1);
        double radius = Math.Clamp(Cfg.ReliefBrush, SmallestBrush, LargestBrush) / cell;
        var brush = BrushOf(Cfg.ReliefMode, Cfg.ReliefStrength);
        if (brush.Mode == BrushMode.Flatten) { _level ??= _draft.HeightAt(u0, v0); brush = brush with { Level = _level }; }
        var box = ReliefBrush.Stroke(_draft, new List<(double, double)> { (u0, v0), (u1, v1) }, radius, brush);
        if (box.Empty) return;
        var area = WorldBox(_sheet, _draft, box);
        _dirty = _dirty is { } d ? d.Merge(area) : area;
        if (Time.GetTicksMsec() - _pushed >= DraftEveryMs) Push();
    }

    void Push()
    {
        if (_sheet == null || _draft == null) return;
        _pushed = Time.GetTicksMsec();
        _c.S.Relief.Draft(_sheet.Id, _draft, _dirty);
        _dirty = null;
    }

    public static Rect2 WorldBox(SheetDef s, ReliefGrid g, CellBox box)
    {
        double u0 = box.X0 / (double)(g.Cols - 1), u1 = box.X1 / (double)(g.Cols - 1), v0 = box.Y0 / (double)(g.Rows - 1), v1 = box.Y1 / (double)(g.Rows - 1);
        var first = Coords.ToWorld(Ground.FromUv(s, u0, v0));
        var r = new Rect2(new Vector2(first.X, first.Z), Vector2.Zero);
        foreach (var (u, v) in new[] { (u1, v0), (u1, v1), (u0, v1) })
        {
            var w = Coords.ToWorld(Ground.FromUv(s, u, v));
            r = r.Expand(new Vector2(w.X, w.Z));
        }
        return r.Grow((float)(Ground.FullWidth(s) / Math.Max(1, g.Cols - 1)));
    }

    public void Release()
    {
        if (_sheet == null || _draft == null) return;
        Push();
        var sheet = _sheet; var grid = _draft;
        _sheet = null; _draft = null; _last = null;
        Commit(_c.S, sheet, grid, (sha, c) => Shaped?.Invoke(sha, c), _c.Say);
    }

    public static void Commit(Session s, SheetDef sheet, ReliefGrid grid, Action<string, Command> share, Action<string> say)
    {
        var root = s.Store.Root;
        Task.Run(() =>
        {
            try
            {
                var (sha, _) = ReliefImport.Save(grid, root);
                Callable.From(() =>
                {
                    s.Relief.Remember(sha, grid);
                    if (sha == sheet.Relief) { s.Relief.Draft(sheet.Id, null); return; }
                    share(sha, new SetSheetRelief { Id = sheet.Id, Relief = sha, Height = sheet.Relief is { Length: > 0 } ? sheet.Height : 1 });
                }).CallDeferred();
            }
            catch (Exception e)
            {
                var why = e.Message;
                Callable.From(() => { s.Relief.Draft(sheet.Id, null); say("The relief could not be saved: " + why); GD.PrintErr("[relief] saving a shaped relief: " + why); }).CallDeferred();
            }
        });
    }

    public void Cancel()
    {
        if (_sheet != null) _c.S.Relief.Draft(_sheet.Id, null);
        _sheet = null; _draft = null; _last = null;
    }

    public void Draw()
    {
        if (_c.Tool != Tool.Relief || _c.Rig == null) return;
        var mouse = _c.Scene.GetViewport().GetMousePosition();
        if (_c.Ground(mouse) is not { } g) return;
        float r = Math.Clamp(Cfg.ReliefBrush, SmallestBrush, LargestBrush);
        var ring = new List<Vec2>();
        for (int i = 0; i <= RingSteps; i++)
        {
            double a = i * Math.Tau / RingSteps;
            ring.Add(new Vec2(g.X + Math.Cos(a) * r, g.Z + Math.Sin(a) * r));
        }
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.85f, 0.4f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        _overlay.RibbonPx(_overlay.Mesh, mat, ring, 0.05f, 2f);
    }

    public void Resize(bool up)
    {
        Cfg.ReliefBrush = Math.Clamp(Cfg.ReliefBrush * (up ? 1.15f : 1 / 1.15f), SmallestBrush, LargestBrush);
        Cfg.Save();
    }
}
