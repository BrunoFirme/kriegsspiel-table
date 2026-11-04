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
    public float LodBoost = 1;

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
        foreach (var n in _sheets.Values) { n.TopZ = top; n.Covered = Covered; n.Reground(where); }
        GroundChanged?.Invoke(where);
    }

    bool Covered(SheetNode under, Vector3[] corners) =>
        _sheets.Values.Any(o => o != under && o.Def.HasPicture && o.Def.Z > under.Def.Z && corners.All(o.ContainsWorld));

    void GroundMoved() { Watch(); _c.S.Relief.Invalidate(); }

    public void Clear()
    {
        foreach (var n in _sheets.Values) n.QueueFree();
        _sheets.Clear();
        _c.Labels.RemoveWhere(k => k.StartsWith("sheet:"));
        GroundMoved();
    }

    public void Add(SheetDef sh)
    {
        var n = SheetNode.Create(sh, _c.S.Assets, _c.S.Relief, _c.S.View.Sheets.Count);
        Root.AddChild(n);
        _sheets[sh.Id] = n;
        n.SetSelected(sh.Id == SelectedId);
        GroundMoved();
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

    public string Stats() => string.Join(" ", _sheets.Values.Select(s => $"{s.Def.Name}:q{s.QuadCount}/L{s.DetailLevel}-{s.CoarsestLevel}"));

    public void TickLoadingLabels()
    {
        foreach (var (id, sn) in _sheets)
        {
            if (sn.Loaded) { _c.Labels.Remove("sheet:" + id); continue; }
            var txt = sn.TilesTotal > 0 ? $"Loading map… {sn.TilesLoaded} / {sn.TilesTotal}" : _c.S.Assets.PendingFiles > 0 ? "Loading map… fetching from the host" : "Loading map…";
            _c.Labels.Set("sheet:" + id, sn.Position + Vector3.Up * 0.2f, txt, new Color(0.25f, 0.2f, 0.12f), 15);
        }
    }

    public void UpdateLod()
    {
        var cam = _c.Rig.Cam;
        var view = (cam.GlobalTransform, cam.Fov, cam.Size, LodBoost);
        ulong frame = Engine.GetProcessFrames();
        bool all = view != _lodView || frame - _lodFrame >= 15;
        if (!all && !_sheets.Values.Any(s => s.LodDue)) return;
        if (all) { _lodView = view; _lodFrame = frame; }
        var lod = LodView.Of(cam, _c.Rig.GetViewport().GetVisibleRect().Size.Y, LodBoost);
        int fresh = FreshQuadsPerFrame;
        foreach (var sn in _sheets.Values) if (all || sn.LodDue) sn.UpdateLod(lod, ref fresh);
    }

    const int FreshQuadsPerFrame = 8;
    (Transform3D, float, float, float) _lodView;
    ulong _lodFrame;

    public Vec2 SnapTo(SheetNode moving, Vec2 p, bool free = false)
    {
        if (free) return p;
        const double th = 1.5;
        double w = moving.Def.Size.W / 2, h = moving.Def.Size.H / 2;
        if (Math.Abs(Math.Round(moving.Def.RotDeg / 90) * 90 - moving.Def.RotDeg) > 0.5) return p;
        if (((int)Math.Round(moving.Def.RotDeg / 90)) % 2 != 0) (w, h) = (h, w);
        double bestX = th, bestZ = th; double sx = 0, sz = 0;
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
