using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class ReliefTool
{
    const ulong DraftEveryMs = 70;
    const int RingSteps = 48;
    const float SmallestBrush = 0.2f, LargestBrush = 400f;
    const float CloseSnapPx = 10f, ShapeLeastPx = 4f;
    const string NotYours = "Only umpires shape the ground, and not while a past turn is shown.";

    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly SheetLayer _sheets;
    SheetDef? _sheet;
    ReliefGrid? _draft;
    Vec2? _last;
    Rect2? _dirty;
    ulong _pushed;
    double? _level;
    (string Sheet, Vec2 At)? _end;
    List<Vec2>? _corners;
    Vec2? _cursor;
    Vec2 _across = new(1, 0);
    string? _shapeSheet;
    byte _salt;

    public ReliefTool(TableContext c, ToolOverlay overlay, SheetLayer sheets)
    {
        _c = c;
        _overlay = overlay;
        _sheets = sheets;
    }

    public event Action<string, Command>? Shaped;
    public event Action? Picked;

    Settings Cfg => _c.Settings;

    public static Brush BrushOf(string mode, double strength, double density = 1, byte salt = 0, byte detail = 0) => mode switch
    {
        "lower" => new Brush(BrushMode.Lower, 0, strength),
        "smooth" => new Brush(BrushMode.Smooth, 0, strength),
        "flatten" => new Brush(BrushMode.Flatten, 0, strength),
        _ when mode.StartsWith("paint:", StringComparison.Ordinal) => new Brush(BrushMode.Paint, 0, strength, GroundKinds.Of(mode[6..]) ?? GroundKinds.None, Density: density, Salt: salt, Detail: detail),
        _ => new Brush(BrushMode.Raise, 0, strength),
    };

    public static byte RollSalt() => (byte)Random.Shared.Next(1, byte.MaxValue + 1);

    Brush Current => BrushOf(Cfg.ReliefMode, Cfg.ReliefStrength, Cfg.ReliefDensity, _salt, Cfg.ReliefLevels(GroundKinds.Of(Cfg.ReliefMode[(Cfg.ReliefMode.IndexOf(':') + 1)..]) ?? GroundKinds.None));

    public static bool Scaled(Session s) => s.View.Calibration.UnitsPerMeter is > 0;

    public static SheetDef? Target(Session s, SheetLayer sheets, Vec2 p) => s.Ground.ReliefAt(p) ?? sheets.TopAt(p)?.Def;

    bool Painting => Cfg.ReliefMode.StartsWith("paint:", StringComparison.Ordinal);
    string Paint => Painting ? Cfg.ReliefPaint : "brush";
    bool Filling => Paint == "fill";
    bool Shaping => Paint is "rect" or "ellipse" or "polygon" or "route";
    bool Routing => Paint == "route";

    public static double HeightUnit(SheetDef s) => s.Relief is { Length: > 0 } ? s.Height : 1;

    public void Press(Vec2 g, bool line, bool pick, bool twice = false)
    {
        var s = _c.S;
        if (pick) { Pick(g); return; }
        if (!Shaping) DropShape();
        else if (_corners != null) { Corner(g, twice); return; }
        if (!s.CanEdit(Layers.Umpire)) { _c.Say(NotYours); return; }
        if (!Scaled(s)) { _c.Say(GroundKinds.NeedsScale + "."); return; }
        if (Target(s, _sheets, g) is not { } target) { _c.Say("Brush over a map sheet."); return; }
        if (Shaping)
        {
            _shapeSheet = target.Id;
            _corners = new() { g };
            _cursor = g;
            _across = PenTool.ScreenAcross(_c.Rig.Cam);
            return;
        }
        if (!Open(target)) return;
        _salt = RollSalt();
        if (Filling) { Fill(g); return; }
        _last = line && _end is { } e && e.Sheet == target.Id ? e.At : null;
        Dab(g);
    }

    bool Open(SheetDef target)
    {
        ReliefGrid? existing = null;
        if (target.Relief is { Length: > 0 } && (existing = _c.S.Relief.Grid(target)) == null) { _c.Say("The relief is still loading."); return false; }
        _sheet = Json.Clone(target);
        var c = target.Crop ?? SheetGeometry.Whole;
        _draft = existing?.Copy() ?? ReliefGrid.Flat(Ground.FullWidth(target), target.Size.H / Math.Max(1e-6, c.H));
        _level = null;
        return true;
    }

    void Pick(Vec2 g)
    {
        Cfg.ReliefMode = "paint:" + GroundKinds.Name(GroundKinds.BrushOf(_c.S.Ground.KindAt(g)));
        Cfg.Save();
        Picked?.Invoke();
    }

    void Fill(Vec2 g)
    {
        if (_sheet == null || _draft == null || Ground.Uv(_sheet, g) is not var (u, v)) return;
        var box = ReliefBrush.Fill(_draft, u, v, Current);
        _end = (_sheet.Id, g);
        Lay(box);
    }

    void Corner(Vec2 g, bool twice)
    {
        var corners = _corners!;
        var back = Routing ? corners[^1] : corners[0];
        bool enough = Routing || corners.Count >= 3;
        if (enough && (twice || (g - back).Length <= _overlay.Px(CloseSnapPx))) Close(corners);
        else if (!twice) corners.Add(g);
    }

    List<Vec2>? Outline(Vec2 at)
    {
        if (_corners == null) return null;
        if (Routing) return new List<Vec2>(_corners) { at };
        if (Paint == "polygon") return new List<Vec2>(_corners) { at, _corners[0] };
        var a = _corners[0];
        var b = Input.IsKeyPressed(Key.Shift) ? Polyline.Squared(a, at, _across) : at;
        return Polyline.ShapePoints(Paint, a, b, _across);
    }

    void Close(List<Vec2> outline)
    {
        var id = _shapeSheet;
        DropShape();
        if (!_c.S.CanEdit(Layers.Umpire)) { _c.Say(NotYours); return; }
        if (_c.S.View.Sheets.FirstOrDefault(x => x.Id == id) is not { } target || !Open(target)) return;
        _salt = RollSalt();
        var sheet = _sheet!;
        var draft = _draft!;
        var uv = outline.Select(p => Ground.UvOf(sheet, p)).ToList();
        if (!Routing) { Lay(ReliefBrush.Shape(draft, uv, Current)); return; }
        double metre = _c.S.View.Calibration.UnitsPerMeter ?? 0;
        if (metre <= 0)
        {
            _c.Say(GroundKinds.NeedsScale + ".");
            Lay(CellBox.None);
            return;
        }
        double cell = Ground.FullWidth(sheet) / Math.Max(1, draft.Cols - 1);
        Lay(GroundRoute.Lay(draft, Current, uv, Math.Clamp(Cfg.ReliefBrush, SmallestBrush, LargestBrush) / cell, cell / metre, HeightUnit(sheet) / metre));
    }

    void Lay(CellBox box)
    {
        if (_sheet == null || _draft == null) return;
        if (box.Empty)
        {
            _sheet = null;
            _draft = null;
            return;
        }
        _dirty = WorldBox(_sheet, _draft, box);
        Push();
    }

    public bool DropShape()
    {
        bool open = _corners != null;
        _corners = null;
        _cursor = null;
        _shapeSheet = null;
        return open;
    }

    public void Motion(Vec2 g)
    {
        if (_corners != null) _cursor = g;
        else if (_draft != null && !Filling) Dab(g);
    }

    void Dab(Vec2 g)
    {
        if (_sheet == null || _draft == null) return;
        var from = _last ?? g;
        _last = g;
        if (Ground.Uv(_sheet, g) is not var (u1, v1) || Ground.Uv(_sheet, from) is not var (u0, v0)) return;
        double cell = Ground.FullWidth(_sheet) / Math.Max(1, _draft.Cols - 1);
        double radius = Math.Clamp(Cfg.ReliefBrush, SmallestBrush, LargestBrush) / cell;
        var brush = Current;
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
        if (_corners != null && !Routing && Paint != "polygon")
        {
            var at = _cursor ?? _corners[0];
            if ((at - _corners[0]).Length >= _overlay.Px(ShapeLeastPx) && Outline(at) is { } outline) Close(outline);
            else DropShape();
        }
        if (_sheet == null || _draft == null) return;
        Push();
        var sheet = _sheet;
        var grid = _draft;
        if (_last is { } end) _end = (sheet.Id, end);
        _sheet = null;
        _draft = null;
        _last = null;
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
                    share(sha, new SetSheetRelief { Id = sheet.Id, Relief = sha, Height = HeightUnit(sheet) });
                }).CallDeferred();
            }
            catch (Exception e)
            {
                var why = e.Message;
                Callable.From(() =>
                {
                    s.Relief.Draft(sheet.Id, null);
                    say("The relief could not be saved: " + why);
                    GD.PrintErr("[relief] saving a shaped relief: " + why);
                }).CallDeferred();
            }
        });
    }

    public void Cancel()
    {
        if (_sheet != null) _c.S.Relief.Draft(_sheet.Id, null);
        _sheet = null;
        _draft = null;
        _last = null;
        DropShape();
    }

    public void Draw()
    {
        if (_c.Tool != Tool.Relief || _c.Rig == null) return;
        var mouse = _c.Scene.GetViewport().GetMousePosition();
        var g = _c.Ground(mouse);
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.85f, 0.4f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (Shaping)
        {
            if (_corners == null || Outline(g ?? _cursor ?? _corners[^1]) is not { } outline) return;
            _overlay.RibbonPx(_overlay.Mesh, mat, outline, 0.05f, 2f);
            if (Paint == "polygon" || Routing) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(Routing ? _corners[^1] : _corners[0], _overlay.Px(CloseSnapPx)), 0.05f, 2f);
            return;
        }
        if (g is not { } at || Filling) return;
        if (_draft == null && _end is { } e && Input.IsKeyPressed(Key.Shift) && ReliefAt(at)?.Id == e.Sheet)
            _overlay.SegPx(_overlay.Mesh, mat, e.At, at, 0.05f, 2f);
        _overlay.RibbonPx(_overlay.Mesh, mat, Ring(at, Math.Clamp(Cfg.ReliefBrush, SmallestBrush, LargestBrush)), 0.05f, 2f);
    }

    static List<Vec2> Ring(Vec2 c, double r)
    {
        var ring = new List<Vec2>();
        for (int i = 0; i <= RingSteps; i++)
        {
            double a = i * Math.Tau / RingSteps;
            ring.Add(new Vec2(c.X + Math.Cos(a) * r, c.Z + Math.Sin(a) * r));
        }
        return ring;
    }

    SheetDef? ReliefAt(Vec2 g) => Target(_c.S, _sheets, g);

    public void Resize(bool up)
    {
        Cfg.ReliefBrush = Math.Clamp(Cfg.ReliefBrush * (up ? 1.15f : 1 / 1.15f), SmallestBrush, LargestBrush);
        Cfg.Save();
    }
}
