using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Client.Prefs;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class Sighting
{
    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly PieceLayer _pieces;
    public Sighting(TableContext c, SheetLayer sheets, PieceLayer pieces)
    {
        _c = c;
        _sheets = sheets;
        _pieces = pieces;
    }

    Selection _selection = null!;
    ToolOverlay _overlay = null!;
    public void Connect(Selection selection, ToolOverlay overlay) { _selection = selection; _overlay = overlay; }

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    public Node3D Root = null!;
    ShaderMaterial _sightMat = null!, _shotMat = null!;
    MeshInstance3D _sightPlane = null!, _shotPlane = null!;
    CanvasLayer _lensLayer = null!;
    SightLens _liveLens = null!;
    SightLens? _shotLens, _cutter;
    ImageTexture? _seenTexture;
    Viewshed? _seenShown;
    int _looksShown;
    Texture2DArray? _eyesTexture;
    int _eyesLaid, _eyesShown = -1;
    Ground? _surveyed;
    double _shownSharp = 1;
    MeshInstance3D _outline = null!;
    ImmediateMesh _outlineMesh = null!;

    public SightingDraft? Draft;
    bool _drawing;
    List<Vec2>? _wall;

    public void BuildPlanes()
    {
        _sightMat = new ShaderMaterial { Shader = new Shader { Code = FileAccess.GetFileAsString("res://shaders/sighting.gdshader") } };
        _sightPlane =
            new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20000, 20000) }, MaterialOverride = _sightMat, Position = new Vector3(0, 0.02f, 0), Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer };
        Root.AddChild(_sightPlane);
        _shotMat = new ShaderMaterial { Shader = new Shader { Code = FileAccess.GetFileAsString("res://shaders/sighting_cut.gdshader") } };
        _shotPlane =
            new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20000, 20000) }, MaterialOverride = _shotMat, Position = new Vector3(0, 0.021f, 0), Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.PictureOnlyLayer };
        Root.AddChild(_shotPlane);
        _liveLens = new SightLens(_c.Rig.Cam, SightLens.NewGlass());
        _liveLens.Glass.SetShaderParameter("doublings", (float)SightingDraft.BlurDoublings);
        _lensLayer = new CanvasLayer { Layer = UnderTheSelectionGlow };
        _lensLayer.AddChild(_liveLens);
        Root.AddChild(_lensLayer);
    }

    const int UnderTheSelectionGlow = -2;

    IEnumerable<ShaderMaterial> Glasses => _shotLens is { } shot ? new[] { _liveLens.Glass, shot.Glass } : new[] { _liveLens.Glass };

    void SetBlurring(string what, Variant value)
    {
        foreach (var glass in Glasses) glass.SetShaderParameter(what, value);
    }

    public void BuildOutline()
    {
        _outlineMesh = new ImmediateMesh();
        _outline = new MeshInstance3D { Mesh = _outlineMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer };
        Root.AddChild(_outline);
    }

    public void TickShade()
    {
        _sightPlane.Visible = Draft is { FirstPerson: false } && _c.Rig.TopDown;
        bool blurs = Draft is { FirstPerson: false } && Math.Min(_shownSharp, Draft.Sharp) < 1;
        _liveLens.Visible = blurs && _c.Rig.TopDown;
        if (_shotLens is { } shot) shot.Visible = blurs;
        _shotPlane.Visible = Draft is { FirstPerson: true };
    }

    const double FocusPull = 14, FocusSettled = 0.0005;

    public void TickBlur(double delta)
    {
        if (Draft is not { FirstPerson: false } d || Math.Abs(_shownSharp - d.Sharp) < FocusSettled) return;
        _shownSharp += (d.Sharp - _shownSharp) * (1 - Math.Exp(-delta * FocusPull));
        if (Math.Abs(_shownSharp - d.Sharp) < FocusSettled) _shownSharp = d.Sharp;
        SetBlurring("sharp", (float)_shownSharp);
        RefreshLines();
    }

    public void SetSharp(double share)
    {
        if (Draft is not { } d) return;
        d.Sharp = Math.Clamp(share, 0, 1);
    }

    public void SetBlur(double share)
    {
        if (Draft is not { } d) return;
        d.Blur = Math.Clamp(share, SightingDraft.LeastBlur, SightingDraft.MostBlur);
        SetBlurring("most", (float)d.Blur);
    }

    void SnapFocus(SightingDraft d)
    {
        _shownSharp = d.Sharp;
        SetBlurring("sharp", (float)_shownSharp);
        SetBlurring("reach", (float)d.BlurReach);
        SetBlurring("square", BlurFrom(d));
        SetBlurring("most", (float)d.Blur);
    }

    const int BlurFromCentre = 0, BlurFromSquare = 1, BlurFromEdge = 2;

    static int BlurFrom(SightingDraft d) => d.Edged ? BlurFromEdge : d.Square ? BlurFromSquare : BlurFromCentre;

    public void Sync()
    {
        _tableChanged = true;
        ClearExtras();
        TickShade();
        _outlineMesh.ClearSurfaces();
        SyncText();
        if (Draft != null) ShowDraft();
    }

    void SyncText()
    {
        var cam = _c.Rig.Cam;
        bool hide = Draft is { HideText: true };
        cam.CullMask = hide ? cam.CullMask & ~Table.TextLayer : cam.CullMask | Table.TextLayer;
        _c.S.Art.MuteText(hide, _c.Scene);
    }

    public string Hint() => Draft is { Many: true } d ? $"What {d.Watchers.Count} pieces see together · {Steps()}" : Steps();

    string Steps() => Draft switch
    {
        { FirstPerson: true } => $"Drag to look around · N E S W in the panel hold a direction · {ReachKeys} set the reach · Enter captures",
        { Drawn: true } => $"Drag round what the piece sees · {ReachKeys} set the frame · the wheel zooms in to draw · Enter captures",
        { Surveyed: true } => $"The ground hides what the eye cannot see · drag to trace what else blocks the view · {ReachKeys} set the reach · Enter captures",
        _ => $"Drag to trace what blocks the view · {ReachKeys} set the reach · the wheel zooms in to trace · Enter captures",
    };

    string ReachKeys => $"Ctrl+wheel or {_c.Settings.Keys.Label("smaller")} {_c.Settings.Keys.Label("larger")}";

    void ClearExtras()
    {
        foreach (var c in Root.GetChildren()) if (c != _sightPlane && c != _shotPlane && c != _lensLayer && c != _outline) c.QueueFree();
    }

    void SetShape(SightingShape shape, Color shade)
    {
        TickShade();
        if (Draft is { } draft)
        {
            SetBlurring("reach", (float)draft.BlurReach);
            SetBlurring("square", BlurFrom(draft));
        }
        var c = Coords.ToWorld(shape.Center);
        var poly = shape.Polygon.Take(SightingDraft.MaxPoints).Select(p => { var w = Coords.ToWorld(p); return new Vector2(w.X, w.Z); }).ToArray();
        var polyArr = new Vector2[512];
        Array.Copy(poly, polyArr, poly.Length);
        var rows = _rows.Lay(poly);
        SetOutline(Rims(shape));
        var seen = Draft?.Seen;
        if (seen != null) ShowSeen(seen);
        var many = Draft is { Many: true } m ? m : null;
        if (many != null) ShowEyes(many);
        var spots = EyeSpots(many);
        foreach (var (mat, col) in new[] { (_sightMat, shade), (_shotMat, Veil), (_liveLens.Glass, shade), (_shotLens?.Glass, Veil), (_cutter?.Glass, Veil) })
        {
            if (mat == null) continue;
            mat.SetShaderParameter("seen_on", many != null ? (many.Surveyed ? 1 : 0) : seen != null ? 1 : 0);
            if (seen != null)
            {
                mat.SetShaderParameter("seen", _seenTexture!);
                mat.SetShaderParameter("seen_reach", (float)seen.Reach);
            }
            mat.SetShaderParameter("eyes_n", many?.Eyes.Count ?? 0);
            if (many != null) ShowEyesTo(mat, many, spots);
            mat.SetShaderParameter("kind", shape.Kind == "circle" ? 0 : 1);
            mat.SetShaderParameter("center", new Vector2(c.X, c.Z));
            mat.SetShaderParameter("radius", (float)shape.Radius);
            mat.SetShaderParameter("poly", polyArr);
            mat.SetShaderParameter("poly_n", poly.Length);
            mat.SetShaderParameter("sight_rows", rows);
            mat.SetShaderParameter("rows_from", _rows.From);
            mat.SetShaderParameter("shade", col);
        }
    }

    void ShowSeen(Viewshed seen)
    {
        if (_seenShown == seen && _looksShown == seen.Looks) return;
        var image = Image.CreateFromData(Viewshed.Bearings, Viewshed.Rings, false, Image.Format.R8, seen.Seen);
        if (_seenTexture == null) _seenTexture = ImageTexture.CreateFromImage(image);
        else _seenTexture.Update(image);
        (_seenShown, _looksShown) = (seen, seen.Looks);
    }

    void ShowEyes(SightingDraft d)
    {
        if (_eyesShown == _eyesLaid || d.Eyes.Count == 0) return;
        var layers = new Godot.Collections.Array<Image>();
        foreach (var eye in d.Eyes) layers.Add(Image.CreateFromData(Viewshed.Bearings, Viewshed.Rings, false, Image.Format.R8, eye.Seen));
        _eyesTexture ??= new Texture2DArray();
        _eyesTexture.CreateFromImages(layers);
        _eyesShown = _eyesLaid;
    }

    void ShowEyesTo(ShaderMaterial mat, SightingDraft d, Vector2[] spots)
    {
        mat.SetShaderParameter("eyes", spots);
        mat.SetShaderParameter("eyes_seen", _eyesTexture!);
        mat.SetShaderParameter("seen_reach", (float)d.SeenReach);
        mat.SetShaderParameter("eyes_drawn", d.Drawn ? 1 : 0);
    }

    static Vector2[] EyeSpots(SightingDraft? d)
    {
        var spots = new Vector2[SightEye.MostEyes];
        for (int i = 0; d != null && i < d.Eyes.Count && i < spots.Length; i++)
        {
            var w = Coords.ToWorld(d.Eyes[i].Center);
            spots[i] = new Vector2(w.X, w.Z);
        }
        return spots;
    }

    public void Recompute(SightingDraft d)
    {
        if (d.Many) SurveyEyes(d);
        else Survey(d);
        Func<Vec2, bool>? sees = d.Many ? (d.Drawn && !d.Surveyed ? null : d.EyesSee) : d.Seen is { } seen ? seen.Sees : null;
        d.Recompute(shape => UmpirePiecesIn(shape, sees));
    }

    void SurveyEyes(SightingDraft d)
    {
        d.Seen = null;
        d.Place(d.Watchers.Select(id => _c.S.View.Find(Layers.Umpire, id)).OfType<PieceInstance>().Select(p => (p.Id, p.Pos)).ToList());
        var ground = _c.S.Ground;
        foreach (var eye in d.Eyes)
        {
            if (!d.Surveyed) eye.Land = null;
            else
            {
                if (eye.Land is not { } land || !land.Fits(eye.Center, d.SeenReach) || _surveyed != ground) eye.Land = land = new Viewshed(eye.Center, d.SeenReach, ground.At);
                land.LookFrom(EyeOver(eye.Center, eye.Ids, d.WatchMetres, ground));
            }
            eye.Lay(d.SeenReach, ang => d.Farthest(eye.Center, ang));
        }
        if (d.Surveyed) _surveyed = ground;
        _eyesLaid++;
    }

    void Survey(SightingDraft d)
    {
        if (!d.Surveyed)
        {
            d.Seen = null;
            return;
        }
        var ground = _c.S.Ground;
        if (d.Seen is not { } seen || !seen.Fits(d.Center, d.SeenReach) || _surveyed != ground) d.Seen = seen = new Viewshed(d.Center, d.SeenReach, ground.At);
        _surveyed = ground;
        seen.LookFrom(EyeOver(d.Center, new[] { d.ObserverId }, d.WatchMetres, ground));
    }

    double EyeOver(Vec2 at, IEnumerable<string> ids, double watchMetres, Ground ground)
    {
        double stands = ground.At(at);
        foreach (var id in ids)
            if (_pieces.NodeOf(Layers.Umpire, id) is { } node) stands = Math.Max(stands, node.GlobalPosition.Y);
        var cal = _c.S.View.Calibration;
        return stands + cal.ToTableUnits(watchMetres * cal.PerMetre);
    }

    static readonly Color Veil = new(0.02f, 0.02f, 0.03f, 1f);
    readonly SightRows _rows = new();

    const int RingPoints = 180;

    List<List<Vec2>> Rims(SightingShape shape)
    {
        if (Draft is { Many: true, Drawn: false } d) return EyeRims(d);
        var ring = Reach(shape);
        return ring.Count >= 2 ? new() { ring.Append(ring[0]).ToList() } : new();
    }

    const double RimInside = 0.999;

    static List<List<Vec2>> EyeRims(SightingDraft d)
    {
        var rims = new List<List<Vec2>>();
        bool Inside(Vec2 p, SightEye other)
        {
            var o = p - other.Center;
            return (d.Square ? Math.Max(Math.Abs(o.X), Math.Abs(o.Z)) : o.Length) < d.Radius * RimInside;
        }
        foreach (var eye in d.Eyes)
        {
            List<Vec2>? rim = null;
            for (int i = 0; i <= RingPoints; i++)
            {
                double a = i * Math.Tau / RingPoints, far = d.Reach(a);
                var p = new Vec2(eye.Center.X + Math.Cos(a) * far, eye.Center.Z + Math.Sin(a) * far);
                if (d.Eyes.Any(o => o != eye && Inside(p, o))) rim = null;
                else (rim ??= Started(rims)).Add(p);
            }
        }
        return rims;
    }

    static List<Vec2> Started(List<List<Vec2>> rims)
    {
        rims.Add(new());
        return rims[^1];
    }

    static List<Vec2> Reach(SightingShape shape)
    {
        if (shape.Kind == "polygon") return shape.Polygon;
        var ring = new List<Vec2>(RingPoints);
        for (int i = 0; i < RingPoints; i++)
        {
            double a = i * Math.Tau / RingPoints;
            ring.Add(new Vec2(shape.Center.X + Math.Cos(a) * shape.Radius, shape.Center.Z + Math.Sin(a) * shape.Radius));
        }
        return ring;
    }

    void SetOutline(List<List<Vec2>> rims)
    {
        var overlay = _overlay;
        _outlineMesh.ClearSurfaces();
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.85f, 0.25f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        foreach (var rim in rims.Where(r => r.Count >= 2)) overlay.RibbonPx(_outlineMesh, mat, rim, 0.05f, 2f);
        if (Draft == null) return;
        if (Draft is { FirstPerson: false, Edged: false, Many: false } && _shownSharp < 1) FocusRing(Draft, _shownSharp);
        if (Draft.Drawn)
        {
            if (_wall is { Count: >= 2 }) overlay.RibbonPx(_outlineMesh, mat, _wall, 0.07f, 2f);
            return;
        }
        var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.3f, 0.2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        foreach (var w in Draft.Walls.Append(_wall!).Where(x => x is { Count: >= 2 }))
            overlay.RibbonPx(_outlineMesh, wallMat, w, 0.07f, 4.5f);
    }

    const int FocusTicks = 48, LongTickEvery = 6;
    const double FocusTickShare = 0.01, FocusLongTickShare = 0.022, FocusTurn = Math.PI * 1.5;
    const float FocusLinePx = 1f;
    static readonly Color FocusInk = new(0.93f, 0.9f, 0.82f, 0.45f);

    void FocusRing(SightingDraft d, double sharp)
    {
        double at = d.Radius * sharp;
        if (at <= 0) return;
        var mat = new StandardMaterial3D { AlbedoColor = FocusInk, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        Vec2 OnRing(double ang, double reach)
        {
            double c = Math.Cos(ang), s = Math.Sin(ang), scale = d.Square ? reach / Math.Max(1e-9, Math.Max(Math.Abs(c), Math.Abs(s))) : reach;
            return new Vec2(d.Center.X + c * scale, d.Center.Z + s * scale);
        }
        var ring = Enumerable.Range(0, RingPoints).Select(i => OnRing(i * Math.Tau / RingPoints, at)).ToList();
        _overlay.RibbonPx(_outlineMesh, mat, ring.Append(ring[0]).ToList(), 0.05f, FocusLinePx);
        double phase = sharp * FocusTurn;
        for (int i = 0; i < FocusTicks; i++)
        {
            double ang = phase + i * Math.Tau / FocusTicks, len = d.Radius * (i % LongTickEvery == 0 ? FocusLongTickShare : FocusTickShare);
            _overlay.SegPx(_outlineMesh, mat, OnRing(ang, at), OnRing(ang, Math.Min(d.Radius, at + len)), 0.05f, FocusLinePx);
        }
    }

    void RefreshLines()
    {
        if (Draft == null) return;
        SetOutline(Rims(Draft.Shape()));
    }

    public void ShowDraft()
    {
        if (Draft == null) return;
        ClearExtras();
        SyncText();
        SetShape(Draft.Shape(), new Color(0.05f, 0.05f, 0.15f, 0.35f));
    }

    void Redraw()
    {
        Recompute(Draft!);
        ShowDraft();
        NotifyChanged();
    }

    public void Press(Vec2 g)
    {
        if (Draft == null) return;
        _wall = new() { Draft.Clip(g) };
        _drawing = true;
    }

    public void Move(Vec2? g)
    {
        if (Draft != null && g != null && _drawing && _wall != null)
        {
            var q = Draft.Clip(g.Value);
            if ((q - _wall[^1]).Length > 0.02) { _wall.Add(q); RefreshLines(); }
        }
    }

    public void Release()
    {
        if (Draft == null || !_drawing) return;
        _drawing = false;
        if (_wall is { Count: >= 2 } && AddStroke(Draft, _wall)) _strokeRedo.Clear();
        _wall = null;
        Redraw();
    }

    bool AddStroke(SightingDraft d, List<Vec2> stroke)
    {
        double tolerance = Math.Max(d.Radius / 2000, 3.0 * Ui.Scale / Math.Max(0.01, _c.Rig.PixelsPerUnit()));
        if (d.Drawn) return d.AddOutline(stroke, tolerance);
        d.Walls.Add(Polyline.Thin(stroke, tolerance));
        return true;
    }

    public void SetFormat(SightingFormat format)
    {
        if (Draft is not { } d || d.Format == format) return;
        d.Format = format;
        _strokeRedo.Clear();
        _wall = null;
        _drawing = false;
        _c.Say(Hint());
        Redraw();
    }

    public void SetSurveyed(bool on)
    {
        if (Draft is not { } d || d.Surveyed == on) return;
        d.Surveyed = on;
        _c.Say(Hint());
        Redraw();
    }

    public void DropWall()
    {
        if (_drawing || _wall != null)
        {
            _drawing = false;
            _wall = null;
            RefreshLines();
        }
    }

    public bool Recording { get; set; }

    public void Aim()
    {
        if (Draft == null || Recording || _c.Tool != Tool.Sighting) return;
        var picked = _selection.Nodes.Select(n => n.Piece.Id).ToHashSet();
        if (picked.Count == 0 || picked.SetEquals(Draft.Watchers)) return;
        if (Draft.Walls.Count + Draft.Outlines.Count > 0) return;
        var was = Draft;
        Draft = null;
        Begin(was.Radius, was);
    }

    public bool Begin(double? radius = null) => Begin(radius, null);

    bool Begin(double? radius, SightingDraft? was)
    {
        var watchers = _selection.Nodes.ToList();
        var obs = watchers.FirstOrDefault();
        if (obs == null) { _c.Say("Pick the piece, or several, whose point of view it is, then press the tool again."); return false; }
        var next = NextVision;
        Draft = new SightingDraft
        {
            ObserverId = obs.Piece.Id, Watchers = watchers.Select(n => n.Piece.Id).ToList(), Center = obs.Piece.Pos, Radius = radius ?? next.Radius,
            Format = SightingDraft.FormatNamed(_c.Settings.SightingFormat), Surveyed = _c.Settings.SightingViewshed,
            FirstPerson = _c.Settings.SightingFirstPerson && watchers.Count == 1, Lens = SightingDraft.FitLens(_c.Settings.SightingLens),
            HideText = _c.Settings.SightingHideText, Gif = _c.Settings.SightingGif,
            EyeHeight = EyeOnTopOf(obs.Piece.Id) * _c.Settings.SightingEyeShare, Sharp = was?.Sharp ?? next.Sharp, Blur = was?.Blur ?? next.Blur,
            WatchMetres = SightingDraft.FitWatch(_c.Settings.SightingWatch),
        };
        SnapFocus(Draft);
        Recompute(Draft);
        ShowDraft();
        FrameOnDraft();
        FaceAsBefore();
        NotifyChanged();
        return true;
    }

    public readonly record struct Vision(double Radius, double Sharp, double Blur);

    const double FirstRadius = 40;

    Vision NextVision
    {
        get
        {
            var v = _c.S.View;
            return new Vision(v.VisionRadius > 0 ? v.VisionRadius : FirstRadius, v.VisionSharp ?? 1,
                v.VisionBlur is { } kept ? Math.Clamp(kept, SightingDraft.LeastBlur, SightingDraft.MostBlur) : SightingDraft.BlurAtEdge);
        }
    }

    public CaptureProfile Profile()
    {
        var cfg = _c.Settings;
        var vision = Draft is { } d ? new Vision(d.Radius, d.Sharp, d.Blur) : NextVision;
        return new CaptureProfile
        {
            SightingGif = Draft?.Gif ?? cfg.SightingGif, GifLarge = cfg.GifLarge,
            FirstPerson = Draft?.FirstPerson ?? cfg.SightingFirstPerson, Format = Draft is { } f ? SightingDraft.NameOf(f.Format) : cfg.SightingFormat,
            Viewshed = Draft?.Surveyed ?? cfg.SightingViewshed,
            HideText = Draft?.HideText ?? cfg.SightingHideText, Lens = Draft?.Lens ?? cfg.SightingLens, EyeShare = cfg.SightingEyeShare,
            Watch = Draft?.WatchMetres ?? cfg.SightingWatch,
            Radius = vision.Radius, Sharp = vision.Sharp, Blur = vision.Blur,
        };
    }

    public void Take(CaptureProfile p)
    {
        var cfg = _c.Settings;
        cfg.SightingGif = p.SightingGif;
        cfg.GifLarge = p.GifLarge;
        cfg.SightingFirstPerson = p.FirstPerson;
        cfg.SightingFormat = p.Format;
        cfg.SightingViewshed = p.Viewshed;
        cfg.SightingHideText = p.HideText;
        cfg.SightingLens = p.Lens;
        cfg.SightingEyeShare = p.EyeShare;
        cfg.SightingWatch = p.Watch;
        cfg.Save();
        if (Draft is not { } d || Recording)
        {
            NotifyChanged();
            return;
        }
        d.Gif = p.SightingGif;
        SetFormat(SightingDraft.FormatNamed(p.Format));
        SetFirstPerson(p.FirstPerson);
        SetHideText(p.HideText);
        SetLens(p.Lens);
        d.Radius = p.Radius;
        d.Sharp = Math.Clamp(p.Sharp, 0, 1);
        d.Blur = Math.Clamp(p.Blur, SightingDraft.LeastBlur, SightingDraft.MostBlur);
        d.EyeHeight = Math.Clamp(EyeOnTopOf(d.ObserverId) * p.EyeShare, 0, EyeHighest);
        d.WatchMetres = SightingDraft.FitWatch(p.Watch);
        d.Surveyed = p.Viewshed;
        CloseUp = false;
        Recompute(d);
        SnapFocus(d);
        ShowDraft();
        FrameOnDraft();
        NotifyChanged();
    }

    public bool CloseUp { get; private set; }

    public bool Looking => Draft is { FirstPerson: true };

    float Framing => Draft is { } d ? (float)(d.Frame * (d.Square || d.Many ? 2 * 1.42 : 2) * 1.12) : 0;

    const float EyeOverTopShare = 0.25f, EyeOverTopLeast = 0.2f, EyeOverTopMost = 3f;

    double EyeOnTopOf(string observerId)
    {
        var node = _pieces.NodeOf(Layers.Umpire, observerId);
        if (node == null) return EyeOverTopLeast;
        float over = Mathf.Clamp(Mathf.Max(node.Footprint.X, node.Footprint.Y) * EyeOverTopShare, EyeOverTopLeast, EyeOverTopMost);
        return node.Thickness + over;
    }

    public double EyeHighest => Draft is { } d ? EyeOnTopOf(d.ObserverId) * SightingDraft.EyeHighestShare : 0;

    public void SetEyeHeight(double height)
    {
        if (Draft is not { } d) return;
        d.EyeHeight = Math.Clamp(height, 0, EyeHighest);
        _c.Settings.SightingEyeShare = d.EyeHeight / EyeOnTopOf(d.ObserverId);
        FrameOnDraft();
    }

    public void SetWatch(double metres)
    {
        if (Draft is not { } d) return;
        d.WatchMetres = SightingDraft.FitWatch(metres);
        _c.Settings.SightingWatch = d.WatchMetres;
        if (d.Surveyed) Recount(d);
    }

    Vector3 EyeOf(SightingDraft d)
    {
        var node = _pieces.NodeOf(Layers.Umpire, d.ObserverId);
        float up = (float)d.EyeHeight;
        if (node == null) return Coords.ToWorld(d.Center, up);
        var frontEdge = new Vector3(node.GroundCentre.X, 0, node.GroundCentre.Y - node.Footprint.Y / 2).Rotated(Vector3.Up, Coords.Yaw(node.Piece.RotDeg));
        return node.TablePosition() + frontEdge + Vector3.Up * up;
    }

    float? FrontYaw(SightingDraft d) => _pieces.NodeOf(Layers.Umpire, d.ObserverId) is { } node ? Coords.Yaw(node.Piece.RotDeg) + Mathf.Pi : null;

    static float Lens(SightingDraft d) => (float)d.Lens;

    public void FrameOnDraft()
    {
        if (Draft is not { } d) return;
        var rig = _c.Rig;
        if (d.FirstPerson) { rig.LookFrom(EyeOf(d), Lens(d), FrontYaw(d)); return; }
        if (rig.Eye != null) rig.LeaveEye();
        float want = Framing;
        var at = Coords.ToWorld(d.Center);
        bool off = (rig.Target - at with { Y = 0 }).Length() > 0.001f;
        bool zoomed = Math.Abs(rig.Distance - Math.Clamp(want * 1.1f, rig.MinDistance, rig.MaxDistance)) > 0.01f;
        if (!off && !zoomed) return;
        if (!rig.TopDown) rig.ToggleTopDown();
        rig.Frame(at, want);
    }

    public void TickFraming()
    {
        var watcher = Draft != null ? _c.S.View.Find(Layers.Umpire, Draft.ObserverId) : null;
        if (watcher != null && !Draft!.Many) Draft.Center = watcher.Pos;
        else if (watcher == null) CloseUp = false;
        if (watcher != null && _c.Tool == Tool.Sighting) { if (!CloseUp) FrameOnDraft(); }
        else if (!_c.Rig.Walking) _c.Rig.LeaveEye();
    }

    public void SetFirstPerson(bool on)
    {
        if (Draft is not { } d || d.FirstPerson == on || on && d.Many) return;
        d.FirstPerson = on;
        d.Facing = null;
        CloseUp = false;
        FrameOnDraft();
        FaceAsBefore();
        _c.Say(Hint());
        NotifyChanged();
    }

    public void SetLens(double degrees)
    {
        if (Draft is not { } d || Math.Abs(d.Lens - SightingDraft.FitLens(degrees)) < 1e-6) return;
        d.Lens = SightingDraft.FitLens(degrees);
        FrameOnDraft();
        NotifyChanged();
    }

    public void SetHideText(bool on)
    {
        if (Draft is not { } d || d.HideText == on) return;
        d.HideText = on;
        SyncText();
        NotifyChanged();
    }

    static readonly (string cardinal, double bearing)[] Cardinals = { ("N", 0), ("E", 90), ("S", 180), ("W", 270) };

    public static IEnumerable<string> CardinalNames => Cardinals.Select(c => c.cardinal);

    public void Face(string cardinal)
    {
        int i = Array.FindIndex(Cardinals, c => c.cardinal == cardinal);
        if (Draft is not { FirstPerson: true } d || i < 0) return;
        d.Facing = cardinal;
        if (_c.Settings.SightingFacing != cardinal)
        {
            _c.Settings.SightingFacing = cardinal;
            _c.Settings.Save();
        }
        _c.Rig.Face(CameraRig.YawLooking(_c.S.View.Calibration.MapBearing(Cardinals[i].bearing)), CameraRig.EyeTiltRadians);
        NotifyChanged();
    }

    public void Look(double bearing)
    {
        if (Draft is not { FirstPerson: true } d) return;
        d.Facing = null;
        _c.Rig.Face(CameraRig.YawLooking(_c.S.View.Calibration.MapBearing(bearing)), CameraRig.EyeTiltRadians);
        NotifyChanged();
    }

    public void LookAhead()
    {
        if (Draft is not { FirstPerson: true } d || FrontYaw(d) is not { } front) return;
        d.Facing = null;
        _c.Rig.Face(front, CameraRig.EyeTiltRadians);
        NotifyChanged();
    }

    public void SetRadius(double radius)
    {
        if (Recording || Draft is not { } d) return;
        CloseUp = false;
        d.Radius = Math.Clamp(radius, NearestReach, FarthestReach);
        Recompute(d);
        ShowDraft();
        FrameOnDraft();
        NotifyChanged();
    }

    public bool Trace(List<Vec2> line)
    {
        if (Draft is not { } d || line.Count < 2 || !AddStroke(d, line.Select(d.Clip).ToList())) return false;
        _strokeRedo.Clear();
        Redraw();
        return true;
    }

    public const double NearestReach = 1, FarthestReach = 100_000;

    public void Unlock()
    {
        if (Draft is not { Facing: not null } d) return;
        d.Facing = null;
        _c.Settings.SightingFacing = null;
        _c.Settings.Save();
        NotifyChanged();
    }

    void FaceAsBefore()
    {
        if (_c.Settings.SightingFacing is { } cardinal) Face(cardinal);
    }

    public void Zoom(float f, Vector2 at)
    {
        var rig = _c.Rig;
        if (Looking) return;
        rig.Zoom(f, at);
        float shot = Mathf.Clamp(Framing * 1.1f, rig.MinDistance, rig.MaxDistance);
        CloseUp = rig.ZoomGoal < shot - 0.01f;
        if (!CloseUp) FrameOnDraft();
    }

    public void Reach(bool farther)
    {
        if (Recording || Draft == null) return;
        CloseUp = false;
        Draft.Radius = Math.Clamp(Draft.Radius * (farther ? 1.1 : 1 / 1.1), NearestReach, FarthestReach);
        Recompute(Draft);
        ShowDraft();
        FrameOnDraft();
        NotifyChanged();
    }

    void KeepRadius()
    {
        if (Draft is not { } d) return;
        var v = _c.S.View;
        bool same = Math.Abs(d.Radius - v.VisionRadius) < 1e-6 && Math.Abs(d.Sharp - (v.VisionSharp ?? 1)) < 1e-6 && Math.Abs(d.Blur - (v.VisionBlur ?? SightingDraft.BlurAtEdge)) < 1e-6;
        if (!same) _c.S.Do(new SetVisionRadius { Radius = d.Radius, Sharp = d.Sharp, Blur = d.Blur });
    }

    public void Cancel()
    {
        KeepRadius();
        Draft = null;
        _drawing = false;
        _strokeRedo.Clear();
        _pieces.ShowRings();
        _c.Rig.LeaveEye();
        Sync();
        NotifyChanged();
        if (_c.Tool == Tool.Sighting) _c.SetTool(Tool.Select);
    }

    public void End()
    {
        if (Draft == null) return;
        KeepRadius();
        _c.Say($"POV capture taken: {Draft.Included.Count} pieces in the picture — Ctrl+V into Discord.");
        _pieces.ShowRings();
        Draft = null;
        _strokeRedo.Clear();
        _c.Rig.LeaveEye();
        Sync();
        NotifyChanged();
        _c.SetTool(Tool.Select);
    }

    public void ClearOutline()
    {
        if (Draft == null) return;
        _strokeRedo.Clear();
        var strokes = Draft.Strokes;
        for (int i = strokes.Count - 1; i >= 0; i--) _strokeRedo.Add(strokes[i]);
        strokes.Clear();
        Draft.Polygon.Clear();
        _wall = null;
        _drawing = false;
        Redraw();
    }

    public int OutlineParts => _outlineMesh.GetSurfaceCount();
    public bool ShadeShown => _sightPlane.Visible;

    readonly List<List<Vec2>> _strokeRedo = new();
    public int StrokesDrawn => Draft?.Strokes.Count ?? 0;
    public int StrokesTakenBack => _strokeRedo.Count;

    public void UndoStroke()
    {
        if (Draft is not { Strokes: { Count: > 0 } strokes }) return;
        _strokeRedo.Add(strokes[^1]);
        strokes.RemoveAt(strokes.Count - 1);
        Redraw();
    }

    public void RedoStroke()
    {
        if (Draft == null || _strokeRedo.Count == 0) return;
        Draft.Strokes.Add(_strokeRedo[^1]);
        _strokeRedo.RemoveAt(_strokeRedo.Count - 1);
        Redraw();
    }

    const int FootprintSamples = 9;

    public IEnumerable<PieceInstance> UmpirePiecesIn(SightingShape shape, Func<Vec2, bool>? sees = null)
    {
        var (lo, hi) = Bounds(shape);
        bool InSight(Vec2 p) => shape.Contains(p) && sees?.Invoke(p) != false;
        return _c.S.View.Layer(Layers.Umpire).Where(p => InSight(p.Pos) || _pieces.NodeOf(Layers.Umpire, p.Id) is { } n && Reaches(n, shape, InSight, lo, hi));
    }

    static (Vec2 lo, Vec2 hi) Bounds(SightingShape shape)
    {
        if (shape.Kind == "circle" || shape.Polygon.Count == 0)
            return (shape.Center - new Vec2(shape.Radius, shape.Radius), shape.Center + new Vec2(shape.Radius, shape.Radius));
        return (new Vec2(shape.Polygon.Min(v => v.X), shape.Polygon.Min(v => v.Z)), new Vec2(shape.Polygon.Max(v => v.X), shape.Polygon.Max(v => v.Z)));
    }

    static bool Reaches(PieceNode n, SightingShape shape, Func<Vec2, bool> inSight, Vec2 lo, Vec2 hi)
    {
        var half = n.Footprint / 2;
        var middle = n.MiddleAt(n.Piece.Pos, n.Piece.RotDeg);
        double reach = half.Length(), x = middle.X, z = middle.Z;
        if (x + reach < lo.X || x - reach > hi.X || z + reach < lo.Z || z - reach > hi.Z) return false;
        if (shape.Kind != "circle" && shape.Polygon.Any(v => n.Contains(v) && inSight(v))) return true;
        for (int i = 0; i < FootprintSamples; i++)
            for (int j = 0; j < FootprintSamples; j++)
            {
                var local = new Vector2(half.X * (2f * i / (FootprintSamples - 1) - 1), half.Y * (2f * j / (FootprintSamples - 1) - 1));
                var p = Coords.FromLocal(local, middle, n.Piece.RotDeg);
                if (inSight(p) && n.Contains(p)) return true;
            }
        return false;
    }

    bool _tableChanged;
    SceneTree? _pictureTree;

    public event Action? Recounted;

    public void TableChanged() => _tableChanged = true;

    public void FollowTable()
    {
        if (_pictureTree != null || !_tableChanged) return;
        if (Draft is { } d) Recount(d);
        _tableChanged = false;
    }

    void Recount(SightingDraft d)
    {
        _tableChanged = false;
        if (!d.Many && _c.S.View.Find(Layers.Umpire, d.ObserverId) is { } watcher) d.Center = watcher.Pos;
        var before = d.Included;
        Recompute(d);
        if (d == Draft) ShowDraft();
        if (!d.Included.SetEquals(before)) Recounted?.Invoke();
    }

    public async System.Threading.Tasks.Task<Image?> Render(SightingDraft d, int px)
    {
        var shot = await OpenPicture(d, PictureSide(d, px));
        try
        {
            await DrawFrame(shot, d);
            var picture = shot.GetTexture().GetImage().GetRegion(PictureCut(shot, d));
            var seen = picture.GetUsedRect();
            return d.FirstPerson || !seen.HasArea() ? picture : picture.GetRegion(seen);
        }
        finally { ClosePicture(shot); }
    }

    const int MostPictureSide = 4096, MostZoomIn = 4, CutMarginPx = 2;

    static bool Cuts(SightingDraft d) => d is { Edged: true, FirstPerson: false };

    public int PictureSide(SightingDraft d, int px)
    {
        if (d.FirstPerson) return px;
        double across = Cuts(d) ? Math.Max(d.Polygon.Max(v => v.X) - d.Polygon.Min(v => v.X), d.Polygon.Max(v => v.Z) - d.Polygon.Min(v => v.Z)) : 2 * d.Radius;
        double zoom = Math.Clamp(2 * d.Frame / Math.Max(across, 1e-6), 1, MostZoomIn);
        return Math.Min((int)(px * zoom), Math.Max(px, MostPictureSide));
    }

    public Rect2I PictureCut(SubViewport shot, SightingDraft d)
    {
        var whole = new Rect2I(Vector2I.Zero, shot.Size);
        if (!Cuts(d) || shot.GetChildCount() == 0 || shot.GetChild(0) is not Camera3D cam) return whole;
        Aim(shot, d);
        var seen = d.Polygon.Select(p => cam.UnprojectPosition(WorldShift.ToGlobal(Coords.ToWorld(p)))).ToList();
        var lo = new Vector2I(Mathf.FloorToInt(seen.Min(s => s.X)) - CutMarginPx, Mathf.FloorToInt(seen.Min(s => s.Y)) - CutMarginPx);
        var hi = new Vector2I(Mathf.CeilToInt(seen.Max(s => s.X)) + CutMarginPx, Mathf.CeilToInt(seen.Max(s => s.Y)) + CutMarginPx);
        var cut = new Rect2I(lo, hi - lo).Intersection(whole);
        return cut.Size is { X: >= 2, Y: >= 2 } ? cut : whole;
    }

    public async System.Threading.Tasks.Task<SubViewport> OpenPicture(SightingDraft d, int px)
    {
        var shot = MakeShotViewport(d, px);
        try
        {
            _pictureTree = _c.Scene.GetTree();
            _pictureTree.NodeAdded += Entered;
            SnapFocus(d);
            TickShade();
            Compose(d);
            for (int i = 0; i < PictureWaitFrames; i++)
            {
                await _c.Scene.ToSignal(_pictureTree, SceneTree.SignalName.ProcessFrame);
                bool settling = i < AssetWaitFrames && _c.S.Assets.Busy;
                if (i >= 2 && !settling && !_c.S.Art.FacesPending) break;
            }
            return shot;
        }
        catch
        {
            ClosePicture(shot);
            throw;
        }
    }

    public async System.Threading.Tasks.Task DrawFrame(SubViewport shot, SightingDraft d)
    {
        await _c.Scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePreDraw);
        if (_tableChanged) Compose(d);
        Aim(shot, d);
        _shotLens?.Aim();
        await _c.Scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    void Compose(SightingDraft d)
    {
        Recount(d);
        _pieces.Lettering.Release();
        _pieces.Lettering.Hold(d.Included.Select(id => _pieces.NodeOf(Layers.Umpire, id)).Where(n => n != null)!);
        foreach (var n in _pieces.Nodes)
        {
            bool leftOut = n.Layer != Layers.Umpire || !d.Included.Contains(n.Piece.Id);
            if (LeftOut(n) != leftOut) LeaveOut(n, leftOut);
        }
    }

    readonly HashSet<PieceNode> _leftOut = new();

    public bool LeftOut(PieceNode n) => _leftOut.Contains(n);

    public void LeaveOut(PieceNode n, bool on)
    {
        if (on) _leftOut.Add(n);
        else _leftOut.Remove(n);
        foreach (var visual in n.FindChildren("*", nameof(VisualInstance3D), recursive: true, owned: false)) PutWithPiece((VisualInstance3D)visual, on);
    }

    static void PutWithPiece(VisualInstance3D visual, bool leftOut)
    {
        if ((visual.Layers & Table.LiveOnlyLayer) != 0) return;
        uint shown = visual is Label3D ? Table.TextLayer : Table.WorldLayer;
        visual.Layers = leftOut ? (visual.Layers & ~shown) | Table.OutOfPictureLayer : (visual.Layers | shown) & ~Table.OutOfPictureLayer;
    }

    void Entered(Node arrived)
    {
        if (!GodotObject.IsInstanceValid(_pieces.Root)) StopWatching();
        else if (arrived is PieceNode) _tableChanged = true;
        else if (arrived is VisualInstance3D visual && PieceOf(visual) is { } piece && LeftOut(piece)) PutWithPiece(visual, true);
    }

    void StopWatching()
    {
        if (_pictureTree != null) _pictureTree.NodeAdded -= Entered;
        _pictureTree = null;
    }

    PieceNode? PieceOf(Node visual)
    {
        PieceNode? piece = null;
        var up = visual.GetParent();
        while (up != null && up != _pieces.Root)
        {
            if (up is PieceNode p) piece = p;
            up = up.GetParent();
        }
        return up == null ? null : piece;
    }

    void Aim(SubViewport shot, SightingDraft d)
    {
        if (shot.GetChildCount() == 0 || shot.GetChild(0) is not Camera3D cam) return;
        if (d.FirstPerson) cam.GlobalTransform = _c.Rig.Cam.GlobalTransform;
        else
        {
            var centre = WorldShift.ToGlobal(Coords.ToWorld(d.Center));
            cam.GlobalPosition = centre + Vector3.Up * PictureCameraHeight;
            cam.LookAt(centre, Vector3.Forward);
        }
        cam.ForceUpdateTransform();
    }

    public void ClosePicture(SubViewport shot)
    {
        shot.QueueFree();
        _shotLens = _cutter = null;
        StopWatching();
        _pieces.Lettering.Release();
        foreach (var n in _pieces.Nodes.Concat(_leftOut).Distinct().Where(GodotObject.IsInstanceValid).ToList()) LeaveOut(n, false);
        _leftOut.Clear();
    }

    const int EyePictureWidth = 16, EyePictureHeight = 9;
    const float PictureCameraHeight = 120;
    const int AssetWaitFrames = 30, PictureWaitFrames = 600;

    SubViewport MakeShotViewport(SightingDraft d, int px)
    {
        var shot = new SubViewport
        {
            Size = d.FirstPerson ? new Vector2I(px * EyePictureWidth / EyePictureHeight, px) : new Vector2I(px, px), World3D = _c.Scene.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X, TransparentBg = !d.FirstPerson,
        };
        uint mask = Table.WorldLayer | Table.PictureOnlyLayer | (d.HideText ? 0 : Table.TextLayer);
        var live = _c.Rig.Cam;
        var cam = d.FirstPerson
            ? new Camera3D { Projection = Camera3D.ProjectionType.Perspective, Fov = Lens(d), KeepAspect = Camera3D.KeepAspectEnum.Width, Near = live.Near, Far = live.Far, CullMask = mask }
            : new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = (float)(d.Frame * 2), Near = 0.05f, Far = 4000, CullMask = mask };
        shot.AddChild(cam);
        if (!d.FirstPerson)
        {
            shot.AddChild(_shotLens = new SightLens(cam, (ShaderMaterial)_liveLens.Glass.Duplicate()));
            shot.AddChild(new BackBufferCopy { CopyMode = BackBufferCopy.CopyModeEnum.Viewport });
            shot.AddChild(_cutter = new SightLens(cam, SightLens.NewCutter()) { Visible = true });
        }
        _c.Scene.AddChild(shot);
        Aim(shot, d);
        return shot;
    }
}

static class SightingDraftOnTable
{
    public static void Recompute(this SightingDraft d, Table t) => t.Sighting.Recompute(d);
}
