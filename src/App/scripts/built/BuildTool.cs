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
    const float PickPx = 12, ClosePx = 10, LeastPlacePx = 6, GhostPx = 1.5f, PickedPx = 2.5f, RingSteps = 40;

    float Lift => _overlay.Lift;
    const double LeastPlaceM = 15;
    const ulong PreviewEveryMs = 60;
    const double PartStep = 1.1, StandDragM = 4;
    const float HandlePx = 5;
    public const string Gate = "gate";
    const float NameBoxWide = 180;
    static readonly Vector2 NameBoxOffset = new(8, -14);
    const string NotYours = "Only umpires build on the map, and not while a past turn is shown.";
    internal const string NoRoom = "No room here: too steep, wet or already built.";
    static readonly Color Brass = SelectionAura.Picked;
    static readonly Color Unlit = new(0.55f, 0.55f, 0.6f);
    const float LampPx = 4;
    const double LampsShownBrushes = 3;

    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly SheetLayer _sheets;
    SheetDef? _sheet;
    Vec2? _from, _at;
    List<Vec2>? _path;
    BuiltThing? _ghost;
    int _seed;
    ulong _previewed;
    string? _picked, _hovered, _ground;
    int _part = -1, _pressedPart = -1;
    bool _pressedPicked;
    Vec2? _grabbed;
    int _corner = -1;
    (BuiltThing Thing, CanvasLayer Box, LineEdit Entry)? _naming;

    readonly Dictionary<BuiltThing, List<Lamp>> _lamps = new();

    public BuildTool(TableContext c, ToolOverlay overlay, SheetLayer sheets)
    {
        _c = c;
        _overlay = overlay;
        _sheets = sheets;
        _sheets.Built += () =>
        {
            _lamps.Clear();
            if (_picked != null) PickChanged?.Invoke();
        };
    }

    public event Action? PickChanged;

    BuildPrefs P => _c.Settings.Build;

    public BuiltThing? Picked => _picked == null ? null : _c.S.View.Built.FirstOrDefault(b => b.Id == _picked);

    public BuiltPart? PickedPart => Picked is { } thing && _part >= 0 && _part < thing.Parts.Count ? thing.Parts[_part] : null;

    static int Roll() => Random.Shared.Next(1, int.MaxValue);

    SheetDef? SheetOf(string id) => _c.S.View.Sheets.FirstOrDefault(s => s.Id == id);

    public static BuiltSite Site(Session s, SheetDef sheet, string? but = null, int manner = -1) => new()
    {
        Manner = Manners.For(Epoch(s), manner),
        Width = Ground.FullWidth(sheet), Height = BuiltGeometry.FullHeight(sheet), UnitsPerMetre = s.View.Calibration.UnitsPerMeter ?? 0, Grid = s.Relief.Under(sheet),
        HeightUnit = Ground.FullWidth(sheet) * ReliefTool.HeightUnit(sheet), Others = s.View.Built.Where(b => b.Sheet == sheet.Id && b.Id != but).ToList(),
        Elders = s.View.Built.TakeWhile(b => b.Id != but).Where(b => b.Sheet == sheet.Id).ToList(),
    };

    public static BuiltThing Heathen(BuiltThing thing, bool ancient)
    {
        if (ancient) thing.Parts.RemoveAll(p => p.Kind == PartKinds.Sign && p.Class is "cross" or "shrine");
        return thing;
    }

    public static bool Ancient(Session s) => MapStyles.Of(s.View.Lighting.MapStyle) == MapStyles.Ancient;

    public static int Epoch(Session s) => Manners.Age(s.View.Lighting.MapStyle);

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

    string Doing => P.Activity();

    public void Press(Vec2 g, bool twice, bool picks = false)
    {
        LayNamed();
        if (picks && P.Mode != BuildPrefs.Pick && Under(g).Thing != null)
        {
            Drop();
            P.Do(BuildPrefs.Pick);
            _c.Settings.Save();
            PickChanged?.Invoke();
        }
        if (P.Mode == BuildPrefs.Pick) { PressPick(g); return; }
        if (Doing == BuildPrefs.Roads && _path != null && _sheet != null) { Waypoint(g, twice); return; }
        if (Doing == BuildPrefs.Blocks && _path != null && _sheet != null) { Corner(g, twice); return; }
        if (!Ready(g, out var sheet)) return;
        if (Doing == BuildPrefs.Things) Pick(null, -1);
        _sheet = sheet;
        _seed = Roll();
        _from = _at = g;
        _ghost = null;
        if (Doing is not (BuildPrefs.Places or BuildPrefs.Things)) _path = new() { g };
    }

    void Waypoint(Vec2 g, bool twice)
    {
        var path = _path!;
        if (twice || (g - path[^1]).Length <= _overlay.Px(ClosePx)) { LayLine(); return; }
        path.Add(g);
        Preview(force: true);
    }

    void Corner(Vec2 g, bool twice)
    {
        var path = _path!;
        if (twice || path.Count >= 3 && (g - path[0]).Length <= _overlay.Px(ClosePx) || (g - path[^1]).Length <= _overlay.Px(ClosePx)) { LayBlock(); return; }
        path.Add(g);
        Preview(force: true);
    }

    void LayBlock()
    {
        if (_sheet is { } sheet && _path is { } path)
        {
            if (BlockCommand(_c.S, sheet, path.Select(p => On(sheet, p)).ToList(), out var refused) is { } drawn) _c.S.Do(drawn);
            else _c.Say(refused!);
        }
        Clear();
    }

    public static Command? BlockCommand(Session s, SheetDef sheet, List<Spot> corners, out string? refused)
    {
        refused = corners.Count < 3 ? "A block takes three corners or more." : Traces.Holder(s.View.Built, sheet.Id, Traces.Middle(corners)) == null ? "Draw a block inside a settlement." : null;
        if (refused != null) return null;
        var holder = Traces.Holder(s.View.Built, sheet.Id, Traces.Middle(corners))!;
        return new SetBuilt { Thing = Traces.WithBlock(Site(s, sheet, holder.Id), holder, corners) };
    }

    public static (Command Done, BuiltThing? Into) StandCommand(Session s, SheetDef sheet, List<BuiltPart> parts, int manner)
    {
        if (Traces.Holder(s.View.Built, sheet.Id, BuiltSite.Round(parts[0]).At) is { } place)
        {
            var into = Traces.Stood(place, parts);
            return (new SetBuilt { Thing = into }, into);
        }
        return (Laying(s, Traces.Alone(Site(s, sheet, null, manner), sheet.Id, parts)), null);
    }

    public static string Fitting(Session s, string id) =>
        Catalogue.Find(id) is { } entry && Catalogue.Fits(entry, Ancient(s), Epoch(s) == Manners.Modern, Epoch(s) == Manners.Medieval) ? id : "house";

    BuiltThing Standing(BuiltSite site, SheetDef sheet, Spot from, Spot to)
    {
        double radius = (to - from).Length;
        var along = radius > 1e-6 ? (to - from).Unit : new Spot(1, 0);
        var parts = Traces.Thing(site, Fitting(_c.S, P.Block), from, along, radius > site.M(StandDragM) ? radius * 2 : 0);
        return Heathen(new BuiltThing { Sheet = sheet.Id, Kind = BuiltKinds.Building, Parts = parts }, Ancient(_c.S));
    }

    void Stand(BuiltThing ghost)
    {
        var (done, into) = StandCommand(_c.S, _sheet!, ghost.Parts, Manners.Parse(P.Manner).Lays);
        _c.S.Do(done);
        if (into != null) Pick(into.Id, into.Parts.Count - ghost.Parts.Count);
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
        if (Doing is BuildPrefs.Details or BuildPrefs.Lamps or BuildPrefs.Homes && _path != null && _from != null && (g - _path[^1]).Length > P.Brush * 0.25f) _path.Add(g);
        Preview(force: false);
    }

    void Preview(bool force)
    {
        if (_sheet is not { } sheet || _at is not { } at) return;
        if (!force && Time.GetTicksMsec() - _previewed < PreviewEveryMs) return;
        _previewed = Time.GetTicksMsec();
        var site = Site(_c.S, sheet, null, Manners.Parse(P.Manner).Lays);
        if (Doing == BuildPrefs.Places && _from is { } from)
        {
            _ghost = Mixed(Place(site, sheet, On(sheet, from), On(sheet, at)));
            NameGround(site, On(sheet, from), (On(sheet, at) - On(sheet, from)).Length);
        }
        else if (Doing == BuildPrefs.Things && _from is { } spot) _ghost = Standing(site, sheet, On(sheet, spot), On(sheet, at));
        else if (Doing == BuildPrefs.Roads && _path is { } path)
            _ghost = Mixed(BuiltRoads.Lay(site, sheet.Id, path.Append(at).Select(p => On(sheet, p)).ToList(), P.Line, P.FindsWay));
        else if (Doing == BuildPrefs.Blocks && _path is { } corners)
            _ghost = new BuiltThing { Sheet = sheet.Id, Parts = { BuiltSite.Area(Traces.Block, corners.Append(at).Select(p => On(sheet, p))) } };
        else if (Doing == BuildPrefs.Details && _path is { } stroke && _from != null)
            _ghost = BuiltDetails.Lay(site, sheet.Id, stroke.Select(p => On(sheet, p)).ToList(), P.Brush, _seed, P.Plots, P.Buildings, P.Shares);
    }

    BuiltThing? Mixed(BuiltThing? laid)
    {
        if (laid != null && (_c.S.View.Cultures?.Any(mix => mix.Code == P.Manner) ?? false)) Manners.Parse(P.Manner).Onto(laid);
        return laid;
    }

    BuiltThing? Place(BuiltSite site, SheetDef sheet, Spot from, Spot to)
    {
        double radius = (to - from).Length;
        var along = radius > 1e-6 ? (to - from).Unit : new Spot(1, 0);
        if (radius < site.M(LeastPlaceM)) return null;
        double heading = Math.Atan2(along.Y, along.X);
        if (P.Place == BuildPrefs.Redoubt && Defences.Fieldwork(site.Manner) is { } work) return Defences.Lay(site, sheet.Id, work.Id, from, radius, _seed, 0, P.Outworks, heading);
        bool ancient = Ancient(_c.S);
        if (P.Place is BuiltKinds.Fort or BuildPrefs.Redoubt) return Defences.Lay(site, sheet.Id, Defences.Fort(site.Manner).Id, from, radius, _seed, P.Bastions, P.Outworks, heading);
        double walls = !Defences.Bastioned(site.Epoch) && P.Walls >= Settlements.BastionedFrom ? (Settlements.WalledFrom + Settlements.BastionedFrom) / 2 : P.Walls;
        return Settlements.Lay(site, sheet.Id, from, radius, _seed, P.Houses, walls, Plans.Value(Plans.For(site.Manner, P.Plan, radius / site.UnitsPerMetre)), ancient, null,
            new Settlements.Shape(P.Spread, P.Order, heading));
    }

    public void Release()
    {
        if (_grabbed != null) { DropGrab(); return; }
        if (_sheet == null || Doing is BuildPrefs.Roads or BuildPrefs.Blocks) return;
        if (Doing == BuildPrefs.Lamps) { SwitchLamps(); return; }
        if (Doing == BuildPrefs.Homes) { SweepHouses(); return; }
        if (Doing == BuildPrefs.Places && (_from is not { } start || _at is not { } end || (end - start).Length <= _overlay.Px(LeastPlacePx)))
        {
            Clear();
            return;
        }
        Preview(force: true);
        if (_ghost is { Parts.Count: > 0 } stood && Doing == BuildPrefs.Things) Stand(stood);
        else if (_ghost is { Parts.Count: > 0 } laid && _at is { } at)
        {
            bool named = Doing == BuildPrefs.Places && P.Place != BuildPrefs.Redoubt && BuiltThings.Named(laid);
            if (named && P.Naming == BuildPrefs.Typed) { AskName(laid, at); return; }
            _c.S.Do(Laying(_c.S, laid));
        }
        else if (Doing == BuildPrefs.Places && _ghost is { Parts.Count: 0 }) _c.Say(NoRoom);
        Clear();
    }

    void AskName(BuiltThing laid, Vec2 at)
    {
        _from = null;
        var entry = new LineEdit { PlaceholderText = "Name", MaxLength = PlaceNames.MostLetters, CustomMinimumSize = Ui.Px(NameBoxWide, 0) };
        var box = new CanvasLayer();
        box.AddChild(entry);
        _c.Scene.AddChild(box);
        entry.Position = _c.Rig.Cam.UnprojectPosition(Coords.ToWorld(at, _c.SheetsTop)) + Ui.Px(NameBoxOffset);
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
        _c.S.Do(Laying(_c.S, thing));
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

    void LayLine()
    {
        if (_sheet is { } sheet && _path is { Count: >= 1 } path)
        {
            var site = Site(_c.S, sheet, null, Manners.Parse(P.Manner).Lays);
            var laid = Heathen(BuiltRoads.Lay(site, sheet.Id, path.Select(p => On(sheet, p)).ToList(), P.Line, P.FindsWay), Ancient(_c.S));
            Mixed(laid);
            if (laid.Parts.Count > 0) _c.S.Do(Laying(_c.S, laid));
        }
        Clear();
    }

    void SweepHouses()
    {
        var stroke = _path;
        if (_at is { } last) stroke?.Add(last);
        Clear();
        if (stroke == null || !_c.S.CanEdit(Layers.Umpire)) return;
        var batch = new Batch();
        foreach (var place in _c.S.View.Built.Where(b => b.Kind == BuiltKinds.Settlement && !BuiltThings.Fixed(b)).ToList())
        {
            if (SheetOf(place.Sheet) is not { } sheet) continue;
            var path = stroke.Select(p => On(sheet, p)).ToList();
            var (middle, reach) = BuiltThings.Round(place);
            if (path.All(p => (p - middle).Length > reach + P.Brush)) continue;
            var site = Site(_c.S, sheet, place.Id);
            if ((P.Thins ? Brushed.Thinned(site, place, path, P.Brush, Roll()) : Brushed.Filled(site, place, path, P.Brush, Roll())) is { } done) batch.Items.Add(new SetBuilt { Thing = done });
        }
        if (batch.Items.Count == 1) _c.S.Do(batch.Items[0]);
        else if (batch.Items.Count > 1) _c.S.Do(batch);
    }

    void SwitchLamps()
    {
        var stroke = _path;
        if (_at is { } last) stroke?.Add(last);
        Clear();
        if (stroke == null || _c.S.View.Calibration.UnitsPerMeter is not { } perMetre || perMetre <= 0) return;
        int epoch = Epoch(_c.S);
        var batch = new Batch();
        foreach (var thing in _c.S.View.Built)
        {
            if (SheetOf(thing.Sheet) is not { } sheet) continue;
            if (Lamps.Switched(thing, epoch, perMetre, stroke.Select(p => On(sheet, p)).ToList(), P.Brush, P.LampsOn) is { } switched)
                batch.Items.Add(new SetBuilt { Thing = switched });
        }
        if (batch.Items.Count == 1) _c.S.Do(batch.Items[0]);
        else if (batch.Items.Count > 1) _c.S.Do(batch);
    }

    List<Lamp> LampsOf(BuiltThing thing)
    {
        if (_lamps.TryGetValue(thing, out var known)) return known;
        var made = Lamps.Of(thing, Epoch(_c.S), _c.S.View.Calibration.UnitsPerMeter ?? 0);
        _lamps[thing] = made;
        return made;
    }

    void DrawLamps(Vec2 at)
    {
        double near = P.Brush * LampsShownBrushes;
        List<List<Vec2>> lit = new(), dark = new();
        foreach (var thing in _c.S.View.Built)
        {
            if (SheetOf(thing.Sheet) is not { } sheet) continue;
            var (middle, reach) = BuiltThings.Round(thing);
            if ((Off(sheet, middle) - at).Length > reach + near) continue;
            foreach (var lamp in LampsOf(thing))
            {
                var spot = Off(sheet, lamp.At);
                if ((spot - at).Length <= near) (lamp.Dark ? dark : lit).Add(Ring(spot, _overlay.Px(LampPx)));
            }
        }
        if (lit.Count > 0) _overlay.RibbonsPx(_overlay.Mesh, Inked(Brass), lit, Lift, PickedPx);
        if (dark.Count > 0) _overlay.RibbonsPx(_overlay.Mesh, Inked(Unlit), dark, Lift, GhostPx);
    }

    static StandardMaterial3D Inked(Color c) => new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };

    public static Command Laying(Session s, BuiltThing laid)
    {
        var lay = new LayBuilt { Thing = laid };
        var sheet = s.View.Sheets.FirstOrDefault(x => x.Id == laid.Sheet);
        var batch = new Batch { Items = { lay } };
        if (laid.Kind == BuiltKinds.Settlement && sheet != null)
            foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet).ToList())
                if (Walls.Joined(Site(s, sheet, other.Id), other, laid) is { } gated) batch.Items.Add(new SetBuilt { Thing = gated });
        var stroke = laid.Kind == BuiltKinds.Details ? BuiltGeometry.Points(laid.Outline).Select(Spot.Of).ToList() : [];
        foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet && b.Kind == BuiltKinds.Details && b.Id != laid.Id).ToList())
            if (BuiltDetails.Cleared(other, stroke, laid.Setting(BuiltDetails.Radius, 0)) is { } cleared)
                batch.Items.Insert(0, cleared.Parts.Count == 0 ? new RemoveBuilt { Id = other.Id } : new SetBuilt { Thing = cleared });
        if (laid.Kind is not (BuiltKinds.Road or BuiltKinds.Water)) return batch.Items.Count == 1 ? lay : batch;
        double perMetre = s.View.Calibration.UnitsPerMeter ?? 0;
        foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet).ToList())
        {
            var cleared = BuiltRoads.MadeWay(other, laid, perMetre);
            var fronted = sheet != null && other.Kind == BuiltKinds.Settlement ? Settlements.Fronted(Site(s, sheet, other.Id), cleared ?? other, laid) : null;
            var gated = sheet == null ? null : Walls.Opened(Site(s, sheet, other.Id), fronted ?? cleared ?? other, laid);
            if ((gated ?? fronted ?? cleared) is { } changed) batch.Items.Add(new SetBuilt { Thing = changed });
        }
        return batch.Items.Count == 1 ? lay : batch;
    }

    void NameGround(BuiltSite site, Spot centre, double radius)
    {
        if (site.Grid == null || Landforms.Of(site, centre, radius).Named(centre) is not { } named || named == _ground) return;
        _ground = named;
        _c.Say(named);
    }

    void Clear()
    {
        _sheet = null;
        _from = null;
        _path = null;
        _ghost = null;
        _ground = null;
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
        _corner = -1;
        return open;
    }

    public void Cancel()
    {
        LayNamed();
        Adding = null;
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

    int CornerUnder(Vec2 g) => Picked is { } place && _part < 0 && _c.S.CanEdit(Layers.Umpire) && SheetOf(place.Sheet) is { } sheet && Walls.Handles(place) is { } trace
        ? Walls.CornerNear(trace, On(sheet, g), _overlay.Px(PickPx)) : -1;

    void PressPick(Vec2 g)
    {
        _at = g;
        if (Add(g)) return;
        _corner = CornerUnder(g);
        var (best, bestPart) = _corner >= 0 ? (_picked, -1) : Under(g);
        _pressedPicked = best != null && best == _picked;
        _pressedPart = bestPart;
        if (!_pressedPicked) Pick(best, -1);
        _grabbed = best != null && _c.S.CanEdit(Layers.Umpire) ? g : null;
        _at = g;
    }

    void DropGrab()
    {
        var from = _grabbed!.Value;
        int corner = _corner;
        _grabbed = null;
        _corner = -1;
        if (_at is not { } to || (to - from).Length <= _overlay.Px(LeastPlacePx))
        {
            if (_pressedPicked) Pick(_picked, _pressedPart == _part ? -1 : _pressedPart);
            return;
        }
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet) return;
        if (corner >= 0)
        {
            if (Walls.CornerMoved(Site(_c.S, sheet, thing.Id), thing, corner, On(sheet, to)) is { } walled) _c.S.Do(new SetBuilt { Thing = walled });
            return;
        }
        var a = BuiltGeometry.ToSheet(sheet, from);
        var b = BuiltGeometry.ToSheet(sheet, to);
        var moved = _part >= 0 ? BuiltThings.WithPartMoved(thing, _part, b.X - a.X, b.Y - a.Y) : BuiltGeometry.Moved(thing, b.X - a.X, b.Y - a.Y);
        _c.S.Do(new SetBuilt { Thing = moved });
    }

    public bool Delete()
    {
        if (_c.Tool != Tool.Build || Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire)) return false;
        if (_part >= 0 && thing.Parts.Count > 1 && SheetOf(thing.Sheet) is { } sheet) _c.S.Do(new SetBuilt { Thing = Traces.Without(Site(_c.S, sheet, thing.Id), thing, _part) });
        else _c.S.Do(new RemoveBuilt { Id = thing.Id });
        Pick(_part >= 0 && thing.Parts.Count > 1 ? thing.Id : null, -1);
        return true;
    }

    public void Again()
    {
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire) || BuiltThings.Fixed(thing) || !Scaled()) return;
        _c.S.Do(new SetBuilt { Thing = BuiltThings.Remade(Site(_c.S, sheet, thing.Id), thing, BuiltThings.Rerolls(thing) ? Roll() : thing.Seed) });
        Pick(thing.Id, -1);
    }

    bool Scaled()
    {
        if (ReliefTool.Scaled(_c.S)) return true;
        _c.Say(GroundKinds.NeedsScale + ".");
        return false;
    }

    public string? Adding { get; private set; }

    public void Arm(string? cls) => Adding = cls;

    public void Stretch((double Long, double Deep) sides)
    {
        if (Picked is not { } thing || _part < 0 || _part >= thing.Parts.Count || !BuiltThings.Stretches(thing.Parts[_part]) || !_c.S.CanEdit(Layers.Umpire)) return;
        _c.S.Do(new SetBuilt { Thing = BuiltThings.WithPartStretched(thing, _part, sides.Long, sides.Deep) });
    }

    public bool Kept => PickedPart is { } part ? part.Kept : Picked is { } thing && BuiltThings.Fixed(thing);

    public void Keep(bool on)
    {
        if (Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire) || Kept == on) return;
        _c.S.Do(new SetBuilt { Thing = BuiltThings.WithKept(thing, PickedPart == null ? -1 : _part, on) });
    }

    public void Reset(string setting, double value)
    {
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire)) return;
        if (BuiltThings.Fixed(thing) || thing.Setting(setting, double.NaN) == value || !Scaled()) return;
        var changed = thing.Clone();
        changed.Settings[setting] = value;
        _c.S.Do(new SetBuilt { Thing = BuiltThings.Remade(Site(_c.S, sheet, thing.Id), changed, thing.Seed) });
    }

    public bool Turn(double degrees)
    {
        if (_c.Tool != Tool.Build || Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire)) return false;
        double radians = degrees * Math.PI / 180;
        _c.S.Do(new SetBuilt { Thing = _part >= 0 ? BuiltThings.WithPartTurned(thing, _part, radians) : BuiltThings.Turned(thing, radians) });
        return true;
    }

    bool Add(Vec2 g)
    {
        if (Adding != Gate || Picked is not { } place || SheetOf(place.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire)) return false;
        if (Walls.Gated(Site(_c.S, sheet, place.Id), place, On(sheet, g), _overlay.Px(PickPx)) is { } gated) _c.S.Do(new SetBuilt { Thing = gated });
        return true;
    }

    public void Resize(bool up)
    {
        if ((P.Mode == BuildPrefs.Pick || Doing == BuildPrefs.Things) && Picked is { } thing && _part >= 0 && _part < thing.Parts.Count && _c.S.CanEdit(Layers.Umpire))
        {
            _c.S.Do(new SetBuilt { Thing = BuiltThings.WithPartSized(thing, _part, up ? PartStep : 1 / PartStep) });
            return;
        }
        P.Brush = Math.Clamp(P.Brush * (up ? 1.15f : 1 / 1.15f), BuildPrefs.SmallestBrush, BuildPrefs.LargestBrush);
        _c.Settings.Save();
    }

    public string Hint() => Doing switch
    {
        BuildPrefs.Roads when _path == null => "Click the waypoints, then the last again or double-click to lay it · Alt+click picks what is built",
        BuildPrefs.Roads => "Click the waypoints, then the last again or double-click to lay it · Esc drops it",
        BuildPrefs.Blocks => "Click the block's corners, then the first again or double-click to close it" + (_path == null ? " · Alt+click picks what is built" : " · Esc drops it"),
        BuildPrefs.Things => "Click to stand it, or drag its length · Q and E turn it, = and − size it",
        BuildPrefs.Details => "Drag over the country to settle it · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Homes => (P.Thins ? "Drag over houses to thin them" : "Drag along the streets to fill them with houses") + " · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Lamps => (P.LampsOn ? "Drag over dark lamps to light them" : "Drag over lamps to put them out") + " · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Pick when Adding == Gate => "Click the wall for a gate, or a gate to close it",
        BuildPrefs.Pick when Picked is { } walled && _part < 0 && Walls.Handles(walled) != null => "Drag moves the place, a corner of its wall moves the wall · Q and E turn it · Delete removes it",
        BuildPrefs.Pick => "Click a built thing to pick it, again for one part · drag moves it · Q and E turn it, = and − size a part · Delete removes it",
        _ => P.Place == BuiltKinds.Settlement ? "Drag from the middle outward, along the line its main street should take · release lays it"
            : "Drag from the middle outward · release lays it",
    };

    public void Draw()
    {
        if (_c.Tool != Tool.Build || _c.Rig == null) return;
        var mat = new StandardMaterial3D { AlbedoColor = Brass, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (_ghost != null && _sheet != null) Outline(mat, _sheet, _ghost, GhostPx, default);
        if (Doing == BuildPrefs.Roads && _path is { } path && _sheet != null)
            _overlay.RibbonPx(_overlay.Mesh, mat, Ring(path[^1], _overlay.Px(ClosePx)), Lift, GhostPx);
        if (Doing is BuildPrefs.Details or BuildPrefs.Lamps or BuildPrefs.Homes && _at is { } at) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(at, P.Brush), Lift, GhostPx);
        if (Doing == BuildPrefs.Blocks)
        {
            if (_path is { Count: >= 3 } corners) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(corners[0], _overlay.Px(ClosePx)), Lift, GhostPx);
            foreach (var thing in _c.S.View.Built)
                if (SheetOf(thing.Sheet) is { } blocked)
                    foreach (var block in thing.Parts.Where(Traces.IsBlock)) Trace(mat, blocked, block, GhostPx, default);
        }
        if (Doing == BuildPrefs.Lamps && _at is { } brushed) DrawLamps(brushed);
        if (P.Mode != BuildPrefs.Pick && Doing != BuildPrefs.Things) return;
        var hovered = _hovered == null || _hovered == _picked ? null : _c.S.View.Built.FirstOrDefault(b => b.Id == _hovered);
        if (hovered != null && SheetOf(hovered.Sheet) is { } under) Outline(mat, under, hovered, GhostPx, default);
        if (Picked is not { } picked || SheetOf(picked.Sheet) is not { } on) return;
        var shift = _grabbed is { } from && _at is { } to && _corner < 0 ? to - from : default;
        if (_part >= 0 && _part < picked.Parts.Count) Trace(mat, on, picked.Parts[_part], PickedPx, shift);
        else Outline(mat, on, picked, PickedPx, shift);
        if (_part >= 0 || Adding != null || Walls.Handles(picked) is not { } trace) return;
        if (_corner >= 0 && _corner < trace.Corners.Count && _grabbed != null && _at is { } pulled) trace = Walls.Moved(trace, _corner, On(on, pulled));
        foreach (var corner in trace.Corners) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(Off(on, corner) + shift, _overlay.Px(HandlePx)), Lift, GhostPx);
        if (_corner >= 0 && _grabbed != null) _overlay.RibbonPx(_overlay.Mesh, mat, trace.Corners.Append(trace.Corners[0]).Select(p => Off(on, p)).ToList(), Lift, PickedPx);
    }

    const int MostPartsTracedWhole = 150;

    void Outline(StandardMaterial3D mat, SheetDef sheet, BuiltThing thing, float px, Vec2 shift)
    {
        bool whole = thing.Parts.Count <= MostPartsTracedWhole;
        var lines = thing.Parts.Where(part => part.Class != BuiltThings.Quarter && (whole || part.Kind != PartKinds.Sign)).Select(part => Traced(sheet, part, shift)).ToList();
        _overlay.RibbonsPx(_overlay.Mesh, mat, lines, Lift, px);
    }

    void Trace(StandardMaterial3D mat, SheetDef sheet, BuiltPart part, float px, Vec2 shift) => _overlay.RibbonPx(_overlay.Mesh, mat, Traced(sheet, part, shift), Lift, px);

    List<Vec2> Traced(SheetDef sheet, BuiltPart part, Vec2 shift)
    {
        var points = BuiltGeometry.Points(part.At).Select(p => BuiltGeometry.ToWorld(sheet, p) + shift).ToList();
        if (points.Count == 1) return Ring(points[0], _overlay.Px(PickPx * 0.6f));
        if (part.Kind is PartKinds.Block or PartKinds.Area) points.Add(points[0]);
        return points;
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
