using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceStudio
{
    void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.Theme() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        Top.Build(Menu.Show, () => _ = Leave()).Resized += Relayout;
        Strip.Build();
        BuildCentre();
        BuildRight();
        _tip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeStyleboxOverride("panel", Ui.Panel(0.96f));
        _tipText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddChild(_tipText);
        _root.AddChild(_tip);
        var loading = new LoadingStrip();
        loading.Track(S.Assets);
        _root.AddChild(loading);
        Callable.From(() => { loading.SetAnchorsPreset(Control.LayoutPreset.CenterTop); loading.OffsetTop = TopH + 10; loading.OffsetBottom = TopH + 38; loading.OffsetLeft = -200; loading.OffsetRight = 200; }).CallDeferred();
        foreach (var p in new Control[] { Top.Panel, Strip.Panel, _right, FaceBar.Pill, _zoomPill }) Ui.SwallowWheel(p);
        ToolPanel.Build();
        Callable.From(Relayout).CallDeferred();
    }

    void BuildCentre()
    {
        _centre = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_centre);
        _canvas = new StudioCanvas { P = _prefs, Visible = false };
        _canvas.SaveSrc = img => ArtStore.AddSrc(_store, _pack.Id, img);
        _canvas.FreeArea = FreeArea;
        _canvas.R.LoadSrc = name => ArtStore.LoadSrc(_store, _pack.Id, name);
        _canvas.Ink = Hex.To(_prefs.Ink, _canvas.Ink); _canvas.Ink2 = Hex.To(_prefs.Ink2, _canvas.Ink2);
        _canvas.Edited += what =>
        {
            Faces.Edited();
            Top.Saved = "editing…"; Strip.RefreshUndo();
            if (what is "Undo" or "Redo") RebuildLayers();
            if (what is "Move" or "Resize" or "Turn" or "Nudge" or "Undo" or "Redo" or "Crop" or "Outline" or "Outline point removed" && !Typing()) Callable.From(RebuildToolPanel).CallDeferred();
            if (what is "Undo" or "Redo") RebuildFaceBar();
        };
        _canvas.LayersChanged += RebuildLayers;
        _canvas.Status += s => { if (_view == "paint") _status.Text = s; };
        _canvas.InkPicked += (c, second) => { if (second) _canvas.Ink2 = c; else _canvas.Ink = c; ToolPanel.Colours.SyncInk(); Flash($"Picked {Hex.Of(c)[..7]}"); };
        _canvas.TextPlaced += l => { RebuildToolPanel(); ToolPanel.FocusTextEdit(); };
        _canvas.PrintMoved += (key, box) => Prints.Moved(key, box, CurFace?.Back == true ? PrintPlace.Back : PrintPlace.Front);
        _canvas.PrintChosen += _ => RebuildToolPanel();
        _canvas.ToolChanged += () => { Strip.SyncTool(_canvas.Tool); RebuildToolPanel(); RefreshHint(); };
        _centre.AddChild(_canvas);
        _centre.AddChild(FaceBar.Build());
        _zoomPill = MakePill();
        var zr = new HBoxContainer();
        zr.AddThemeConstantOverride("separation", 6);
        _status = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center, Modulate = Ui.Muted };
        _status.AddThemeFontSizeOverride("font_size", 11);
        zr.AddChild(_status);
        var fit = new Button { Flat = true, CustomMinimumSize = new Vector2(24, 22), FocusMode = Control.FocusModeEnum.None, TooltipText = "Fit the picture to the view   (Ctrl+0)" };
        fit.AddChild(new ToolIcon { Kind = "frame" });
        fit.Pressed += () => _canvas.Fit();
        zr.AddChild(fit);
        _zoomPill.AddChild(zr);
        _centre.AddChild(_zoomPill);
        _empty = BuildEmpty();
        _centre.AddChild(_empty);
    }

    void BuildRight()
    {
        _right = new PanelContainer();
        _right.AddThemeStyleboxOverride("panel", Ui.Panel());
        _root.AddChild(_right);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        _right.AddChild(col);
        var split = PieceList.Build(col);
        var pieceBox = new VBoxContainer();
        pieceBox.AddThemeConstantOverride("separation", 2);
        split.AddChild(pieceBox);
        var ph = new HBoxContainer();
        _pieceName = new Label { ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _pieceName.AddThemeFontSizeOverride("font_size", 15);
        _pieceName.AddThemeFontOverride("font", Ui.SansStrong());
        ph.AddChild(_pieceName);
        Button Act(string icon, string tip, Action a)
        {
            var b = new Button { Flat = true, CustomMinimumSize = new Vector2(28, 26), FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
            b.AddChild(new StudioIcon { Kind = icon });
            b.Pressed += a;
            ph.AddChild(b);
            return b;
        }
        Act("dup", "Duplicate this piece, pictures and all", () => DuplicateSel());
        Act("trash", "Delete this piece", () => DeleteSel());
        pieceBox.AddChild(ph);
        pieceBox.AddChild(Inspector.Build());
    }

    internal static PanelContainer MakePill()
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var box = Ui.Box(new Color(Ui.Bar, 0.92f), 14, 10, 4);
        p.AddThemeStyleboxOverride("panel", box);
        return p;
    }

    Rect2 FreeArea()
    {
        var r = _canvas.GetGlobalRect();
        float l = r.Position.X, top = 0, bottom = r.Size.Y - 44;
        if (ToolPanel.Panel is { Visible: true } p)
        {
            var pr = p.GetGlobalRect();
            if (pr.End.X > r.Position.X && pr.Position.X < r.GetCenter().X) l = Mathf.Max(l, pr.End.X);
        }
        return new Rect2(l - r.Position.X, top, Mathf.Max(0, r.End.X - l), Mathf.Max(0, bottom));
    }

    internal void Relayout()
    {
        float top = Mathf.Max(TopH, Top.Panel.Size.Y) + Gap;
        var strip = Strip.Panel;
        Ui.Place(strip, Control.LayoutPreset.TopLeft, Gap, top, Gap + StripW, top + strip.GetCombinedMinimumSize().Y);
        float left = Gap + StripW + Gap;
        Ui.Place(_right, Control.LayoutPreset.RightWide, -Gap - RightW, top, -Gap, -Gap);
        Ui.Place(_centre, Control.LayoutPreset.FullRect, left, top, -Gap - RightW - Gap, -Gap);
        Ui.Place(_canvas, Control.LayoutPreset.FullRect, 0, 0, 0, 0);
        if (_model != null) Ui.Place(_model, Control.LayoutPreset.FullRect, 0, 0, 0, 0);
        Callable.From(PlacePills).CallDeferred();
        ToolPanel.Place();
    }

    internal void PlacePills()
    {
        if (!IsInstanceValid(_centre)) return;
        var c = _centre.Size;
        var faceBar = FaceBar.Pill;
        faceBar.ResetSize();
        var fs = faceBar.GetCombinedMinimumSize();
        faceBar.Position = new Vector2(Mathf.Round((c.X - fs.X) / 2), c.Y - fs.Y - 8);
        faceBar.Size = fs;
        _zoomPill.ResetSize();
        var zs = _zoomPill.GetCombinedMinimumSize();
        _zoomPill.Position = new Vector2(c.X - zs.X - 8, c.Y - zs.Y - 8);
        _zoomPill.Size = zs;
        _empty!.ResetSize();
        var es = _empty.GetCombinedMinimumSize();
        _empty.Position = ((c - es) / 2).Round();
    }

    Control BuildEmpty()
    {
        var p = new PanelContainer { Visible = false };
        p.AddThemeStyleboxOverride("panel", Ui.Panel(0.96f));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        p.AddChild(box);
        var h = new Label { Text = "This pack has no pieces yet", HorizontalAlignment = HorizontalAlignment.Center };
        h.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(h);
        box.AddChild(new Label { Text = "Make the first one — or drop pictures or 3D models onto the window.", Modulate = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(NewPieces.Cards(() => { }));
        return p;
    }

    public override void _Process(double delta)
    {
        _packSaver.Tick(delta);
        Faces.Tick(delta, Input.IsMouseButtonPressed(MouseButton.Left));
        if (Markers.Active) Markers.Watch(delta);
        Top.Tick();
    }

    public override void _Input(InputEvent e)
    {
        if (_view == "paint" && e is InputEventKey { Keycode: Key.Space } sk && !Typing())
        {
            _canvas.SetSpace(sk.Pressed);
            if (sk.Pressed) GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        if (k.AltPressed && k.Keycode is Key.Key1 or Key.Key2 or Key.Key3)
        {
            var order = ViewKeys();
            int i = k.Keycode == Key.Key1 ? 0 : k.Keycode == Key.Key2 ? 1 : 2;
            if (i < order.Count) SetView(order[i].key);
            Handled(); return;
        }
        if ((k.CtrlPressed || k.MetaPressed) && k.Keycode is Key.Z or Key.Y)
        {
            bool redo = k.Keycode == Key.Y || k.ShiftPressed;
            if (_view != "table" || (redo ? CanRedo : CanUndo)) { if (redo) Redo(); else Undo(); Handled(); return; }
        }
        if (k.CtrlPressed && k.Keycode == Key.S) { _ = SaveNow(); Handled(); return; }
        if (k.CtrlPressed && k.Keycode == Key.N) { NewPieces.Show(); Handled(); return; }
        if (k.Keycode == Key.F1 || (k.ShiftPressed && k.Keycode == Key.Slash)) { Menu.ShowKeys(); Handled(); return; }
        if (_view == "table")
        {
            if (k.Keycode == Key.Home) { Paper.FrameAll(); Handled(); }
            if (k.Keycode == Key.V && k.CtrlPressed && AppClipboard.HasImage()) { NewPieces.NewPiece(7); Handled(); }
            return;
        }
        if (_view == "model") { if (_model != null && _model.Key(k)) Handled(); return; }
        if (_canvas.Key(k)) { Handled(); return; }
        if (k.Echo && !(k.Keycode is Key.Bracketleft or Key.Bracketright)) return;
        if (PaintKey(k)) Handled();
    }

    bool PaintKey(InputEventKey k)
    {
        bool ctrl = k.CtrlPressed || k.MetaPressed;
        switch (k.Keycode)
        {
            case Key.C when ctrl: LayerPanel.Copy(); break;
            case Key.V when ctrl: LayerPanel.Paste(); break;
            case Key.D when ctrl: LayerPanel.Duplicate(); break;
            case Key.Key0 when ctrl: _canvas.Fit(); break;
            case Key.Key1 when ctrl: _canvas.ZoomTo(1); break;
            case Key.Equal or Key.KpAdd when ctrl: _canvas.ZoomTo(_canvas.Zoom * 1.25f); break;
            case Key.Minus or Key.KpSubtract when ctrl: _canvas.ZoomTo(_canvas.Zoom / 1.25f); break;
            case Key.Apostrophe when ctrl: _prefs.Grid = !_prefs.Grid; Strip.SyncToggles(); break;
            case Key.Semicolon when ctrl: _prefs.Snap = !_prefs.Snap; Strip.SyncToggles(); break;
            case Key.Delete or Key.Backspace when _canvas.Tool == "move" && _canvas.Sel != null: LayerPanel.Delete(); break;
            case Key.Bracketleft: _canvas.ChangeSize(1 / 1.15f); RebuildToolPanel(); break;
            case Key.Bracketright: _canvas.ChangeSize(1.15f); RebuildToolPanel(); break;
            case Key.X when !ctrl: (_canvas.Ink, _canvas.Ink2) = (_canvas.Ink2, _canvas.Ink); ToolPanel.Colours.SyncInk(); break;
            case Key.Y when !ctrl: Strip.CycleSymmetry(); break;
            case Key.F when !ctrl && CurFace is { } shown && !PieceFaces.IsShapeFace(shown.Key) && _sel?.Kind != "model": ShowSide(!shown.Back); break;
            default:
                var tool = k.Keycode is >= Key.A and <= Key.Z ? FaceDocs.ToolFor((char)k.Keycode) : null;
                if (tool == null || ctrl) return false;
                _canvas.SetTool(tool);
                break;
        }
        return true;
    }

    void Handled() => GetViewport().SetInputAsHandled();

    async Task SaveNow()
    {
        var typing = GetViewport().GuiGetFocusOwner();
        var problem = await FlushAll();
        if (_gone) return;
        Flash(problem == null ? "Saved." : "Not saved: " + problem.Replace("\n", "; "));
        if (typing != null && IsInstanceValid(typing) && typing.IsVisibleInTree() && GetViewport().GuiGetFocusOwner() == null) typing.GrabFocus();
    }

    List<(string key, string text, string tip)> ViewKeys() => _sel != null && EffectFx.IsEffect(_sel)
        ? new() { ("table", "Try it", "The effect working on the table   (Alt+1)"), ("paint", "Picture", "Paint a particle of its own (Effect → Particle: its own picture)   (Alt+2)") }
        : new() { ("model", "3D", "Build the piece from shapes and paint on it   (Alt+1)"), ("paint", "2D", _sel?.Kind == "model" ? "The model's texture laid out flat   (Alt+2)" : "A face laid flat: layers, text, rulers, symmetry   (Alt+2)"), ("table", "Try it", "On the table as in a game, scripts running   (Alt+3)") };

    internal void SetView(string v) => _ = SetViewAsync(v);

    internal async Task SetViewAsync(string v)
    {
        if (v == "model" && _sel != null && EffectFx.IsEffect(_sel)) v = "table";
        int gen = ++_viewGen;
        var from = _view;
        if (from != v)
        {
            ReleaseTyping();
            bool ok = from switch { "paint" => await Faces.Settle(), "model" => _model == null || await _model.Settle(), _ => true };
            if (_gone || gen != _viewGen) return;
            if (!ok && v is "paint" or "model")
            {
                Flash(from == "paint"
                    ? $"Still on the picture: it could not be saved ({Faces.Problem}), and the 3D view would open without it."
                    : $"Still in 3D: the model could not be saved ({_model?.Problem}), and its texture would open without it.");
                RefreshViews();
                return;
            }
        }
        if (v == "paint" && Faces.Dirty && Faces.CanvasOpen && (Faces.OpenDef != _sel || Faces.OpenKey != _faceKey))
        {
            bool ok = await Faces.Settle();
            if (_gone || gen != _viewGen) return;
            if (!ok)
            {
                Flash($"The face open could not be saved ({Faces.Problem}), so it stays open.");
                if (Faces.OpenDef != _sel) { RefreshViews(); return; }
                _faceKey = Faces.OpenKey;
            }
        }
        if (v == "paint" && from == "model" && FaceOfPicked() is { } picked) _faceKey = picked;
        ShowView(v);
        if (v == "model" && _sel != null)
        {
            await OpenModel(_sel);
            if (from == "paint") PickFaceOwner();
        }
    }

    string? FaceOfPicked()
    {
        if (_sel == null || _model is not { } m || !m.Holds(_sel) || m.SelPartId is not { } part) return null;
        var keys = PieceFaces.Of(_sel).Select(f => f.Key).ToList();
        if (!m.IsPart(part)) return keys.Contains(part) ? part : null;
        if (_faceKey.StartsWith($"p:{part}:") && keys.Contains(_faceKey)) return _faceKey;
        return keys.FirstOrDefault(k => k.StartsWith($"p:{part}:"));
    }

    void PickFaceOwner()
    {
        if (_gone || _view != "model" || _sel == null || _model is not { } m || !m.Holds(_sel)) return;
        var owner = PieceFaces.IsShapeFace(_faceKey) ? _faceKey.Split(':')[1] : _faceKey;
        if (!m.Views.ContainsKey(owner) || m.SelPartId == owner) return;
        m.SelPartId = owner;
        m.RefreshSelection();
        RebuildModelPanels();
    }

    void ShowView(string v)
    {
        if (Markers.Active && v != "table") Markers.End();
        _view = v;
        RefreshViews();
        _canvas.Visible = v == "paint" && _sel != null;
        if (v == "model") EnsureModel(open: false); else if (_model != null) _model.Visible = false;
        T.SetProcessUnhandledInput(v == "table");
        bool wasTable = T.Visible;
        T.Visible = v == "table";
        if (v == "table" && !wasTable && Paper.Instance(_sel) is { } shown && !Markers.Active)
            T.Rig.Frame(Coords.ToWorld(shown.Pos), (float)Math.Max(6, Math.Max(_sel!.Size.W, _sel.Size.H) * 4));
        _tip.Visible = false;
        if (v == "paint" && _sel != null && Faces.Stale(_sel, _faceKey)) OpenFace(_faceKey);
        _empty!.Visible = _sel == null;
        _zoomPill.Visible = v == "paint" && _sel != null;
        Strip.Rebuild();
        RebuildFaceBar();
        RebuildInspector();
        RebuildToolPanel();
        RefreshHint();
        Relayout();
    }

    internal void RefreshHint()
    {
        var hint = _sel == null ? "Make a piece to begin: ＋ New piece, or drop pictures or 3D models on the window"
            : _view switch
            {
                "table" => Markers.Active ? "Drag the markers where they belong beside the piece; their places are kept as you go" : "Try the pieces as in a game: drag them, right-click for their menu, middle-click turns one over · scripts run",
                "model" => _model?.Panels.ToolHint ?? "",
                _ => _canvas.Tool == "shape" && _prefs.ShapeKind == "poly" ? "Click each corner · click the first one, double-click or press Enter to close · Backspace takes one back" : TopLine.PaintHints.GetValueOrDefault(_canvas.Tool, ""),
            };
        Top.Show(hint, _sel);
        Strip.Panel.Visible = _sel != null;
        if (_view == "paint" && Faces.CanvasOpen && _status.Text.Length == 0) _status.Text = $"{_canvas.Doc.W} × {_canvas.Doc.H} px · {_canvas.Zoom * 100:0}%";
        _pieceName.Text = _sel?.Name ?? "No piece";
    }
}
