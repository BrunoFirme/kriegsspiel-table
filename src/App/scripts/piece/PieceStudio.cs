using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class PieceStudio : CanvasLayer
{
    internal Session S = null!;
    internal Table T = null!;
    PackageStore _store = null!;
    PackManifest _pack = null!;
    Settings _cfg = null!;
    Action _done = () => { };
    readonly StudioPrefs _prefs = StudioPrefs.Load();

    Control _root = null!;
    PanelContainer _right = null!, _zoomPill = null!, _tip = null!;
    Control _centre = null!;
    Control? _empty;
    StudioCanvas _canvas = null!;
    Label _status = null!, _pieceName = null!, _tipText = null!;

    string _view = "table";
    PieceDef? _sel;
    string _faceKey = "front";
    Window.FilesDroppedEventHandler? _onDrop;
    int _selGen, _faceGen, _viewGen;
    bool _gone;
    bool _leaving;
    const float Gap = 4, StripW = 38, RightW = 360, TopH = 32;

    public Action? ScriptsChanged;

    internal StudioInPlace? InPlace { get; init; }

    internal IEnumerable<PieceDef> Kinds => InPlace is { } place ? _pack.Pieces.Where(d => d.Id == place.Kind) : _pack.Pieces;

    public readonly HashSet<string> MadeHere = new();

    PackSaver _packSaver = null!;
    internal FaceSaver Faces { get; }
    internal TopLine Top { get; }
    internal StudioToolStrip Strip { get; }
    internal FaceBar FaceBar { get; }
    internal PieceList PieceList { get; }
    internal TablePaper Paper { get; }
    internal MarkerPlacer Markers { get; }
    internal PrintPlacer Prints { get; }
    internal Inspector Inspector { get; }
    internal ScriptTab Script { get; }
    internal StudioLayersPanel LayerPanel { get; }
    internal ToolPanel ToolPanel { get; }
    internal NewPieces NewPieces { get; }
    internal PieceFramer Framer { get; }
    internal StudioMenu Menu { get; }
    internal ScriptFileWatch FileWatch { get; private set; } = null!;

    public PieceStudio()
    {
        Faces = new FaceSaver(this);
        Top = new TopLine(this);
        Strip = new StudioToolStrip(this);
        FaceBar = new FaceBar(this);
        PieceList = new PieceList(this);
        Paper = new TablePaper(this);
        Markers = new MarkerPlacer(this);
        Prints = new PrintPlacer(this);
        Inspector = new Inspector(this);
        Script = new ScriptTab(this);
        LayerPanel = new StudioLayersPanel(this);
        ToolPanel = new ToolPanel(this);
        NewPieces = new NewPieces(this);
        Framer = new PieceFramer(this);
        Menu = new StudioMenu(this);
    }

    internal PackageStore Store => _store;
    internal Settings Cfg => _cfg;
    internal PackManifest Pack => _pack;
    internal StudioPrefs Prefs => _prefs;
    internal Control Root => _root;
    internal Control Centre => _centre;
    internal StudioCanvas Canvas => _canvas;
    internal PieceDef? Sel => _sel;
    internal string View => _view;
    internal bool Gone => _gone;
    internal string FaceKey { get => _faceKey; set => _faceKey = value; }

    internal async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public void Superseded(string? old, string? now)
    {
        if (old != null && old != now && MadeHere.Contains(old)) PackEdit.Forget(_pack, old);
    }

    public override void _ExitTree()
    {
        _gone = true;
        PieceFaces.ShapeFaces = null;
        if (_packSaver.Pending) FlushPack();
        if (_onDrop != null) GetWindow().FilesDropped -= _onDrop;
        _onDrop = null;
        _prefs.Save();
    }

    public async Task<string?> FlushAllAsync()
    {
        try
        {
            var problem = await FlushAll();
            if (problem != null) GD.PrintErr("[studio] not saved on closing: " + problem.Replace("\n", "; "));
            return problem;
        }
        catch (Exception e) { GD.PrintErr("[studio] saving on closing: " + e); return e.Message; }
    }

    async Task<string?> FlushAll(bool leaving = false)
    {
        try
        {
            var problems = new List<string>();
            if (await SettleWork() is { } p) problems.Add(p);
            if (leaving && Markers.Active && !_gone) Markers.End();
            if (!FlushPack()) problems.Add($"the pack's list of pieces, pack.json ({_packSaver.Problem})");
            _prefs.Save();
            return problems.Count == 0 ? null : string.Join("\n", problems);
        }
        catch (Exception e) { GD.PrintErr("[studio] saving everything: " + e); return e.Message; }
    }

    internal async Task<string?> SettleWork()
    {
        try
        {
            ReleaseTyping();
            var problems = new List<string>();
            if (!await Faces.Settle()) problems.Add($"the face being painted ({Faces.Problem})");
            if (_model != null && !await _model.Settle()) problems.Add($"the model ({_model.Problem})");
            if (Script.SaveDraft() is { } s) problems.Add(s);
            return problems.Count == 0 ? null : string.Join("\n", problems);
        }
        catch (Exception e) { GD.PrintErr("[studio] saving: " + e); return e.Message; }
    }

    void ReleaseTyping()
    {
        if (!_gone && GetViewport() is { } vp && vp.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox) vp.GuiReleaseFocus();
        Faces.FlushCommit();
    }

    void DropUnsaved(bool model = true)
    {
        Faces.Drop();
        if (model) _model?.DiscardUnsaved();
    }

    Task<bool> Ask(string title, string text, string yes, string no)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dlg = Ui.Confirm(_root, title, yes, () => tcs.TrySetResult(true), text, 420);
        dlg.CancelButtonText = no;
        dlg.Canceled += () => tcs.TrySetResult(false);
        dlg.Closed += () => tcs.TrySetResult(false);
        return tcs.Task;
    }

    public void Bind(Session s, Table t, PackageStore store, string packId, Settings cfg, Action done, string? kind = null)
    {
        S = s;
        T = t;
        _store = store;
        _cfg = cfg;
        _done = done;
        Layer = 2;
        _pack = store.Pack(packId) ?? throw new InvalidOperationException("pack not found: " + packId);
        _packSaver = new PackSaver(store, _pack, msg => { if (!_gone) { Flash("Could not save pack.json: " + msg); Top.Saved = "NOT SAVED"; } }, InPlace == null ? null : InPlace.Show);
        if (_pack.MoveCounterMarkers()) SavePack();
        FileWatch = new ScriptFileWatch(s, ScriptFileWatch.PackFolder(_pack.Id), $"{_pack.Name} ({_pack.Id})");
        AddChild(FileWatch);
        FileWatch.LinksChanged += () => { if (!_gone) RebuildInspector(); };
        FileWatch.Noted += (text, bad) =>
        {
            if (_gone) return;
            Script.LogLine(text, bad ? "#ff8a70" : "#9aa0a6");
            if (bad) Flash(text);
        };
        Build();
        foreach (var p in Problems.Take()) Flash(p, 10);
        Action<string> told = p => Callable.From(() => { if (!_gone && IsInstanceValid(this)) Flash(p, 10); }).CallDeferred();
        Problems.Told += told;
        TreeExiting += () => Problems.Told -= told;
        Paper.LayOutAll();
        T.SelectionChanged += Paper.OnTableSelection;
        T.Hint += h => { if (_view == "table" && h.Length > 0) Flash(h); };
        T.HoverChanged += (text, at) =>
        {
            _tip.Visible = text != null && _view == "table";
            if (text == null) return;
            _tipText.Text = text;
            _tip.ResetSize();
            _tip.Position = at + new Vector2(18, 18);
        };
        T.ContextMenu += (at, hit) => { if (hit != null && _view == "table") Paper.ShowMenu(at); };
        S.Applied += OnApplied;
        PieceFaces.ShapeFaces = d => _model != null && _model.Holds(d) ? _model.ShapeFaces.Keys() : new();
        GetWindow().FilesDropped += _onDrop = NewPieces.Dropped;
        T.SetTool(Tool.Select);
        RebuildList();
        var first = _pack.Pieces.FirstOrDefault(d => d.Id == kind) ?? _pack.Pieces.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        _view = first != null && EffectFx.IsEffect(first) ? "table" : "model";
        if (first != null) Select(first, frame: true);
        InPlace?.Begin(this);
        SetView(_view);
    }

    void OnApplied(Command c)
    {
        if (c is Say say) Script.LogLine(say.Text, say.Color);
        if (_view == "paint" && Paper.Instance(_sel) is { } p) { _canvas.Widgets = p.Widgets.ToList(); _canvas.Redraw(); }
        if (_view == "model" && _model != null && Paper.Instance(_sel) is { } shown && Reprints(c, shown.Id)) _model.Reprint();
    }

    static bool Reprints(Command c, string id) => c switch
    {
        Batch b => b.Items.Any(i => Reprints(i, id)),
        SetDecals x => x.Id == id,
        SetWidgets x => x.Id == id,
        EditWidget x => x.Id == id,
        SetLines x => x.Id == id,
        SetText x => x.Id == id,
        SetCounter x => x.Id == id,
        SetPicture x => x.Id == id,
        SetTint x => x.Id == id,
        _ => false,
    };

    internal async void Select(PieceDef d, bool frame)
    {
        try { await SelectAsync(d, frame); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    async Task SelectAsync(PieceDef d, bool frame)
    {
        if (InPlace is { } place && d.Id != place.Kind) return;
        if (_sel == d)
        {
            ++_selGen;
            RebuildInspector();
            return;
        }
        int gen = ++_selGen;
        ++_faceGen;
        var problem = await SettleWork();
        if (_gone || gen != _selGen || _sel == d) return;
        if (problem != null)
        {
            bool go = await Ask("Not everything is saved", problem + "\n\nWhat was not saved is lost if you choose another piece now.", "Choose it anyway", "Stay");
            if (_gone || gen != _selGen || _sel == d) return;
            if (!go) { PieceList.Show(_sel); return; }
            DropUnsaved();
        }
        if (Markers.Active) Markers.End();
        _sel = d;
        _empty!.Visible = false;
        PieceList.Show(d);
        if (Paper.Instance(d) is { } p)
        {
            if (!T.Selected.Contains(p.Id)) T.SetSelection(new[] { p.Id });
            if (frame) T.Rig.Frame(Coords.ToWorld(p.Pos), (float)Math.Max(6, Math.Max(d.Size.W, d.Size.H) * 4));
        }
        if (!PieceFaces.Of(d).Any(f => f.Key == _faceKey)) _faceKey = PieceFaces.Of(d)[0].Key;
        if (d.Kind == "model" && _view == "paint") { SetView("model"); return; }
        if (EffectFx.IsEffect(d) && _view == "model") { SetView("table"); return; }
        if (EffectFx.IsEffect(d) && _view == "paint" && d.Effect?.OwnPicture != true) { SetView("table"); return; }
        RebuildInspector();
        if (_view == "model") EnsureModel();
        else if (_view == "paint") OpenFace(_faceKey);
        RefreshViews();
        RefreshHint();
        RebuildFaceBar();
    }

    internal void RebuildList()
    {
        PieceList.Rebuild(_sel);
        if (_empty != null) _empty.Visible = _sel == null;
    }

    internal void SavePack() => _packSaver.Soon();

    internal bool FlushPack() => _packSaver.Flush();

    internal void Apply(Action<PieceDef> change, bool list = false, bool inspector = false, string what = "a change")
    {
        if (_sel == null) return;
        var d = _sel;
        Edited(d, what, () => change(d));
        SavePack();
        T.RebuildPiecesOf(_sel.Id);
        if (list) RebuildList();
        if (inspector) RebuildInspector();
        if (_view == "paint") SyncCanvasPiece();
        if (_view == "model" && _sel.Kind != "model" && _model != null) _ = OpenModel(_sel);
    }

    async Task Leave()
    {
        if (_leaving) return;
        _leaving = true;
        try
        {
            var problem = await FlushAll(leaving: true);
            if (_gone) return;
            if (problem != null && !await Ask("Not everything is saved", problem + "\n\nWhat was not saved is lost if you leave now.", "Leave anyway", "Stay")) return;
            if (_gone) return;
            _done();
        }
        finally { _leaving = false; }
    }

    internal async void DuplicateSel(PieceDef? which = null)
    {
        try { await DuplicateSelAsync(which); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    internal async Task DuplicateSelAsync(PieceDef? which = null)
    {
        var src = which ?? _sel;
        if (src == null || InPlace != null) return;
        if (await SettleWork() is { } problem) { Flash("Not duplicated: " + problem.Replace("\n", "; ")); return; }
        if (_gone || !_pack.Pieces.Contains(src)) return;
        var copy = PackEdit.Duplicate(_store, _pack, src);
        SavePack();
        RebuildList();
        Paper.LayOutAll();
        Select(copy, frame: true);
    }

    internal void DeleteSel(PieceDef? which = null)
    {
        var d = which ?? _sel;
        if (d == null || InPlace != null) return;
        Ui.Confirm(_root, $"Delete \"{d.Name}\"", "Delete", () => DeleteNow(d));
    }

    internal void AskLeave() => _ = InPlace != null ? LeaveInPlace(confirm: true) : Leave();

    internal void AskConfirm() => _ = LeaveInPlace(confirm: true);

    internal void AskDiscard() => _ = LeaveInPlace(confirm: false);

    async Task LeaveInPlace(bool confirm)
    {
        if (_leaving || InPlace is not { } place) return;
        _leaving = true;
        try
        {
            var problem = await FlushAll(leaving: true);
            if (_gone) return;
            if (confirm)
            {
                if (problem != null && !await Ask("Not everything is kept", problem + "\n\nWhat was not kept is not sent.", "Confirm anyway", "Stay")) return;
                if (_gone) return;
                if (place.Confirm(_store, _pack) is { } why) { Flash("Not sent: " + why); return; }
            }
            else
            {
                if (place.Changed(_pack) && !await Ask($"Discard the changes to {_pack.Name}?", "The pieces go back to how they were.", "Discard", "Stay")) return;
                if (_gone) return;
                place.Discard(_store, _pack);
            }
            _done();
        }
        finally { _leaving = false; }
    }

    internal async Task LeaveForQuit(Action quit)
    {
        if (_leaving || InPlace is not { } place) return;
        if (place.Ended) { quit(); return; }
        _leaving = true;
        try
        {
            await FlushAll(leaving: true);
            if (_gone) return;
            if (!place.Changed(_pack))
            {
                place.Discard(_store, _pack);
                quit();
                return;
            }
            var q = Ui.Confirm(_root, "Your changes are not confirmed", "Confirm", () =>
            {
                if (place.Confirm(_store, _pack) is { } why) Flash("Not sent: " + why);
                else quit();
            }, "Confirm sends them to everyone at the table; Discard puts the pieces back as they were.", 420);
            q.Also("Discard", () => { place.Discard(_store, _pack); quit(); });
            q.CancelButtonText = "Stay";
        }
        finally { _leaving = false; }
    }

    internal void DeleteNow(PieceDef d)
    {
        foreach (var p in S.View.Layer(Layers.Umpire).Where(p => p.Def == d.Id).ToList()) S.Do(new DeletePiece { Layer = Layers.Umpire, Id = p.Id });
        Paper.Forget(d);
        PackEdit.Remove(_store, _pack, d);
        _model?.DiscardUnsaved(d);
        if (Faces.OpenDef == d) Faces.Closed();
        SavePack();
        RebuildList();
        if (d != _sel) { PieceList.Show(_sel); return; }
        ++_selGen;
        ++_faceGen;
        _sel = null;
        var next = _pack.Pieces.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (next != null) Select(next, frame: false); else { RebuildInspector(); SetView("table"); }
    }

    internal void PickFiles(string filter, Action<string[]> got, string title)
    {
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFiles, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { filter }, Title = title, Size = new Vector2I(900, 600) };
        Ui.RememberFolder(fd);
        if (filter == ImageFiles.Filter)
        {
            FileThumbs.Show(fd);
            PictureLinks.Offer(fd, _store.Root, f => got(new[] { f }));
        }
        _root.AddChild(fd);
        fd.FilesSelected += f => got(f);
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    internal void AddState(string? name = null)
    {
        Apply(x =>
        {
            if (x.States.Count == 0)
                x.States.Add(new PieceStateDef { Name = x.Name, Image = x.Image, Back = x.Back, Texture = x.Texture, Mesh = x.Kind == "model" ? x.Mesh : null });
            var b = x.States[0];
            x.States.Add(new PieceStateDef { Name = name ?? $"Look {x.States.Count + 1}", Image = b.Image, Back = b.Back, Texture = b.Texture, Mesh = b.Mesh, Shape = b.Shape });
        }, list: true, inspector: true, what: "a new look");
        RebuildFaceBar();
    }

    internal async void RemoveState(int i)
    {
        try { await RemoveStateAsync(i); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    internal async Task RemoveStateAsync(int i)
    {
        var def = _sel;
        if (await SettleWork() is { } problem) { if (!_gone) Flash("The look was not taken away: " + problem.Replace("\n", "; ")); return; }
        if (_gone || _sel != def || def == null) return;
        ++_faceGen;
        Apply(x =>
        {
            if (i < 0 || i >= x.States.Count) return;
            x.States.RemoveAt(i);
            if (x.States.Count == 1)
            {
                var only = x.States[0];
                x.Image = only.Image ?? x.Image;
                x.Back = only.Back;
                x.Texture = only.Texture ?? x.Texture;
                if (only.Shape != null) x.Shape = only.Shape;
                if (only.Size is { } own) x.Size = own;
                x.States.Clear();
            }
        }, list: true, inspector: true, what: "a look taken away");
        _faceKey = PieceFaces.Of(_sel!)[0].Key;
        RebuildFaceBar();
        if (_view == "paint") OpenFace(_faceKey);
    }

    internal void ShowSide(bool back)
    {
        if (_sel == null) return;
        if (_view != "paint" || CurFace is not { } f || f.Back == back)
        {
            SyncCanvasPiece();
            RebuildToolPanel();
            return;
        }
        _ = SwitchFace(PieceFaces.Key(f.State > 0 ? f.State : 0, back));
    }

    internal async void SwapSides()
    {
        try { await SwapSidesAsync(); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    internal async Task SwapSidesAsync()
    {
        var def = _sel;
        if (def == null || def.Kind == "model") return;
        if (await SettleWork() is { } problem) { if (!_gone) Flash("The sides were not swapped: " + problem.Replace("\n", "; ")); return; }
        if (_gone || _sel != def) return;
        ++_faceGen;
        Apply(PiecePrinting.SwapSides, list: true, inspector: true, what: "front and back swapped");
        RebuildFaceBar();
        if (_view == "paint") OpenFace(_faceKey);
    }

    internal PieceFaces.Face? CurFace => _sel == null ? null : PieceFaces.Of(_sel).FirstOrDefault(f => f.Key == _faceKey);

    internal async Task SwitchFace(string key)
    {
        int gen = ++_faceGen;
        ReleaseTyping();
        bool saved = await Faces.Settle();
        if (_gone || gen != _faceGen) return;
        if (!saved)
        {
            bool go = await Ask("The face is not saved", $"The face being painted could not be saved ({Faces.Problem}).\n\nWhat was not saved is lost if you open the other face now.", "Open it anyway", "Stay");
            if (_gone || gen != _faceGen) return;
            if (!go) { RebuildFaceBar(); return; }
            DropUnsaved(model: false);
        }
        _faceKey = key;
        OpenFace(key);
        RebuildFaceBar();
    }

    internal void OpenFace(string key)
    {
        if (_sel == null) return;
        _faceKey = key;
        var f = CurFace ?? PieceFaces.Of(_sel)[0];
        _faceKey = f.Key;
        string? note = null;
        var doc = (PieceFaces.IsShapeFace(f.Key) ? _model?.ShapeFaces.FaceDoc(f.Key) : null) ?? ArtStore.Load(_store, _pack.Id, f.Sha) ?? DocForPicture(_sel, f, out note);
        _canvas.Open(doc);
        Faces.Opened(_sel, f.Key, f.Sha);
        Top.Saved = "";
        SyncCanvasPiece();
        Strip.RefreshUndo();
        RebuildLayers();
        RebuildToolPanel();
        if (note != null) Flash(note, 15);
    }

    internal ArtDoc DocForPicture(PieceDef d, PieceFaces.Face f, out string? note)
    {
        note = null;
        var img = PackEdit.LoadAsset(_store, _pack, f.Sha);
        var path = _store.AssetPath(_pack, f.Sha);
        if (img != null)
        {
            var src = path != null && File.Exists(path) ? ArtStore.AddSrcFile(_store, _pack.Id, path, out _) : null;
            src ??= ArtStore.AddSrc(_store, _pack.Id, img);
            return PieceFaces.DocAround(img, src);
        }
        var doc = BlankFor(d);
        if (f.Sha == null) return doc;
        if (path != null && File.Exists(path))
        {
            try
            {
                var a = _pack.Assets[f.Sha];
                doc.Layers.Insert(0, FaceDocs.Unreadable(doc, ArtStore.AddSrcBytes(_store, _pack.Id, path), a.W, a.H));
            }
            catch (Exception e) { GD.PrintErr($"[studio] kept nothing of {path}: {e.Message}"); }
            note = $"This face's picture ({Path.GetFileName(path)}) could not be read, so it opens blank; the original stays in the pack. What you paint here replaces it on the table.";
        }
        else note = "This face's picture is missing from the pack's folder, so it opens blank. What you paint here becomes the face.";
        return doc;
    }

    ArtDoc BlankFor(PieceDef d)
    {
        var front = d.Image != null && _pack.Assets.TryGetValue(d.Image, out var a) ? a : null;
        return FaceDocs.Blank(d.Size.W, d.Size.H, front?.W ?? 0, front?.H ?? 0);
    }

    internal async Task CopyFrontIntoFace()
    {
        if (_sel == null || CurFace is null) return;
        if (!await Faces.Settle()) { Flash($"The face could not be saved ({Faces.Problem}): nothing copied."); return; }
        if (_gone || _sel == null || CurFace is not { } f || !Faces.CanvasOpen || Faces.OpenDef != _sel || Faces.OpenKey != f.Key) return;
        if (f.Sha != null)
        {
            Flash("This side has paint on it now, so it was not replaced by the front.");
            RebuildFaceBar();
            return;
        }
        var front = PieceFaces.Of(_sel)[0];
        var doc = ArtStore.Load(_store, _pack.Id, front.Sha);
        if (doc == null)
        {
            var img = PackEdit.LoadAsset(_store, _pack, front.Sha);
            if (img == null) { Flash("The front has no picture to start from."); return; }
            doc = PieceFaces.DocAround(img, ArtStore.AddSrc(_store, _pack.Id, img));
        }
        doc = ArtStore.Deserialize(ArtStore.Serialize(doc))!;
        _canvas.Open(doc);
        _canvas.Commit("Start from the front");
        await Faces.Save();
        RebuildFaceBar();
    }

    internal void SyncCanvasPiece()
    {
        if (_sel == null || CurFace is not { } f) return;
        var st = f.State >= 0 && f.State < _sel.States.Count ? _sel.States[f.State] : null;
        var size = st?.Size ?? _sel.Size;
        bool shapeFace = PieceFaces.IsShapeFace(f.Key);
        _canvas.PieceShape = _sel.Kind == "model" || shapeFace ? "alpha" : st?.Shape ?? _sel.Shape;
        _canvas.PieceUnits = PieceFaces.IsShapeFace(f.Key) && _model != null ? _model.ShapeFaces.FaceUnits(f.Key) : new Vector2((float)size.W, (float)size.H);
        _canvas.Prints = _sel.Kind == "model" || shapeFace ? new() : PrintPlacer.Boxes(_sel, f.Back ? PrintPlace.Back : PrintPlace.Front);
        bool turned = _canvas.FaceSide != (f.Back ? "back" : "front");
        if (!PrintPlacer.IsItem(_sel, _canvas.Chosen) || turned && !_canvas.Prints.Any(p => p.Key == _canvas.Chosen)) _canvas.Chosen = _canvas.Prints.FirstOrDefault()?.Key;
        _canvas.FaceSide = f.Back ? "back" : "front";
        var inst = Paper.Instance(_sel);
        _canvas.Widgets = inst != null && !shapeFace && inst.DrawnOnState == Math.Max(0, f.State) ? inst.Widgets.ToList() : new();
        _canvas.Redraw();
    }

    internal void PaintFace(string key)
    {
        if (_view == "paint") { _ = SwitchFace(key); return; }
        _faceKey = key;
        SetView("paint");
    }

    internal void RebuildInspector() => Inspector.Rebuild();
    internal void RebuildFaceBar() => FaceBar.Rebuild();
    internal void RebuildToolPanel() => ToolPanel.Rebuild();
    internal void RebuildLayers() => LayerPanel.Rebuild();
    internal void RefreshViews() => Top.ShowViews(ViewKeys(), _view, _sel);
    internal bool Typing() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;

    internal static void Clear(Container box) { foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); } }

    internal void Flash(string text, double seconds = 5) => Top.Flash(text, seconds);

    public void AskFrame(PieceDef? d) => Framer.Ask(d);

    public void PickModelPart() => NewPieces.PickModelPart();
}
