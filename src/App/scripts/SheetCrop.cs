using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class CropTool
{
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly Gizmo _gizmo;
    public CropTool(TableContext c, ToolOverlay overlay, Gizmo gizmo) { _c = c; _overlay = overlay; _gizmo = gizmo; }

    public event Action? Changed;

    public string? SheetId { get; private set; }
    public FaceBox Rect { get; private set; } = new() { X = 0, Y = 0, W = 1, H = 1 };
    int _edge = -1;
    (Vec2 centre, double w, double h, double rot) _full;

    public bool Dragging => _edge >= 0;

    public void Begin(string sheetId)
    {
        var s = _c.S.View.Sheets.FirstOrDefault(x => x.Id == sheetId);
        if (s == null) return;
        SheetId = sheetId;
        var c = s.Crop ?? SheetGeometry.Whole;
        Rect = new FaceBox { X = c.X, Y = c.Y, W = c.W, H = c.H };
        var (centre, w, h) = SheetGeometry.Full(s);
        _full = (centre, w, h, s.RotDeg);
        _c.SetTool(Tool.Crop);
        Changed?.Invoke();
    }

    public void SetRect(FaceBox r)
    {
        const double min = 0.02;
        double x = Math.Clamp(r.X, 0, 1 - min), y = Math.Clamp(r.Y, 0, 1 - min);
        Rect = new FaceBox { X = x, Y = y, W = Math.Clamp(r.W, min, 1 - x), H = Math.Clamp(r.H, min, 1 - y) };
        Changed?.Invoke();
    }

    public void Apply()
    {
        if (SheetId == null) return;
        var r = Rect;
        bool whole = r.X <= 0.0005 && r.Y <= 0.0005 && r.W >= 0.999 && r.H >= 0.999;
        _c.S.Do(new CropSheet { Id = SheetId, Crop = whole ? null : r });
        _c.SetTool(Tool.Select);
    }

    public void End()
    {
        if (SheetId == null) return;
        SheetId = null; _edge = -1;
        Changed?.Invoke();
    }

    public Vector3 World(double u, double v)
    {
        var off = SheetGeometry.Offset((u - 0.5) * _full.w, (v - 0.5) * _full.h, _full.rot);
        return Coords.ToWorld(_full.centre + off, 0.08f);
    }

    (double u, double v) Frac(Vec2 g)
    {
        var l = Coords.ToLocal(g, _full.centre, _full.rot);
        return (l.X / _full.w + 0.5, l.Y / _full.h + 0.5);
    }

    IEnumerable<(double u, double v)> Knobs()
    {
        var r = Rect;
        yield return (r.X, r.Y + r.H / 2);
        yield return (r.X + r.W / 2, r.Y);
        yield return (r.X + r.W, r.Y + r.H / 2);
        yield return (r.X + r.W / 2, r.Y + r.H);
    }

    public void Draw()
    {
        if (_c.Tool != Tool.Crop || SheetId == null) return;
        var mesh = _overlay.Mesh;
        var r = Rect;
        double x1 = r.X + r.W, y1 = r.Y + r.H;
        var veil = new StandardMaterial3D { AlbedoColor = new Color(0, 0, 0, 0.5f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = true };
        void Quad(double u0, double v0, double u1, double v1)
        {
            if (u1 - u0 <= 1e-6 || v1 - v0 <= 1e-6) return;
            mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, veil);
            Vector3 a = World(u0, v0), b = World(u1, v0), c = World(u1, v1), d = World(u0, v1);
            foreach (var p in new[] { a, b, c, a, c, d }) mesh.SurfaceAddVertex(p);
            mesh.SurfaceEnd();
        }
        Quad(0, 0, 1, r.Y); Quad(0, y1, 1, 1); Quad(0, r.Y, r.X, y1); Quad(x1, r.Y, 1, y1);
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.93f, 0.22f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _overlay.RibbonPx(mesh, mat,
            new[] { (r.X, r.Y), (x1, r.Y), (x1, y1), (r.X, y1), (r.X, r.Y) }.Select(c => Coords.FromWorld(World(c.Item1, c.Item2))).ToList(), 0.08f);
        float k = _gizmo.KnobSize();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        foreach (var (u, v) in Knobs())
        {
            var p = World(u, v);
            foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, -1), (1, 1), (-1, 1) })
                mesh.SurfaceAddVertex(p + new Vector3(dx * k * 0.9f, 0, dz * k * 0.9f));
        }
        mesh.SurfaceEnd();
    }

    int KnobAt(Vector2 screen)
    {
        int i = 0;
        foreach (var (u, v) in Knobs())
        {
            if (_c.Rig.Cam.UnprojectPosition(World(u, v)).DistanceTo(screen) < 14) return i;
            i++;
        }
        return -1;
    }

    public void Press(Vector2 screen) => _edge = KnobAt(screen);

    public void Drag(Vec2 g)
    {
        if (_edge < 0) return;
        var (u, v) = Frac(g);
        var r = Rect;
        double x1 = r.X + r.W, y1 = r.Y + r.H;
        const double min = 0.02;
        switch (_edge)
        {
            case 0: { double x = Math.Clamp(u, 0, x1 - min); SetRect(new FaceBox { X = x, Y = r.Y, W = x1 - x, H = r.H }); break; }
            case 1: { double y = Math.Clamp(v, 0, y1 - min); SetRect(new FaceBox { X = r.X, Y = y, W = r.W, H = y1 - y }); break; }
            case 2: SetRect(new FaceBox { X = r.X, Y = r.Y, W = Math.Clamp(u, r.X + min, 1) - r.X, H = r.H }); break;
            case 3: SetRect(new FaceBox { X = r.X, Y = r.Y, W = r.W, H = Math.Clamp(v, r.Y + min, 1) - r.Y }); break;
        }
    }

    public void Release() => _edge = -1;
}
