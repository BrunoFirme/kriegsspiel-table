using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceLayer
{
    readonly TableContext _c;
    readonly IReadOnlySet<string> _picked;
    readonly Dictionary<(string layer, string id), PieceNode> _pieces = new();
    public Node3D Root = null!;
    public PieceGrid Grid { get; } = new();
    public Lettering Lettering { get; }

    public PieceLayer(TableContext c, IReadOnlySet<string> picked)
    {
        _c = c;
        _picked = picked;
        Lettering = new Lettering(c, Grid);
    }

    public event Action? Resynced;

    public Dictionary<(string layer, string id), PieceNode>.ValueCollection Nodes => _pieces.Values;
    public Dictionary<(string layer, string id), PieceNode>.KeyCollection Keys => _pieces.Keys;
    public int Count => _pieces.Count;

    public PieceNode? NodeOf(string layer, string id) => _pieces.GetValueOrDefault((layer, id));
    public bool TryGet(string layer, string id, out PieceNode node) => _pieces.TryGetValue((layer, id), out node!);

    public void Clear()
    {
        foreach (var n in _pieces.Values) n.QueueFree();
        _pieces.Clear();
        Grid.Clear();
        Lettering.Forget();
    }

    public void SyncLayer(string layer)
    {
        var shown = _c.LayerShown(layer);
        var list = shown && _c.S.View.Layers.TryGetValue(layer, out var l) ? l : new List<PieceInstance>();
        var ids = new HashSet<string>(list.Select(p => p.Id));
        foreach (var key in _pieces.Keys.Where(k => k.layer == layer && !ids.Contains(k.id)).ToList())
        {
            _pieces[key].QueueFree();
            _pieces.Remove(key);
        }
        foreach (var p in list) SyncPiece(layer, p);
        Resynced?.Invoke();
    }

    public void SyncPieces(string layer, HashSet<string>? ids)
    {
        if (ids == null || !_c.LayerShown(layer)) { SyncLayer(layer); return; }
        var list = _c.S.View.Layers.TryGetValue(layer, out var l) ? l : new List<PieceInstance>();
        var found = new HashSet<string>();
        foreach (var p in list)
            if (ids.Contains(p.Id) && found.Add(p.Id)) SyncPiece(layer, p);
        foreach (var id in ids)
            if (!found.Contains(id) && _pieces.Remove((layer, id), out var gone)) gone.QueueFree();
        Resynced?.Invoke();
    }

    void SyncPiece(string layer, PieceInstance p)
    {
        const bool ghost = false;
        var s = _c.S;
        if (s.Def(p.Def) == null)
        {
            if (_pieces.TryGetValue((layer, p.Id), out var unknown)) { unknown.QueueFree(); _pieces.Remove((layer, p.Id)); }
            return;
        }
        if (_pieces.TryGetValue((layer, p.Id), out var n))
        {
            if (n.Ghost != ghost || Math.Abs(n.BuiltScale - p.Scale) > 1e-6)
            {
                n.QueueFree();
                _pieces.Remove((layer, p.Id));
            }
            else
            {
                n.Refresh(p);
                Grid.Index(n);
                return;
            }
        }
        var node = PieceNode.Create(s, layer, p, ghost, Colors.White, lettered: false);
        node.Seating = Seat;
        node.Sounds = _c.Sound;
        if (_c.S.Ground.Any) node.Resettle();
        Root.AddChild(node);
        _pieces[(layer, p.Id)] = node;
        Grid.Index(node);
        if (_picked.Contains(p.Id) && _c.LayerInteractive(layer)) node.SetSelected(true);
    }

    public void SyncAllLayers()
    {
        foreach (var layer in _c.S.View.Layers.Keys.ToList()) SyncLayer(layer);
        foreach (var key in _pieces.Keys.Where(k => !_c.S.View.Layers.ContainsKey(k.layer)).ToList()) { _pieces[key].QueueFree(); _pieces.Remove(key); }
    }

    public void RebuildPiecesOf(string defId)
    {
        foreach (var key in _pieces.Keys.Where(k => _pieces[k].Piece.Def == defId).ToList())
        {
            var old = _pieces[key];
            var p = _c.S.View.Find(key.layer, key.id);
            old.QueueFree();
            _pieces.Remove(key);
            if (p == null) continue;
            var node = PieceNode.Create(_c.S, key.layer, p, old.Ghost, Colors.White, lettered: old.Lettered);
            node.Seating = Seat;
            node.Sounds = _c.Sound;
            if (_c.S.Ground.Any) node.Resettle();
            Root.AddChild(node);
            _pieces[key] = node;
            if (_picked.Contains(p.Id) && _c.LayerInteractive(key.layer)) node.SetSelected(true);
        }
        Resynced?.Invoke();
    }

    public void Resettle(Rect2? where)
    {
        foreach (var n in _pieces.Values.OrderBy(n => n.Piece.Y).ToList())
            if (where == null || where.Value.Grow(n.Footprint.Length()).HasPoint(new Vector2(n.Position.X, n.Position.Z))) n.Resettle();
    }

    const float Gap = 0.03f, SupportAbove = 0.12f, SupportBelowPerSize = 0.35f;

    (float y, Vector3 up)? Seat(PieceNode n, Vec2 pos, double rotDeg, double y, int stack)
    {
        var ground = _c.S.Ground;
        if (!ground.Any || n.IsDie || n.IsEffect || n.Ghost || stack > 0) return null;
        if (y <= 0)
        {
            var seat = ground.SeatOn(pos, rotDeg, n.Footprint.X, n.Footprint.Y);
            return ((float)seat.Y + Gap, new Vector3((float)seat.UpX, (float)seat.UpY, (float)-seat.UpZ));
        }
        var world = Coords.ToWorld(pos);
        float want = (float)y + Gap, reach = Mathf.Max(n.Footprint.X, n.Footprint.Y) * SupportBelowPerSize + SupportAbove;
        PieceNode? under = null;
        float top = float.MinValue;
        foreach (var o in Grid.Near(pos))
        {
            if (o == n || o.Layer != n.Layer || o.IsDie || o.IsEffect || o.Piece.NoCollision || o.Placed) continue;
            if (o.SurfaceAt(world) is not { } t || t > want + SupportAbove || t < want - reach || t <= top) continue;
            top = t;
            under = o;
        }
        return under is { Tilted: true } ? (top, under.Up) : null;
    }

    public void RefreshFaceText() { foreach (var n in _pieces.Values) { n.Refresh(n.Piece); n.SyncWidgets(force: true); } }

    public void ShadeEffects() { foreach (var n in _pieces.Values) if (n.CarriesEffect) n.ShadeEffect(); }

    public void SteerEffects() { foreach (var n in _pieces.Values) if (n.CarriesEffect) n.SteerEffect(); }

    public PieceNode? Pick(Vec2 p, bool interactiveOnly = true, Vector2? screen = null)
    {
        if (screen is { } sc) return PickAlong(_c.Rig.Cam.ProjectRayOrigin(sc), _c.Rig.Cam.ProjectRayNormal(sc), p, interactiveOnly);
        PieceNode? best = null;
        foreach (var n in Grid.Near(p))
        {
            if (interactiveOnly && !_c.LayerInteractive(n.Layer)) continue;
            if (!n.Contains(p)) continue;
            if (interactiveOnly && n.Piece.NotInteractable && n.WidgetAt(p) == null) continue;
            if (best != null && n.IsEffect && !best.IsEffect) continue;
            if (best == null || (best.IsEffect && !n.IsEffect) || n.Position.Y > best.Position.Y || (Mathf.IsEqualApprox(n.Position.Y, best.Position.Y) && n.GetIndex() > best.GetIndex())) best = n;
        }
        return best;
    }

    PieceNode? PickAlong(Vector3 ro, Vector3 rd, Vec2 ground, bool interactiveOnly)
    {
        PieceNode? best = null;
        float bestT = float.MaxValue;
        foreach (var n in Grid.Near(ground))
        {
            if (interactiveOnly && !_c.LayerInteractive(n.Layer)) continue;
            float? widget = n.WidgetAlong(ro, rd)?.t;
            if (interactiveOnly && n.Piece.NotInteractable && widget == null) continue;
            float t;
            if (n.IsDie)
            {
                var c = n.ModelWorldCenter;
                t = (c - ro).Dot(rd);
                if (t < 0 || (c - (ro + rd * t)).Length() > n.Footprint.Length() * 0.55f) continue;
            }
            else if (RayMeets(n, ro, rd) is { } box && MeshOrBox(n, ro, rd, box) is { } hit) t = widget is { } w ? Mathf.Min(hit, w) : hit;
            else if (widget is { } w) t = w;
            else continue;
            if (best == null || (n.IsEffect == best.IsEffect ? t < bestT : !n.IsEffect)) { best = n; bestT = t; }
        }
        return best;
    }

    static float? MeshOrBox(PieceNode n, Vector3 ro, Vector3 rd, float box) => n.ModelAlong(ro, rd) switch
    {
        { Known: false } => box,
        { At: { } at } => at,
        _ => null,
    };

    static float? RayMeets(PieceNode n, Vector3 ro, Vector3 rd)
    {
        var inv = n.GlobalTransform.AffineInverse();
        Vector3 o = inv * ro, d = inv.Basis * rd;
        Vector3 lo = new(-n.Footprint.X / 2, 0, -n.Footprint.Y / 2), hi = new(n.Footprint.X / 2, Mathf.Max(0.02f, n.Thickness), n.Footprint.Y / 2);
        float near = float.MinValue, far = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(d[a]) < 1e-9f) { if (o[a] < lo[a] || o[a] > hi[a]) return null; continue; }
            float t1 = (lo[a] - o[a]) / d[a], t2 = (hi[a] - o[a]) / d[a];
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
        }
        if (near > far || far < 0) return null;
        float at = Mathf.Max(near, 0);
        var local = o + d * at;
        if (Mathf.Abs(local.Y - hi.Y) < 1e-3f || Mathf.Abs(local.Y - lo.Y) < 1e-3f)
            if (!n.Contains(Coords.FromWorld(n.GlobalTransform * local))) return null;
        return at;
    }

    public void ShowRings()
    {
        foreach (var n in _pieces.Values) n.SetSelected(_picked.Contains(n.Piece.Id) && _c.LayerInteractive(n.Layer));
    }

    public int RingedCount() => _pieces.Values.Count(n => n.Ringed);
}
