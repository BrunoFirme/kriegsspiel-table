using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class SheetLayer
{
    readonly TableContext _c;
    readonly CameraControl _cam;
    readonly Dictionary<string, SheetNode> _sheets = new();
    public Node3D Root = null!;
    public string? SelectedId;
    readonly Queue<string> _toBuild = new();
    readonly Dictionary<string, Rect2?> _queued = new();
    bool _planDue;

    public SheetLayer(TableContext c, CameraControl cam) { _c = c; _cam = cam; }

    public Dictionary<string, SheetNode>.ValueCollection Nodes => _sheets.Values;
    public int Count => _sheets.Count;
    public bool TryGet(string id, out SheetNode node) => _sheets.TryGetValue(id, out node!);

    public SheetNode? Selected => SelectedId != null && _sheets.TryGetValue(SelectedId, out var s) ? s : null;

    public event Action<Rect2?>? GroundChanged;
    ReliefCache? _watched;

    void Watch()
    {
        var relief = _c.S.Relief;
        if (_watched == relief) return;
        if (_watched != null) _watched.Changed -= Reground;
        _watched = relief;
        relief.Changed += Reground;
    }

    void Reground(Rect2? where)
    {
        int top = _sheets.Count == 0 ? 0 : _sheets.Values.Max(s => s.Def.Z);
        _c.SheetsTop = Math.Max(0, top) * SheetNode.ZStep;
        foreach (var (id, n) in _sheets)
        {
            n.TopZ = top;
            n.Covered = Covered;
            if (n.Touches(where)) Queue(id, where);
        }
        GroundChanged?.Invoke(where);
    }

    void Queue(string id, Rect2? where)
    {
        if (_queued.TryGetValue(id, out var had)) { _queued[id] = had is { } a && where is { } b ? a.Merge(b) : null; return; }
        _queued[id] = where;
        _toBuild.Enqueue(id);
    }

    public void RegroundAll() => Reground(null);

    bool Covered(SheetNode under, Vector3[] corners) =>
        _sheets.Values.Any(o => o != under && o.Def.HasPicture && o.Def.Z > under.Def.Z && corners.All(o.ContainsWorld));

    void GroundMoved() { Watch(); _c.S.Relief.Invalidate(); }

    public void Clear()
    {
        foreach (var n in _sheets.Values) n.QueueFree();
        _sheets.Clear();
        _toBuild.Clear();
        _queued.Clear();
        _c.Labels.RemoveWhere(k => k.StartsWith("sheet:"));
        GroundMoved();
    }

    public void Add(SheetDef sh)
    {
        var n = SheetNode.Create(sh, _c.S.Assets, _c.S.Relief);
        string id = sh.Id;
        n.BuiltHere = () => _c.S.View.Built.Where(b => b.Sheet == id).ToList();
        n.IndexKnown += () => _planDue = true;
        _planDue = true;
        Root.AddChild(n);
        _sheets[sh.Id] = n;
        n.SetSelected(sh.Id == SelectedId);
        GroundMoved();
    }

    public void BuiltChanged()
    {
        foreach (var n in _sheets.Values) n.Rebuilt();
    }

    public void Arrived(string id)
    {
        if (!_sheets.ContainsKey(id) && _c.S.View.Sheets.FirstOrDefault(s => s.Id == id) is { } added) { Add(added); _cam.FitRange(); }
    }

    public void Refresh()
    {
        foreach (var sh in _c.S.View.Sheets) if (_sheets.TryGetValue(sh.Id, out var n)) { n.UpdateDef(sh); n.RefreshOutline(); }
        _cam.FitRange();
        GroundMoved();
    }

    public void Recut(string id)
    {
        if (_sheets.Remove(id, out var oldSheet)) oldSheet.QueueFree();
        _c.Labels.Remove("sheet:" + id);
        if (_c.S.View.Sheets.FirstOrDefault(s => s.Id == id) is { } ns) Add(ns);
        else GroundMoved();
    }

    public void Reliefed(string id)
    {
        if (!_sheets.TryGetValue(id, out var n) || _c.S.View.Sheets.FirstOrDefault(s => s.Id == id) is not { } sh) { Recut(id); return; }
        n.UpdateDef(sh);
        GroundMoved();
    }

    public void Fade(string id, double opacity)
    {
        if (_sheets.TryGetValue(id, out var n)) n.Fade(opacity);
    }

    public void Faded(string id)
    {
        if (_c.S.View.Sheets.FirstOrDefault(s => s.Id == id) is not { } sh || !_sheets.TryGetValue(id, out var n)) return;
        n.UpdateDef(sh);
        n.Fade(sh.Seen);
    }

    public void Remove(string id)
    {
        if (_sheets.Remove(id, out var rn)) rn.QueueFree();
        _c.Labels.Remove("sheet:" + id);
        if (SelectedId == id) SelectedId = null;
        GroundMoved();
    }

    public void Select(string? id)
    {
        if (SelectedId != null && _sheets.TryGetValue(SelectedId, out var o)) o.SetSelected(false);
        SelectedId = id;
        if (id != null && _sheets.TryGetValue(id, out var n)) n.SetSelected(true);
        _c.NotifySelectionChanged();
    }

    public SheetNode? TopAt(Vec2 g) => _sheets.Values.OrderByDescending(s => s.Def.Z).FirstOrDefault(s => s.ContainsWorld(Coords.ToWorld(g)));

    public string Stats()
    {
        var pics = _sheets.Values.Select(s => s.Picture).Where(p => p != null).Select(p => p!).ToList();
        var levels = string.Join(" ", pics.GroupBy(p => p.Level).OrderBy(g => g.Key).Select(g => $"L{g.Key}:{g.Count()}"));
        return $"sheets={_sheets.Count} {levels} mb={pics.Sum(p => p.Bytes) >> 20} compose={SheetPicture.ComposeMs / Math.Max(1, SheetPicture.Composed):0}ms×{SheetPicture.Composed} queued={_toBuild.Count}";
    }

    public void PlanLevels()
    {
        var pics = _sheets.Values.Select(s => s.Picture).Where(p => p != null).Select(p => p!).ToList();
        if (pics.Count == 0) return;
        long budget = (long)(_c.Settings is { MapPictureMemory: > 0 } cfg ? cfg.MapPictureMemory : VideoMemory.SuggestedPictureMegabytes()) << 20;
        int deepest = pics.Max(p => p.Coarsest);
        int level = 0;
        while (level < deepest && pics.Sum(p => SheetPicture.BytesAt(p.Index, level, p.HasAlpha)) > budget) level++;
        foreach (var p in pics) p.Want(level);
    }

    const double BuildMillisPerFrame = 6;

    bool _packed = true;

    public void Tick()
    {
        if (_packed != SheetPicture.Packs) { _packed = SheetPicture.Packs; _planDue = true; }
        if (_planDue) { _planDue = false; PlanLevels(); }
        if (_toBuild.Count == 0) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            var id = _toBuild.Dequeue();
            var where = _queued.GetValueOrDefault(id);
            _queued.Remove(id);
            if (_sheets.TryGetValue(id, out var n)) n.Reground(where);
        } while (_toBuild.Count > 0 && clock.Elapsed.TotalMilliseconds < BuildMillisPerFrame);
    }

    public void TickLoadingLabels()
    {
        foreach (var (id, sn) in _sheets)
        {
            if (sn.Loaded) { _c.Labels.Remove("sheet:" + id); continue; }
            var txt = sn.TilesTotal > 0 ? $"Loading map… {sn.TilesLoaded} / {sn.TilesTotal}" : _c.S.Assets.PendingFiles > 0 ? "Loading map… fetching from the host" : "Loading map…";
            _c.Labels.Set("sheet:" + id, sn.Position + Vector3.Up * 0.2f, txt, new Color(0.25f, 0.2f, 0.12f), 15);
        }
    }

    public Vec2 SnapTo(SheetNode moving, Vec2 p, bool free = false)
    {
        if (free) return p;
        const double th = 1.5;
        double w = moving.Def.Size.W / 2, h = moving.Def.Size.H / 2;
        if (Math.Abs(Math.Round(moving.Def.RotDeg / 90) * 90 - moving.Def.RotDeg) > 0.5) return p;
        if (((int)Math.Round(moving.Def.RotDeg / 90)) % 2 != 0) (w, h) = (h, w);
        double bestX = th, bestZ = th;
        double sx = 0, sz = 0;
        foreach (var o in _sheets.Values)
        {
            if (o == moving) continue;
            double ow = o.Def.Size.W / 2, oh = o.Def.Size.H / 2;
            if (((int)Math.Round(o.Def.RotDeg / 90)) % 2 != 0) (ow, oh) = (oh, ow);
            foreach (var (mine, theirs) in new[] { (p.X - w, o.Def.Pos.X + ow), (p.X + w, o.Def.Pos.X - ow), (p.X - w, o.Def.Pos.X - ow), (p.X + w, o.Def.Pos.X + ow) })
                if (Math.Abs(mine - theirs) < bestX) { bestX = Math.Abs(mine - theirs); sx = theirs - mine; }
            foreach (var (mine, theirs) in new[] { (p.Z - h, o.Def.Pos.Z + oh), (p.Z + h, o.Def.Pos.Z - oh), (p.Z - h, o.Def.Pos.Z - oh), (p.Z + h, o.Def.Pos.Z + oh) })
                if (Math.Abs(mine - theirs) < bestZ) { bestZ = Math.Abs(mine - theirs); sz = theirs - mine; }
        }
        return new Vec2(p.X + sx, p.Z + sz);
    }

    public void Nudge(InputEventKey k)
    {
        var sh = _c.S.View.Sheets.FirstOrDefault(s => s.Id == SelectedId);
        if (sh == null || sh.Locked) { _c.Say("Unlock the sheet to nudge it."); return; }
        double step = k.ShiftPressed ? 1 : 0.05;
        var d = k.Keycode switch { Key.Left => new Vec2(-step, 0), Key.Right => new Vec2(step, 0), Key.Up => new Vec2(0, step), _ => new Vec2(0, -step) };
        _c.S.Do(new MoveSheet { Id = sh.Id, Pos = sh.Pos + d, RotDeg = sh.RotDeg, Z = sh.Z });
    }

    public void SendDragged(SheetNode sn) => _c.S.Do(new MoveSheet { Id = sn.Def.Id, Pos = Coords.FromWorld(sn.Position), RotDeg = sn.Def.RotDeg, Z = sn.Def.Z });
}
