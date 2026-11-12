using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public enum Tool { Select, Transform, Place, Ruler, Draw, Erase, Calibrate, Sighting, Capture, Zone, Crop, Range, Shape, Relief, Formation, Letter, Decal }

public partial class Table : Node3D
{
    readonly TableContext _c;
    public Session S => _c.S;
    public Settings Settings => _c.Settings;
    public CameraRig Rig => _c.Rig;
    internal TableSounds Sound => _c.Sound;
    public Tool Tool => _c.Tool;
    public string? PlaceDef => _c.PlaceDef;
    public Rect2? LastCaptureRect { get => Router.LastCaptureRect; internal set => Router.LastCaptureRect = value; }

    public event Action? SelectionChanged { add => _c.SelectionChanged += value; remove => _c.SelectionChanged -= value; }
    public event Action? ToolChanged;
    public event Action<string>? Hint { add => _c.Hint += value; remove => _c.Hint -= value; }
    public event Action<Rect2?>? ScreenRect { add => _c.ScreenRect += value; remove => _c.ScreenRect -= value; }
    public event Action<PieceNode, int>? ContentsAsked { add => _c.ContentsAsked += value; remove => _c.ContentsAsked -= value; }
    public event Action<double>? CalibrationMeasured { add => Router.CalibrationMeasured += value; remove => Router.CalibrationMeasured -= value; }
    public event Action? SightingChanged { add => Sighting.Changed += value; remove => Sighting.Changed -= value; }
    public event Action? SightingRecounted { add => Sighting.Recounted += value; remove => Sighting.Recounted -= value; }
    public event Action? MenuRequested { add => Keys.MenuRequested += value; remove => Keys.MenuRequested -= value; }
    public event Action? PublishRequested { add => Keys.PublishRequested += value; remove => Keys.PublishRequested -= value; }
    public event Action<Rect2>? CaptureRequested
    {
        add { Router.CaptureRequested += value; Keys.CaptureRequested += value; }
        remove { Router.CaptureRequested -= value; Keys.CaptureRequested -= value; }
    }
    public Func<bool>? StopRecording { get => Keys.StopRecording; set => Keys.StopRecording = value; }
    public event Action<string>? PanelRequested { add => Keys.PanelRequested += value; remove => Keys.PanelRequested -= value; }
    public event Action<Vector2, PieceNode?>? ContextMenu { add => Router.ContextMenu += value; remove => Router.ContextMenu -= value; }
    public event Action? BoardsChanged;
    public event Action<string?, Vector2>? HoverChanged { add => Hover.Changed += value; remove => Hover.Changed -= value; }
    public event Action? CropChanged { add => Crop.Changed += value; remove => Crop.Changed -= value; }
    public event Action<List<Vec2>>? ZoneDrawn { add => Zones.Drawn += value; remove => Zones.Drawn -= value; }
    public event Action<string, Command>? ReliefShaped { add => Relief.Shaped += value; remove => Relief.Shaped -= value; }
    public event Action? ReliefPicked { add => Relief.Picked += value; remove => Relief.Picked -= value; }
    public event Action<Vec2>? PointerMoved { add => Pointers.Moved += value; remove => Pointers.Moved -= value; }
    public event Action<IReadOnlyList<KTable.Server.CarriedPiece>, KTable.Client.Hand.HandAt, double>? CarryLayoutShared
    {
        add => Motion.LayoutShared += value;
        remove => Motion.LayoutShared -= value;
    }
    public event Action<KTable.Client.Hand.HandAt, double>? CarryPlaceShared { add => Motion.PlaceShared += value; remove => Motion.PlaceShared -= value; }
    public event Action<double, IReadOnlyList<KTable.Client.Hand.MotionSample>>? FlightShared { add => Motion.FlightShared += value; remove => Motion.FlightShared -= value; }

    public ScreenLabels Labels => _c.Labels;

    public const uint WorldLayer = 1u << 0;
    public const uint LiveOnlyLayer = 1u << 2;
    public const uint PictureOnlyLayer = 1u << 3;
    public const uint OutOfPictureLayer = 1u << 4;
    public const uint TextLayer = 1u << 7;

    public const float ToolPx = 3f;

    internal TableStage Stage { get; }
    internal CameraControl Cam { get; }
    internal SheetLayer Sheets { get; }
    internal ContourLines Contours { get; }
    internal MapLettering MapLettering { get; }
    internal PieceLayer Pieces { get; }
    internal DrawingLayer Drawings { get; }
    internal Sighting Sighting { get; }
    internal ToolOverlay Overlay { get; }
    internal ViewSync Sync { get; }
    internal Selection Selection { get; }
    internal SelectionReport Report { get; }
    internal PendingPoses Poses { get; }
    internal Gizmo Gizmo { get; }
    internal Carry Hand { get; }
    internal SharedMotion Motion { get; }
    internal Dice Dice { get; }
    internal PieceClipboard Clip { get; }
    internal Hover Hover { get; }
    internal PieceActions Actions { get; }
    internal NameEditor Names { get; }
    internal PaletteDrag Palette { get; }
    internal PenTool Pen { get; }
    internal LetterTool Letters { get; }
    internal RulerTool Ruler { get; }
    internal RangeRings Ranges { get; }
    internal StampTool Decals { get; }
    internal CropTool Crop { get; }
    internal ZoneLayer Zones { get; }
    internal ReliefTool Relief { get; }
    internal FormationTool Forms { get; }
    internal UmpirePointers Pointers { get; }
    internal SelectTool SelectTool { get; }
    internal TableInput Router { get; }
    internal TableKeys Keys { get; }

    SelectionAura _aura = null!, _hoverAura = null!;

    public Table()
    {
        _c = new TableContext(this, SetTool);
        var picked = new HashSet<string>();
        var press = new PointerPress();
        Stage = new TableStage(_c);
        Cam = new CameraControl(_c);
        Sheets = new SheetLayer(_c, Cam);
        MapLettering = new MapLettering(_c);
        Contours = new ContourLines(_c, Sheets, MapLettering);
        Pieces = new PieceLayer(_c, picked);
        Drawings = new DrawingLayer(_c, MapLettering);
        Sighting = new Sighting(_c, Sheets, Pieces);
        Overlay = new ToolOverlay(_c);
        Sync = new ViewSync(_c, picked, Stage, Sheets, Pieces, Drawings);
        Selection = new Selection(_c, picked, Pieces);
        Report = new SelectionReport(_c, Selection);
        Poses = new PendingPoses(_c, Pieces, Selection);
        Gizmo = new Gizmo(_c, Sheets, Overlay, Selection, Poses);
        Hand = new Carry(_c, press, Pieces, Selection, Poses);
        Motion = new SharedMotion(_c, Pieces, Hand);
        _c.Moving = Motion.Moving;
        Dice = new Dice(_c, Pieces, Selection, Hand);
        Clip = new PieceClipboard(_c, Pieces, Selection, Hand);
        Hover = new Hover(_c, press, Pieces, Hand);
        Actions = new PieceActions(_c, Selection, Poses, Hand, Hover);
        Names = new NameEditor(_c);
        Palette = new PaletteDrag(_c, Pieces, Selection, Hand);
        Pen = new PenTool(_c, Overlay, Drawings);
        Letters = new LetterTool(_c, Overlay, Drawings);
        Ruler = new RulerTool(_c, Overlay);
        Ranges = new RangeRings(_c, Overlay, Selection);
        Decals = new StampTool(_c, Overlay, Pieces, Drawings);
        Crop = new CropTool(_c, Overlay, Gizmo);
        Zones = new ZoneLayer(_c, Overlay);
        Relief = new ReliefTool(_c, Overlay, Sheets);
        Forms = new FormationTool(_c, Overlay, Selection, Hand);
        Pointers = new UmpirePointers(_c, p => Hand.SurfaceY(p, Layers.Umpire), Motion.Carrying, name => UmpireHue.Of(_c.S, name) ?? UmpireHue.Unassigned);
        SelectTool = new SelectTool(_c, press, Sheets, Pieces, Selection, Hand, Names, Actions);
        Router = new TableInput(_c, press, Cam, Pieces, Sighting, Selection, Poses, Gizmo, Hand, Actions, Names, Palette, Pen, Letters, Ruler, Ranges, Crop, Zones, SelectTool, Relief, Forms)
        {
            Decals = Decals,
        };
        Keys = new TableKeys(_c, Router, Cam, Sheets, Pieces, Sighting, Selection, Poses, Hand, Dice, Clip, Hover, Actions, Pen, Ruler, Forms);

        Stage.ShadeChanged += Pieces.ShadeEffects;
        Stage.WindChanged += Pieces.SteerEffects;
        Pieces.Resynced += Hand.Rehome;
        Sighting.Connect(Selection, Overlay);
        Overlay.Drawing += Gizmo.Draw;
        Overlay.Drawing += Relief.Draw;
        Overlay.Drawing += Crop.Draw;
        Overlay.Drawing += Zones.DrawDraft;
        Overlay.Drawing += Ranges.Draw;
        Overlay.Drawing += Decals.Draw;
        Overlay.Drawing += Ruler.Draw;
        Overlay.Drawing += () => Hand.DrawMeasures(Overlay);
        Overlay.Drawing += Pen.DrawInHand;
        Overlay.Drawing += Letters.Draw;
        Overlay.Drawing += Forms.Draw;
        Sync.Connect(Poses, Clip, Zones);
        Sync.BoardsChanged += RefreshDrawings;
        Sync.LookChanged += () => Contours.Due(Stage.LightNow());
        Selection.Picked += Sighting.Aim;
        Sheets.GroundChanged += Pieces.Resettle;
        Sheets.GroundChanged += Drawings.Reground;
        Sheets.GroundChanged += Zones.Resync;
        Sheets.GroundChanged += _ => Contours.Due();
        Poses.Connect(Hand, Gizmo);
        Gizmo.Connect(SelectTool);
        Hand.Connect(Dice);
        Dice.Connect(Hover);
        Router.KeyPressed += Keys.OnKey;
        Router.KeyReleased += Keys.OnKeyUp;
    }

    public override void _Ready()
    {
        _c.Rig = new CameraRig { Terrain = () => _c.S?.Ground ?? KTable.Core.Model.Ground.Flat, Leash = () => _c.S?.View.Sheets is { } sheets ? KTable.Core.Model.SheetLeash.Of(sheets) : null };
        AddChild(Rig);
        _aura = new SelectionAura(Rig.Cam);
        AddChild(_aura);
        _hoverAura = new SelectionAura(Rig.Cam, SelectionAura.HoverLayer, new Color(1f, 1f, 1f, 0.95f));
        AddChild(_hoverAura);
        Stage.BuildLightAndSurface();
        Rig.FlatChanged += () => Stage.ShowFlat(Rig.Flat);
        Stage.ShowFlat(Rig.Flat);
        _c.Labels = new ScreenLabels(() => Rig?.Cam);
        AddChild(Labels);
        SetStandIn(true);
        AddChild(Sheets.Root = new Node3D { Name = "Sheets" });
        AddChild(Contours.Root);
        AddChild(MapLettering.Root);
        AddChild(Pieces.Root = new Node3D { Name = "Pieces" });
        AddChild(Drawings.Root = new Node3D { Name = "Drawings" });
        AddChild(Sighting.Root = new Node3D { Name = "Sightings" });
        AddChild(Overlay.Root = new Node3D { Name = "Overlay" });
        Overlay.Build();
        Sighting.BuildPlanes();
        Stage.BuildGrid();
        Sighting.BuildOutline();
    }

    public void Bind(Session s, Settings settings)
    {
        _c.S = s;
        _c.Settings = settings;
        s.Art.Look.ShowFaceText = settings.FaceText;
        s.Art.Look.LabelPlates = settings.LabelPlates;
        s.Art.Look.LetteringReach = settings.LetteringReach;
        s.Art.Look.FaceTextScale = settings.FaceTextScale;
        ShowCursors = settings.ShowCursors;
        ShowZones = settings.ShowZones;
        ApplyGraphics();
        S.Reset += Rebuild;
        S.Applied += Sync.OnApplied;
        S.Applied += _ => Sighting.TableChanged();
        S.Outgoing = Hand.Falls.With;
        S.Error += _ => Sync.Refused = true;
        Rebuild();
        if (!Cam.Restore()) FrameAll();
    }

    public void Rebuild()
    {
        var drawClock = System.Diagnostics.Stopwatch.StartNew();
        ApplyLighting(Stage.LightNow());
        Stage.FitTable(TableExtent.Of(S.View));
        Stage.ApplyGrid();
        ApplyScale(S.View.Calibration);
        Stage.ApplyWind(S.View.Wind);
        Sheets.Clear();
        Pieces.Clear();
        foreach (var sh in S.View.Sheets) Sheets.Add(sh);
        Cam.FitRange();
        foreach (var layer in S.View.Layers.Keys.ToList()) Pieces.SyncLayer(layer);
        Hand.Rehome();
        Drawings.Sync();
        Sighting.Sync();
        Zones.Sync();
        Selected.RemoveWhere(id => !Pieces.Keys.Any(k => k.id == id));
        if (!Cam.Restored && Cam.Restore()) Cam.Restored = true;
        _c.NotifySelectionChanged();
        if (drawClock.ElapsedMilliseconds > 100)
            GD.Print($"[table] drew {Pieces.Count} pieces and {Sheets.Count} sheet(s) in {drawClock.ElapsedMilliseconds} ms");
    }

    public void RefreshLayers()
    {
        Pieces.SyncAllLayers();
        Hand.Rehome();
        Drawings.Sync();
        Sighting.Sync();
    }

    public void RefreshDrawings()
    {
        Drawings.Sync();
        BoardsChanged?.Invoke();
        CheckDrawingLock();
    }

    public void ApplyGraphics()
    {
        if (_c.Settings is not { } cfg) return;
        Graphics.ApplyEdges(GetViewport(), cfg);
        Stage.ApplyGraphics(cfg);
        Sheets.PlanLevels();
    }

    const double FitEverySeconds = 0.5;
    double _fitIn;

    void FitTableNow(double delta)
    {
        _fitIn -= delta;
        if (_fitIn > 0) return;
        _fitIn = FitEverySeconds;
        Stage.FitTable(TableExtent.Of(S.View));
    }

    public void ApplyLettering()
    {
        if (_c.Settings is not { } cfg) return;
        var look = S.Art.Look;
        bool shown = look.ShowFaceText != cfg.FaceText;
        bool sized = !Mathf.IsEqualApprox(look.FaceTextScale, cfg.FaceTextScale);
        bool reached = !Mathf.IsEqualApprox(look.LetteringReach, cfg.LetteringReach);
        look.ShowFaceText = cfg.FaceText;
        look.FaceTextScale = cfg.FaceTextScale;
        look.LetteringReach = cfg.LetteringReach;
        if (shown) RefreshLayers();
        if (sized) RefreshFaceText();
        if (reached) RefreshLettering();
    }

    public void CheckDrawingLock()
    {
        if (DrawingLocked && Tool is Tool.Draw or Tool.Shape or Tool.Erase or Tool.Letter) SetTool(Tool.Select);
    }

    public override void _Process(double delta)
    {
        if (S == null) return;
        S.Assets.Process();
        Router.CatchLostReleases();
        Rig.Tick(delta);
        if (Sync.Refused) Sync.ResyncRefused();
        Poses.Tick(delta);
        Clip.Tick(delta);
        Names.Tick(delta);
        Palette.Tick(Input.IsMouseButtonPressed(MouseButton.Left), Pointer.In(GetViewport()));
        Sighting.TickBlur(delta);
        Sighting.TickShade();
        Cam.TickMemory(delta);
        _aura.Active = Selected.Count > 0;
        _hoverAura.Active = Hand.StowTarget != null || Hover.Node is { } hv && GodotObject.IsInstanceValid(hv) && !hv.Ringed;
        Sighting.TickFraming();
        Sighting.FollowTable();
        if (S.Art.Look.FlatSelection != Rig.TopDown) { S.Art.Look.FlatSelection = Rig.TopDown; foreach (var n in SelectedNodes) n.RefreshSelectionLook(); }
        Pieces.Lettering.Tick();
        var mouse = Pointer.In(GetViewport());
        bool overUi = GetViewport().GuiGetHoveredControl() is { } hc && hc.MouseFilter != Control.MouseFilterEnum.Ignore;
        Hand.Tick(overUi);
        Motion.Tick();
        if (!overUi)
        {
            Hover.Update(mouse, delta);
            if (S.IsUmpire && (Settings.ShareCursor || S.View.PointersAlwaysShown)) Pointers.Track(mouse, delta);
        }
        else Hover.Clear(mouse);
        Pointers.Tick(delta);
        Sheets.TickLoadingLabels();
        Cam.TickKeys(delta, Typing);
        Stage.FollowView(Rig.Distance, Rig.FarthestGroundDepth());
        FitTableNow(delta);
        Sheets.Tick();
        Contours.Tick(delta);
        Overlay.Redraw();
    }

    internal bool Typing => Names.Node != null || AnyTextFocus();

    internal static bool AnyTextFocus()
    {
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        if (root.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return true;
        foreach (var w in root.GetEmbeddedSubwindows())
            if (w.Visible && w.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) return true;
        return false;
    }

    public override void _UnhandledInput(InputEvent e) => Router.Handle(e);

    public override void _Notification(int what)
    {
        if (what is not ((int)NotificationApplicationFocusOut or (int)NotificationWMWindowFocusOut)) return;
        Callable.From(() => { if (IsInstanceValid(this) && S != null) Router.LetGoOfEverything(); }).CallDeferred();
    }

    public void SetTool(Tool t, string? placeDef = null)
    {
        if (t is Tool.Draw or Tool.Shape or Tool.Erase or Tool.Letter && DrawingLocked)
        {
            Say("Layer is hidden · show it to unlock");
            return;
        }
        if (t != Tool) Router.EndGesture();
        if (t == Tool.Sighting && Draft == null) { if (!Sighting.Begin()) return; }
        if (Tool == Tool.Crop && t != Tool.Crop) Crop.End();
        if (Tool == Tool.Relief && t != Tool.Relief) Relief.Cancel();
        var was = Tool;
        _c.Tool = t;
        KeepDrawMode(t);
        if (t == Tool.Decal) Decals.ArmAsBefore();
        _c.PlaceDef = placeDef;
        if (t != Tool.Ruler && t != Tool.Calibrate) Ruler.Clear();
        if (t != Tool.Range) Ranges.ClearReadings();
        ToolChanged?.Invoke();
        if (ToolLine(t) is { } line) Say(line);
        else if (ToolLine(was) != null) Say("");
    }

    public void RefreshHint()
    {
        if (ToolLine(Tool) is { } line) Say(line);
    }

    static readonly Tool[] DrawModeTools = { Tool.Draw, Tool.Shape, Tool.Erase, Tool.Letter };

    public Tool DrawMode => DrawModeTools[Math.Max(0, Array.IndexOf(Settings.DrawModes, _c.Settings.DrawMode))];

    void KeepDrawMode(Tool t)
    {
        int i = Array.IndexOf(DrawModeTools, t);
        if (i < 0 || _c.Settings.DrawMode == Settings.DrawModes[i]) return;
        _c.Settings.DrawMode = Settings.DrawModes[i];
        _c.Settings.Save();
    }

    string? ToolLine(Tool t) => t switch
    {
        Tool.Range => Ranges.Hint(),
        Tool.Decal => Decals.Hint(),
        Tool.Sighting => Sighting.Hint(),
        Tool.Place => "Shift+click places another",
        Tool.Relief => ReliefLine(),
        Tool.Formation => Forms.Hint(),
        Tool.Transform => "The round knob turns, the square knob resizes · Shift snaps · the wheel over the piece turns 10° a notch, Ctrl 2°, Shift ½°",
        Tool.Ruler => "Drag to measure · map units until the map is calibrated",
        Tool.Calibrate => "Drag along the map's scale bar, then type what it measures",
        Tool.Draw => "Drag to draw on the layer picked in the panel",
        Tool.Shape => Polyline.IsPath(ShapeKind) ? PathHint() : "Drag the shape out, end to end or corner to corner, on the layer picked in the panel",
        Tool.Erase => "Drag over strokes to rub them out · only the layer picked in the panel",
        Tool.Letter => "Click, or drag a path, and type the name · Enter lays it, Esc drops it · double-click a name to retype it, drag to move it",
        Tool.Capture => _c.Settings.CaptureGif
            ? $"Drag the region to record a GIF · Enter or Stop ends it · {_c.Settings.Keys.Label("captureAgain")} records it again"
            : $"Drag the region to capture · {_c.Settings.Keys.Label("captureAgain")} captures it again",
        Tool.Zone => "Drag a rectangle on the board to give it to an umpire",
        Tool.Crop => "Drag the knobs to trim the sheet · Apply or Cancel in the Map page",
        _ => null,
    };

    string PathHint() => ShapeKind == Polyline.Area
        ? "Click round the area, or drag a side out · click the first point or press Enter to close · Backspace takes one back · the see-through slider sets how strong the wash is"
        : "Click each bend, or drag a straight run · double-click or Enter ends it · Backspace takes one back";

    string ReliefLine() => !_c.Settings.ReliefMode.StartsWith("paint:", StringComparison.Ordinal)
        ? "Drag over a sheet to shape the ground · Shift+click goes straight on from the last stroke · = and − or Ctrl+wheel size the brush"
        : _c.Settings.ReliefPaint switch
        {
            "fill" => "Click a patch of ground to fill it up to other ground types, or a bare sheet whole; on its own kind, lays it afresh · Alt+click picks up the ground there",
            "rect" => "Drag the rectangle across the ground · Shift for a square",
            "ellipse" => "Drag the ellipse across the ground · Shift for a circle",
            "polygon" => "Click the polygon's corners, then the first again or double-click to close it · Esc drops it",
            _ => "Drag over a sheet to paint the ground · Shift+click goes straight on from the last stroke · Alt+click picks up the ground there · = and − or Ctrl+wheel size the brush",
        };

    internal Vec2? Ground(Vector2 screen) => _c.Ground(screen);

    internal static double Norm360(double d) => KTable.Client.Hand.Angles.Norm360(d);

    internal void Say(string hint) => _c.Say(hint);

    public PieceNode? NodeOf(string layer, string id) => Pieces.NodeOf(layer, id);
    public PieceInstance? Find(string layer, string id) => S.View.Find(layer, id);
    public IEnumerable<PieceNode> Near(Vec2 p) => Pieces.Grid.Near(p);
    public IEnumerable<PieceNode> NearAny(IEnumerable<Vector3> places, float radius) => Pieces.Grid.NearAny(places, radius);
    public void SyncLayer(string layer) => Pieces.SyncLayer(layer);
    public void RebuildPiecesOf(string defId) => Pieces.RebuildPiecesOf(defId);
    public void RefreshFaceText() => Pieces.RefreshFaceText();

    public void ShowLabelPlates(bool on)
    {
        S.Art.Look.LabelPlates = on;
        RefreshFaceText();
    }
    public void RefreshLettering() => Pieces.Lettering.Refresh();
    public int LetteredCount => Pieces.Lettering.Count;
    public long LetteredWeight => Pieces.Lettering.Weight;
    public string GridDebug() => Pieces.Grid.Debug();
    public int RingedCount() => Pieces.RingedCount();
    public float SurfaceY(Vec2 p, string layer, PieceNode? except = null, HashSet<PieceNode>? skip = null) => Hand.SurfaceY(p, layer, except, skip);

    public HashSet<string> Selected => Selection.Ids;
    public IEnumerable<PieceNode> SelectedNodes => Selection.Nodes;
    public IEnumerable<PieceNode> MovableNodes => Selection.Movable;
    public void SetSelection(IEnumerable<string> ids, bool add = false) => Selection.Set(ids, add);
    public void SelectAllEditable() => Selection.SelectAllEditable();
    public PieceNode? HoveredNode => Hover.Node;

    public void FlipPieces(IEnumerable<PieceNode> nodes) => Actions.Flip(nodes);
    public void FlipSelection() => Actions.Flip(SelectedNodes.ToList());
    public void DeleteSelection() => Actions.Delete();
    public void CycleState(int dir) => Actions.CycleState(dir);
    public bool SetStateNumber(int n) => Actions.SetStateNumber(n);
    public void PressWidget(PieceNode hit, int index, Widget w) => Actions.PressWidget(hit, index, w);
    public void CopySelection() => Clip.Copy();
    public void CutSelection() { Clip.Copy(cut: true); DeleteSelection(); }
    public void Paste(Vec2 at) => Clip.Paste(at);
    public int Clipped => Clip.Count;

    public const double MinScale = PendingPoses.MinScale, MaxScale = PendingPoses.MaxScale;
    public (Vec2 pos, double rot, double scale) Current(PieceNode n) => Poses.Current(n);
    public void RotateSmooth(double deg) => Poses.RotateSmooth(deg);
    public void ScaleSmooth(double factor) => Poses.ScaleSmooth(factor);
    public void SetSelectionTransform(double? rot, double? scale) => Poses.SetSelectionTransform(rot, scale);
    public bool Holding => Hand.Holding;
    public void FormRows(int rows) => Hand.FormRows(rows);
    public const int SimulateAtMost = Carry.SimulateAtMost, SlideAtMost = Carry.SlideAtMost, ShoveAtMost = Carry.ShoveAtMost, SettleBudgetMs = Carry.SettleBudgetMs;

    public static bool IsDie(PieceNode n) => n.IsDie;
    public bool RollDiceAtHand() => Dice.RollAtHand();
    public void RollDicePhysics(List<PieceNode> dice, Vector3 throwVel, bool hop) => Dice.Roll(dice, throwVel, hop);
    public string DiceDebug() => Dice.Debug();

    public bool PaletteDragging => Palette.Dragging;
    public void BeginPaletteDrag(string def, Func<Vector2, bool> overDrawer) => Palette.Begin(def, overDrawer);
    public void EndPaletteDrag(bool drop) => Palette.End(drop);
    public void BeginTakeOut(string layer, string holder, string id, Func<Vector2, bool> overWindow) => Palette.BeginTakeOut(layer, holder, id, overWindow);

    public string? SelectedSheet { get => Sheets.SelectedId; set => Sheets.SelectedId = value; }
    public void SelectSheet(string? id) => Sheets.Select(id);
    public string SheetStats() => Sheets.Stats();
    public void FrameAll() => Cam.FrameAll();

    public void Locate(IReadOnlyCollection<PieceInstance> pieces)
    {
        SetSelection(pieces.Select(p => p.Id));
        Cam.FramePieces(pieces);
    }

    public void DeletePieces(IEnumerable<(string Layer, PieceInstance Piece)> pieces) =>
        _c.DoAll(pieces.Select(x => (Command)new DeletePiece { Layer = x.Layer, Id = x.Piece.Id }).ToList());
    public string? CropSheetId => Crop.SheetId;
    public FaceBox CropRect => Crop.Rect;
    public void BeginCrop(string sheetId) => Crop.Begin(sheetId);
    public void SetCropRect(FaceBox r) => Crop.SetRect(r);
    public void ApplyCrop() => Crop.Apply();
    public void CancelCrop() => SetTool(Tool.Select);

    public Lighting LightNow() => Stage.LightNow();
    public void ApplyLighting(Lighting l) { Stage.ApplyLighting(l); Contours.Due(l); }
    public static Color LightTint(float warmth) => TableStage.LightTint(warmth);
    public void ApplyGrid() => Stage.ApplyGrid();
    public void ApplyWind(Wind w) => Stage.ApplyWind(w);
    public void ApplyScale(Calibration c)
    {
        Stage.ApplyScale(c);
        ReliefSurface.ShowScale(c);
        Contours.Due();
    }

    public bool Resting { get; private set; }

    List<CanvasLayer> _restingLayers = new();

    public override void _ExitTree() => Hover.Release();

    public void Rest(bool resting, bool pause = false)
    {
        if (resting) Hover.Clear(Vector2.Zero);
        SetProcessUnhandledInput(!resting);
        if (pause) ProcessMode = resting ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        if (Resting == resting) return;
        Resting = resting;
        Visible = !resting;
        if (resting) _restingLayers = FindChildren("*", nameof(CanvasLayer), true, false).OfType<CanvasLayer>().Where(l => l.Visible).ToList();
        foreach (var layer in _restingLayers.Where(IsInstanceValid)) layer.Visible = !resting;
        if (!resting) _restingLayers = new();
        Stage.Rest(resting);
        if (resting) { Rig.Cam.ClearCurrent(false); return; }
        Rig.Cam.MakeCurrent();
        ApplyLighting(LightNow());
        ApplyScale(S.View.Calibration);
        Woke?.Invoke();
    }

    public event Action? Woke;

    public bool Lent { get; private set; }

    List<CanvasLayer> _lentLayers = new();

    public void Lend(bool lent)
    {
        if (Lent == lent) return;
        Lent = lent;
        if (lent) _lentLayers = FindChildren("*", nameof(CanvasLayer), true, false).OfType<CanvasLayer>().Where(l => l.Visible).ToList();
        foreach (var layer in _lentLayers.Where(IsInstanceValid)) layer.Visible = !lent;
        if (lent) return;
        _lentLayers = new();
        if (!Resting) Woke?.Invoke();
    }

    public void SetStandIn(bool on, float shade = 0) => Stage.SetStandIn(on, shade);

    public string Board { get => Drawings.Board; set => Drawings.Board = value; }
    public IReadOnlySet<string> HiddenBoards => Drawings.HiddenBoards;
    public void ShowBoard(string id, bool shown) => Drawings.ShowBoard(id, shown);
    public string BoardName(string id) => Drawings.BoardName(id);
    public string DrawBoard() => Drawings.DrawBoard();
    public bool BoardShown(string board) => Drawings.BoardShown(board);
    public bool DrawingLocked => Drawings.Locked;
    public bool OnDrawBoard(string board) => Drawings.OnDrawBoard(board);
    public int DrawnSurfaces() => Drawings.DrawnSurfaces();
    public int DrawingCount() => Drawings.DrawingCount();
    public void EraseAllDrawings() => Drawings.EraseAll();
    public string ShapeKind { get => Pen.ShapeKind; set => Pen.ShapeKind = value; }
    public Color PenColor { get => Pen.PenColor; set => Pen.PenColor = value; }
    public float PenPixels { get => Pen.PenPixels; set => Pen.PenPixels = value; }
    public float PenOpacity { get => Pen.PenOpacity; set => Pen.PenOpacity = value; }
    public double RubRadius() => Pen.RubRadius();
    public static List<Vec2> ShapePoints(string kind, Vec2 a, Vec2 b) => Polyline.ShapePoints(kind, a, b);
    public static List<Vec2>? ShapeHead(string kind, Vec2 a, Vec2 b) => Polyline.ShapeHead(kind, a, b);

    public static double NiceStep(double span) => RulerTool.NiceStep(span);
    public double RangeMetres { get => Ranges.RangeMetres; set => Ranges.RangeMetres = value; }
    public double RangeOf(PieceNode n) => Ranges.RangeOf(n);
    public int RangeReadings => Ranges.Readings;

    public SightingDraft? Draft => Sighting.Draft;
    public bool DraftCloseUp => Sighting.CloseUp;
    public void ShowDraft() => Sighting.ShowDraft();
    public void FrameOnDraft() => Sighting.FrameOnDraft();
    public void CancelSighting() => Sighting.Cancel();
    public void EndDraft() => Sighting.End();
    public void RefreshSightingPanel() => Sighting.NotifyChanged();
    public KTable.Client.Prefs.CaptureProfile CaptureProfile() => Sighting.Profile();
    public void TakeCaptureProfile(KTable.Client.Prefs.CaptureProfile p) => Sighting.Take(p);
    public void SetSightingFirstPerson(bool on) => Sighting.SetFirstPerson(on);
    public void SetSightingLens(double degrees) => Sighting.SetLens(degrees);
    public void SetSightingFormat(SightingFormat format) => Sighting.SetFormat(format);
    public void SetSightingEye(double height) => Sighting.SetEyeHeight(height);
    public void SetSightingSharp(double share) => Sighting.SetSharp(share);
    public void SetSightingBlur(double share) => Sighting.SetBlur(share);
    public double SightingEyeHighest => Sighting.EyeHighest;
    public void SetSightingHideText(bool on) => Sighting.SetHideText(on);
    public void FaceSighting(string cardinal) => Sighting.Face(cardinal);
    public void ClearOutline() => Sighting.ClearOutline();
    public int StrokesDrawn => Sighting.StrokesDrawn;
    public int StrokesTakenBack => Sighting.StrokesTakenBack;
    public void UndoStroke() => Sighting.UndoStroke();
    public void RedoStroke() => Sighting.RedoStroke();
    public IEnumerable<PieceInstance> UmpirePiecesIn(SightingShape shape) => Sighting.UmpirePiecesIn(shape);
    public System.Threading.Tasks.Task<Image?> RenderSighting(SightingDraft d, int px) => Sighting.Render(d, px);

    public bool ShowZones { get => Zones.Show; set => Zones.Show = value; }
    public void SyncZones() => Zones.Sync();
    public bool ShowCursors { get => Pointers.Show; set => Pointers.Show = value; }
    public void ShowRemoteCursor(long peer, string name, Vec2 pos) => Pointers.ShowRemote(peer, name, pos);
    public IEnumerable<long> CursorPeers() => Pointers.Peers();
    public void ForgetCursor(long peer) => Pointers.Forget(peer);
    public void CarryLayoutArrived(long carrier, IReadOnlyList<KTable.Server.CarriedPiece> pieces, KTable.Client.Hand.HandAt at, double t) => Motion.LayoutArrived(carrier, pieces, at, t);
    public void CarryPlaceArrived(long carrier, KTable.Client.Hand.HandAt at, double t) => Motion.HandArrived(carrier, at, t);
    public void FlightArrived(long carrier, double t, IReadOnlyList<KTable.Client.Hand.MotionSample> samples) => Motion.FlightArrived(carrier, t, samples);
}
