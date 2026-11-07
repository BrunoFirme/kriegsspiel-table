using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ViewSync
{
    readonly TableContext _c;
    readonly HashSet<string> _picked;
    readonly TableStage _stage;
    readonly SheetLayer _sheets;
    readonly PieceLayer _pieces;
    readonly DrawingLayer _drawings;
    public ViewSync(TableContext c, HashSet<string> picked, TableStage stage, SheetLayer sheets, PieceLayer pieces, DrawingLayer drawings)
    {
        _c = c;
        _picked = picked;
        _stage = stage;
        _sheets = sheets;
        _pieces = pieces;
        _drawings = drawings;
    }

    PendingPoses _poses = null!;
    PieceClipboard _clip = null!;
    ZoneLayer _zones = null!;
    public void Connect(PendingPoses poses, PieceClipboard clip, ZoneLayer zones)
    {
        _poses = poses;
        _clip = clip;
        _zones = zones;
    }

    public event Action? BoardsChanged;
    public event Action? LookChanged;

    sealed class BatchChanges
    {
        public readonly Dictionary<string, HashSet<string>?> Pieces = new();
        public bool Drawings, Selection, Arrived;
    }
    BatchChanges? _batch;

    readonly List<(string layer, string id, Godot.Vector3 at, float yaw)> _moved = new();

    void NoteMove(MovePiece mp)
    {
        if (_pieces.NodeOf(mp.Layer, mp.Id) is { IsDie: false, Placed: false } n) _moved.Add((mp.Layer, mp.Id, n.GlobalPosition, n.Heading));
    }

    void HearMoves()
    {
        if (_moved.Count == 0) return;
        var landed = new List<(PieceNode n, bool onPiece, double after)>();
        var turned = new List<PieceNode>();
        foreach (var (layer, id, at, yaw) in _moved)
        {
            if (_pieces.NodeOf(layer, id) is not { Placed: false } n) continue;
            if (n.GlobalPosition.DistanceTo(at) > Godot.Mathf.Max(0.05f, 0.25f * Godot.Mathf.Min(n.Footprint.X, n.Footprint.Y))) landed.Add((n, n.Piece.Y > 0.01, 0));
            else if (Math.Abs(Godot.Mathf.AngleDifference(yaw, n.Heading)) > 0.005f) turned.Add(n);
        }
        _moved.Clear();
        _c.Sound.LandMany(landed, 0.6f);
        if (turned.Count > 0) _c.Sound.Turn(turned);
    }

    public bool Refused;

    public void ResyncRefused()
    {
        Refused = false;
        _pieces.SyncLayer(Layers.Umpire);
        _poses.Preview();
    }

    HashSet<string>? TouchedIds(LayerCommand c)
    {
        if (c is Attach a) return new HashSet<string> { a.Parent, a.Child };
        var id = PieceIdOf(c);
        if (string.IsNullOrEmpty(id)) return null;
        var ids = new HashSet<string> { id };
        if (c is MovePiece)
        {
            var open = new Stack<string>(ids);
            while (open.Count > 0)
                if (_c.S.View.Find(c.Layer, open.Pop()) is { } p)
                    foreach (var cid in p.Attached) if (ids.Add(cid)) open.Push(cid);
        }
        return ids;
    }

    static string? PieceIdOf(LayerCommand c) => c switch
    {
        Detach dt => dt.Child,
        MovePiece m => m.Id,
        DeletePiece d => d.Id,
        SpawnPiece sp => sp.Id,
        RestorePiece rp => rp.Piece.Id,
        SetState x => x.Id,
        SetCounter x => x.Id,
        SetText x => x.Id,
        SetLabel x => x.Id,
        SetNote x => x.Id,
        SetTint x => x.Id,
        SetLocked x => x.Id,
        SetHighlight x => x.Id,
        SetBookHighlight x => x.Id,
        SetPieceOption x => x.Id,
        KTable.Core.Protocol.SetScale x => x.Id,
        FlipPiece x => x.Id,
        SetPicture x => x.Id,
        SetScript x => x.Id,
        SetScriptState x => x.Id,
        SetTags x => x.Id,
        SetMemo x => x.Id,
        SetWidgets x => x.Id,
        SetMenuItems x => x.Id,
        SetLines x => x.Id,
        SetDecals x => x.Id,
        SetDef x => x.Id,
        ClickWidget x => x.Id,
        NumberTyped x => x.Id,
        PickMenuItem x => x.Id,
        HoldPiece x => x.Id,
        _ => null,
    };

    void SyncAfter(string layer, HashSet<string>? ids)
    {
        if (_batch == null) { _pieces.SyncPieces(layer, ids); return; }
        if (!_batch.Pieces.TryGetValue(layer, out var have)) _batch.Pieces[layer] = ids;
        else if (have != null) { if (ids == null) _batch.Pieces[layer] = null; else have.UnionWith(ids); }
    }

    void FlushBatch()
    {
        if (_batch == null) return;
        foreach (var (layer, ids) in _batch.Pieces.ToList()) _pieces.SyncPieces(layer, ids);
        _batch.Pieces.Clear();
    }

    void SelectionAfter() { if (_batch != null) _batch.Selection = true; else _c.NotifySelectionChanged(); }

    public void OnApplied(Command c)
    {
        if (c is Batch outer && _batch == null)
        {
            var changes = _batch = new BatchChanges();
            try { foreach (var i in outer.Items) OnApplied(i); }
            finally
            {
                FlushBatch();
                _batch = null;
                HearMoves();
                if (changes.Drawings) _drawings.Sync();
                if (changes.Arrived) _clip.SelectArrived();
                if (changes.Selection) _c.NotifySelectionChanged();
            }
            return;
        }
        if (ApplyToPieces(c)) { if (_batch == null) HearMoves(); return; }
        ApplyToTable(c);
    }

    bool ApplyToPieces(Command c)
    {
        var s = _c.S;
        var pieces = _pieces;
        switch (c)
        {
            case Batch b:
                foreach (var i in b.Items) OnApplied(i);
                return true;
            case LayerCommand when c is Draw or EraseStroke or StampDecal or RemoveDecal:
                if (_batch != null) _batch.Drawings = true;
                else _drawings.Sync();
                return true;
            case EditWidget ew:
                if (pieces.TryGet(ew.Layer, ew.Id, out var wn)) wn.SyncWidgets(force: true);
                return true;
            case SetUi { Piece: not null } su:
                if (pieces.TryGet(su.Layer ?? Layers.Umpire, su.Piece, out var un)) un.SyncUi(force: true);
                return true;
            case SelectPiece sp:
                if (sp.To == $"p:{s.Me.PeerId}" && (sp.Selected ? _picked.Add(sp.Id) : _picked.Remove(sp.Id))) _c.NotifySelectionChanged();
                return true;
            case PlayEffect pe:
                if (pieces.TryGet(pe.Layer, pe.Id, out var fx)) fx.PlayOnce();
                return true;
            case ShowContents sc:
                if (sc.To == $"p:{s.Me.PeerId}" && pieces.TryGet(sc.Layer, sc.Id, out var holder)) _c.NotifyContentsAsked(holder, sc.Most);
                return true;
            case SetUiAttributes { Piece: not null } sua:
                if (pieces.TryGet(sua.Layer ?? Layers.Umpire, sua.Piece, out var un2)) un2.SyncUi(force: true);
                return true;
            case SetUiPictures:
                foreach (var p in s.View.Layer(Layers.Umpire).Where(p => p.Ui.Count > 0))
                    if (pieces.TryGet(Layers.Umpire, p.Id, out var pn)) pn.SyncUi(force: true);
                return true;
            case UiEvent { Piece: not null } uev:
                if (pieces.TryGet(uev.Layer ?? Layers.Umpire, uev.Piece, out var un3)) un3.SyncUi(force: true);
                return true;
            case LayerCommand lc:
                if (lc is MovePiece mp) NoteMove(mp);
                SyncAfter(lc.Layer, TouchedIds(lc));
                if (_batch != null) _batch.Arrived = true;
                else _clip.SelectArrived();
                if (c is DeletePiece dp) { _picked.Remove(dp.Id); SelectionAfter(); }
                else if (lc is not MovePiece and not KTable.Core.Protocol.SetScale) SelectionAfter();
                return true;
            case RollDice rd when rd.Layer != null:
                FlushBatch();
                if (!rd.Physical)
                    foreach (var id in rd.PieceIds)
                        if (pieces.TryGet(rd.Layer, id, out var dn) && !dn.Rolling) dn.RollArrived();
                pieces.SyncPieces(rd.Layer, rd.PieceIds.ToHashSet());
                return true;
        }
        return false;
    }

    void ApplyToTable(Command c)
    {
        var s = _c.S;
        switch (c)
        {
            case AddSheet a:
                _sheets.Arrived(a.Sheet.Id);
                _c.NotifySelectionChanged();
                return;
            case MoveSheet or LockSheet or RenameSheet:
                _sheets.Refresh();
                _c.NotifySelectionChanged();
                return;
            case CropSheet cs:
                _sheets.Recut(cs.Id);
                _c.NotifySelectionChanged();
                return;
            case SetSheetPicture sp:
                _sheets.Recut(sp.Id);
                _c.NotifySelectionChanged();
                return;
            case SetSheetRelief sr:
                s.Relief.Draft(sr.Id, null);
                _sheets.Recut(sr.Id);
                _c.NotifySelectionChanged();
                return;
            case RemoveSheet r:
                if (s.View.Sheets.Any(x => x.Id == r.Id)) return;
                _sheets.Remove(r.Id);
                _c.NotifySelectionChanged();
                return;
            case SetZone or RemoveZone:
                _zones.Sync();
                _c.NotifySelectionChanged();
                return;
            case AddBoard or RenameBoard or RemoveBoard:
                if (c is RemoveBoard rb && _drawings.Board == rb.Id) _drawings.Board = "";
                BoardsChanged?.Invoke();
                return;
            case SetLighting:
                _stage.ApplyLighting(_stage.LightNow());
                LookChanged?.Invoke();
                return;
            case SetWind:
                _stage.ApplyWind(s.View.Wind);
                return;
            case SetGrid:
                _stage.ApplyGrid();
                return;
            case AdvanceClock or SetClock:
                if (s.View.Lighting.FromClock) _stage.ApplyLighting(_stage.LightNow());
                return;
        }
    }
}
