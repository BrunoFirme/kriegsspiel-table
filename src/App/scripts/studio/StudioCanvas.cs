using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

public partial class StudioCanvas : Control
{
    public readonly ArtRenderer R = new();
    public ArtDoc Doc => R.Doc;
    public StudioPrefs P = new();
    public Color Ink = new(0.1f, 0.1f, 0.1f), Ink2 = new(0.95f, 0.95f, 0.95f);

    public string Tool { get; private set; } = "brush";

    public event Action<string>? Edited;
    public event Action? LayersChanged;
    public event Action<string>? Status;
    public event Action<Color, bool>? InkPicked;
    public event Action<ArtLayer>? TextPlaced;
    public event Action<string, FaceBox>? PrintMoved;
    public event Action<string>? PrintChosen;
    public event Action? ToolChanged;
    public event Action? PickChanged;
    public Func<Image, string> SaveSrc = _ => "";

    internal StudioPick Picked { get; }
    public StudioCanvas() { Picked = new StudioPick(this); }
    internal void PickWasChanged() => PickChanged?.Invoke();

    public const float ThinnestLine = 0.5f, ThickestLine = 200;

    public string PieceShape = "box";
    public Vector2 PieceUnits = new(2, 1);
    public sealed record PrintBox(string Key, string Label, FaceBox Box);
    public List<PrintBox> Prints = new();
    public string? Chosen;
    public List<Widget> Widgets = new();
    public string FaceSide = "front";

    float _zoom = 1;
    Vector2 _pan;
    readonly UndoHistory _history = new();
    Control? _stack, _over;
    Polygon2D? _dim;
    Texture2D _checker = null!;
    Image? _flat;

    public ArtLayer? Sel => Doc.Layer(Doc.Selected);
    public float Zoom => _zoom;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.Click;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        TextureRepeat = TextureRepeatEnum.Enabled;
        AddChild(R);
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) img.SetPixel(x, y, (x < 8) ^ (y < 8) ? new Color(0.36f, 0.36f, 0.38f) : new Color(0.28f, 0.28f, 0.30f));
        _checker = ImageTexture.CreateFromImage(img);
        _stack = new StackView { Canvas = this, MouseFilter = MouseFilterEnum.Ignore, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha } };
        _stack.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_stack);
        _dim = new Polygon2D { InvertEnabled = true, InvertBorder = 20000, Color = new Color(Ui.Bar, 0.62f), Visible = false };
        AddChild(_dim);
        _over = new OverView { Canvas = this, MouseFilter = MouseFilterEnum.Ignore };
        _over.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_over);
        Resized += () => { if (_fitted) Fit(); else Redraw(); };
    }

    bool _fitted = true;

    public void Open(ArtDoc doc)
    {
        Picked.Forget();
        R.Doc = doc;
        if (doc.Selected == null || doc.Layer(doc.Selected) == null) doc.Selected = doc.Layers.LastOrDefault(l => l.Kind == "paint")?.Id ?? doc.Layers.LastOrDefault()?.Id;
        R.KeepOnly(doc);
        R.RebuildAll();
        _history.Start(ArtStore.Serialize(doc));
        _flat = null;
        CancelGesture();
        Fit();
        LayersChanged?.Invoke();
    }

    const ulong SameStepMs = 1200;
    string _lastStep = "";
    ulong _lastStepAt;

    public void Commit(string what, bool sameStep = false)
    {
        ulong now = Time.GetTicksMsec();
        bool amend = sameStep && what == _lastStep && now - _lastStepAt < SameStepMs;
        string state = ArtStore.Serialize(Doc);
        if (!(amend ? _history.Amend(state, StudioClock.Next()) : _history.Record(state, StudioClock.Next()))) return;
        _lastStep = what;
        _lastStepAt = now;
        _flat = null;
        Redraw();
        Edited?.Invoke(what);
    }

    public IEnumerable<string> History => _history.All;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public long UndoStamp => _history.UndoStamp;
    public long RedoStamp => _history.RedoStamp;

    public void Undo()
    {
        if (_history.Undo(StudioClock.Next()) is { } s) Restore(s, "Undo");
    }

    public void Redo()
    {
        if (_history.Redo(StudioClock.Next()) is { } s) Restore(s, "Redo");
    }

    void Restore(string json, string what)
    {
        if (ArtStore.Deserialize(json) is not { } d) return;
        _lastStep = "";
        var sel = Doc.Selected;
        bool resized = d.W != Doc.W || d.H != Doc.H;
        R.Doc = d;
        if (d.Layer(sel) != null) d.Selected = sel;
        R.RebuildAll();
        _flat = null;
        CancelGesture();
        Picked.Rebind();
        if (resized) Fit();
        else Redraw();
        LayersChanged?.Invoke();
        Edited?.Invoke(what);
    }

    public void Refresh(ArtLayer l)
    {
        R.Rebuild(l);
        _flat = null;
        Redraw();
    }

    public void Select(string? id)
    {
        Picked.Drop();
        Doc.Selected = id;
        Redraw();
        LayersChanged?.Invoke();
    }

    public void AddLayer(ArtLayer l, int? at = null)
    {
        Picked.Drop();
        Doc.Insert(l, at);
        R.Rebuild(l);
        Redraw();
        LayersChanged?.Invoke();
    }

    ArtLayer PaintTarget()
    {
        if (Sel is { Locked: false, Visible: true } s) return s;
        var l = new ArtLayer { Name = NextName("Paint"), Kind = "paint" };
        AddLayer(l);
        return l;
    }

    public string NextName(string stem) => Doc.NextName(stem);

    public ArtLayer AddImage(Image img, string name, Vector2? at = null)
    {
        var src = SaveSrc(img);
        R.Remember(src, img);
        float k = Mathf.Min(1, Mathf.Min(Doc.W * 0.96f / img.GetWidth(), Doc.H * 0.96f / img.GetHeight()));
        var c = at ?? new Vector2(Doc.W, Doc.H) / 2;
        var l = new ArtLayer { Name = name, Kind = "image", Src = src, SrcW = img.GetWidth(), SrcH = img.GetHeight(), X = c.X, Y = c.Y, Sx = k, Sy = k };
        AddLayer(l);
        if (Tool is not ("move" or "crop")) SetTool("move");
        return l;
    }

    public Vector2 ToDoc(Vector2 local) => (local - _pan) / _zoom;
    public Vector2 ToScreen(Vector2 doc) => _pan + doc * _zoom;
    public Rect2 DocRect => new(ToScreen(Vector2.Zero), new Vector2(Doc.W, Doc.H) * _zoom);

    public Func<Rect2>? FreeArea;

    public void Fit()
    {
        _fitted = true;
        var area = FreeArea?.Invoke() ?? new Rect2(Vector2.Zero, Size);
        if (area.Size.X < 160 || area.Size.Y < 120) area = new Rect2(Vector2.Zero, Size);
        var avail = area.Size - new Vector2(56, 56);
        if (avail.X < 20 || avail.Y < 20 || Doc.W <= 0) return;
        _zoom = Mathf.Min(avail.X / Doc.W, avail.Y / Doc.H);
        _pan = area.Position + (area.Size - new Vector2(Doc.W, Doc.H) * _zoom) / 2;
        Redraw();
    }

    public void Refit() { if (_fitted) Fit(); }

    public void ZoomTo(float z, Vector2? around = null)
    {
        _fitted = false;
        var at = around ?? Size / 2;
        var d = ToDoc(at);
        _zoom = Mathf.Clamp(z, 0.02f, 64);
        _pan = at - d * _zoom;
        Redraw();
    }

    public void Redraw()
    {
        QueueRedraw();
        _stack?.QueueRedraw();
        _over?.QueueRedraw();
        UpdateDim();
    }

    void UpdateDim()
    {
        if (_dim == null) return;
        if (Doc.Outline is { Count: >= 6 } ol)
        {
            var pts = new Vector2[ol.Count / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = ToScreen(new Vector2(ol[i * 2], ol[i * 2 + 1]));
            _dim.Polygon = pts;
            _dim.Visible = true;
        }
        else _dim.Visible = false;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Ui.Raised);
        var r = DocRect;
        DrawRect(r.Grow(1), new Color(0, 0, 0, 0.5f), false, 2);
        if (Doc.Bg == null) DrawTextureRect(_checker, r, true);
        else DrawRect(r, Hex.To(Doc.Bg, Colors.White));
    }

    partial class StackView : Control
    {
        public StudioCanvas Canvas = null!;
        public override void _Draw()
        {
            var c = Canvas;
            TextureFilter = c._zoom >= 4 ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
            var r = c.DocRect;
            foreach (var l in c.Doc.Layers)
            {
                if (!l.Visible || c.R.LayerTexture(l.Id) is not { } t) continue;
                float o = Mathf.Clamp(l.Opacity, 0, 1);
                DrawTextureRect(t, r, false, new Color(o, o, o, o));
            }
        }
    }

    public void SetTool(string t)
    {
        if (Tool == t) return;
        CancelGesture();
        if (t is not ("move" or "shape")) Picked.Drop();
        Tool = t;
        Redraw();
        ToolChanged?.Invoke();
        MouseDefaultCursorShape = t switch { "pan" => CursorShape.Drag, "move" => CursorShape.Arrow, "text" => CursorShape.Ibeam, _ => CursorShape.Cross };
    }

    internal enum Drag { None, Pan, Paint, Shape, Poly, Move, Handle, Rotate, Crop, DocCrop, OutlinePt, Measure, MeasureEnd, Print, Pick }
    Drag _drag;
    Vector2 _downDoc, _downScreen, _lastDoc, _hoverDoc;
    bool _hoverIn;
    Transform2D _startXf;
    int _handle;
    float[]? _startCrop;
    readonly List<Vector2> _stroke = new();
    readonly List<Vector2> _poly = new();
    int _outlineIdx = -1, _measureIdx = -1, _measureEnd;
    Rect2 _docCrop;
    FaceBox? _boxStart;
    PrintBox? _print;
    bool _spaceDown;

    void CancelGesture()
    {
        Picked.Cancel();
        _drag = Drag.None;
        _stroke.Clear();
        _poly.Clear();
        R.LiveOp = null;
        R.LiveCopies.Clear();
        if (R.LiveLayer != null) { R.LiveChanged(); R.LiveLayer = null; }
        Redraw();
    }

    internal float Px(float screenPx) => screenPx / _zoom;

    internal Vector2 Snap(Vector2 d)
    {
        if (!P.Snap) return d;
        float step = Doc.W / (float)Math.Max(1, P.GridCells);
        return new Vector2(Mathf.Round(d.X / step) * step, Mathf.Round(d.Y / step) * step);
    }

    internal static Vector2 Constrain(Vector2 a, Vector2 b, float stepDeg = 15) => G(HandleMath.Constrain(N(a), N(b), stepDeg));

    static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);
    static Vector2 G(System.Numerics.Vector2 v) => new(v.X, v.Y);

    internal List<Transform2D> Sym() => ArtGeom.Symmetry(P.Symmetry, P.Radial, new Vector2(Doc.W, Doc.H) / 2);

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb:
                OnButton(mb);
                break;
            case InputEventMouseMotion mm:
                OnMotion(mm);
                break;
            case InputEventMagnifyGesture mg:
                ZoomTo(_zoom * mg.Factor, mg.Position);
                AcceptEvent();
                break;
            case InputEventPanGesture pg:
                _pan -= pg.Delta * 12;
                _fitted = false;
                Redraw();
                AcceptEvent();
                break;
        }
    }

    void OnButton(InputEventMouseButton mb)
    {
        var d = ToDoc(mb.Position);
        if (mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (mb.CtrlPressed && Tool is "brush" or "erase") { ChangeSize(mb.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f); }
            else ZoomTo(_zoom * (mb.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), mb.Position);
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex == MouseButton.Middle || (mb.ButtonIndex == MouseButton.Left && (Tool == "pan" || _spaceDown)))
        {
            _drag = mb.Pressed ? Drag.Pan : Drag.None;
            _downScreen = mb.Position;
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
        {
            RightClick(d, mb);
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        GrabFocus();
        AcceptEvent();
        if (mb.Pressed) Press(d, mb);
        else Release(d, mb);
    }

    void OnMotion(InputEventMouseMotion mm)
    {
        var d = ToDoc(mm.Position);
        _hoverDoc = d;
        _hoverIn = true;
        if (_drag == Drag.Pan && (mm.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle)) == 0) _drag = Drag.None;
        else if (_drag != Drag.None && _drag != Drag.Pan && (mm.ButtonMask & MouseButtonMask.Left) == 0)
            Release(d, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = mm.Position });
        if (_drag == Drag.Pan)
        {
            _pan += mm.Relative;
            _fitted = false;
            Redraw();
            return;
        }
        if (_drag != Drag.None) Drag_(d, mm);
        else HoverCursor(d);
        _over!.QueueRedraw();
        StatusAt(d);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hoverIn = false; _over?.QueueRedraw(); }
    }

    void StatusAt(Vector2 d)
    {
        var u = DocToPiece(d);
        bool inside = d.X >= 0 && d.Y >= 0 && d.X <= Doc.W && d.Y <= Doc.H;
        Status?.Invoke(inside
            ? string.Format(Inv, "{0:0}, {1:0} px · x {2:0.###} z {3:0.###} u · {4:0}%", d.X, d.Y, u.X, u.Y, _zoom * 100)
            : string.Format(Inv, "{0} × {1} px · {2:0}%", Doc.W, Doc.H, _zoom * 100));
    }

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public Vector2 DocToPiece(Vector2 d) => new((d.X / Doc.W - 0.5f) * PieceUnits.X, (d.Y / Doc.H - 0.5f) * PieceUnits.Y);
    public Vector2 PieceToDoc(Vector2 u) => new((u.X / PieceUnits.X + 0.5f) * Doc.W, (u.Y / PieceUnits.Y + 0.5f) * Doc.H);

    void Press(Vector2 d, InputEventMouseButton mb)
    {
        _downDoc = d;
        _downScreen = mb.Position;
        _lastDoc = d;
        if (mb.AltPressed && Tool is "brush" or "shape" or "fill" or "text") { _ = Pick(d, false); return; }
        switch (Tool)
        {
            case "brush":
            case "erase":
                PressBrush(d, mb);
                break;
            case "shape":
                PressShape(d, mb);
                break;
            case "text":
                PressText(d);
                break;
            case "fill":
                _ = Fill(d);
                break;
            case "pick":
                _ = Pick(d, false);
                break;
            case "move":
                PressMove(d, mb);
                break;
            case "crop":
                PressCrop(d, mb);
                break;
            case "outline":
                PressOutline(d, mb);
                break;
            case "measure":
                PressMeasure(d, mb);
                break;
            case "place":
                PressPrint(mb);
                break;
        }
    }

    void PressBrush(Vector2 d, InputEventMouseButton mb)
    {
        var l = PaintTarget();
        if (l.Locked) return;
        _stroke.Clear();
        var p = Snap(d);
        if (mb.ShiftPressed && _lastStrokeEnd is { } le) _stroke.Add(le);
        _stroke.Add(p);
        R.LiveLayer = l.Id;
        R.LiveOp = new PaintOp { K = "path", C = Hex.Of(Ink), W = (Tool == "erase" ? P.Eraser : P.Brush) / LayerScale(l), E = Tool == "erase" };
        LiveFromStroke(l);
        _drag = Drag.Paint;
    }

    const float ClosePixels = 10, SamePixels = 3;

    void PressShape(Vector2 d, InputEventMouseButton mb)
    {
        if (_poly.Count == 0 && Picked.PressOwn(d, mb.Position)) { _drag = Drag.Pick; return; }
        Picked.Drop();
        var l = PaintTarget();
        var p = Snap(d);
        if (ArtShapes.IsPath(P.ShapeKind))
        {
            bool closes = ArtShapes.Closes(P.ShapeKind);
            if (closes && _poly.Count >= 3 && (ToScreen(_poly[0]) - mb.Position).Length() < ClosePixels) { FinishPoly(); return; }
            if (mb.DoubleClick && _poly.Count >= (closes ? 3 : 2)) { FinishPoly(); return; }
            if (mb.ShiftPressed && _poly.Count > 0) p = Constrain(_poly[^1], p);
            if (_poly.Count == 0 || (ToScreen(_poly[^1]) - ToScreen(p)).Length() > SamePixels) _poly.Add(p);
            R.LiveLayer = l.Id;
            _drag = Drag.Poly;
            LivePoly(l, null);
            return;
        }
        _downDoc = p;
        R.LiveLayer = l.Id;
        _drag = Drag.Shape;
        LiveShape(l, p, p, mb.ShiftPressed, mb.AltPressed);
    }

    void PressText(Vector2 d)
    {
        var hit = HitLayer(d, l => l.Kind == "text");
        if (hit != null)
        {
            Select(hit.Id);
            TextPlaced?.Invoke(hit);
            return;
        }
        var l = new ArtLayer
        {
            Kind = "text", Name = NextName("Text"), Text = "Text", X = d.X, Y = d.Y, Font = P.TextFont, Bold = P.TextBold,
            Size = P.TextSize, Color = Hex.Of(Ink),
        };
        AddLayer(l);
        Commit("Text");
        TextPlaced?.Invoke(l);
    }

    void PressMove(Vector2 d, InputEventMouseButton mb)
    {
        if (Picked.PressHandle(d, mb.Position)) { _drag = Drag.Pick; return; }
        if (!Picked.Any && Sel is { } s && !s.Locked && HandleAt(s, mb.Position) is int h and >= 0)
        {
            _startXf = s.Xf();
            _handle = h;
            _drag = h == 8 ? Drag.Rotate : Drag.Handle;
            return;
        }
        var (hit, shape) = Picked.Under(d);
        if (hit != null && shape != null) Picked.Take(hit, shape, loadLook: true);
        if (shape != null || (Picked.Any && Picked.InBox(d)))
        {
            Picked.GrabBody(d);
            _drag = Drag.Pick;
            return;
        }
        Picked.Drop();
        if (Sel is { Locked: false } held && InBounds(held, d) && (hit == null || Doc.Layers.IndexOf(hit) < Doc.Layers.IndexOf(held))) hit = held;
        if (hit == null) { if (Sel != null && !InBounds(Sel, d)) Select(null); return; }
        if (mb.DoubleClick && hit.Kind == "text")
        {
            Select(hit.Id);
            SetTool("text");
            TextPlaced?.Invoke(hit);
            return;
        }
        if (hit.Id != Doc.Selected) Select(hit.Id);
        _startXf = hit.Xf();
        _drag = Drag.Move;
    }

    void PressCrop(Vector2 d, InputEventMouseButton mb)
    {
        if (Sel is { Kind: "image", Locked: false } s)
        {
            int h = CropHandleAt(s, mb.Position);
            if (h >= 0)
            {
                _handle = h;
                _startCrop = s.Crop?.ToArray() ?? new float[] { 0, 0, s.SrcW, s.SrcH };
                _drag = Drag.Crop;
                return;
            }
        }
        _docCrop = new Rect2(Snap(d), Vector2.Zero);
        _drag = Drag.DocCrop;
    }

    void PressOutline(Vector2 d, InputEventMouseButton mb)
    {
        var ol = OutlinePts();
        int vi = NearestVertex(ol, mb.Position, 9);
        if (vi >= 0)
        {
            _outlineIdx = vi;
            _drag = Drag.OutlinePt;
            return;
        }
        int ei = NearestEdge(ol, mb.Position, 7);
        var p = Snap(d);
        if (ei >= 0) ol.Insert(ei + 1, p);
        else ol.Add(p);
        SetOutline(ol);
        _outlineIdx = ei >= 0 ? ei + 1 : ol.Count - 1;
        _drag = Drag.OutlinePt;
    }

    void PressMeasure(Vector2 d, InputEventMouseButton mb)
    {
        for (int i = 0; i + 3 < Doc.Measures.Count; i += 4)
            for (int end = 0; end < 2; end++)
                if ((ToScreen(new Vector2(Doc.Measures[i + end * 2], Doc.Measures[i + end * 2 + 1])) - mb.Position).Length() < 9)
                {
                    _measureIdx = i;
                    _measureEnd = end;
                    _drag = Drag.MeasureEnd;
                    return;
                }
        var p = Snap(d);
        Doc.Measures.AddRange(new[] { p.X, p.Y, p.X, p.Y });
        _measureIdx = Doc.Measures.Count - 4;
        _measureEnd = 1;
        _drag = Drag.Measure;
    }

    void PressPrint(InputEventMouseButton mb)
    {
        var chosen = Prints.Find(p => p.Key == Chosen);
        int handle = chosen != null ? BoxHandleAt(chosen, mb.Position) : -1;
        if (chosen == null || handle < 0)
        {
            if (chosen == null || !BoxRect(chosen).HasPoint(mb.Position)) chosen = Prints.LastOrDefault(p => BoxRect(p).HasPoint(mb.Position));
            if (chosen == null) return;
            handle = 9;
            if (chosen.Key != Chosen)
            {
                Chosen = chosen.Key;
                PrintChosen?.Invoke(chosen.Key);
                Redraw();
            }
        }
        _print = chosen;
        _boxStart = new FaceBox { X = chosen.Box.X, Y = chosen.Box.Y, W = chosen.Box.W, H = chosen.Box.H };
        _handle = handle;
        _drag = Drag.Print;
    }

    Vector2? _lastStrokeEnd;

    void RightClick(Vector2 d, InputEventMouseButton mb)
    {
        if (mb.CtrlPressed)
        {
            var u = DocToPiece(d);
            var text = string.Format(Inv, "x = {0:0.###}, z = {1:0.###}", u.X, u.Y);
            AppClipboard.SetText(text);
            Status?.Invoke("Copied " + text + " (piece units, for a script's widgets)");
            return;
        }
        switch (Tool)
        {
            case "pick": case "brush": case "fill":
                _ = Pick(d, true);
                break;
            case "shape" when _poly.Count > 0:
                _poly.RemoveAt(_poly.Count - 1);
                if (_poly.Count == 0) CancelGesture();
                else if (Doc.Layer(R.LiveLayer) is { } l) LivePoly(l, _hoverDoc);
                break;
            case "outline":
            {
                var ol = OutlinePts();
                int vi = NearestVertex(ol, mb.Position, 9);
                if (vi >= 0)
                {
                    ol.RemoveAt(vi);
                    SetOutline(ol);
                    Commit("Outline point removed");
                }
                break;
            }
            case "measure":
                for (int i = 0; i + 3 < Doc.Measures.Count; i += 4)
                {
                    var a = ToScreen(new Vector2(Doc.Measures[i], Doc.Measures[i + 1]));
                    var b = ToScreen(new Vector2(Doc.Measures[i + 2], Doc.Measures[i + 3]));
                    if (Geometry2D.GetClosestPointToSegment(mb.Position, a, b).DistanceTo(mb.Position) < 8)
                    {
                        Doc.Measures.RemoveRange(i, 4);
                        Commit("Measure removed");
                        break;
                    }
                }
                break;
        }
    }

    void Drag_(Vector2 d, InputEventMouseMotion mm)
    {
        switch (_drag)
        {
            case Drag.Paint:
            {
                if (Doc.Layer(R.LiveLayer) is not { } l) return;
                var p = Snap(d);
                if (mm.ShiftPressed && _stroke.Count > 0) { _stroke.RemoveRange(1, _stroke.Count - 1); _stroke.Add(Constrain(_stroke[0], p)); }
                else if (_stroke.Count == 0 || (p - _stroke[^1]).Length() > Mathf.Max(0.6f, Px(1.5f))) _stroke.Add(p);
                LiveFromStroke(l);
                break;
            }
            case Drag.Shape:
                if (Doc.Layer(R.LiveLayer) is { } sl) LiveShape(sl, _downDoc, Snap(d), mm.ShiftPressed, mm.AltPressed);
                break;
            case Drag.Poly:
                if (Doc.Layer(R.LiveLayer) is { } pl) LivePoly(pl, mm.ShiftPressed && _poly.Count > 0 ? Constrain(_poly[^1], Snap(d)) : Snap(d));
                break;
            case Drag.Pick:
                Picked.Drag(d, mm.ShiftPressed);
                break;
            case Drag.Move:
            {
                if (Sel is not { } s) return;
                var delta = d - _downDoc;
                if (mm.ShiftPressed) delta = Mathf.Abs(delta.X) > Mathf.Abs(delta.Y) ? new Vector2(delta.X, 0) : new Vector2(0, delta.Y);
                var t = _startXf;
                t.Origin += delta;
                if (P.Snap) { var c = t * LocalBounds(s).GetCenter(); t.Origin += Snap(c) - c; }
                s.SetXf(t);
                R.Moved(s);
                Redraw();
                break;
            }
            case Drag.Handle:
                if (Sel is { } hs) ScaleByHandle(hs, d, mm.ShiftPressed);
                break;
            case Drag.Rotate:
            {
                if (Sel is not { } rs) return;
                var pivot = _startXf * LocalBounds(rs).GetCenter();
                float a = (d - pivot).Angle() - (_downDoc - pivot).Angle();
                if (mm.ShiftPressed)
                {
                    float total = Mathf.RadToDeg(_startXf.Rotation + a);
                    a = Mathf.DegToRad(Mathf.Round(total / 15) * 15) - _startXf.Rotation;
                }
                var t = new Transform2D(0, pivot) * new Transform2D(a, Vector2.Zero) * new Transform2D(0, -pivot) * _startXf;
                rs.SetXf(t);
                R.Moved(rs);
                Redraw();
                break;
            }
            case Drag.Crop:
                if (Sel is { } cs) CropByHandle(cs, d);
                break;
            case Drag.DocCrop:
                _docCrop = new Rect2(_docCrop.Position, Snap(d) - _docCrop.Position);
                Redraw();
                break;
            case Drag.OutlinePt:
            {
                var ol = OutlinePts();
                if (_outlineIdx >= 0 && _outlineIdx < ol.Count) { ol[_outlineIdx] = Snap(d); SetOutline(ol); }
                break;
            }
            case Drag.Measure:
            case Drag.MeasureEnd:
            {
                if (_measureIdx < 0 || _measureIdx + 3 >= Doc.Measures.Count) return;
                var p = Snap(d);
                var other = new Vector2(Doc.Measures[_measureIdx + (1 - _measureEnd) * 2], Doc.Measures[_measureIdx + (1 - _measureEnd) * 2 + 1]);
                if (mm.ShiftPressed) p = Constrain(other, p);
                Doc.Measures[_measureIdx + _measureEnd * 2] = p.X;
                Doc.Measures[_measureIdx + _measureEnd * 2 + 1] = p.Y;
                Redraw();
                break;
            }
            case Drag.Print:
                DragBox(d);
                break;
        }
        _lastDoc = d;
    }

    void Release(Vector2 d, InputEventMouseButton mb)
    {
        var was = _drag;
        _drag = was == Drag.Poly ? Drag.Poly : Drag.None;
        switch (was)
        {
            case Drag.Paint:
            {
                if (Doc.Layer(R.LiveLayer) is { } l && R.LiveCopies.Count > 0)
                {
                    l.Ops.AddRange(R.LiveCopies.Select(c => c.Clone()));
                    _lastStrokeEnd = _stroke.Count > 0 ? _stroke[^1] : null;
                    P.Used(Ink);
                    EndLive(l);
                    Commit(Tool == "erase" ? "Rub out" : "Brush");
                }
                else EndLive(null);
                break;
            }
            case Drag.Shape:
            {
                if (Doc.Layer(R.LiveLayer) is { } l && (Snap(d) - _downDoc).Length() * _zoom > 2)
                {
                    if (d != _lastDoc) LiveShape(l, _downDoc, Snap(d), mb.ShiftPressed, mb.AltPressed);
                    if (R.LiveCopies.Count > 0) Lay(l, "Shape");
                    else EndLive(null);
                }
                else EndLive(null);
                break;
            }
            case Drag.Pick:
                Picked.Release();
                break;
            case Drag.Move:
                if (Sel is { } ms && ms.Xf() != _startXf) Commit("Move");
                break;
            case Drag.Handle:
                Commit("Resize");
                break;
            case Drag.Rotate:
                Commit("Turn");
                break;
            case Drag.Crop:
                Commit("Crop");
                break;
            case Drag.DocCrop:
                if (_docCrop.Abs().Size.X * _zoom > 6 && _docCrop.Abs().Size.Y * _zoom > 6) CropDocument(_docCrop.Abs());
                _docCrop = new Rect2();
                Redraw();
                break;
            case Drag.OutlinePt:
                Commit("Outline");
                break;
            case Drag.Measure:
                if (_measureIdx >= 0 && _measureIdx + 3 < Doc.Measures.Count && new Vector2(Doc.Measures[_measureIdx] - Doc.Measures[_measureIdx + 2],
                    Doc.Measures[_measureIdx + 1] - Doc.Measures[_measureIdx + 3]).Length() * _zoom < 3)
                { Doc.Measures.RemoveRange(_measureIdx, 4); Redraw(); }
                else Commit("Measure");
                break;
            case Drag.MeasureEnd:
                Commit("Measure");
                break;
            case Drag.Print:
                if (_print != null && _boxStart != null && new[] { _print.Box.X - _boxStart.X, _print.Box.Y - _boxStart.Y, _print.Box.W - _boxStart.W,
                    _print.Box.H - _boxStart.H }.Any(v => Math.Abs(v) > 1e-6))
                    PrintMoved?.Invoke(_print.Key, _print.Box);
                break;
        }
    }

    void EndLive(ArtLayer? committed)
    {
        R.LiveOp = null;
        R.LiveCopies.Clear();
        if (committed != null) R.Rebuild(committed);
        else R.LiveChanged();
        R.LiveLayer = null;
        _stroke.Clear();
        _flat = null;
    }

    void Lay(ArtLayer l, string what)
    {
        string group = ArtDoc.NewId();
        var at = new List<int>();
        foreach (var live in R.LiveCopies)
        {
            var op = live.Clone();
            op.G = group;
            op.S = P.ShapeKind;
            at.Add(l.Ops.Count);
            l.Ops.Add(op);
        }
        P.Used(Ink);
        EndLive(l);
        Commit(what);
        Picked.Take(l, at, loadLook: false);
    }

    internal static float LayerScale(ArtLayer l) => Mathf.Max(0.0001f, (Mathf.Abs(l.Sx) + Mathf.Abs(l.Sy)) / 2);

    List<Vector2[]> Copies(ArtLayer l, IReadOnlyList<Vector2> docPts)
    {
        var inv = l.Xf().AffineInverse();
        var res = new List<Vector2[]>();
        foreach (var s in Sym())
        {
            var a = new Vector2[docPts.Count];
            for (int i = 0; i < a.Length; i++) a[i] = inv * (s * docPts[i]);
            res.Add(a);
        }
        return res;
    }

    void LiveFromStroke(ArtLayer l)
    {
        if (R.LiveOp is not { } op) return;
        R.LiveCopies.Clear();
        foreach (var pts in Copies(l, _stroke))
        {
            var c = op.Clone();
            c.P = ArtXf.Flat(pts);
            R.LiveCopies.Add(c);
        }
        R.LiveChanged();
    }

    List<(List<Vector2> pts, bool closed)> ShapeGeometry(Vector2 a, Vector2 b, bool shift, bool fromCentre)
    {
        var kind = P.ShapeKind;
        if (kind is "line" or "arrow") { if (shift) b = Constrain(a, b); }
        else if (kind is "ngon" or "star") { if (shift) b = Constrain(a, b, 360f / Math.Max(3, P.Sides) / 2); }
        else
        {
            if (shift)
            {
                var dd = b - a;
                float m = Mathf.Max(Mathf.Abs(dd.X), Mathf.Abs(dd.Y));
                b = a + new Vector2(Mathf.Sign(dd.X == 0 ? 1 : dd.X) * m, Mathf.Sign(dd.Y == 0 ? 1 : dd.Y) * m);
            }
            if (fromCentre) a = a - (b - a);
        }
        return kind switch
        {
            "line" => new() { (new List<Vector2> { a, b }, false) },
            "arrow" => new() { (new List<Vector2> { a, ArtGeom.ArrowShaftEnd(a, b, P.Line) }, false), (ArtGeom.ArrowHead(a, b, P.Line), true) },
            "ellipse" => new() { (ArtGeom.Ellipse(a, b), true) },
            "round" => new() { (ArtGeom.RoundRect(a, b, Mathf.Min(Mathf.Abs(b.X - a.X), Mathf.Abs(b.Y - a.Y)) * 0.18f), true) },
            "ngon" => new() { (ArtGeom.Ngon(a, b, P.Sides), true) },
            "star" => new() { (ArtGeom.Star(a, b, P.Sides, P.StarInner), true) },
            _ => new() { (ArtGeom.Rect(a, b), true) },
        };
    }

    PaintOp ShapeOp(ArtLayer l, List<Vector2> pts, bool closed, bool head = false)
    {
        float w = P.Line / LayerScale(l);
        if (!closed) return new PaintOp { K = "path", C = Hex.Of(Ink), W = w };
        if (head) return new PaintOp { K = "shape", C = Hex.Of(Ink), W = 0, Fc = Hex.Of(Ink) };
        return P.ShapeFill switch
        {
            "fill" => new PaintOp { K = "shape", C = Hex.Of(Ink), W = 0, Fc = Hex.Of(Ink) },
            "both" => new PaintOp { K = "shape", C = Hex.Of(Ink), W = w, Fc = Hex.Of(Ink2) },
            _ => new PaintOp { K = "shape", C = Hex.Of(Ink), W = w },
        };
    }

    void LiveShape(ArtLayer l, Vector2 a, Vector2 b, bool shift, bool alt)
    {
        R.LiveCopies.Clear();
        var geo = ShapeGeometry(a, b, shift, alt);
        R.LiveOp = null;
        for (int g = 0; g < geo.Count; g++)
        {
            var proto = ShapeOp(l, geo[g].pts, geo[g].closed, head: P.ShapeKind == "arrow" && g == 1);
            R.LiveOp ??= proto;
            foreach (var pts in Copies(l, geo[g].pts))
            {
                var c = proto.Clone();
                c.P = ArtXf.Flat(pts);
                R.LiveCopies.Add(c);
            }
        }
        R.LiveChanged();
    }

    List<(List<Vector2> pts, bool closed, bool head)> PathGeometry(List<Vector2> knots)
    {
        bool ring = knots.Count >= 3;
        switch (P.ShapeKind)
        {
            case Polyline.Curve:
                return new() { (ArtGeom.Smooth(knots, false), false, false) };
            case Polyline.CurvedArrow:
            {
                var (body, head) = ArtGeom.CurvedArrow(knots, P.Line);
                return head.Count == 3 ? new() { (body, false, false), (head, true, true) } : new() { (body, false, false) };
            }
            case Polyline.Area:
                return new() { (ArtGeom.Smooth(knots, ring), ring, false) };
            default:
                return new() { (knots, ring, false) };
        }
    }

    void LivePoly(ArtLayer l, Vector2? hover)
    {
        R.LiveCopies.Clear();
        var knots = _poly.ToList();
        if (hover is { } h && knots.Count > 0 && (ToScreen(knots[^1]) - ToScreen(h)).Length() > SamePixels) knots.Add(h);
        R.LiveOp = null;
        foreach (var (pts, closed, head) in PathGeometry(knots))
        {
            var proto = ShapeOp(l, pts, closed, head);
            R.LiveOp ??= proto;
            foreach (var copy in Copies(l, pts))
            {
                var c = proto.Clone();
                c.P = ArtXf.Flat(copy);
                R.LiveCopies.Add(c);
            }
        }
        R.LiveChanged();
    }

    public void FinishPoly()
    {
        if (Doc.Layer(R.LiveLayer) is { } l && _poly.Count >= 2)
        {
            LivePoly(l, null);
            string what = P.ShapeKind switch { Polyline.Curve or Polyline.CurvedArrow => "Curve", Polyline.Area => "Area", _ => "Polygon" };
            _poly.Clear();
            _drag = Drag.None;
            Lay(l, what);
        }
        else CancelGesture();
    }

    async Task<Image> Flat()
    {
        if (_flat != null) return _flat;
        _flat = await R.Flatten(cut: false);
        return _flat;
    }

    async Task Pick(Vector2 d, bool second)
    {
        var img = await Flat();
        int x = (int)d.X, y = (int)d.Y;
        if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) return;
        var c = img.GetPixel(x, y);
        if (c.A < 0.02f) return;
        InkPicked?.Invoke(c with { A = 1 }, second);
    }

    async Task Fill(Vector2 d)
    {
        var l = PaintTarget();
        if (l.Locked) return;
        var img = P.FillAllLayers ? await Flat() : await R.Flatten(cut: false, only: l.Id);
        var ops = new List<PaintOp>();
        foreach (var s in Sym())
        {
            var p = s * d;
            if (Flood(img, (int)p.X, (int)p.Y, P.FillTolerance) is not { } m) continue;
            var name = SaveSrc(m.mask);
            R.Remember(name, m.mask);
            var t = l.Xf().AffineInverse() * new Transform2D(0, m.at);
            ops.Add(new PaintOp
            {
                K = "fill", Fc = Hex.Of(Ink), C = Hex.Of(Ink), M = name, W = 0,
                P = new List<float> { m.mask.GetWidth(), m.mask.GetHeight() },
                Mx = new[] { t.X.X, t.X.Y, t.Y.X, t.Y.Y, t.Origin.X, t.Origin.Y },
            });
        }
        if (ops.Count == 0) return;
        l.Ops.AddRange(ops);
        P.Used(Ink);
        R.Rebuild(l);
        Commit("Fill");
    }

    static (Image mask, Vector2 at)? Flood(Image img, int sx, int sy, float tol)
    {
        if (FloodFill.Region(img.GetData(), img.GetWidth(), img.GetHeight(), sx, sy, tol) is not { } m) return null;
        return (Image.CreateFromData(m.w, m.h, false, Image.Format.Rgba8, m.mask), new Vector2(m.x, m.y));
    }

    public static Rect2 LocalBounds(ArtLayer l) => l.Kind switch
    {
        "image" => ArtDraw.ImageBounds(l),
        "text" => ArtDraw.TextBounds(l),
        "block" => new Rect2(0, 0, l.Block?.BW ?? 1, l.Block?.BH ?? 1),
        _ => OpsBounds(l),
    };

    static Rect2 OpsBounds(ArtLayer l)
    {
        Rect2? b = null;
        foreach (var op in l.Ops) { var r = ArtDraw.OpBounds(op); b = b is { } bb ? bb.Merge(r) : r; }
        return b ?? new Rect2(0, 0, 0, 0);
    }

    bool InBounds(ArtLayer l, Vector2 d) => LocalBounds(l).HasPoint(l.Xf().AffineInverse() * d);

    internal bool Covers(ArtLayer l, Vector2 d)
    {
        var local = l.Xf().AffineInverse() * d;
        if (!LocalBounds(l).HasPoint(local)) return false;
        if (l.Kind != "image" || l.Src == null || R.Img(l.Src) is not { } img) return true;
        int x = (int)(local.X + l.SrcW / 2f), y = (int)(local.Y + l.SrcH / 2f);
        return x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight() || img.GetPixel(x, y).A >= 0.1f;
    }

    ArtLayer? HitLayer(Vector2 d, Func<ArtLayer, bool> ok)
    {
        for (int i = Doc.Layers.Count - 1; i >= 0; i--)
        {
            var l = Doc.Layers[i];
            if (l.Visible && ok(l) && Covers(l, d)) return l;
        }
        return null;
    }

    internal static Vector2[] HandlePts(Rect2 b) => Array.ConvertAll(HandleMath.Points(N(b.Position), N(b.End)), G);

    internal Vector2 RotateKnob(ArtLayer l)
    {
        var b = LocalBounds(l);
        var xf = l.Xf();
        var top = ToScreen(xf * new Vector2(b.GetCenter().X, b.Position.Y));
        var centre = ToScreen(xf * b.GetCenter());
        var dir = (top - centre).Normalized();
        if (dir == Vector2.Zero) dir = Vector2.Up;
        return top + dir * 26;
    }

    int HandleAt(ArtLayer l, Vector2 screen)
    {
        if ((RotateKnob(l) - screen).Length() < 9) return 8;
        var xf = l.Xf();
        var hp = HandlePts(LocalBounds(l));
        for (int i = 0; i < hp.Length; i++) if ((ToScreen(xf * hp[i]) - screen).Length() < 8) return i;
        return -1;
    }

    void ScaleByHandle(ArtLayer l, Vector2 d, bool free)
    {
        var b = LocalBounds(l);
        var hp = HandlePts(b);
        var anchor = hp[HandleMath.Opposite(_handle)];
        var (sx, sy) = HandleMath.Scale(_handle, N(anchor), N(hp[_handle]), N(_startXf.AffineInverse() * d), free);
        var t = _startXf * new Transform2D(0, anchor) * new Transform2D(0, new Vector2(sx, sy), 0, Vector2.Zero) * new Transform2D(0, -anchor);
        l.SetXf(t);
        R.Moved(l);
        Redraw();
    }

    int CropHandleAt(ArtLayer l, Vector2 screen)
    {
        var xf = l.Xf();
        var hp = HandlePts(ArtDraw.ImageBounds(l));
        for (int i = 0; i < hp.Length; i++) if ((ToScreen(xf * hp[i]) - screen).Length() < 9) return i;
        return -1;
    }

    void CropByHandle(ArtLayer l, Vector2 d)
    {
        if (_startCrop == null) return;
        var local = l.Xf().AffineInverse() * d + new Vector2(l.SrcW, l.SrcH) / 2;
        l.Crop = HandleMath.Crop(_handle, _startCrop, N(local), l.SrcW, l.SrcH);
        R.Rebuild(l);
        Redraw();
    }

    public void CropDocument(Rect2 r)
    {
        r = r.Intersection(new Rect2(0, 0, Doc.W, Doc.H));
        if (r.Size.X < 4 || r.Size.Y < 4) return;
        ResizeDocument((int)r.Size.X, (int)r.Size.Y, -r.Position);
        Commit("Crop the face");
    }

    public const int SmallestSide = 8, LargestSide = 4096;

    public void ResizeDocument(int w, int h, Vector2 shift)
    {
        Shift(Doc, w, h, shift);
        R.Resized();
        _flat = null;
        Fit();
    }

    static void Shift(ArtDoc doc, int w, int h, Vector2 shift)
    {
        doc.W = Math.Clamp(w, SmallestSide, LargestSide);
        doc.H = Math.Clamp(h, SmallestSide, LargestSide);
        foreach (var l in doc.Layers) { l.X += shift.X; l.Y += shift.Y; }
        if (doc.Outline != null) for (int i = 0; i + 1 < doc.Outline.Count; i += 2) { doc.Outline[i] += shift.X; doc.Outline[i + 1] += shift.Y; }
        for (int i = 0; i + 1 < doc.Measures.Count; i += 2) { doc.Measures[i] += shift.X; doc.Measures[i + 1] += shift.Y; }
    }

    public static void ScaleLayers(ArtDoc doc, int w, int h)
    {
        float kx = w / (float)doc.W, ky = h / (float)doc.H;
        foreach (var l in doc.Layers) l.SetXf(new Transform2D(0, new Vector2(kx, ky), 0, Vector2.Zero) * l.Xf());
        if (doc.Outline != null) for (int i = 0; i + 1 < doc.Outline.Count; i += 2) { doc.Outline[i] *= kx; doc.Outline[i + 1] *= ky; }
        for (int i = 0; i + 1 < doc.Measures.Count; i += 2) { doc.Measures[i] *= kx; doc.Measures[i + 1] *= ky; }
    }

    public static void Resize(ArtDoc doc, int w, int h, bool scale)
    {
        if (scale)
        {
            ScaleLayers(doc, w, h);
            Shift(doc, w, h, Vector2.Zero);
        }
        else Shift(doc, w, h, new Vector2(w - doc.W, h - doc.H) / 2);
    }

    public List<Vector2> OutlinePts()
    {
        var l = new List<Vector2>();
        if (Doc.Outline != null) for (int i = 0; i + 1 < Doc.Outline.Count; i += 2) l.Add(new Vector2(Doc.Outline[i], Doc.Outline[i + 1]));
        return l;
    }

    public void SetOutline(List<Vector2>? pts)
    {
        Doc.Outline = pts == null || pts.Count == 0 ? null : ArtXf.Flat(pts);
        Redraw();
    }

    int NearestVertex(List<Vector2> pts, Vector2 screen, float within) => HandleMath.NearestPoint(pts.ConvertAll(p => N(ToScreen(p))), N(screen), within);

    int NearestEdge(List<Vector2> pts, Vector2 screen, float within) => HandleMath.NearestEdge(pts.ConvertAll(p => N(ToScreen(p))), N(screen), within);

    internal Rect2 BoxRect(PrintBox p) => new(ToScreen(new Vector2((float)p.Box.X * Doc.W, (float)p.Box.Y * Doc.H)), new Vector2((float)p.Box.W * Doc.W, (float)p.Box.H * Doc.H) * _zoom);

    int BoxHandleAt(PrintBox p, Vector2 screen)
    {
        var hp = HandlePts(BoxRect(p));
        for (int i = 0; i < hp.Length; i++) if ((hp[i] - screen).Length() < 8) return i;
        return -1;
    }

    void DragBox(Vector2 d)
    {
        if (_print == null || _boxStart == null) return;
        var delta = d - _downDoc;
        var b = _print.Box;
        (b.X, b.Y, b.W, b.H) = HandleMath.Box(_handle, _boxStart.X, _boxStart.Y, _boxStart.W, _boxStart.H, delta.X / Doc.W, delta.Y / Doc.H);
        Redraw();
    }

    public void ChangeSize(float k)
    {
        if (Tool == "erase") P.Eraser = Mathf.Clamp(P.Eraser * k, 1, 600);
        else if (Tool == "shape" || Picked.Any)
        {
            P.Line = Mathf.Clamp(P.Line * k, ThinnestLine, ThickestLine);
            Picked.Restyle("Line width");
        }
        else P.Brush = Mathf.Clamp(P.Brush * k, 0.5f, 600);
        ToolChanged?.Invoke();
        _over!.QueueRedraw();
    }

    public void SetSpace(bool down) { _spaceDown = down; MouseDefaultCursorShape = down ? CursorShape.Drag : Tool == "pan" ? CursorShape.Drag : CursorShape.Cross; }

    public bool Key(InputEventKey k)
    {
        switch (k.Keycode)
        {
            case Godot.Key.Escape:
                if (_poly.Count > 0 || _drag != Drag.None) { CancelGesture(); return true; }
                if (Picked.Any) { Picked.Drop(); return true; }
                if (Tool == "move" && Sel != null) { Select(null); return true; }
                return false;
            case Godot.Key.Enter or Godot.Key.KpEnter:
                if (_poly.Count > 0) { FinishPoly(); return true; }
                return false;
            case Godot.Key.Backspace when _poly.Count > 0:
                _poly.RemoveAt(_poly.Count - 1);
                if (_poly.Count == 0) CancelGesture();
                else if (Doc.Layer(R.LiveLayer) is { } pl) LivePoly(pl, _hoverDoc);
                return true;
            case Godot.Key.Delete or Godot.Key.Backspace when Picked.Any:
                return Picked.Delete();
            case Godot.Key.Left or Godot.Key.Right or Godot.Key.Up or Godot.Key.Down when Picked.Any:
                return Picked.Nudge(Step(k));
            case Godot.Key.Left or Godot.Key.Right or Godot.Key.Up or Godot.Key.Down when Tool == "move" && Sel is { Locked: false } s:
            {
                var v = Step(k);
                s.X += v.X;
                s.Y += v.Y;
                R.Moved(s);
                Commit("Nudge");
                return true;
            }
        }
        return false;
    }

    static Vector2 Step(InputEventKey k)
    {
        float step = k.ShiftPressed ? 10 : 1;
        return k.Keycode switch { Godot.Key.Left => new Vector2(-step, 0), Godot.Key.Right => new Vector2(step, 0), Godot.Key.Up => new Vector2(0, -step), _ => new Vector2(0, step) };
    }

    void HoverCursor(Vector2 d)
    {
        if (Picked.Any)
        {
            var grip = Picked.GripAt(ToScreen(d)).Grip;
            MouseDefaultCursorShape = grip switch
            {
                StudioPick.Grip.Turn => CursorShape.PointingHand,
                StudioPick.Grip.Corner or StudioPick.Grip.End => CursorShape.Cross,
                _ => (Tool == "move" ? Picked.InBox(d) : Picked.OnInk(d)) ? CursorShape.Move : Tool == "move" ? CursorShape.Arrow : CursorShape.Cross,
            };
            return;
        }
        if (Tool == "shape") MouseDefaultCursorShape = CursorShape.Cross;
        if (Tool == "move" && Sel is { } s)
        {
            var screen = ToScreen(d);
            int h = HandleAt(s, screen);
            MouseDefaultCursorShape = h == 8 ? CursorShape.PointingHand : h is 0 or 2 ? CursorShape.Fdiagsize : h is 1 or 3 ? CursorShape.Bdiagsize : h is 4 or 6 ? CursorShape.Vsize : h is 5 or 7
                ? CursorShape.Hsize
                : InBounds(s, d) ? CursorShape.Move : CursorShape.Arrow;
        }
    }

    partial class OverView : Control
    {
        public StudioCanvas Canvas = null!;
        public override void _Draw() => Canvas.DrawOverlay(this);
    }

    StudioOverlay? _overlay;
    void DrawOverlay(Control o) => (_overlay ??= new StudioOverlay(this)).Draw(o);
    internal Vector2 HoverDoc => _hoverDoc;
    internal Vector2 DownDoc => _downDoc;
    internal bool HoverIn => _hoverIn;
    internal Rect2 DocCrop => _docCrop;
    internal Drag Gesture => _drag;
    internal IReadOnlyList<Vector2> Poly => _poly;
}
