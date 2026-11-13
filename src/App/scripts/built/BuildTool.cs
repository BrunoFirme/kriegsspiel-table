using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BuildTool
{
    const float PickPx = 12, ClosePx = 10, LeastPlacePx = 6, GhostPx = 1.5f, PickedPx = 2.5f, RingSteps = 40, Lift = 0.05f;
    const double LeastPlaceM = 15;
    const ulong PreviewEveryMs = 60;
    const float NameBoxWide = 180;
    static readonly Vector2 NameBoxOffset = new(8, -14);
    const string NotYours = "Only umpires build on the map, and not while a past turn is shown.";
    static readonly Color Brass = SelectionAura.Picked;

    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly SheetLayer _sheets;
    SheetDef? _sheet;
    Vec2? _from, _at;
    List<Vec2>? _path;
    BuiltThing? _ghost;
    int _seed;
    ulong _previewed;
    string? _picked, _hovered;
    int _part = -1, _pressedPart = -1;
    bool _pressedPicked;
    Vec2? _grabbed;
    (BuiltThing Thing, CanvasLayer Box, LineEdit Entry)? _naming;

    public BuildTool(TableContext c, ToolOverlay overlay, SheetLayer sheets)
    {
        _c = c;
        _overlay = overlay;
        _sheets = sheets;
    }

    public event Action? PickChanged;

    BuildPrefs P => _c.Settings.Build;

    public BuiltThing? Picked => _picked == null ? null : _c.S.View.Built.FirstOrDefault(b => b.Id == _picked);

    public BuiltPart? PickedPart => Picked is { } thing && _part >= 0 && _part < thing.Parts.Count ? thing.Parts[_part] : null;

    static int Roll() => Random.Shared.Next(1, int.MaxValue);

    SheetDef? SheetOf(string id) => _c.S.View.Sheets.FirstOrDefault(s => s.Id == id);

    public static BuiltSite Site(Session s, SheetDef sheet, string? but = null) => new()
    {
        Width = Ground.FullWidth(sheet), Height = BuiltGeometry.FullHeight(sheet), UnitsPerMetre = s.View.Calibration.UnitsPerMeter ?? 0, Grid = s.Relief.Grid(sheet),
        HeightUnit = Ground.FullWidth(sheet) * ReliefTool.HeightUnit(sheet), Others = s.View.Built.Where(b => b.Sheet == sheet.Id && b.Id != but).ToList(),
    };

    public static BuiltThing Heathen(BuiltThing thing, bool ancient)
    {
        if (ancient) thing.Parts.RemoveAll(p => p.Kind == PartKinds.Sign && p.Class is "cross" or "shrine");
        return thing;
    }

    public static bool Ancient(Session s) => MapStyles.Of(s.View.Lighting.MapStyle) == MapStyles.Ancient;

    static Spot On(SheetDef sheet, Vec2 world) => Spot.Of(BuiltGeometry.ToSheet(sheet, world));

    static Vec2 Off(SheetDef sheet, Spot p) => BuiltGeometry.ToWorld(sheet, p.Stored);

    bool Ready(Vec2 g, out SheetDef sheet)
    {
        sheet = null!;
        if (!_c.S.CanEdit(Layers.Umpire)) { _c.Say(NotYours); return false; }
        if (!ReliefTool.Scaled(_c.S)) { _c.Say(GroundKinds.NeedsScale + "."); return false; }
        if (ReliefTool.Target(_c.S, _sheets, g) is not { } target) { _c.Say("Build over a map sheet."); return false; }
        sheet = target;
        return true;
    }

    public bool Holds(Vec2 g) => Under(g).Thing != null;

    public void Press(Vec2 g, bool twice, bool picks = false)
    {
        LayNamed();
        if (picks && P.Mode != BuildPrefs.Pick && Under(g).Thing != null)
        {
            Drop();
            P.Mode = BuildPrefs.Pick;
            _c.Settings.Save();
            PickChanged?.Invoke();
        }
        if (P.Mode == BuildPrefs.Pick) { PressPick(g); return; }
        if (P.Mode == BuildPrefs.Roads && _path != null && _sheet != null) { Waypoint(g, twice); return; }
        if (!Ready(g, out var sheet)) return;
        _sheet = sheet;
        _seed = Roll();
        _from = _at = g;
        _ghost = null;
        if (P.Mode != BuildPrefs.Places) _path = new() { g };
    }

    void Waypoint(Vec2 g, bool twice)
    {
        var path = _path!;
        if (twice || (g - path[^1]).Length <= _overlay.Px(ClosePx)) { LayLine(); return; }
        path.Add(g);
        Preview(force: true);
    }

    public void Motion(Vec2 g)
    {
        _at = g;
        if (_grabbed != null) return;
        if (P.Mode == BuildPrefs.Pick && Time.GetTicksMsec() - _previewed >= PreviewEveryMs)
        {
            _previewed = Time.GetTicksMsec();
            _hovered = Under(g).Thing;
        }
        if (_sheet == null) return;
        if (P.Mode == BuildPrefs.Details && _path != null && _from != null && (g - _path[^1]).Length > P.Brush * 0.25f) _path.Add(g);
        Preview(force: false);
    }

    void Preview(bool force)
    {
        if (_sheet is not { } sheet || _at is not { } at) return;
        if (!force && Time.GetTicksMsec() - _previewed < PreviewEveryMs) return;
        _previewed = Time.GetTicksMsec();
        var site = Site(_c.S, sheet);
        if (P.Mode == BuildPrefs.Places && _from is { } from) _ghost = Place(site, sheet, On(sheet, from), On(sheet, at));
        else if (P.Mode == BuildPrefs.Roads && _path is { } path)
            _ghost = BuiltRoads.Lay(site, sheet.Id, path.Append(at).Select(p => On(sheet, p)).ToList(), P.Line, P.FindsWay);
        else if (P.Mode == BuildPrefs.Details && _path is { } stroke && _from != null)
            _ghost = BuiltDetails.Lay(site, sheet.Id, stroke.Select(p => On(sheet, p)).ToList(), P.Brush, _seed, P.Plots, P.Buildings);
    }

    BuiltThing? Place(BuiltSite site, SheetDef sheet, Spot from, Spot to)
    {
        double radius = (to - from).Length;
        var along = radius > 1e-6 ? (to - from).Unit : new Spot(1, 0);
        if (P.Place == BuiltKinds.Building) return Heathen(BuiltThings.Building(site, sheet.Id, P.Block, from, along, radius > site.M(4) ? radius * 2 : 0), Ancient(_c.S));
        if (radius < site.M(LeastPlaceM)) return null;
        double heading = Math.Atan2(along.Y, along.X);
        if (P.Place == BuildPrefs.Redoubt) return Forts.Lay(site, sheet.Id, from, radius, _seed, 0, P.Outworks, heading);
        bool ancient = Ancient(_c.S);
        if (P.Place == BuiltKinds.Fort) return ancient ? Camps.Lay(site, sheet.Id, from, radius, _seed, heading) : Forts.Lay(site, sheet.Id, from, radius, _seed, P.Bastions, P.Outworks, heading);
        double walls = ancient && P.Walls >= Settlements.BastionedFrom ? (Settlements.WalledFrom + Settlements.BastionedFrom) / 2 : P.Walls;
        return Settlements.Lay(site, sheet.Id, from, radius, _seed, P.Houses, walls, Plans.Value(P.Plan), ancient);
    }

    public void Release()
    {
        if (_grabbed != null) { DropGrab(); return; }
        if (_sheet == null || P.Mode == BuildPrefs.Roads) return;
        Preview(force: true);
        if (_ghost is { Parts.Count: > 0 } laid && _at is { } at)
        {
            bool named = P.Mode == BuildPrefs.Places && P.Place != BuildPrefs.Redoubt && BuiltThings.Named(laid);
            if (named && P.Naming == BuildPrefs.Typed) { AskName(laid, at); return; }
            if (named && P.Naming == BuildPrefs.Generated) laid.Name = PlaceNames.Make(P.Tongue, NameKind.Place, _seed);
            _c.S.Do(new LayBuilt { Thing = laid });
        }
        Clear();
    }

    void AskName(BuiltThing laid, Vec2 at)
    {
        _from = null;
        var entry = new LineEdit { PlaceholderText = "Name", MaxLength = PlaceNames.MostLetters, CustomMinimumSize = new Vector2(NameBoxWide, 0) };
        var box = new CanvasLayer();
        box.AddChild(entry);
        _c.Scene.AddChild(box);
        entry.Position = _c.Rig.Cam.UnprojectPosition(Coords.ToWorld(at, _c.SheetsTop)) + NameBoxOffset;
        _naming = (laid, box, entry);
        entry.TextSubmitted += _ => LayNamed();
        entry.FocusExited += () => { if (!entry.IsQueuedForDeletion()) LayNamed(); };
        entry.GrabFocus();
    }

    void LayNamed()
    {
        if (_naming is not var (thing, box, entry)) return;
        _naming = null;
        thing.Name = PlaceNames.Tidy(entry.Text);
        box.QueueFree();
        _c.S.Do(new LayBuilt { Thing = thing });
        Clear();
    }

    public bool Naming => _naming != null;

    public void Rename(string? name)
    {
        if (Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire) || PlaceNames.Tidy(name) == thing.Name) return;
        var named = thing.Clone();
        named.Name = PlaceNames.Tidy(name);
        _c.S.Do(new SetBuilt { Thing = named });
    }

    public void NameAfresh() => Rename(PlaceNames.Make(P.Tongue, NameKind.Place, Roll()));

    void LayLine()
    {
        if (_sheet is { } sheet && _path is { Count: >= 1 } path)
        {
            var laid = Heathen(BuiltRoads.Lay(Site(_c.S, sheet), sheet.Id, path.Select(p => On(sheet, p)).ToList(), P.Line, P.FindsWay), Ancient(_c.S));
            if (laid.Parts.Count > 0) _c.S.Do(Laying(_c.S, laid));
        }
        Clear();
    }

    public static Command Laying(Session s, BuiltThing laid)
    {
        var lay = new LayBuilt { Thing = laid };
        if (laid.Kind is not (BuiltKinds.Road or BuiltKinds.Water)) return lay;
        double perMetre = s.View.Calibration.UnitsPerMeter ?? 0;
        var sheet = s.View.Sheets.FirstOrDefault(x => x.Id == laid.Sheet);
        var batch = new Batch { Items = { lay } };
        foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet).ToList())
        {
            var cleared = BuiltRoads.MadeWay(other, laid, perMetre);
            var fronted = sheet != null && other.Kind == BuiltKinds.Settlement ? Settlements.Fronted(Site(s, sheet, other.Id), cleared ?? other, laid) : null;
            if ((fronted ?? cleared) is { } changed) batch.Items.Add(new SetBuilt { Thing = changed });
        }
        return batch.Items.Count == 1 ? lay : batch;
    }

    void Clear()
    {
        _sheet = null;
        _from = null;
        _path = null;
        _ghost = null;
    }

    public bool Drop()
    {
        bool open = _sheet != null || _grabbed != null;
        if (_naming is var (_, box, _))
        {
            _naming = null;
            box.QueueFree();
        }
        Clear();
        _grabbed = null;
        return open;
    }

    public void Cancel()
    {
        LayNamed();
        Drop();
        _hovered = null;
        Pick(null, -1);
    }

    void Pick(string? id, int part)
    {
        if (_picked == id && _part == part) return;
        _picked = id;
        _part = part;
        PickChanged?.Invoke();
    }

    (string? Thing, int Part) Under(Vec2 g)
    {
        string? best = null;
        int bestPart = -1;
        double near = _overlay.Px(PickPx), least = double.MaxValue;
        foreach (var thing in _c.S.View.Built)
        {
            if (SheetOf(thing.Sheet) is not { } sheet) continue;
            var at = On(sheet, g);
            var (part, reach) = BuiltThings.Nearest(thing, at);
            bool touched = part >= 0 && reach < near;
            if (!touched && !BuiltThings.StandsOver(thing, at)) continue;
            double rank = touched ? reach : near + reach;
            if (rank >= least) continue;
            least = rank;
            best = thing.Id;
            bestPart = touched ? part : -1;
        }
        return (best, bestPart);
    }

    void PressPick(Vec2 g)
    {
        var (best, bestPart) = Under(g);
        _pressedPicked = best != null && best == _picked;
        _pressedPart = bestPart;
        if (!_pressedPicked) Pick(best, -1);
        _grabbed = best != null && _c.S.CanEdit(Layers.Umpire) ? g : null;
        _at = g;
    }

    void DropGrab()
    {
        var from = _grabbed!.Value;
        _grabbed = null;
        if (_at is not { } to || (to - from).Length <= _overlay.Px(LeastPlacePx))
        {
            if (_pressedPicked) Pick(_picked, _pressedPart == _part ? -1 : _pressedPart);
            return;
        }
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet) return;
        var a = BuiltGeometry.ToSheet(sheet, from);
        var b = BuiltGeometry.ToSheet(sheet, to);
        var moved = _part >= 0 ? BuiltThings.WithPartMoved(thing, _part, b.X - a.X, b.Y - a.Y) : BuiltGeometry.Moved(thing, b.X - a.X, b.Y - a.Y);
        _c.S.Do(new SetBuilt { Thing = moved });
    }

    public bool Delete()
    {
        if (_c.Tool != Tool.Build || Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire)) return false;
        if (_part >= 0 && thing.Parts.Count > 1) _c.S.Do(new SetBuilt { Thing = BuiltThings.WithoutPart(thing, _part) });
        else _c.S.Do(new RemoveBuilt { Id = thing.Id });
        Pick(_part >= 0 && thing.Parts.Count > 1 ? thing.Id : null, -1);
        return true;
    }

    public void Again()
    {
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire)) return;
        _c.S.Do(new SetBuilt { Thing = BuiltThings.Remade(Site(_c.S, sheet, thing.Id), thing, BuiltThings.Rerolls(thing) ? Roll() : thing.Seed) });
        Pick(thing.Id, -1);
    }

    public void Resize(bool up)
    {
        P.Brush = Math.Clamp(P.Brush * (up ? 1.15f : 1 / 1.15f), BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush);
        _c.Settings.Save();
    }

    public string Hint() => P.Mode switch
    {
        BuildPrefs.Roads when _path == null => "Click the waypoints, then the last again or double-click to lay it · Alt+click picks what is built",
        BuildPrefs.Roads => "Click the waypoints, then the last again or double-click to lay it · Esc drops it",
        BuildPrefs.Details => "Drag over the country to settle it · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Pick => "Click a built thing to pick it, again for one part of it · drag moves what is picked · Delete removes it",
        _ => P.Place == BuiltKinds.Building ? "Click to stand a building, or drag its length" : "Drag from the middle outward · release lays it",
    };

    public void Draw()
    {
        if (_c.Tool != Tool.Build || _c.Rig == null) return;
        var mat = new StandardMaterial3D { AlbedoColor = Brass, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (_ghost != null && _sheet != null) Outline(mat, _sheet, _ghost, GhostPx, default);
        if (P.Mode == BuildPrefs.Roads && _path is { } path && _sheet != null)
            _overlay.RibbonPx(_overlay.Mesh, mat, Ring(path[^1], _overlay.Px(ClosePx)), Lift, GhostPx);
        if (P.Mode == BuildPrefs.Details && _at is { } at) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(at, P.Brush), Lift, GhostPx);
        if (P.Mode != BuildPrefs.Pick) return;
        var hovered = _hovered == null || _hovered == _picked ? null : _c.S.View.Built.FirstOrDefault(b => b.Id == _hovered);
        if (hovered != null && SheetOf(hovered.Sheet) is { } under) Outline(mat, under, hovered, GhostPx, default);
        if (Picked is not { } picked || SheetOf(picked.Sheet) is not { } on) return;
        var shift = _grabbed is { } from && _at is { } to ? to - from : default;
        if (_part >= 0 && _part < picked.Parts.Count) Trace(mat, on, picked.Parts[_part], PickedPx, shift);
        else Outline(mat, on, picked, PickedPx, shift);
    }

    void Outline(StandardMaterial3D mat, SheetDef sheet, BuiltThing thing, float px, Vec2 shift)
    {
        foreach (var part in thing.Parts)
            if (part.Class != BuiltThings.Quarter) Trace(mat, sheet, part, px, shift);
    }

    void Trace(StandardMaterial3D mat, SheetDef sheet, BuiltPart part, float px, Vec2 shift)
    {
        var points = BuiltGeometry.Points(part.At).Select(p => BuiltGeometry.ToWorld(sheet, p) + shift).ToList();
        if (points.Count == 1) { _overlay.RibbonPx(_overlay.Mesh, mat, Ring(points[0], _overlay.Px(PickPx * 0.6f)), Lift, px); return; }
        if (part.Kind is PartKinds.Block or PartKinds.Area) points.Add(points[0]);
        _overlay.RibbonPx(_overlay.Mesh, mat, points, Lift, px);
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
}
