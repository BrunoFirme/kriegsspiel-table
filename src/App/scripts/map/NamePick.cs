using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

enum NameGrip { None, Body, Corner, Knob }

sealed class NameFrame
{
    public const float PadPx = 5, PickPx = 6, GhostPx = 1.5f;
    const float LinePx = 2, CasingPx = 2, CornerPx = 9, KnobPx = 8, StalkPx = 22, GrabSlackPx = 5;
    const int KnobSteps = 20;
    readonly TableContext _c;
    readonly ToolOverlay _overlay;

    public NameFrame(TableContext c, ToolOverlay overlay)
    {
        _c = c;
        _overlay = overlay;
    }

    static StandardMaterial3D Flat(Color ink, int over) =>
        new()
        {
            AlbedoColor = ink, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = over,
        };

    float Px(float pixels) => _overlay.Px(pixels);

    double Pad => Px(PadPx);

    public Vec2 Knob(MapLettering.NameBox box) => box.At(0, box.HalfTall + Pad + Px(StalkPx));

    public List<Vec2> Corners(MapLettering.NameBox box) => box.Corners(Pad);

    Vec2 Stalk(MapLettering.NameBox box) => box.At(0, box.HalfTall + Pad);

    public Vector2 Screen(Vec2 p) => _c.Rig.Cam.UnprojectPosition(Coords.ToWorld(p, _c.SheetsTop + (float)_c.S.Ground.At(p)));

    public void Outline(MapLettering.NameBox box)
    {
        var ring = box.Corners(Pad);
        ring.Add(ring[0]);
        _overlay.RibbonPx(_overlay.Mesh, Flat(SelectionAura.Picked, 1), ring, _overlay.Lift, GhostPx);
    }

    public void Picked(MapLettering.NameBox box, bool handles)
    {
        var ring = box.Corners(Pad);
        ring.Add(ring[0]);
        var casing = Flat(SelectionAura.Casing, 0);
        var ink = Flat(SelectionAura.Picked, 1);
        float y = _overlay.Lift;
        _overlay.RibbonPx(_overlay.Mesh, casing, ring, y, LinePx + CasingPx * 2);
        if (handles) Handles(casing, box, CasingPx * 2, y);
        _overlay.RibbonPx(_overlay.Mesh, ink, ring, y, LinePx);
        if (handles) Handles(ink, box, 0, y);
    }

    void Handles(StandardMaterial3D mat, MapLettering.NameBox box, float grow, float y)
    {
        _overlay.SegPx(_overlay.Mesh, mat, Stalk(box), Knob(box), y, LinePx + grow);
        foreach (var corner in box.Corners(Pad)) Square(mat, box, corner, CornerPx + grow, y);
        Disc(mat, Knob(box), KnobPx + grow / 2, y);
    }

    void Square(StandardMaterial3D mat, MapLettering.NameBox box, Vec2 at, float px, float y)
    {
        double half = Px(px) / 2;
        _overlay.SegPx(_overlay.Mesh, mat, new Vec2(at.X - box.Along.X * half, at.Z - box.Along.Z * half), new Vec2(at.X + box.Along.X * half, at.Z + box.Along.Z * half), y, px);
    }

    void Disc(StandardMaterial3D mat, Vec2 at, float radiusPx, float y)
    {
        double r = Px(radiusPx) / 2;
        var ring = new List<Vec2>(KnobSteps + 2);
        for (int i = 0; i <= KnobSteps + 1; i++)
        {
            double a = i * Math.Tau / KnobSteps;
            ring.Add(new Vec2(at.X + Math.Cos(a) * r, at.Z + Math.Sin(a) * r));
        }
        _overlay.RibbonPx(_overlay.Mesh, mat, ring, y, radiusPx);
    }

    public NameGrip At(MapLettering.NameBox box, Vector2 screen, Vec2 g)
    {
        if ((Screen(Knob(box)) - screen).Length() <= Ui.Pxf(KnobPx + GrabSlackPx)) return NameGrip.Knob;
        if (box.Corners(Pad).Any(c => (Screen(c) - screen).Length() <= Ui.Pxf(CornerPx / 2 + GrabSlackPx))) return NameGrip.Corner;
        return box.Holds(g, Pad) ? NameGrip.Body : NameGrip.None;
    }
}

sealed class NameGesture
{
    const float MovePx = 4;
    const double TurnSnap = 15, FineSnap = 1, LeastReach = 1e-6;
    public readonly NameGrip Grip;
    public readonly Stroke Start;
    readonly Vec2 _from, _centre;
    readonly Vector2 _screen;
    readonly double _letter;

    public NameGesture(NameGrip grip, Stroke start, MapLettering.NameBox box, Vec2 from, Vector2 screen, double letter)
    {
        Grip = grip;
        Start = start;
        _from = from;
        _centre = box.Centre;
        _screen = screen;
        _letter = letter;
    }

    public bool Moved { get; private set; }

    public Stroke? To(Vec2 g, Vector2 screen, bool snap, bool fine)
    {
        if (!Moved && (screen - _screen).Length() < Ui.Pxf(MovePx)) return null;
        Moved = true;
        return Grip switch
        {
            NameGrip.Body => NameEdits.Moved(Start, g - _from),
            NameGrip.Corner => NameEdits.Sized(Start, _centre, Reach(g) / Math.Max(LeastReach, Reach(_from)), _letter),
            _ => NameEdits.Turned(Start, _centre, Snapped(Clockwise(g), snap, fine)),
        };
    }

    double Reach(Vec2 g) => (g - _centre).Length;

    double Clockwise(Vec2 g)
    {
        double now = Math.Atan2(g.Z - _centre.Z, g.X - _centre.X), was = Math.Atan2(_from.Z - _centre.Z, _from.X - _centre.X);
        return -Math.IEEERemainder(now - was, Math.Tau) * 180 / Math.PI;
    }

    double Snapped(double by, bool snap, bool fine)
    {
        double step = snap ? TurnSnap : fine ? FineSnap : 0;
        if (step == 0) return by;
        double from = Start.Points.Count == 1 ? Start.TurnDeg : 0;
        return Math.Round((from + by) / step) * step - from;
    }

    public string Said(Stroke now, Func<double, string> letters) => Grip switch
    {
        NameGrip.Body => "Drag to move it",
        NameGrip.Corner => $"Letters {letters(now.Letter ?? _letter)} tall",
        _ => $"Turned {Stroke.Turned(now.Points.Count == 1 ? now.TurnDeg : 0):0.#}° — Shift snaps to 15°, Ctrl to 1°",
    };
}

sealed class NamePick
{
    const double LeastTurn = 1e-6;
    readonly TableContext _c;
    readonly DrawingLayer _drawings;
    readonly ToolOverlay _overlay;
    public readonly NameFrame Frame;
    NameGesture? _gesture;
    Stroke? _shown;

    public NamePick(TableContext c, DrawingLayer drawings, ToolOverlay overlay)
    {
        _c = c;
        _drawings = drawings;
        _overlay = overlay;
        Frame = new NameFrame(c, overlay);
    }

    public string? Id { get; private set; }
    public event Action<Stroke>? EditAsked;

    public MapLettering Lettering => _drawings.Lettering;
    bool CanEdit => _c.S.CanEdit(Layers.Umpire);

    List<Stroke> Layer => _c.S.View.DrawLayer(Layers.Umpire);

    IEnumerable<Stroke> Names => Layer.Where(st => st.Text is { Length: > 0 } && st.Points.Count > 0 && _drawings.BoardShown(st.Board));

    public Stroke? Picked => Id == null || !CanEdit ? null : Names.FirstOrDefault(st => st.Id == Id);

    public Stroke? Shown => _shown ?? Picked;

    public MapLettering.NameBox? Box => Shown is { } st ? Lettering.BoxOf(st) : null;

    public void Pick(string? id)
    {
        if (Id == id) return;
        if (_gesture != null) End(lay: false);
        Id = id;
        _c.NotifySelectionChanged();
    }

    public Stroke? Under(Vec2 g)
    {
        if (!CanEdit) return null;
        double pad = _overlay.Px(NameFrame.PickPx);
        return Names.Reverse().FirstOrDefault(st => Lettering.Holds(st, g, pad));
    }

    public void Edit(Stroke name) => EditAsked?.Invoke(name);

    public void Lay(Stroke old, Stroke changed)
    {
        if (!CanEdit) return;
        _c.S.Do(NameEdits.Relaid(Layer, Layers.Umpire, old, changed));
    }

    public void Show(Stroke? changed)
    {
        _shown = changed;
        Lettering.Preview(changed, changed == null ? null : changed.Id);
    }

    public void Settle(Stroke? changed)
    {
        Show(null);
        if (changed != null && Picked is { } st) Lay(st, changed);
    }

    public bool Remove()
    {
        if (Picked is not { } st) return false;
        _c.S.Do(new EraseStroke { Layer = Layers.Umpire, StrokeId = st.Id });
        _c.Say($"{st.Text} rubbed out · Ctrl+Z brings it back");
        Pick(null);
        return true;
    }

    public bool Nudge(Vec2 by)
    {
        if (Picked is not { } st) return false;
        Lay(st, NameEdits.Moved(st, by));
        return true;
    }

    public bool Turn(double clockwise)
    {
        if (Picked is not { } st || Lettering.BoxOf(st) is not { } box || Math.Abs(clockwise) < LeastTurn) return false;
        Lay(st, NameEdits.Turned(st, box.Centre, clockwise));
        return true;
    }

    public bool Size(double by)
    {
        if (Picked is not { } st || Lettering.BoxOf(st) is not { } box) return false;
        Lay(st, NameEdits.Sized(st, box.Centre, by, Lettering.LetterOf(st)));
        return true;
    }

    public void MoveTo(Vec2 centre)
    {
        if (Picked is { } st && Lettering.BoxOf(st) is { } box) Lay(st, NameEdits.Moved(st, centre - box.Centre));
    }

    public void TurnTo(double turn)
    {
        if (Picked is { } st) Turn(Math.IEEERemainder(turn - TurnOf(st), Stroke.FullTurn));
    }

    public void SizeTo(double letter)
    {
        if (Picked is { } st && Stroke.Lettered(letter)) Size(letter / LetterOf(st));
    }

    public double TurnOf(Stroke st) => st.Points.Count == 1 ? st.TurnDeg : Lettering.BoxOf(st) is { } box ? MapLettering.TurnOf(box.Along) : 0;

    public double LetterOf(Stroke st) => Lettering.LetterOf(st);

    public NameGrip GripAt(Vector2 screen, Vec2 g)
    {
        if (Picked == null || Box is not { } box) return NameGrip.None;
        if (_c.Tool != Tool.Transform) return Frame.At(box, screen, g);
        return box.Holds(g, _overlay.Px(NameFrame.PadPx)) ? NameGrip.Body : NameGrip.None;
    }

    public bool Gripping => _gesture != null;

    public void Grip(NameGrip grip, Vec2 g, Vector2 screen)
    {
        if (grip == NameGrip.None || Picked is not { } st || Lettering.BoxOf(st) is not { } box) return;
        _gesture = new NameGesture(grip, st, box, g, screen, Lettering.LetterOf(st));
    }

    public void GripTo(Vec2 g, Vector2 screen, bool snap, bool fine)
    {
        if (_gesture?.To(g, screen, snap, fine) is not { } now) return;
        Show(now);
        _c.Say(_gesture.Said(now, LetterSaid));
    }

    public void End(bool lay)
    {
        if (_gesture is not { } m) return;
        _gesture = null;
        Settle(lay && m.Moved ? _shown : null);
    }

    string LetterSaid(double units) => Fmt.Distance(units, _c.S.View);

    public void Draw()
    {
        if (_c.Tool is not (Tool.Select or Tool.Transform or Tool.Range) || Box is not { } box) return;
        Frame.Picked(box, handles: _c.Tool != Tool.Transform);
    }
}
