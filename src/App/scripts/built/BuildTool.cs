using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BuildTool
{
    const float PickPx = 12, ClosePx = 10, LeastPlacePx = 6, GhostPx = 1.5f, PickedPx = 2.5f, RingSteps = 40, StrokePx = 4, StraightenPx = 6, SnapPx = 14, MiddlePx = 3.5f;
    const double RoadBend = 0.6, OntoShare = 0.25;

    float Lift => _overlay.Lift;
    const double LeastPlaceM = 15;
    const float LeastBrushM = 50;
    const ulong PreviewEveryMs = 60;
    const double PartStep = 1.1, StandDragM = 4;
    const float HandlePx = 5, HandleReachPx = 8, HandleSlackPx = 3;
    const int HandleSteps = 8;
    const float HandlesFromReaches = 3;
    public const string Gate = "gate", Wing = "wing", Tower = "tower";
    const double LeastSideM = 1.5;
    const float NameBoxWide = 180;
    static readonly Vector2 NameBoxOffset = new(8, -14);
    const string NotYours = "Only umpires build on the map, and not while a past turn is shown.";
    internal const string NoRoom = "No room here: too steep, wet or already built.";
    internal const string TooMuchCountry = "Too much country for one stroke: a smaller brush or a shorter stroke.";
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
    bool _pressedPicked, _alone;
    Vec2? _grabbed;
    int _corner = -1, _middle = -1, _handle = -1;
    bool _chain;
    Vec2? _wing;
    double _least;
    HashSet<string>? _awaited;
    (BuiltThing Thing, CanvasLayer Box, LineEdit Entry)? _naming;
    Task<BuiltThing?>? _drafting;
    (Spot From, Spot To)? _draftFor, _wanted, _draftedFor;
    BuiltThing? _drafted;
    readonly List<(Task<BuiltThing?> Laying, Vec2 At, bool Asks)> _laying = new();
    readonly List<Task<(BuiltThing Laid, bool Again)>> _lining = new();
    Task<BuiltThing>? _lineDraft;

    readonly Dictionary<BuiltThing, List<Lamp>> _lamps = new();

    public BuildTool(TableContext c, ToolOverlay overlay, SheetLayer sheets)
    {
        _c = c;
        _overlay = overlay;
        _sheets = sheets;
        Zone = new BuildZone(c, this);
        _sheets.Built += () =>
        {
            _lamps.Clear();
            if (_awaited is { } known && _c.S.View.Built.LastOrDefault(b => b.Kind is BuiltKinds.Building or BuiltKinds.Settlement && !known.Contains(b.Id)) is { } stood)
            {
                _awaited = null;
                Pick(stood.Id, -1);
            }
            else if (_picked != null) PickChanged?.Invoke();
        };
    }

    public event Action? PickChanged;

    public BuildZone Zone { get; }

    public Func<Vector2, Vec2?>? Roofed { get; set; }

    Vec2 Aimed(Vec2 g) => Roofed?.Invoke(Pointer.In(_c.Scene.GetViewport())) ?? g;

    BuildPrefs P => _c.Settings.Build;

    public BuiltThing? Picked => _picked == null ? null : _c.S.View.Built.FirstOrDefault(b => b.Id == _picked);

    public BuiltPart? PickedLead => Picked is { } thing && _part >= 0 && _part < thing.Parts.Count ? thing.Parts[_part] : null;

    public IReadOnlyList<int> PickedParts => Picked is { } thing && PickedLead != null ? _alone ? [_part] : Groups.Of(thing, _part) : [];

    public BuiltPart? PickedPart => PickedParts.Count == 1 ? PickedLead : null;

    (BuiltThing Thing, int Part, SheetDef Sheet)? Shape()
    {
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire)) return null;
        int part = PickedParts.Count == 1 ? _part : _part < 0 ? PartHandles.Lone(thing) : -1;
        if (part < 0 || !BuiltThings.Stretches(thing.Parts[part])) return null;
        var (wide, deep) = BuiltThings.Sides(thing.Parts[part]);
        return Math.Min(wide, deep) >= _overlay.Px(HandleReachPx) * HandlesFromReaches ? (thing, part, sheet) : null;
    }

    public BuiltPart? Shaped => Shape() is var (thing, part, _) ? thing.Parts[part] : null;

    bool Reshaping(Vec2 g) => Shape() is var (thing, part, sheet) && (Adding == Wing || PartHandles.Near(thing.Parts[part], On(sheet, g), _overlay.Px(HandleReachPx)) >= 0);

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
        if (!BuiltGeometry.Fits(target)) { _c.Say(BuiltGeometry.TooWideToBuild); return false; }
        sheet = target;
        return true;
    }

    public bool Holds(Vec2 g) => Under(g).Thing != null;

    string Doing => P.Activity();

    bool Outlined => Doing == BuildPrefs.Places && P.Place == BuiltKinds.Settlement && P.Outline != BuildPrefs.Round;

    bool Painting => Outlined && P.Outline == BuildPrefs.Painted;

    bool Cornering => Outlined && P.Outline == BuildPrefs.Cornered;

    bool Brushing => Doing is BuildPrefs.Details or BuildPrefs.Lamps or BuildPrefs.Homes or BuildPrefs.Wealth or BuildPrefs.Grow or BuildPrefs.Shrink or BuildPrefs.Peopled or BuildPrefs.KeptClear;

    (BuiltThing? Thing, List<List<Vec2>> Lines) _traced = (null, []);
    (BuiltThing? Thing, Walls.Trace? Trace) _cornered;

    Walls.Trace? Corners(BuiltThing place)
    {
        if (!ReferenceEquals(_cornered.Thing, place))
            _cornered = (place, place.Kind == BuiltKinds.Works ? new Walls.Trace(Works.Corners(place), [], false)
                : Walls.Handles(place) ?? (Outlines.Of(place) is { } ring ? new Walls.Trace(ring, [], false) : null));
        return _cornered.Trace;
    }

    static bool Closes(BuiltThing place) => place.Kind != BuiltKinds.Works || Works.Closed(Works.Course(place));

    static bool Bendable(BuiltThing place) => place.Kind == BuiltKinds.Works || Walls.Of(place) != null || Walls.Handles(place) == null && Outlines.Of(place) != null;

    public void Press(Vec2 g, bool shift, bool picks = false)
    {
        LayNamed();
        if (picks && P.Mode != BuildPrefs.Pick && Under(g).Thing != null)
        {
            Drop();
            P.Do(BuildPrefs.Pick);
            _c.Settings.Save();
            PickChanged?.Invoke();
        }
        if (P.Mode == BuildPrefs.Pick || Doing == BuildPrefs.Things && Reshaping(g) || Doing == BuildPrefs.Places && _path == null && CornerUnder(g) >= 0) { PressPick(g); return; }
        if (Lining && _chain && _path != null && _sheet != null) { Chain(g, shift); return; }
        if (!Ready(g, out var sheet)) return;
        if (Doing is BuildPrefs.Things or BuildPrefs.Places) Pick(null, -1);
        _sheet = sheet;
        _seed = Roll();
        _from = _at = g;
        _ghost = null;
        _chain = Lining && shift;
        if (Doing is not (BuildPrefs.Places or BuildPrefs.Things) || Outlined) _path = new() { g };
    }

    bool Lining => Doing is BuildPrefs.Roads or BuildPrefs.Blocks or BuildPrefs.Works || Cornering;

    bool Ringing => Doing == BuildPrefs.Blocks || Cornering;

    bool Pointing => _chain && _path != null && _sheet != null && Lining;

    void Chain(Vec2 g, bool shift)
    {
        var path = _path!;
        if (Doing != BuildPrefs.Roads && path.Count >= 3 && (g - path[0]).Length <= _overlay.Px(ClosePx))
        {
            LayPath(true);
            return;
        }
        if ((g - path[^1]).Length > _overlay.Px(ClosePx)) path.Add(g);
        if (!shift)
        {
            LayPath(Ringing);
            return;
        }
        Preview(force: true);
    }

    public bool Unpoint()
    {
        if (!Pointing) return false;
        if (_path!.Count > 1)
        {
            _path.RemoveAt(_path.Count - 1);
            Preview(force: true);
        }
        else Clear();
        return true;
    }

    void LayPath(bool closed)
    {
        if (_sheet is not { } sheet || _path is not { Count: >= 2 } path)
        {
            Clear();
            return;
        }
        var course = Course(sheet, path, closed);
        if (Doing == BuildPrefs.Roads) LayLine(course);
        else if (Doing == BuildPrefs.Blocks) LayBlock(course.Take(course.Count - 1).ToList());
        else if (Doing == BuildPrefs.Works) LayWorks(sheet, course, closed);
        else LayOutlined(course.Count >= 4 ? course.Take(course.Count - 1).ToList() : null);
    }

    double Bending => Doing == BuildPrefs.Works ? Works.Bend(Fortified) : Doing == BuildPrefs.Roads ? RoadBend : 1;

    string Fortified => Works.Is(P.Work) ? P.Work : Works.Wall;

    List<Spot> Course(SheetDef sheet, IEnumerable<Vec2> stroke, bool closed)
    {
        var points = stroke.Select(p => On(sheet, p)).ToList();
        if (_chain)
        {
            if (closed && points.Count >= 3) points.Add(points[0]);
            return points;
        }
        if (closed && points.Count >= 3 && (points[^1] - points[0]).Length <= _overlay.Px(ClosePx) * 2) points.RemoveAt(points.Count - 1);
        var straight = Works.Straightened(points, _overlay.Px(StraightenPx) * Bending);
        if (closed && straight.Count >= 3) straight.Add(straight[0]);
        return straight;
    }

    void LayBlock(List<Spot> corners)
    {
        if (_sheet is { } sheet)
        {
            if (BlockCommand(_c.S, sheet, corners, out var refused) is { } drawn) _c.S.Do(drawn);
            else _c.Say(refused!);
        }
        Clear();
    }

    void LayWorks(SheetDef sheet, List<Spot> course, bool closed)
    {
        double reach = _overlay.Px(SnapPx);
        if (WallCommand(_c.S, sheet, Snapped(_c.S, sheet, course, closed, reach), closed, Fortified, P.WorkTowers, P.WorkDitch, Manners.Parse(P.Manner).Lays, reach) is { } done) _c.S.Do(done);
        else _c.Say(NoRoom);
        Clear();
    }

    public static Command? WallCommand(Session s, SheetDef sheet, List<Spot> course, bool closed, string cls, int towers, bool ditch, int manner, double reach)
    {
        bool circuit = cls is Works.Wall or Works.Palisade;
        if (circuit && closed && TownRinged(s, sheet, course, cls, towers, ditch) is { } walled) return new SetBuilt { Thing = walled };
        if (circuit && !closed && Looped(s, sheet, course, towers, reach) is { } looped) return looped;
        var laid = Works.Lay(Site(s, sheet, null, manner), sheet.Id, course, cls, towers, ditch);
        return laid.Parts.Count == 0 ? null : Laying(s, laid);
    }

    static BuiltThing? TownRinged(Session s, SheetDef sheet, List<Spot> course, string cls, int towers, bool ditch)
    {
        var ring = course.Take(course.Count - 1).ToList();
        var place = s.View.Built.Where(b => b.Sheet == sheet.Id && b.Kind == BuiltKinds.Settlement && !BuiltThings.Fixed(b) && b.Setting(Settlements.Walls, 0) < Settlements.BastionedFrom)
            .Where(b => Enclosures.Inside(ring, BuiltThings.Round(b).At)).OrderByDescending(b => BuiltThings.Round(b).Reach).FirstOrDefault();
        return place == null ? null : Walls.Ringed(Site(s, sheet, place.Id), place, ring, cls == Works.Wall, towers, ditch);
    }

    static Command? Looped(Session s, SheetDef sheet, List<Spot> course, int towers, double reach)
    {
        foreach (var place in s.View.Built.Where(b => b.Sheet == sheet.Id && b.Kind == BuiltKinds.Settlement && !BuiltThings.Fixed(b)).ToList())
        {
            if (Walls.Spliced(Site(s, sheet, place.Id), place, course, reach) is not { } spliced) continue;
            var inner = Works.Lay(Site(s, sheet), sheet.Id, spliced.Inner, Walls.Of(place)!.Masonry ? Works.Wall : Works.Palisade, towers, false);
            return new Batch { Items = { new SetBuilt { Thing = spliced.Place }, new LayBuilt { Thing = inner } } };
        }
        return null;
    }

    public static List<Spot> Snapped(Session s, SheetDef sheet, List<Spot> course, bool closed, double reach)
    {
        if (closed || course.Count < 2) return course;
        var walls = s.View.Built.Where(b => b.Sheet == sheet.Id).SelectMany(WallLines).Where(w => w.Count >= 2).ToList();
        Spot Snap(Spot p)
        {
            Spot best = p;
            double least = reach;
            foreach (var wall in walls)
                for (int i = 0; i + 1 < wall.Count; i++)
                {
                    var ab = wall[i + 1] - wall[i];
                    var at = wall[i] + ab * Math.Clamp((p - wall[i]).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
                    if ((at - p).Length >= least) continue;
                    (best, least) = (at, (at - p).Length);
                }
            return best;
        }
        var snapped = course.ToList();
        snapped[0] = Snap(course[0]);
        snapped[^1] = Snap(course[^1]);
        return snapped;
    }

    static IEnumerable<List<Spot>> WallLines(BuiltThing thing) =>
        thing.Kind == BuiltKinds.Works ? [Works.Course(thing)] : Walls.Of(thing) is { } trace ? [trace.Corners.Append(trace.Corners[0]).ToList()] : [];

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
        string id = Fitting(_c.S, P.Block);
        bool dragged = radius > site.M(StandDragM);
        Spot span = to - from, level = Math.Abs(span.X) >= Math.Abs(span.Y) ? new Spot(1, 0) : new Spot(0, 1);
        var parts = P.FromCorner && dragged && Catalogue.Find(id) is { Block: true }
            ? BuiltThings.Building(site, "", id, (from + to) * 0.5, level, Math.Abs(span.Dot(level)), Math.Max(Math.Abs(span.Dot(level.Left)), site.M(LeastSideM))).Parts
            : Traces.Thing(site, id, from, along, dragged ? radius * 2 : 0);
        return Heathen(new BuiltThing { Sheet = sheet.Id, Kind = BuiltKinds.Building, Parts = parts }, Ancient(_c.S));
    }

    void Stand(BuiltThing ghost)
    {
        var (done, into) = StandCommand(_c.S, _sheet!, ghost.Parts, Manners.Parse(P.Manner).Lays);
        _awaited = into == null ? _c.S.View.Built.Select(b => b.Id).ToHashSet() : null;
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
            _hovered = Under(Aimed(g)).Thing;
        }
        if (_sheet == null) return;
        if ((Brushing || Painting) && _path != null && _from != null && (g - _path[^1]).Length > Brush * 0.25f) _path.Add(g);
        else if (Lining && !_chain && _path != null && (g - _path[^1]).Length > _overlay.Px(StrokePx)) _path.Add(g);
        Preview(force: false);
    }

    void Preview(bool force)
    {
        if (_sheet is not { } sheet || _at is not { } at) return;
        if (!force && Time.GetTicksMsec() - _previewed < PreviewEveryMs) return;
        _previewed = Time.GetTicksMsec();
        if (Brushing && Doing != BuildPrefs.Details) return;
        var site = Site(_c.S, sheet, null, Manners.Parse(P.Manner).Lays);
        if ((Painting || Doing == BuildPrefs.Details) && _path is { } painted && _from != null) _ghost = Ringed(sheet, Outlines.Around(painted.Select(p => On(sheet, p)).ToList(), Brush));
        else if (Cornering && _path is { } outline) _ghost = Ringed(sheet, outline.Append(at).Select(p => On(sheet, p)).ToList());
        else if (Doing == BuildPrefs.Places && _from is { } from)
        {
            NameGround(site, On(sheet, from), (On(sheet, at) - On(sheet, from)).Length);
            Draft(site, sheet, On(sheet, from), On(sheet, at));
        }
        else if (Doing == BuildPrefs.Things && _from is { } spot) _ghost = Standing(site, sheet, On(sheet, spot), On(sheet, at));
        else if (Doing == BuildPrefs.Roads && _path is { } path && _lineDraft == null)
        {
            var (course, line, finds, surface) = (Course(sheet, path.Append(at), false), P.Line, P.FindsWay, Looks.Surface(P.Surface));
            _lineDraft = Task.Run(() => BuiltRoads.Lay(site, sheet.Id, course, line, finds, surface));
        }
        else if (Doing == BuildPrefs.Works && _path is { } course)
            _ghost = Works.Lay(site, sheet.Id, Course(sheet, course.Append(at), false), Fortified, P.WorkTowers, P.WorkDitch);
        else if (Doing == BuildPrefs.Blocks && _path is { } corners)
            _ghost = new BuiltThing { Sheet = sheet.Id, Parts = { BuiltSite.Area(Traces.Block, corners.Append(at).Select(p => On(sheet, p))) } };
    }

    static BuiltThing? Ringed(SheetDef sheet, List<Spot>? ring) => ring == null ? null : new BuiltThing { Sheet = sheet.Id, Parts = { BuiltSite.Area(Traces.Block, ring) } };

    internal static float LeastBrush(Session s) =>
        s.View.Calibration.UnitsPerMeter is > 0 and var upm ? Math.Clamp((float)(LeastBrushM * upm), BuildPrefs.FinestBrush, BuildPrefs.SmallestBrush) : BuildPrefs.SmallestBrush;

    internal static float MostBrush(Session s) =>
        Math.Max(LeastBrush(s), BuildPrefs.LargestBrush);

    float Brush => Math.Clamp(P.Brush, LeastBrush(_c.S), MostBrush(_c.S));

    double Walled(BuiltSite site) => !Defences.Bastioned(site.Epoch) && P.Walls >= Settlements.BastionedFrom ? (Settlements.WalledFrom + Settlements.BastionedFrom) / 2 : P.Walls;

    void LayOutlined(List<Spot>? ring)
    {
        if (_sheet is { } sheet && ring != null && _at is { } at)
        {
            var site = Site(_c.S, sheet, null, Manners.Parse(P.Manner).Lays);
            double plan = Plans.Value(Plans.For(site.Manner, P.Plan, Outlines.Framed(ring).Size / site.UnitsPerMetre));
            int seed = _seed;
            double houses = P.Houses, walls = Walled(site);
            bool ancient = Ancient(_c.S);
            var shape = new Settlements.Shape(P.Spread, P.Order);
            Later(() => Settlements.Lay(site, sheet.Id, ring, seed, houses, walls, plan, ancient, shape), at, P.Naming == BuildPrefs.Typed);
        }
        Clear();
    }

    internal bool Busy => _laying.Count > 0 || _lining.Count > 0 || _drafting != null;

    void Later(Func<BuiltThing?> lay, Vec2 at, bool asks) => _laying.Add((Task.Run(lay), at, asks));

    void Draft(BuiltSite site, SheetDef sheet, Spot from, Spot to)
    {
        _wanted = (from, to);
        if (_drafted != null && _draftedFor == _wanted) _ghost = Mixed(_drafted.Clone());
        else if (_drafting == null)
        {
            _draftFor = _wanted;
            _drafting = Task.Run(Placer(site, sheet, from, to));
        }
    }

    void Settle()
    {
        if (_drafting is { IsCompleted: true } drafted)
        {
            _drafting = null;
            if (_sheet != null && _wanted != null)
            {
                _drafted = Made(drafted, false);
                _draftedFor = _draftFor;
                _ghost = Mixed(_drafted?.Clone());
                if (_draftedFor != _wanted) Preview(force: true);
            }
        }
        for (int i = _laying.Count - 1; i >= 0; i--)
        {
            var (laying, at, asks) = _laying[i];
            if (!laying.IsCompleted) continue;
            _laying.RemoveAt(i);
            if (Made(laying) is not { Parts.Count: > 0 } laid) _c.Say(NoRoom);
            else if (asks && BuiltThings.Named(laid) && _naming == null) AskName(laid, at);
            else Lay(laid);
        }
        if (_lineDraft is { IsCompleted: true } drawn)
        {
            _lineDraft = null;
            if (drawn.Exception == null && Doing == BuildPrefs.Roads && _path != null) _ghost = Mixed(drawn.Result);
        }
        for (int i = _lining.Count - 1; i >= 0; i--)
        {
            var lining = _lining[i];
            if (!lining.IsCompleted) continue;
            _lining.RemoveAt(i);
            if (lining.Exception is { } failed) GD.PushError($"[build] laying failed: {failed.InnerException?.Message}");
            else if (lining.Result is { Laid.Parts.Count: > 0 } done) _c.S.Do(Laying(_c.S, done.Again ? done.Laid : Mixed(done.Laid)!, done.Again));
        }
    }

    BuiltThing? Made(Task<BuiltThing?> done, bool mixed = true)
    {
        if (done.Exception is { } failed)
        {
            GD.PushError($"[build] laying failed: {failed.InnerException?.Message}");
            return null;
        }
        return mixed ? Mixed(done.Result) : done.Result;
    }

    static BuiltThing Circle(SheetDef sheet, Spot centre, double radius) =>
        new() { Sheet = sheet.Id, Parts = { BuiltSite.Area(Traces.Block, Enumerable.Range(0, (int)RingSteps).Select(i => centre + Spot.Toward(i * Math.Tau / RingSteps) * radius)) } };

    void Lay(BuiltThing laid)
    {
        _awaited = laid.Kind == BuiltKinds.Settlement ? _c.S.View.Built.Select(b => b.Id).ToHashSet() : null;
        _c.S.Do(Laying(_c.S, laid));
    }

    void Regrow()
    {
        var stroke = _path;
        if (_at is { } last) stroke?.Add(last);
        bool grows = Doing == BuildPrefs.Grow;
        Clear();
        if (stroke == null || !_c.S.CanEdit(Layers.Umpire) || !Scaled()) return;
        foreach (var place in _c.S.View.Built.Where(b => b.Kind == BuiltKinds.Settlement && !BuiltThings.Fixed(b)).ToList())
        {
            if (SheetOf(place.Sheet) is not { } sheet) continue;
            var path = stroke.Select(p => On(sheet, p)).ToList();
            var (middle, reach) = BuiltThings.Round(place);
            var was = Outlines.Of(place) ?? Outlines.Circle(middle, reach);
            if (!path.Any(p => Outlines.Depth(was, p) > -Brush)) continue;
            Reoutline(place, sheet, grows ? Outlines.Grown(was, path, Brush) : Outlines.Shrunk(was, path, Brush));
            return;
        }
    }

    void Reoutline(BuiltThing place, SheetDef sheet, List<Spot>? ring)
    {
        if (ring == null) return;
        var changed = place.Clone();
        changed.Outline = Outlines.Stored(Outlines.Framed(ring), ring);
        _c.S.Do(new SetBuilt { Thing = BuiltThings.Remade(Site(_c.S, sheet, place.Id), changed, place.Seed) });
    }

    BuiltThing? Mixed(BuiltThing? laid)
    {
        if (laid != null && (_c.S.View.Cultures?.Any(mix => mix.Code == P.Manner) ?? false)) Manners.Parse(P.Manner).Onto(laid);
        return laid;
    }

    Func<BuiltThing?> Placer(BuiltSite site, SheetDef sheet, Spot from, Spot to)
    {
        double radius = (to - from).Length;
        var along = radius > 1e-6 ? (to - from).Unit : new Spot(1, 0);
        if (radius < site.M(LeastPlaceM)) return () => null;
        double heading = Math.Atan2(along.Y, along.X);
        int seed = _seed, bastions = P.Bastions, castle = CastlePlan(site.Manner);
        double outworks = P.Outworks;
        if (P.Place == BuildPrefs.Redoubt && Defences.Fieldwork(site.Manner) is { } work) return () => Defences.Lay(site, sheet.Id, work.Id, from, radius, seed, 0, outworks, heading);
        bool ancient = Ancient(_c.S);
        if (P.Place is BuiltKinds.Fort or BuildPrefs.Redoubt)
        {
            string fort = Defences.Fort(site.Manner).Id;
            return () => Defences.Lay(site, sheet.Id, fort, from, radius, seed, bastions, outworks, heading, plan: castle);
        }
        double houses = P.Houses, walls = Walled(site), plan = Plans.Value(Plans.For(site.Manner, P.Plan, radius / site.UnitsPerMetre));
        var shape = new Settlements.Shape(P.Spread, P.Order, heading);
        return () => Settlements.Lay(site, sheet.Id, from, radius, seed, houses, walls, plan, ancient, null, shape);
    }

    int CastlePlan(int manner) => P.CastlePlan >= 0 && P.CastlePlan < Castles.Plans.Count && Castles.PlansOf(manner).Contains(Castles.Plans[P.CastlePlan]) ? P.CastlePlan : -1;

    public void Release()
    {
        if (_grabbed != null) { DropGrab(); return; }
        if (_sheet == null) return;
        if (Lining)
        {
            if (_chain || _path == null) return;
            if (_at is { } let) _path.Add(let);
            if (_path.Max(p => (p - _path[0]).Length) <= _overlay.Px(LeastPlacePx))
            {
                Clear();
                return;
            }
            LayPath(Ringing || Doing == BuildPrefs.Works && _path.Count >= 3 && (_path[^1] - _path[0]).Length <= _overlay.Px(ClosePx) * 2);
            return;
        }
        if (Doing == BuildPrefs.Lamps) { SwitchLamps(); return; }
        if (Doing is BuildPrefs.Peopled or BuildPrefs.KeptClear) { CrowdBrush.Stroke(_c, Stroke(), P); return; }
        if (Doing == BuildPrefs.Homes) { SweepHouses(); return; }
        if (Doing == BuildPrefs.Wealth) { Zone.Brush(Stroke(), Brush, P.Richer); return; }
        if (Doing is BuildPrefs.Grow or BuildPrefs.Shrink) { Regrow(); return; }
        if (Doing == BuildPrefs.Details) { LayCountry(); return; }
        if (Painting)
        {
            if (_at is { } tip) _path?.Add(tip);
            LayOutlined(_path == null ? null : Outlines.Around(_path.Select(p => On(_sheet, p)).ToList(), Brush));
            return;
        }
        if (Doing == BuildPrefs.Places && (_from is not { } start || _at is not { } end || (end - start).Length <= _overlay.Px(LeastPlacePx)))
        {
            Clear();
            return;
        }
        if (Doing == BuildPrefs.Places && _from is { } begun && _at is { } ended)
        {
            LayPlace(_sheet, On(_sheet, begun), On(_sheet, ended), ended);
            Clear();
            return;
        }
        Preview(force: true);
        if (_ghost is { Parts.Count: > 0 } stood && Doing == BuildPrefs.Things) Stand(stood);
        else if (_ghost is { Parts.Count: > 0 } laid && _at is { } at)
        {
            bool named = Doing == BuildPrefs.Places && P.Place != BuildPrefs.Redoubt && BuiltThings.Named(laid);
            if (named && P.Naming == BuildPrefs.Typed) { AskName(laid, at); return; }
            Lay(laid);
        }
        else if (Doing == BuildPrefs.Places && _ghost is { Parts.Count: 0 }) _c.Say(NoRoom);
        Clear();
    }

    void LayPlace(SheetDef sheet, Spot from, Spot to, Vec2 at)
    {
        var site = Site(_c.S, sheet, null, Manners.Parse(P.Manner).Lays);
        bool asks = P.Place != BuildPrefs.Redoubt && P.Naming == BuildPrefs.Typed;
        if (_drafted != null && _draftedFor == (from, to)) _laying.Add((Task.FromResult<BuiltThing?>(_drafted), at, asks));
        else Later(Placer(site, sheet, from, to), at, asks);
    }

    void LayCountry()
    {
        if (_sheet is { } sheet && _path is { } stroke && _from != null)
        {
            var site = Site(_c.S, sheet, null, Manners.Parse(P.Manner).Lays);
            var spots = stroke.Select(p => On(sheet, p)).ToList();
            if (BuiltDetails.TooMuch(site, spots, Brush, P.Plots, P.Buildings, P.Shares)) _c.Say(TooMuchCountry);
            else if (BuiltDetails.Lay(site, sheet.Id, spots, Brush, _seed, P.Plots, P.Buildings, P.Shares) is { Parts.Count: > 0 } laid) Lay(laid);
        }
        Clear();
    }

    void AskName(BuiltThing laid, Vec2 at)
    {
        _from = null;
        var entry = new LineEdit { PlaceholderText = "Name", MaxLength = PlaceNames.MostLetters, CustomMinimumSize = Ui.Px(NameBoxWide, 0) };
        var box = new CanvasLayer();
        box.AddChild(entry);
        _c.Scene.AddChild(box);
        entry.Position = _c.Rig.Screen(Coords.ToWorld(at, _c.SheetsTop)) + Ui.Px(NameBoxOffset);
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
        Lay(thing);
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

    void LayLine(List<Spot> asked)
    {
        if (_sheet is { } sheet && asked.Count >= 2)
        {
            int lays = Manners.Parse(P.Manner).Lays;
            var olds = _c.S.View.Built.Where(b => b.Sheet == sheet.Id && b.Kind == BuiltKinds.Road && !BuiltThings.Fixed(b)).Select(old => (Old: old, Site: Site(_c.S, sheet, old.Id, lays))).ToList();
            var site = Site(_c.S, sheet, null, lays);
            var (line, finds, surface, ancient) = (P.Line, P.FindsWay, Looks.Surface(P.Surface), Ancient(_c.S));
            _lining.Add(Task.Run(() => olds.Select(o => BuiltRoads.Extended(o.Site, o.Old, asked, line, finds, surface)).FirstOrDefault(made => made != null) is { } longer
                ? (longer, true) : (Heathen(BuiltRoads.Lay(site, sheet.Id, asked, line, finds, surface), ancient), false)));
        }
        Clear();
    }

    List<Vec2>? Stroke()
    {
        var stroke = _path;
        if (_at is { } last) stroke?.Add(last);
        Clear();
        return stroke;
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
            if (path.All(p => (p - middle).Length > reach + Brush)) continue;
            var site = Site(_c.S, sheet, place.Id);
            if ((P.Thins ? Brushed.Thinned(site, place, path, Brush, Roll()) : Brushed.Filled(site, place, path, Brush, Roll())) is { } done) batch.Items.Add(new SetBuilt { Thing = done });
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
            if (Lamps.Switched(thing, epoch, perMetre, stroke.Select(p => On(sheet, p)).ToList(), Brush, P.LampsOn) is { } switched)
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
        double near = Brush * LampsShownBrushes;
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

    public static Command Laying(Session s, BuiltThing laid, bool again = false)
    {
        Command lay = again ? new SetBuilt { Thing = laid } : new LayBuilt { Thing = laid };
        var sheet = s.View.Sheets.FirstOrDefault(x => x.Id == laid.Sheet);
        var batch = new Batch { Items = { lay } };
        if (laid.Kind == BuiltKinds.Settlement && sheet != null)
            foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet).ToList())
                if (Walls.Joined(Site(s, sheet, other.Id), other, laid) is { } gated) batch.Items.Add(new SetBuilt { Thing = gated });
        var stroke = laid.Kind == BuiltKinds.Details ? BuiltGeometry.Points(laid.Outline).Select(Spot.Of).ToList() : [];
        foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet && b.Kind == BuiltKinds.Details && b.Id != laid.Id).ToList())
            if (BuiltDetails.Cleared(other, stroke, laid.Setting(BuiltDetails.Radius, 0)) is { } cleared)
                batch.Items.Insert(0, cleared.Parts.Count == 0 ? new RemoveBuilt { Id = other.Id } : new SetBuilt { Thing = cleared });
        double perMetre = s.View.Calibration.UnitsPerMeter ?? 0;
        if (laid.Kind == BuiltKinds.Works)
        {
            foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet && b.Id != laid.Id).ToList())
                if (Works.Cleared(other, laid, perMetre) is { } clear) batch.Items.Add(new SetBuilt { Thing = clear });
            return batch.Items.Count == 1 ? lay : batch;
        }
        if (laid.Kind is not (BuiltKinds.Road or BuiltKinds.Water)) return batch.Items.Count == 1 ? lay : batch;
        foreach (var other in s.View.Built.Where(b => b.Sheet == laid.Sheet && b.Id != laid.Id).ToList())
        {
            var cleared = BuiltRoads.MadeWay(other, laid, perMetre);
            var fronted = sheet != null && other.Kind == BuiltKinds.Settlement ? Settlements.Fronted(Site(s, sheet, other.Id), cleared ?? other, laid) : null;
            var gated = sheet == null ? null
                : other.Kind == BuiltKinds.Works ? Works.Opened(Site(s, sheet, other.Id), other, laid) : Walls.Opened(Site(s, sheet, other.Id), fronted ?? cleared ?? other, laid);
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
        _drafting = null;
        _drafted = null;
        _draftFor = _wanted = _draftedFor = null;
        _sheet = null;
        _from = null;
        _path = null;
        _ghost = null;
        _ground = null;
        _chain = false;
    }

    public bool Drop()
    {
        bool open = _sheet != null || _grabbed != null || _naming != null || _laying.Count > 0 || _lining.Count > 0;
        if (_naming is var (_, box, _))
        {
            _naming = null;
            box.QueueFree();
        }
        _laying.Clear();
        _lining.Clear();
        Clear();
        _grabbed = null;
        _corner = _middle = _handle = -1;
        _wing = null;
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

    void Pick(string? id, int part, bool alone = false)
    {
        if (_picked == id && _part == part && _alone == alone) return;
        _picked = id;
        _part = part;
        _alone = alone;
        PickChanged?.Invoke();
    }

    public bool PickedAlone => _alone && PickedLead != null;

    public void PickWhole() => Pick(_picked, -1);

    public void PickBuilding() => Pick(_picked, _part);

    void PickNext(int pressed)
    {
        var parts = PickedParts;
        if (pressed < 0 || _alone && pressed == _part) Pick(_picked, -1);
        else if (parts.Contains(pressed)) Pick(_picked, parts.Count > 1 ? pressed : -1, parts.Count > 1);
        else Pick(_picked, pressed);
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

    int CornerUnder(Vec2 g) => Picked is { } place && _part < 0 && _c.S.CanEdit(Layers.Umpire) && SheetOf(place.Sheet) is { } sheet && Corners(place) is { } trace
        ? Walls.CornerNear(trace, On(sheet, g), _overlay.Px(PickPx)) : -1;

    int MiddleUnder(Vec2 g) => Picked is { } place && _part < 0 && _c.S.CanEdit(Layers.Umpire) && SheetOf(place.Sheet) is { } sheet && Bendable(place) && Corners(place) is { } trace
        ? Works.MiddleNear(trace.Corners, Closes(place), On(sheet, g), _overlay.Px(PickPx)) : -1;

    void PressPick(Vec2 g)
    {
        _at = g;
        if (Add(g)) return;
        if (Shape() is var (shaped, part, on))
        {
            _least = Site(_c.S, on, shaped.Id).M(LeastSideM);
            _wing = Adding == Wing ? g : null;
            _handle = _wing == null ? PartHandles.Near(shaped.Parts[part], On(on, g), _overlay.Px(HandleReachPx)) : -1;
        }
        _corner = _handle >= 0 || _wing != null ? -1 : CornerUnder(g);
        _middle = _handle >= 0 || _wing != null || _corner >= 0 ? -1 : MiddleUnder(g);
        var (best, bestPart) = _corner >= 0 || _middle >= 0 ? (_picked, -1) : Under(Aimed(g));
        _pressedPicked = best != null && best == _picked;
        _pressedPart = bestPart;
        if (_handle >= 0 || _wing != null)
        {
            _grabbed = g;
            return;
        }
        if (!_pressedPicked) Pick(best, -1);
        _grabbed = best != null && _c.S.CanEdit(Layers.Umpire) ? g : null;
        _at = g;
    }

    void DropGrab()
    {
        var from = _grabbed!.Value;
        int corner = _corner, middle = _middle, handle = _handle;
        var wing = _wing;
        _grabbed = null;
        _corner = _middle = _handle = -1;
        _wing = null;
        if ((handle >= 0 || wing != null) && _at is { } pulled && (pulled - from).Length > _overlay.Px(HandleSlackPx))
        {
            if (Shape() is var (shaped, part, on)) Reshape(shaped, part, on, handle, wing, pulled);
            return;
        }
        if (handle >= 0 && P.Mode != BuildPrefs.Pick)
        {
            StandAt(from);
            return;
        }
        if (_at is not { } to || (to - from).Length <= _overlay.Px(LeastPlacePx))
        {
            if (_pressedPicked) PickNext(_pressedPart);
            return;
        }
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet) return;
        if (corner >= 0 || middle >= 0)
        {
            Bend(thing, sheet, corner, middle, On(sheet, to));
            return;
        }
        var a = BuiltGeometry.ToSheet(sheet, from);
        var b = BuiltGeometry.ToSheet(sheet, to);
        var moved = PickedParts is { Count: > 0 } parts ? BuiltThings.WithPartsMoved(thing, parts, b.X - a.X, b.Y - a.Y) : BuiltGeometry.Moved(thing, b.X - a.X, b.Y - a.Y);
        _c.S.Do(new SetBuilt { Thing = moved });
    }

    void Bend(BuiltThing thing, SheetDef sheet, int corner, int middle, Spot to)
    {
        if (Corners(thing) is not { } trace) return;
        var corners = trace.Corners;
        int onto = corner >= 0 ? Neighbour(corners, corner, Closes(thing), to) : -1;
        var site = Site(_c.S, sheet, thing.Id);
        BuiltThing? bent;
        if (thing.Kind == BuiltKinds.Works)
            bent = middle >= 0 ? Works.CornerAdded(site, thing, middle, to) : onto >= 0 ? Works.CornerRemoved(site, thing, corner) : Works.CornerMoved(site, thing, corner, to);
        else if (Walls.Of(thing) != null)
            bent = middle >= 0 ? Walls.CornerAdded(site, thing, middle, to) : onto >= 0 ? Walls.CornerRemoved(site, thing, corner) : Walls.CornerMoved(site, thing, corner, to);
        else if (Walls.Handles(thing) != null) bent = corner >= 0 ? Walls.CornerMoved(site, thing, corner, to) : null;
        else
        {
            var ring = corners.ToList();
            if (middle >= 0) ring.Insert(middle + 1, to);
            else if (onto >= 0 && ring.Count > 3) ring.RemoveAt(corner);
            else ring[corner] = to;
            Reoutline(thing, sheet, ring);
            return;
        }
        if (bent != null) _c.S.Do(new SetBuilt { Thing = bent });
    }

    int Neighbour(IReadOnlyList<Spot> corners, int corner, bool ring, Spot to)
    {
        foreach (int side in new[] { corner - 1, corner + 1 })
        {
            int other = ring ? (side + corners.Count) % corners.Count : side;
            if (other < 0 || other >= corners.Count || other == corner) continue;
            double reach = Math.Min(_overlay.Px(PickPx), (corners[other] - corners[corner]).Length * OntoShare);
            if ((corners[other] - to).Length < reach) return other;
        }
        return -1;
    }

    void StandAt(Vec2 g)
    {
        if (!Ready(g, out var sheet)) return;
        Pick(null, -1);
        _sheet = sheet;
        _seed = Roll();
        _from = _at = g;
        _ghost = null;
        Release();
    }

    void Reshape(BuiltThing shaped, int part, SheetDef on, int handle, Vec2? wing, Vec2 to)
    {
        if (wing is { } from)
        {
            if (PartHandles.WithWing(shaped, part, On(on, from), On(on, to), _least) is { } winged) _c.S.Do(new SetBuilt { Thing = winged });
            Adding = null;
            PickChanged?.Invoke();
        }
        else _c.S.Do(new SetBuilt { Thing = PartHandles.WithPulled(shaped, part, handle, On(on, to), _least) });
    }

    public bool Delete()
    {
        if (_c.Tool != Tool.Build || Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire)) return false;
        var parts = PickedParts;
        bool some = parts.Count > 0 && thing.Parts.Count > parts.Count;
        if (some && SheetOf(thing.Sheet) is { } sheet) _c.S.Do(new SetBuilt { Thing = Traces.Without(Site(_c.S, sheet, thing.Id), thing, parts) });
        else _c.S.Do(new RemoveBuilt { Id = thing.Id });
        Pick(some ? thing.Id : null, -1);
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
        if (Shape() is var (thing, part, _)) _c.S.Do(new SetBuilt { Thing = BuiltThings.WithPartStretched(thing, part, sides.Long, sides.Deep) });
    }

    public void Pave(string? surface)
    {
        if (Picked is not { } thing || PickedLead is not { } lead || !Looks.Paved(lead) || !_c.S.CanEdit(Layers.Umpire) || lead.Surface == Looks.Surface(surface)) return;
        _c.S.Do(new SetBuilt { Thing = Looks.WithSurface(thing, _part, surface) });
    }

    public void Look(int storeys, string? roof, string? cover)
    {
        if (Picked is not { } thing || PickedLead is not { } lead || !Looks.Takes(lead) || !_c.S.CanEdit(Layers.Umpire)) return;
        if (lead.Storeys == storeys && lead.Roof == roof && lead.Cover == cover) return;
        _c.S.Do(new SetBuilt { Thing = Looks.With(thing, _part, storeys, roof, cover) });
    }

    public bool Kept => Picked is { } thing && (PickedParts is { Count: > 0 } parts ? parts.All(i => thing.Parts[i].Kept) : BuiltThings.Fixed(thing));

    public void Keep(bool on)
    {
        if (Picked is not { } thing || !_c.S.CanEdit(Layers.Umpire) || Kept == on) return;
        _c.S.Do(new SetBuilt { Thing = BuiltThings.WithKept(thing, PickedParts, on) });
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
        _c.S.Do(new SetBuilt { Thing = PickedParts is { Count: > 0 } parts ? BuiltThings.WithPartsTurned(thing, parts, radians) : BuiltThings.Turned(thing, radians) });
        return true;
    }

    bool Add(Vec2 g)
    {
        if (Adding is not (Gate or Tower) || Picked is not { } place || SheetOf(place.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire)) return false;
        var site = Site(_c.S, sheet, place.Id);
        var at = On(sheet, g);
        double reach = _overlay.Px(PickPx);
        bool works = place.Kind == BuiltKinds.Works;
        var changed = Adding == Gate ? works ? Works.Gated(site, place, at, reach) : Walls.Gated(site, place, at, reach)
            : works ? Works.Towered(place, at, reach) : Walls.Towered(place, at, reach);
        if (changed != null) _c.S.Do(new SetBuilt { Thing = changed });
        return true;
    }

    public void Rework(string? cls = null, int? towers = null, bool? ditch = null)
    {
        if (Picked is not { } thing || SheetOf(thing.Sheet) is not { } sheet || !_c.S.CanEdit(Layers.Umpire) || !Scaled()) return;
        var site = Site(_c.S, sheet, thing.Id);
        var changed = thing.Kind == BuiltKinds.Works ? Works.Reworked(site, thing, cls, towers, ditch)
            : Walls.Of(thing) is { } trace ? Walls.Optioned(site, thing, cls == null ? trace.Masonry : cls == Works.Wall, towers ?? trace.Towers, ditch ?? trace.Ditch) : null;
        if (changed != null) _c.S.Do(new SetBuilt { Thing = changed });
    }

    public void Resize(bool up)
    {
        if ((P.Mode == BuildPrefs.Pick || Doing == BuildPrefs.Things) && Picked is { } thing && PickedParts is { Count: > 0 } parts && _c.S.CanEdit(Layers.Umpire))
        {
            _c.S.Do(new SetBuilt { Thing = BuiltThings.WithPartsSized(thing, parts, up ? PartStep : 1 / PartStep) });
            return;
        }
        P.Brush = Math.Clamp(Brush * (up ? 1.15f : 1 / 1.15f), LeastBrush(_c.S), MostBrush(_c.S));
        _c.Settings.Save();
    }

    public string Hint() => Doing switch
    {
        BuildPrefs.Roads when _chain => "Shift+click adds a waypoint, a plain click lays the last" + PointsHint,
        BuildPrefs.Roads => "Drag the road's course · release lays it · Shift+click sets exact waypoints · Alt+click picks what is built",
        BuildPrefs.Works when _chain => "Shift+click adds a corner, a plain click lays the last, a click on the start closes it" + PointsHint,
        BuildPrefs.Works => "Drag its course · release lays it, on its start it closes · Shift+click sets exact corners · Alt+click picks what is built",
        BuildPrefs.Blocks when _chain => "Shift+click adds a corner, a plain click on the last or the start closes it" + PointsHint,
        BuildPrefs.Blocks => "Drag round the block · release lays it · Shift+click sets exact corners · Alt+click picks what is built",
        BuildPrefs.Things when Adding == Wing => WingHint,
        BuildPrefs.Things => (P.FromCorner ? "Click to stand it, or drag from corner to corner" : "Click to stand it, or drag its length") + " · drag a handle to reshape it · Q and E turn it",
        BuildPrefs.Details => "Drag over the country to settle it · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Homes => (P.Thins ? "Drag over houses to thin them" : "Drag along the streets to fill them with houses") + " · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Lamps => (P.LampsOn ? "Drag over dark lamps to light them" : "Drag over lamps to put them out") + " · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Wealth => (P.Richer ? "Drag over houses to make them richer" : "Drag over houses to make them poorer") + " · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Peopled => $"Drag over houses and workplaces to set them to {(P.Level == 0 ? "nobody" : P.Level + "%")} · = and − or Ctrl+wheel size the brush",
        BuildPrefs.KeptClear => (P.Clearing ? "Drag over ground to keep crowds out of it" : "Drag over cleared ground to open it again") + " · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Pick when Adding == Gate => "Click the wall for a gate, or a gate to close it",
        BuildPrefs.Pick when Adding == Tower => "Click the wall for a tower, or a tower to take it down",
        BuildPrefs.Pick when Adding == Wing => WingHint,
        BuildPrefs.Pick when Shape() != null => "Drag a side or a corner to reshape it, the building to move it · Q and E turn it · Delete removes it",
        BuildPrefs.Pick when Picked is { Kind: BuiltKinds.Works } && _part < 0 => "Drag moves it · " + BendHint + " · Delete removes it",
        BuildPrefs.Pick when Picked is { } walled && _part < 0 && Walls.Of(walled) != null => "Drag moves the place · on its wall, " + BendHint + " · Q and E turn it · Delete removes it",
        BuildPrefs.Pick when Picked is { } starred && _part < 0 && Walls.Handles(starred) != null => "Drag moves the place, a corner of its wall moves the wall · Q and E turn it · Delete removes it",
        BuildPrefs.Pick when Picked is { } bound && _part < 0 && Outlines.Of(bound) != null => "Drag moves the place · on its outline, " + BendHint + " · Q and E turn it · Delete removes it",
        BuildPrefs.Grow => "Drag over the ground a settlement should take · = and − or Ctrl+wheel size the brush",
        BuildPrefs.Shrink => "Drag over the ground a settlement should give up · = and − or Ctrl+wheel size the brush",
        _ when Painting => "Drag the brush over the ground the settlement should fill · release lays it · = and − or Ctrl+wheel size the brush",
        _ when Cornering && _chain => "Shift+click adds a corner, a plain click on the last or the start lays it" + PointsHint,
        _ when Cornering => "Drag round its outline · release lays it · Shift+click sets exact corners",
        BuildPrefs.Pick => "Click a built thing to pick it, again for one building, again for one part · drag moves it · Q and E turn it, = and − size it · Delete removes it",
        _ => P.Place == BuiltKinds.Settlement ? "Drag from the middle outward, along the line its main street should take · release lays it"
            : "Drag from the middle outward · release lays it",
    };

    const string PointsHint = " · Backspace or Ctrl+Z takes the last point back · Esc drops it";
    const string BendHint = "drag a corner to move it, the middle of a side for a new corner, a corner onto the next to take it out";
    const string WingHint = "Drag out from a side of the building for a wing";

    public void Draw()
    {
        Settle();
        if (_c.Tool != Tool.Build || _c.Rig == null) return;
        var mat = new StandardMaterial3D { AlbedoColor = Brass, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        if (_ghost != null && _sheet != null) Outline(mat, _sheet, _ghost, GhostPx, default);
        if ((Brushing || Painting) && _at is { } at) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(at, Brush), Lift, GhostPx);
        if (Lining && Doing != BuildPrefs.Roads && _path is { Count: >= 3 } outline) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(outline[0], _overlay.Px(ClosePx)), Lift, GhostPx);
        if (Doing == BuildPrefs.Blocks)
        {
            foreach (var thing in _c.S.View.Built)
                if (SheetOf(thing.Sheet) is { } blocked)
                    foreach (var block in thing.Parts.Where(Traces.IsBlock)) Trace(mat, blocked, block, GhostPx, default);
        }
        if (Doing == BuildPrefs.Lamps && _at is { } brushed) DrawLamps(brushed);
        if (P.Mode != BuildPrefs.Pick && Doing is not (BuildPrefs.Things or BuildPrefs.Places)) return;
        bool quiet = Doing == BuildPrefs.Places;
        var hovered = _hovered == null || _hovered == _picked ? null : _c.S.View.Built.FirstOrDefault(b => b.Id == _hovered);
        if (hovered != null && SheetOf(hovered.Sheet) is { } under) Outline(mat, under, hovered, GhostPx, default);
        if (Picked is not { } picked || SheetOf(picked.Sheet) is not { } on) return;
        var shift = _grabbed is { } from && _at is { } to && _corner < 0 && _handle < 0 && _wing == null ? to - from : default;
        if (Shape() is var (shaped, lone, _)) DrawShape(mat, on, shaped.Parts[lone], shift);
        if (PickedParts is { Count: > 0 } parts)
            foreach (int part in parts) Trace(mat, on, picked.Parts[part], PickedPx, shift);
        else if (!quiet) Outline(mat, on, picked, PickedPx, shift);
        if (_part >= 0 || Adding != null || Corners(picked) is not { } trace) return;
        bool ring = Closes(picked);
        var corners = trace.Corners.ToList();
        if (_grabbed != null && _at is { } pulled)
        {
            if (_corner >= 0 && _corner < corners.Count) corners[_corner] = On(on, pulled);
            else if (_middle >= 0 && _middle < corners.Count) corners.Insert(_middle + 1, On(on, pulled));
        }
        _overlay.RibbonsPx(_overlay.Mesh, mat, corners.Select(corner => Ring(Off(on, corner) + shift, _overlay.Px(HandlePx), HandleSteps)).ToList(), Lift, GhostPx);
        if (Bendable(picked) && _corner < 0 && _middle < 0)
            _overlay.RibbonsPx(_overlay.Mesh, mat, Works.Middles(corners, ring).Select(middle => Ring(Off(on, middle) + shift, _overlay.Px(MiddlePx), HandleSteps)).ToList(), Lift, GhostPx);
        if ((_corner >= 0 || _middle >= 0) && _grabbed != null)
            _overlay.RibbonPx(_overlay.Mesh, mat, (ring ? corners.Append(corners[0]) : corners).Select(p => Off(on, p)).ToList(), Lift, PickedPx);
    }

    void DrawShape(StandardMaterial3D mat, SheetDef on, BuiltPart part, Vec2 shift)
    {
        if (_grabbed != null && _at is { } to)
        {
            var ring = _wing is { } from ? PartHandles.Wing(part, On(on, from), On(on, to), _least) is { } wing ? BuiltGeometry.Points(wing.At).Select(Spot.Of).ToList() : null
                : _handle >= 0 ? PartHandles.Pulled(part, _handle, On(on, to), _least) : null;
            if (ring != null) _overlay.RibbonPx(_overlay.Mesh, mat, ring.Append(ring[0]).Select(p => Off(on, p)).ToList(), Lift, PickedPx);
            if (ring != null || _handle >= 0 || _wing != null) return;
        }
        if (Adding == Wing) return;
        foreach (var handle in PartHandles.Of(part)) _overlay.RibbonPx(_overlay.Mesh, mat, Ring(Off(on, handle) + shift, _overlay.Px(HandlePx)), Lift, GhostPx);
    }

    const int MostPartsTracedWhole = 150;

    void Outline(StandardMaterial3D mat, SheetDef sheet, BuiltThing thing, float px, Vec2 shift)
    {
        if (!ReferenceEquals(_traced.Thing, thing))
        {
            bool whole = thing.Parts.Count <= MostPartsTracedWhole;
            var parts = whole ? thing.Parts.Where(part => part.Class != BuiltThings.Quarter) : thing.Parts.Where(part => part.Class == BuiltThings.Quarter || Walls.Circuit(part));
            _traced = (thing, parts.Select(part => Traced(sheet, part, default)).ToList());
            if (_traced.Lines.Count == 0 && BuiltThings.Round(thing) is { Reach: > 0 } round) _traced.Lines.Add(Ring(Off(sheet, round.At), round.Reach));
        }
        var lines = shift == default ? _traced.Lines : _traced.Lines.Select(line => line.Select(p => p + shift).ToList()).ToList();
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

    static List<Vec2> Ring(Vec2 c, double r, int steps = (int)RingSteps)
    {
        var ring = new List<Vec2>();
        for (int i = 0; i <= steps; i++)
        {
            double a = i * Math.Tau / steps;
            ring.Add(new Vec2(c.X + Math.Cos(a) * r, c.Z + Math.Sin(a) * r));
        }
        return ring;
    }
}
