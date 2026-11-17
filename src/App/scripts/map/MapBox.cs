using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

readonly record struct MapArea(Vec2 Centre, double Wide, double Deep)
{
    public double Left => Centre.X - Wide / 2;
    public double Right => Centre.X + Wide / 2;
    public double Bottom => Centre.Z - Deep / 2;
    public double Top => Centre.Z + Deep / 2;

    public bool Holds(Vec2 p) => Reaches(p, 0);

    public bool Reaches(Vec2 p, double by) => p.X >= Left - by && p.X <= Right + by && p.Z >= Bottom - by && p.Z <= Top + by;

    public static MapArea Between(double left, double right, double bottom, double top) =>
        new(new Vec2((left + right) / 2, (bottom + top) / 2), Math.Abs(right - left), Math.Abs(top - bottom));

    public MapArea Fitted() => new(
        new Vec2(Math.Clamp(Centre.X, -MapBox.Farthest, MapBox.Farthest), Math.Clamp(Centre.Z, -MapBox.Farthest, MapBox.Farthest)),
        Math.Clamp(Wide, MapBox.LeastSide, MapBox.MostSide), Math.Clamp(Deep, MapBox.LeastSide, MapBox.MostSide));

    public bool Sound => double.IsFinite(Centre.X) && double.IsFinite(Centre.Z) && double.IsFinite(Wide) && double.IsFinite(Deep) && Wide > 0 && Deep > 0;
}

sealed class MapBox
{
    public const double LeastSide = MapView.MinSize, MostSide = MapView.MaxSize, Farthest = 100000;
    public const int LeastPixels = MapView.MinPixels, MostPixels = MapView.MaxPixels, UsualPixels = 2048;
    const float KnobReach = 14, Lift = 0.08f, KnobShare = 0.9f;
    const int KeptNumbers = 5;
    static readonly Color Frame = new(1, 0.93f, 0.22f);

    enum Grip { None, West, South, East, North, Whole, Laying }

    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly Gizmo _gizmo;

    public MapBox(TableContext c, ToolOverlay overlay, Gizmo gizmo)
    {
        _c = c;
        _overlay = overlay;
        _gizmo = gizmo;
        overlay.Drawing += Draw;
    }

    public event Action? Changed;

    MapArea? _area, _was;
    int _pixels = UsualPixels;
    bool _read;
    string? _readFor;
    Grip _grip;
    Vec2 _from;

    string? Scenario => _c.S.View.ScenarioId is { Length: > 0 } id ? id : null;

    public bool On => _c.Tool == Tool.Capture && _c.Settings.CaptureBoxed;
    public bool Dragging => _grip != Grip.None;

    public MapArea? Area
    {
        get
        {
            Read();
            return _area;
        }
    }

    public int Pixels
    {
        get
        {
            Read();
            return _pixels;
        }
    }

    public static int FitPixels(double pixels) => (int)Math.Clamp(Math.Round(pixels), LeastPixels, MostPixels);

    void Read()
    {
        if (_read && _readFor == Scenario) return;
        bool moved = _read;
        _read = true;
        _readFor = Scenario;
        if (_readFor is { } key && _c.Settings.CaptureBoxes.TryGetValue(key, out var kept) && kept.Length >= KeptNumbers && kept.All(double.IsFinite)
            && new MapArea(new Vec2(kept[0], kept[1]), kept[2], kept[3]) is { Sound: true } area)
        {
            _area = area.Fitted();
            _pixels = FitPixels(kept[4]);
        }
        else if (moved) _area = null;
    }

    public void Set(MapArea area)
    {
        Read();
        if (!area.Sound) return;
        _area = area.Fitted();
        Changed?.Invoke();
    }

    public void SetPixels(int pixels)
    {
        Read();
        _pixels = FitPixels(pixels);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Read();
        _area = null;
        _grip = Grip.None;
        Keep();
        Changed?.Invoke();
    }

    public void Keep()
    {
        if (Scenario is not { } key) return;
        if (Area is { } a) _c.Settings.CaptureBoxes[key] = new[] { a.Centre.X, a.Centre.Z, a.Wide, a.Deep, _pixels };
        else _c.Settings.CaptureBoxes.Remove(key);
        _c.Settings.Save();
    }

    static IEnumerable<(Grip grip, Vec2 at)> Knobs(MapArea a)
    {
        yield return (Grip.West, new Vec2(a.Left, a.Centre.Z));
        yield return (Grip.South, new Vec2(a.Centre.X, a.Bottom));
        yield return (Grip.East, new Vec2(a.Right, a.Centre.Z));
        yield return (Grip.North, new Vec2(a.Centre.X, a.Top));
    }

    Vector3 World(Vec2 p) => Coords.ToWorld(p, _c.SheetsTop + Lift + (float)_c.S.Ground.At(p));

    Grip KnobAt(MapArea a, Vector2 screen)
    {
        foreach (var (grip, at) in Knobs(a))
            if (_c.Rig.Screen(World(at)).DistanceTo(screen) < Ui.Pxf(KnobReach)) return grip;
        return Grip.None;
    }

    public void Press(Vector2 screen, Vec2 g)
    {
        _was = Area;
        _from = g;
        _grip = _was is not { } box ? Grip.Laying : KnobAt(box, screen) is var knob and not Grip.None ? knob : box.Holds(g) ? Grip.Whole : Grip.Laying;
    }

    public void Drag(Vec2 g)
    {
        if (_grip == Grip.Laying)
        {
            Set(MapArea.Between(_from.X, g.X, _from.Z, g.Z));
            return;
        }
        if (_area is not { } a || _was is not { } was) return;
        switch (_grip)
        {
            case Grip.Whole:
                Set(was with { Centre = was.Centre + (g - _from) });
                break;
            case Grip.West:
                Set(MapArea.Between(Math.Clamp(g.X, a.Right - MostSide, a.Right - LeastSide), a.Right, a.Bottom, a.Top));
                break;
            case Grip.East:
                Set(MapArea.Between(a.Left, Math.Clamp(g.X, a.Left + LeastSide, a.Left + MostSide), a.Bottom, a.Top));
                break;
            case Grip.South:
                Set(MapArea.Between(a.Left, a.Right, Math.Clamp(g.Z, a.Top - MostSide, a.Top - LeastSide), a.Top));
                break;
            case Grip.North:
                Set(MapArea.Between(a.Left, a.Right, a.Bottom, Math.Clamp(g.Z, a.Bottom + LeastSide, a.Bottom + MostSide)));
                break;
        }
    }

    public void Release(bool moved)
    {
        if (_grip == Grip.None) return;
        if (_grip == Grip.Laying && !moved && _area != _was)
        {
            _area = _was;
            Changed?.Invoke();
        }
        _grip = Grip.None;
        Keep();
    }

    void Draw()
    {
        if (!On || Area is not { } a) return;
        var mesh = _overlay.Mesh;
        var mat = new StandardMaterial3D { AlbedoColor = Frame, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _overlay.RibbonPx(mesh, mat,
            new List<Vec2> { new(a.Left, a.Bottom), new(a.Right, a.Bottom), new(a.Right, a.Top), new(a.Left, a.Top), new(a.Left, a.Bottom) }, Lift);
        float k = _gizmo.KnobSize() * KnobShare;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        foreach (var (_, at) in Knobs(a))
        {
            var p = World(at);
            foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, -1), (1, 1), (-1, 1) })
                mesh.SurfaceAddVertex(p + new Vector3(dx * k, 0, dz * k));
        }
        mesh.SurfaceEnd();
    }
}
