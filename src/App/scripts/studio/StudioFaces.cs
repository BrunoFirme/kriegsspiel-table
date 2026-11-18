using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

internal static class PieceFaces
{
    internal sealed record Face(string Key, string Label, string? Sha, Action<PieceDef, string?> Set, int State, bool Back);

    internal static Func<PieceDef, List<(string key, string label)>>? ShapeFaces;

    public static bool IsShapeFace(string key) => key.StartsWith("p:");

    public static string Key(int state, bool back) => state <= 0 ? (back ? "back" : "front") : (back ? $"s{state}b" : $"s{state}");

    const string Front = "Front", Back = "Back", Texture = "Texture";

    public static string FaceFor(PieceDef d, int state, bool back) => d.Kind == "model" ? (state <= 0 ? "tex" : $"s{state}") : Key(state, back);

    public static List<Face> Of(PieceDef d)
    {
        var l = new List<Face>();
        if (d.Kind == "model")
        {
            l.Add(new Face("tex", Texture, StateLooks.TextureOf(d, 0), (x, s) =>
            {
                StateLooks.PinOthers(x);
                if (x.States.Count > 0)
                {
                    if (x.Texture == x.States[0].Texture || x.Texture == null) x.Texture = s;
                    x.States[0].Texture = s;
                }
                else x.Texture = s;
            }, d.States.Count > 0 ? 0 : -1, false));
            for (int i = 1; i < d.States.Count; i++)
            {
                int si = i;
                l.Add(new Face($"s{i}", Texture, StateLooks.TextureOf(d, i), (x, s) =>
                {
                    var st = x.States[si];
                    st.FinishTexture ??= StateLooks.FinishOf(x, si);
                    st.Mesh ??= x.Mesh;
                    st.Texture = s;
                }, i, false));
            }
            AddShapeFaces(d, l);
            return l;
        }
        if (EffectFx.IsEffect(d) && d.States.Count == 0)
        {
            l.Add(new Face("front", "Particle picture", d.Image, (x, s) => x.Image = s, -1, false));
            return l;
        }
        if (d.States.Count == 0)
        {
            l.Add(new Face("front", Front, d.Image, (x, s) => x.Image = s, -1, false));
            l.Add(new Face("back", Back, d.Back, (x, s) => x.Back = s, -1, true));
            AddShapeFaces(d, l);
            return l;
        }
        l.Add(new Face("front", Front, d.States[0].Image ?? d.Image, (x, s) =>
        {
            StateLooks.PinOthers(x);
            if (x.Image == x.States[0].Image || x.Image == null) x.Image = s;
            x.States[0].Image = s;
        }, 0, false));
        l.Add(new Face("back", Back, d.States[0].Back ?? d.Back, (x, s) =>
        {
            if (x.Back == x.States[0].Back || x.Back == null) x.Back = s;
            x.States[0].Back = s;
        }, 0, true));
        for (int i = 1; i < d.States.Count; i++)
        {
            int si = i;
            var st = d.States[i];
            l.Add(new Face($"s{i}", Front, st.Image ?? (st.Texture == null ? d.Image : null), (x, s) => x.States[si].Image = s, i, false));
            l.Add(new Face($"s{i}b", Back, st.Back, (x, s) => x.States[si].Back = s, i, true));
        }
        AddShapeFaces(d, l);
        return l;
    }

    static void AddShapeFaces(PieceDef d, List<Face> l)
    {
        if (ShapeFaces?.Invoke(d) is { } shapes)
            foreach (var (key, label) in shapes) l.Add(new Face(key, label, null, (_, _) => { }, -1, false));
    }

    internal static ArtDoc DocAround(Image img, string src)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        float k = Math.Min(1f, 2048f / Math.Max(w, h));
        var doc = new ArtDoc { W = Math.Max(8, (int)Math.Round(w * k)), H = Math.Max(8, (int)Math.Round(h * k)) };
        doc.Layers.Add(new ArtLayer { Name = "Picture", Kind = "image", Src = src, SrcW = w, SrcH = h, X = doc.W / 2f, Y = doc.H / 2f, Sx = doc.W / (float)w, Sy = doc.H / (float)h });
        doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
        doc.Selected = doc.Layers[^1].Id;
        return doc;
    }
}

internal sealed class FaceSaver
{
    readonly PieceStudio _s;
    readonly Debounce _save = new();
    const double SaveDelay = 1.0, SaveMaxDelay = 4.0;
    Task<bool>? _faceTask;
    string? _commitWhat;
    double _commitIn = -1;

    public FaceSaver(PieceStudio s) => _s = s;

    public bool Dirty { get; private set; }
    public bool CanvasOpen { get; private set; }
    public PieceDef? OpenDef { get; private set; }
    public string OpenKey { get; private set; } = "";
    public string? OpenSha { get; private set; }
    public string? Problem { get; private set; }

    StudioCanvas Canvas => _s.Canvas;

    public void Edited()
    {
        Dirty = true;
        _save.Arm(SaveDelay, SaveMaxDelay);
    }

    public void Opened(PieceDef def, string key, string? sha)
    {
        OpenDef = def;
        OpenKey = key;
        OpenSha = sha;
        CanvasOpen = true;
        Dirty = false;
        _save.Clear();
        _commitWhat = null;
        _commitIn = -1;
    }

    public void Closed()
    {
        OpenDef = null;
        CanvasOpen = false;
        Dirty = false;
        _save.Clear();
        _commitWhat = null;
    }

    public void Drop()
    {
        Dirty = false;
        _save.Clear();
        _commitWhat = null;
        _commitIn = -1;
    }

    public void CommitSoon(string what) { _commitWhat = what; _commitIn = 0.8; }

    public void FlushCommit()
    {
        if (_commitWhat is not { } what) return;
        _commitWhat = null;
        _commitIn = -1;
        if (CanvasOpen) Canvas.Commit(what);
    }

    public void Tick(double delta, bool hand)
    {
        if (_commitIn > 0 && (_commitIn -= delta) <= 0) { if (hand) _commitIn = 0.3; else FlushCommit(); }
        if (_save.Tick(delta))
        {
            if (hand) _save.In = 0.3;
            else { _save.Clear(); _ = Save(); }
        }
    }

    public bool Stale(PieceDef? sel, string faceKey) => !CanvasOpen || OpenDef != sel || OpenKey != faceKey
        || (!Dirty && sel != null && PieceFaces.Of(sel).FirstOrDefault(f => f.Key == OpenKey)?.Sha != OpenSha);

    public async Task<bool> Settle()
    {
        FlushCommit();
        for (int round = 0; round < 4; round++)
        {
            if (!await Save()) return false;
            if (!Dirty) return true;
        }
        return !Dirty;
    }

    public async Task<bool> Save()
    {
        while (_faceTask is { IsCompleted: false } running) await running;
        if (!Dirty) return true;
        if (!CanvasOpen || OpenDef == null) { Dirty = false; return true; }
        var t = SaveNow();
        _faceTask = t;
        return await t;
    }

    async Task<bool> SaveNow()
    {
        FlushCommit();
        var def = OpenDef!;
        var key = OpenKey;
        var f = PieceFaces.Of(def).FirstOrDefault(x => x.Key == key);
        if (f == null) { Dirty = false; return true; }
        Dirty = false;
        _save.Clear();
        var doc = ArtStore.Deserialize(ArtStore.Serialize(Canvas.Doc))!;
        ArtRenderer? r = null;
        try
        {
            _s.Top.Saved = "saving…";
            if (PieceFaces.IsShapeFace(key))
            {
                r = Canvas.R.Twin(doc);
                _s.AddChild(r);
                r.RebuildAll();
                var look = await r.Flatten();
                if (_s.Model == null || !_s.Model.Holds(def) || !_s.Model.ShapeFaces.TakeFace(key, look, doc)) throw new InvalidOperationException("its shape is no longer open in 3D");
                Problem = null;
                if (!_s.Gone) _s.SetSaved(Dirty ? "editing…" : "saved");
                return true;
            }
            var sha = await Flatten(doc, f);
            Wear(def, key, f, doc, sha);
            return true;
        }
        catch (Exception e)
        {
            Problem = e.Message;
            GD.PrintErr($"[studio] could not save a face of {def.Name}: {e}");
            if (OpenDef == def && OpenKey == key) { Dirty = true; if (_save.In < 0) _save.In = 3; }
            if (!_s.Gone) { _s.SetSaved("NOT SAVED"); _s.Flash("Could not save the face: " + e.Message); }
            return false;
        }
        finally { r?.QueueFree(); }

        async Task<string?> Flatten(ArtDoc d, PieceFaces.Face face)
        {
            bool empty = d.Layers.All(l => l.Ops.Count == 0 && l.Kind == "paint") && d.Bg == null;
            if (empty && (face.Back || face.State >= 0)) return null;
            r = Canvas.R.Twin(d);
            _s.AddChild(r);
            r.RebuildAll();
            var img = await r.Flatten();
            var made = PackEdit.AddImage(_s.Store, _s.Pack, img);
            _s.MadeHere.Add(made);
            ArtStore.Save(_s.Store, _s.Pack.Id, made, d);
            return made;
        }
    }

    public async Task SaveFromModel(PieceDef def, string key, string? openSha, Image? flipFront, string layerId, Image? paint)
    {
        var f = PieceFaces.Of(def).FirstOrDefault(x => x.Key == key) ?? throw new InvalidOperationException("that face is no longer the piece's");
        string? sha = openSha;
        ArtDoc? doc = null;
        if (paint != null)
        {
            if (flipFront != null) doc = PieceFaces.DocAround(flipFront, ArtStore.AddSrc(_s.Store, _s.Pack.Id, flipFront));
            else
            {
                doc = ArtStore.Load(_s.Store, _s.Pack.Id, openSha) ?? _s.DocForPicture(def, f with { Sha = openSha }, out _);
                if (openSha == null) doc.Bg ??= "#ffffffff";
            }
            doc = ArtStore.Deserialize(ArtStore.Serialize(doc))!;
            int w = paint.GetWidth(), h = paint.GetHeight();
            var layer =
                new ArtLayer { Id = layerId, Name = "Painted in 3D", Kind = "image", Src = ArtStore.AddSrc(_s.Store, _s.Pack.Id, paint), SrcW = w, SrcH = h, X = doc.W / 2f, Y = doc.H / 2f, Sx = doc.W / (float)w,
                    Sy = doc.H / (float)h };
            doc.Layers.Add(layer);
            doc.Selected = layer.Id;
            var r = Canvas.R.Twin(doc);
            _s.AddChild(r);
            try
            {
                r.RebuildAll();
                sha = PackEdit.AddImage(_s.Store, _s.Pack, await r.Flatten());
            }
            finally { r.QueueFree(); }
            _s.MadeHere.Add(sha);
            ArtStore.Save(_s.Store, _s.Pack.Id, sha, doc);
        }
        var old = f.Sha;
        f.Set(def, sha);
        if (doc?.Outline != null) StateLooks.SetShape(def, f.State, "alpha");
        if (old != openSha) _s.Superseded(old, sha);
        if (_s.Gone) { _s.FlushPack(); return; }
        _s.SavePack();
        if (_s.Pack.Pieces.Contains(def)) _s.T.RebuildPiecesOf(def.Id);
        _s.RebuildList();
        if (def == _s.Sel && !_s.Typing()) _s.RebuildInspector();
    }

    public async Task<string> Write(PieceDef def, string key, ArtDoc doc)
    {
        var f = PieceFaces.Of(def).FirstOrDefault(x => x.Key == key) ?? throw new InvalidOperationException($"{def.Name} has no face {key}");
        if (PieceFaces.IsShapeFace(key)) throw new InvalidOperationException("a model's shape faces are painted in the 3D view");
        var r = Canvas.R.Twin(doc);
        _s.AddChild(r);
        string sha;
        try
        {
            r.RebuildAll();
            sha = PackEdit.AddImage(_s.Store, _s.Pack, await r.Flatten());
        }
        finally { r.QueueFree(); }
        _s.MadeHere.Add(sha);
        ArtStore.Save(_s.Store, _s.Pack.Id, sha, doc);
        Wear(def, key, f, doc, sha);
        return sha;
    }

    void Wear(PieceDef def, string key, PieceFaces.Face f, ArtDoc doc, string? sha)
    {
        var old = f.Sha;
        f.Set(def, sha);
        if (doc.Outline != null && def.Kind != "model") StateLooks.SetShape(def, f.State, "alpha");
        if (OpenDef == def && OpenKey == key) OpenSha = sha;
        _s.Superseded(old, sha);
        Problem = null;
        if (_s.Gone) { _s.FlushPack(); return; }
        _s.SavePack();
        if (_s.Pack.Pieces.Contains(def)) _s.T.RebuildPiecesOf(def.Id);
        _s.RebuildList();
        if (def == _s.Sel)
        {
            if (_s.Inspector.Tabs.CurrentTab == 0 && !_s.Typing()) _s.RebuildInspector();
            _s.RebuildStates();
        }
        _s.SetSaved(Dirty ? "editing…" : "saved");
    }
}
