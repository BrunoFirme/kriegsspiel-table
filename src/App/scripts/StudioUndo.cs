using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Studio;
using KTable.Core;
using Json = KTable.Core.Json;
using KTable.Core.Model;

namespace KTable.App;

static class StudioClock
{
    static long _now;

    public static long Next() => Interlocked.Increment(ref _now);
}

sealed class PieceHistory
{
    const int Keep = 80;
    const ulong SameEditMs = 1200;

    sealed class Step
    {
        public required long At;
        public required string DefId, What, From;
        public required Dictionary<string, AssetEntry> Assets;
        public string To = "";
    }

    readonly List<Step> _undo = new(), _redo = new();
    Step? _open;
    ulong _openAt;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public long UndoStamp => CanUndo ? _undo[^1].At : 0;
    public long RedoStamp => CanRedo ? _redo[^1].At : 0;

    public void Clear() { _undo.Clear(); _redo.Clear(); _open = null; }

    public void Before(PackManifest pack, PieceDef d, string what)
    {
        ulong now = Time.GetTicksMsec();
        if (_open is { } o && _undo.Count > 0 && _undo[^1] == o && o.DefId == d.Id && o.What == what && now - _openAt < SameEditMs) { _openAt = now; return; }
        _open = new Step { At = StudioClock.Next(), DefId = d.Id, What = what, From = Json.Serialize(d), Assets = AssetsOf(pack, d) };
        _openAt = now;
        _undo.Add(_open);
        if (_undo.Count > Keep) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void After(PieceDef d)
    {
        if (_open is not { } o || o.DefId != d.Id || _undo.Count == 0 || _undo[^1] != o) return;
        o.To = Json.Serialize(d);
        if (o.To != o.From) return;
        _undo.RemoveAt(_undo.Count - 1);
        _open = null;
    }

    public string? Take(PieceStudio s, bool undo)
    {
        var from = undo ? _undo : _redo;
        if (from.Count == 0) return null;
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        _open = null;
        if (s.Pack.Pieces.FirstOrDefault(p => p.Id == step.DefId) is not { } now) return $"{step.What}: that kind is gone";
        foreach (var (hash, entry) in step.Assets) s.Pack.Assets.TryAdd(hash, entry);
        var current = Json.Serialize(now);
        var (back, ahead) = undo ? (step.From, step.To) : (step.To, step.From);
        var merged = EditMerge.Back(JsonNode.Parse(back), JsonNode.Parse(ahead), JsonNode.Parse(current));
        (undo ? _redo : _undo).Add(new Step { At = StudioClock.Next(), DefId = step.DefId, What = step.What, From = step.From, To = step.To, Assets = AssetsOf(s.Pack, now) });
        if (merged?.ToJsonString() is { } json) Wear(s, now, Json.Deserialize<PieceDef>(json));
        return step.What;
    }

    static Dictionary<string, AssetEntry> AssetsOf(PackManifest pack, PieceDef d) =>
        PackEdit.AssetsOf(d).Distinct().Where(pack.Assets.ContainsKey).ToDictionary(h => h, h => pack.Assets[h]);

    internal static void Wear(PieceStudio s, PieceDef now, PieceDef back)
    {
        foreach (var prop in typeof(PieceDef).GetProperties().Where(p => p.CanRead && p.CanWrite)) prop.SetValue(now, prop.GetValue(back));
        s.Store.RegisterDef(s.Pack, now);
        s.SavePack();
        s.T.RebuildPiecesOf(now.Id);
        s.RebuildList();
        if (now == s.Sel) { s.RebuildInspector(); s.RebuildFaceBar(); }
        if (s.View == "paint" && s.Faces.Stale(s.Sel, s.FaceKey)) s.OpenFace(s.FaceKey);
        else if (s.View == "paint") s.SyncCanvasPiece();
        if (s.Model is { } m && m.Holds(now)) { m.DiscardUnsaved(now); _ = s.OpenModel(now); }
    }
}

public partial class PieceStudio
{
    internal readonly PieceHistory Edits = new();

    long ViewStamp(bool undo) =>
        _view == "paint" && Faces.CanvasOpen ? (undo ? _canvas.UndoStamp : _canvas.RedoStamp)
        : _view == "model" && _model != null ? (undo ? _model.History.UndoStamp : _model.History.RedoStamp)
        : 0;

    internal bool CanUndo => Edits.CanUndo || ViewStamp(true) > 0;
    internal bool CanRedo => Edits.CanRedo || ViewStamp(false) > 0;

    internal void Undo() => _ = UndoStep(true);
    internal void Redo() => _ = UndoStep(false);

    async Task UndoStep(bool undo)
    {
        long piece = undo ? Edits.UndoStamp : Edits.RedoStamp, here = ViewStamp(undo);
        if (piece == 0 && here == 0) return;
        if (here > piece)
        {
            if (_view == "paint") { if (undo) _canvas.Undo(); else _canvas.Redo(); }
            else if (undo) _model!.Undo(); else _model!.Redo();
            Strip.RefreshUndo();
            return;
        }
        if (await SettleWork() is { } problem) { Flash((undo ? "Not undone: " : "Not redone: ") + problem.Replace("\n", "; ")); return; }
        if (_gone) return;
        if (Edits.Take(this, undo) is { } what) Flash((undo ? "Undone: " : "Redone: ") + what, 3);
        Strip.RefreshUndo();
    }

    internal void Edited(PieceDef d, string what, System.Action change)
    {
        Edits.Before(_pack, d, what);
        change();
        Edits.After(d);
        Strip.RefreshUndo();
    }
}
