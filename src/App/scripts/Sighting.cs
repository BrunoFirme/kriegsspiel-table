using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
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
    }

    public void BuildOutline()
    {
        _outlineMesh = new ImmediateMesh();
        _outline = new MeshInstance3D { Mesh = _outlineMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer };
        Root.AddChild(_outline);
    }

    public void TickRim()
    {
        if (_sightPlane.Visible) _sightMat.SetShaderParameter("edge_w", SightEdge());
    }

    public void Sync()
    {
        ClearExtras();
        _sightPlane.Visible = _shotPlane.Visible = false;
        _outlineMesh.ClearSurfaces();
        if (Draft != null) ShowDraft();
    }

    void ClearExtras()
    {
        foreach (var c in Root.GetChildren()) if (c != _sightPlane && c != _shotPlane && c != _outline) c.QueueFree();
    }

    void SetShape(SightingShape shape, Color shade)
    {
        _sightPlane.Visible = true;
        _shotPlane.Visible = true;
        var c = Coords.ToWorld(shape.Center);
        var poly = shape.Polygon.Take(SightingDraft.MaxPoints).Select(p => { var w = Coords.ToWorld(p); return new Vector2(w.X, w.Z); }).ToArray();
        var polyArr = new Vector2[512];
        Array.Copy(poly, polyArr, poly.Length);
        SetOutline(shape.Kind == "polygon" ? shape.Polygon : null);
        foreach (var (mat, col, rim) in new[] { (_sightMat, shade, SightEdge()), (_shotMat, new Color(0.02f, 0.02f, 0.03f, 1f), 0f) })
        {
            mat.SetShaderParameter("kind", shape.Kind == "circle" ? 0 : 1);
            mat.SetShaderParameter("center", new Vector2(c.X, c.Z));
            mat.SetShaderParameter("radius", (float)shape.Radius);
            mat.SetShaderParameter("poly", polyArr);
            mat.SetShaderParameter("poly_n", poly.Length);
            mat.SetShaderParameter("dabs", new Vector4[96]);
            mat.SetShaderParameter("dab_n", 0);
            mat.SetShaderParameter("shade", col);
            mat.SetShaderParameter("edge_w", rim);
        }
    }

    void SetOutline(List<Vec2>? pts)
    {
        var overlay = _overlay;
        _outlineMesh.ClearSurfaces();
        if (pts is { Count: >= 2 })
        {
            var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.85f, 0.25f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
            overlay.RibbonPx(_outlineMesh, mat, pts.Append(pts[0]).ToList(), 0.05f, 2f);
        }
        if (Draft == null) return;
        var wallMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.3f, 0.2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        foreach (var w in Draft.Walls.Append(_wall!).Where(x => x is { Count: >= 2 }))
            overlay.RibbonPx(_outlineMesh, wallMat, w, 0.07f, 4.5f);
    }

    void RefreshLines()
    {
        if (Draft == null) return;
        var shape = Draft.Shape();
        SetOutline(shape.Kind == "polygon" ? shape.Polygon : null);
    }

    float SightEdge() => Mathf.Max(0.08f, 2.0f / Mathf.Max(0.01f, _c.Rig.PixelsPerUnit()));

    public void ShowDraft()
    {
        if (Draft == null) return;
        ClearExtras();
        SetShape(Draft.Shape(), new Color(0.05f, 0.05f, 0.15f, 0.35f));
    }

    void Redraw()
    {
        Draft!.Recompute(UmpirePiecesIn);
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
        if (_wall is { Count: >= 2 }) { Draft.Walls.Add(Polyline.Thin(_wall, Math.Max(Draft.Radius / 2000, 3.0 / Math.Max(0.01, _c.Rig.PixelsPerUnit())))); _wallRedo.Clear(); }
        _wall = null;
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

    public void Aim()
    {
        if (Draft == null || _c.Tool != Tool.Sighting) return;
        if (_selection.Ids.Count != 1) return;
        var obs = _selection.Nodes.FirstOrDefault();
        if (obs == null || obs.Piece.Id == Draft.ObserverId) return;
        if (Draft.Walls.Count > 0) return;
        double radius = Draft.Radius;
        Draft = null;
        Begin(radius);
    }

    public bool Begin(double? radius = null)
    {
        var obs = _selection.Nodes.FirstOrDefault();
        if (obs == null) { _c.Say("Pick the piece whose point of view it is, then press the tool again."); return false; }
        Draft = new SightingDraft
        {
            ObserverId = obs.Piece.Id, Center = obs.Piece.Pos, Radius = radius ?? (_c.S.View.VisionRadius > 0 ? _c.S.View.VisionRadius : 40),
            Square = _c.Settings.SightingSquare,
        };
        Draft.Recompute(UmpirePiecesIn);
        ShowDraft();
        FrameOnDraft();
        NotifyChanged();
        return true;
    }

    public bool CloseUp { get; private set; }

    float Framing => Draft is { } d ? (float)(d.Radius * (d.Square ? 2 * 1.42 : 2) * 1.12) : 0;

    public void FrameOnDraft()
    {
        if (Draft is not { } d) return;
        var rig = _c.Rig;
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
        if (Draft != null && _c.S.View.Find(Layers.Umpire, Draft.ObserverId) is { } watcher)
        {
            Draft.Center = watcher.Pos;
            if (!CloseUp) FrameOnDraft();
        }
        else CloseUp = false;
    }

    public void Wheel(float f, Vector2 at, bool alt)
    {
        var rig = _c.Rig;
        if (alt)
        {
            rig.Zoom(f, at);
            float shot = Mathf.Clamp(Framing * 1.1f, rig.MinDistance, rig.MaxDistance);
            CloseUp = rig.ZoomGoal < shot - 0.01f;
            if (!CloseUp) FrameOnDraft();
            return;
        }
        CloseUp = false;
        Draft!.Radius = Math.Clamp(Draft.Radius * (f < 1 ? 1 / 1.1 : 1.1), 1, 100_000);
        Draft.Recompute(UmpirePiecesIn);
        ShowDraft();
        FrameOnDraft();
        NotifyChanged();
    }

    void KeepRadius()
    {
        if (Draft != null && Math.Abs(Draft.Radius - _c.S.View.VisionRadius) > 1e-6) _c.S.Do(new SetVisionRadius { Radius = Draft.Radius });
    }

    public void Cancel()
    {
        KeepRadius();
        Draft = null;
        _drawing = false;
        _wallRedo.Clear();
        _pieces.ShowRings();
        Sync();
        NotifyChanged();
        if (_c.Tool == Tool.Sighting) _c.SetTool(Tool.Select);
    }

    public void End()
    {
        if (Draft == null) return;
        KeepRadius();
        _c.Say($"POV screenshot taken: {Draft.Included.Count} pieces in the picture — Ctrl+V into Discord.");
        _pieces.ShowRings();
        Draft = null;
        _wallRedo.Clear();
        Sync();
        NotifyChanged();
        _c.SetTool(Tool.Select);
    }

    public void ClearOutline()
    {
        if (Draft == null) return;
        _wallRedo.Clear();
        for (int i = Draft.Walls.Count - 1; i >= 0; i--) _wallRedo.Add(Draft.Walls[i]);
        Draft.Walls.Clear();
        Draft.Polygon.Clear();
        _wall = null;
        _drawing = false;
        Redraw();
    }

    readonly List<List<Vec2>> _wallRedo = new();
    public int WallsTraced => Draft?.Walls.Count ?? 0;
    public int WallsTakenBack => _wallRedo.Count;

    public void UndoWall()
    {
        if (Draft == null || Draft.Walls.Count == 0) return;
        _wallRedo.Add(Draft.Walls[^1]);
        Draft.Walls.RemoveAt(Draft.Walls.Count - 1);
        Redraw();
    }

    public void RedoWall()
    {
        if (Draft == null || _wallRedo.Count == 0) return;
        Draft.Walls.Add(_wallRedo[^1]);
        _wallRedo.RemoveAt(_wallRedo.Count - 1);
        Redraw();
    }

    const int FootprintSamples = 9;

    public IEnumerable<PieceInstance> UmpirePiecesIn(SightingShape shape)
    {
        var (lo, hi) = Bounds(shape);
        return _c.S.View.Layer(Layers.Umpire).Where(p => shape.Contains(p.Pos) || _pieces.NodeOf(Layers.Umpire, p.Id) is { } n && Reaches(n, shape, lo, hi));
    }

    static (Vec2 lo, Vec2 hi) Bounds(SightingShape shape)
    {
        if (shape.Kind == "circle" || shape.Polygon.Count == 0)
            return (shape.Center - new Vec2(shape.Radius, shape.Radius), shape.Center + new Vec2(shape.Radius, shape.Radius));
        return (new Vec2(shape.Polygon.Min(v => v.X), shape.Polygon.Min(v => v.Z)), new Vec2(shape.Polygon.Max(v => v.X), shape.Polygon.Max(v => v.Z)));
    }

    static bool Reaches(PieceNode n, SightingShape shape, Vec2 lo, Vec2 hi)
    {
        var half = n.Footprint / 2;
        double reach = half.Length(), x = n.Piece.Pos.X, z = n.Piece.Pos.Z;
        if (x + reach < lo.X || x - reach > hi.X || z + reach < lo.Z || z - reach > hi.Z) return false;
        if (shape.Kind != "circle" && shape.Polygon.Any(v => n.Contains(v) && shape.Contains(v))) return true;
        for (int i = 0; i < FootprintSamples; i++)
            for (int j = 0; j < FootprintSamples; j++)
            {
                var local = new Vector2(half.X * (2f * i / (FootprintSamples - 1) - 1), half.Y * (2f * j / (FootprintSamples - 1) - 1));
                var p = Coords.FromLocal(local, n.Piece.Pos, n.Piece.RotDeg);
                if (shape.Contains(p) && n.Contains(p)) return true;
            }
        return false;
    }

    public async System.Threading.Tasks.Task<Image?> Render(SightingDraft d, int px)
    {
        var shot = MakeShotViewport(d, px);
        PrepareForPicture(d);
        for (int i = 0; i < 30; i++)
        {
            await _c.Scene.ToSignal(_c.Scene.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (i >= 2 && !_c.S.Assets.Busy) break;
        }
        try
        {
            await _c.Scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await _c.Scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return shot.GetTexture().GetImage();
        }
        finally
        {
            shot.QueueFree();
            _pieces.Lettering.Release();
            _shotPlane.Position = new Vector3(0, 0.021f, 0);
            foreach (var n in _pieces.Nodes) n.HideFromPicture(false);
        }
    }

    SubViewport MakeShotViewport(SightingDraft d, int px)
    {
        var centre = Coords.ToWorld(d.Center);
        var shot = new SubViewport
        {
            Size = new Vector2I(px, px), World3D = _c.Scene.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X, TransparentBg = false,
        };
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Size = (float)(d.Radius * 2),
            Near = 0.05f, Far = 4000, CullMask = Table.WorldLayer | Table.PictureOnlyLayer,
        };
        shot.AddChild(cam);
        _c.Scene.AddChild(shot);
        cam.GlobalPosition = centre + Vector3.Up * 120;
        cam.LookAt(centre, Vector3.Forward);
        return shot;
    }

    void PrepareForPicture(SightingDraft d)
    {
        _pieces.Lettering.Hold(d.Included.Select(id => _pieces.NodeOf(Layers.Umpire, id)).Where(n => n != null)!);
        foreach (var n in _pieces.Nodes)
            n.HideFromPicture(n.Layer != Layers.Umpire || !d.Included.Contains(n.Piece.Id));
        _shotPlane.Position = new Vector3(0, 60, 0);
    }
}

static class SightingDraftOnTable
{
    public static void Recompute(this SightingDraft d, Table t) => d.Recompute(t.UmpirePiecesIn);
}
