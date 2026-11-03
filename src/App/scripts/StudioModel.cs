using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceStudio
{
    ModelStudio? _model;

    internal ModelStudio? Model => _model;

    void EnsureModel(bool open = true)
    {
        if (_model == null)
        {
            _model = new ModelStudio();
            _model.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _centre.AddChild(_model);
            _centre.MoveChild(_model, 0);
            _model.Setup(this, _store, _pack, _prefs);
            _model.InkChanged(_canvas.Ink);
        }
        _model.Visible = true;
        if (open && _sel != null && !EffectFx.IsEffect(_sel)) _ = OpenModel(_sel);
    }

    internal async Task OpenModel(PieceDef d)
    {
        if (_model == null) return;
        if (!await _model.Open(d))
        {
            if (!_gone && _model.Problem != null) Flash($"The model open before could not be saved ({_model.Problem}), so it is still the one shown.");
            return;
        }
        if (_gone || _sel != d || _view != "model") return;
        Strip.Rebuild(); RebuildToolPanel();
        if (!Typing()) RebuildLayers();
    }

    public void PaintIn2D(string key) => PaintFace(key);

    public void Say(string text) { if (!_gone) Flash(text); }
    public void SetSaved(string text) { if (!_gone) Top.Saved = text; }
    public void PickInk(Color c) { _canvas.Ink = c; ToolPanel.Colours.SyncInk(); Flash($"Picked {Hex.Of(c)[..7]}"); }
    public void ModelColour() => ToolPanel.Colours.Section(false);
    public void RebuildModelPanels() { if (_view == "model") { RebuildLayers(); RebuildToolPanel(); } }
    public void RebuildModelStrip() { if (_view == "model") Strip.Rebuild(); }
    public void RebuildModelOptions() { if (_view == "model") RebuildToolPanel(); }

    public void ModelBaked(PieceDef d)
    {
        if (d.Mesh != null) PieceList.ForgetThumb(d.Mesh);
        if (_gone) { FlushPack(); return; }
        SavePack();
        Paper.Quiet = true;
        try
        {
            if (!Paper.Has(d)) Paper.LayOutAll();
            T.RebuildPiecesOf(d.Id);
            if (d == _sel && Paper.Instance(d) is { } p) T.SetSelection(new[] { p.Id });
        }
        finally { Paper.Quiet = false; }
        RebuildList();
        if (d == _sel && !Typing()) RebuildInspector();
    }

    internal async Task NewModelPiece(string name, MeshData m, string kind, bool addFigure = false)
    {
        if (await SettleWork() is { } problem) { Flash("No new model yet: " + problem); return; }
        if (_gone) return;
        var def = new PieceDef { Id = PackEdit.NewDefId(_pack, name), Name = name, Kind = "model", Size = new Size2(2, 2), Thickness = 0.2 };
        EnsureModel(open: false);
        if (!await _model!.StartNew(def))
        {
            if (_gone) return;
            if (_view != "model") _model.Visible = false;
            Flash("No new model yet: the model open before could not be saved (" + _model.Problem + ").");
            return;
        }
        if (_gone) return;
        _pack.Pieces.Add(def);
        _store.RegisterDef(_pack, def);
        ++_selGen; ++_faceGen;
        _sel = def;
        _view = "model";
        Top.PressView("model");
        _canvas.Visible = false; FaceBar.Pill.Visible = false;
        T.Visible = false; T.SetProcessUnhandledInput(false);
        _model.AddParts(m, kind == "base" ? "Base" : name, kind);
        if (addFigure) _model.AddParts(ModelImport.Primitive("box"), "Box", "box");
        Paper.Quiet = true;
        try { await _model.Bake(); }
        finally { Paper.Quiet = false; }
        RebuildList();
        Select(def, frame: true);
        if (Paper.Instance(def) is { } placed) { Paper.Quiet = true; T.SetSelection(new[] { placed.Id }); Paper.Quiet = false; }
        Strip.Rebuild(); RebuildToolPanel(); RebuildLayers(); Relayout();
    }
}
