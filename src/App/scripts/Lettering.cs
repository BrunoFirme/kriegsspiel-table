using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class Lettering
{
    readonly TableContext _c;
    readonly PieceGrid _grid;
    readonly HashSet<PieceNode> _lettered = new(), _sweep = new();
    bool _held;

    Vector3 _sweptFrom = Vector3.Inf, _sweptLook;
    float _sweptLens;
    int _sweptAt = -1;
    readonly Plane[] _frustum = new Plane[6];

    readonly List<PieceNode> _queue = new();
    int _next;
    const double BudgetMs = 4;

    public Lettering(TableContext c, PieceGrid grid) { _c = c; _grid = grid; }

    void DrainQueue()
    {
        if (_next >= _queue.Count) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (_next < _queue.Count && clock.Elapsed.TotalMilliseconds < BudgetMs)
        {
            var n = _queue[_next++];
            if (!GodotObject.IsInstanceValid(n) || n.IsQueuedForDeletion() || !_sweep.Contains(n)) continue;
            n.SetLettered(true);
            _lettered.Add(n);
        }
        if (_next >= _queue.Count) { _queue.Clear(); _next = 0; }
    }

    public void Refresh()
    {
        if (!_held)
        {
            foreach (var n in _lettered) if (GodotObject.IsInstanceValid(n)) n.SetLettered(false);
            _lettered.Clear(); _queue.Clear(); _next = 0;
        }
        _grid.RefileReach();
    }

    public void Forget() { _lettered.Clear(); _sweep.Clear(); }

    public void Tick()
    {
        if (_held) return;
        DrainQueue();
        var cam = _c.Rig.Cam;
        var eye = cam.GlobalPosition;
        var look = -cam.GlobalBasis.Z;
        float orthographicZoom = cam.Size * 1000f;
        float lens = cam.Fov + orthographicZoom;
        if (_sweptAt == _grid.Filings && eye.DistanceSquaredTo(_sweptFrom) < 0.04f && look.Dot(_sweptLook) > 0.9995f && lens == _sweptLens) return;
        _sweptFrom = eye; _sweptLook = look; _sweptLens = lens; _sweptAt = _grid.Filings;
        Sweep(cam, eye);
        TrimToBudget(eye);
        foreach (var n in _lettered)
            if (GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion() && !_sweep.Contains(n)) n.SetLettered(false);
        _lettered.Clear();
        _queue.Clear(); _next = 0;
        foreach (var n in _sweep)
            if (n.Lettered) _lettered.Add(n);
            else _queue.Add(n);
        _queue.Sort((a, b) => eye.DistanceSquaredTo(a.Position).CompareTo(eye.DistanceSquaredTo(b.Position)));
        DrainQueue();
    }

    void Sweep(Camera3D cam, Vector3 eye)
    {
        var ground = Coords.FromWorld(eye);
        _sweep.Clear();
        var fr = cam.GetFrustum();
        int planeCount = Math.Min(fr.Count, _frustum.Length);
        for (int i = 0; i < planeCount; i++) _frustum[i] = fr[i];
        bool InView(PieceNode n, Vector3 c)
        {
            float margin = n.Footprint.Length() * 0.5f * (float)Math.Max(1, n.Piece.Scale) + 2f;
            for (int i = 0; i < planeCount; i++) if (_frustum[i].DistanceTo(c) > margin) return false;
            return true;
        }
        void Look(PieceNode n)
        {
            var p = n.Position;
            if (eye.DistanceTo(p) <= n.LetteringRange() * (n.Lettered ? 1.15f : 1f) && InView(n, p)) _sweep.Add(n);
        }
        _grid.InLetteringReach(eye, ground, Look);
    }

    const int Budget = 50_000;
    PieceNode[] _trimNodes = Array.Empty<PieceNode>();
    float[] _trimKeys = Array.Empty<float>();

    void TrimToBudget(Vector3 eye)
    {
        long total = 0;
        foreach (var n in _sweep) total += n.LetteringWeight;
        if (total <= Budget) return;
        int c = _sweep.Count;
        if (_trimNodes.Length < c) { _trimNodes = new PieceNode[c]; _trimKeys = new float[c]; }
        int i = 0;
        foreach (var n in _sweep) { _trimNodes[i] = n; _trimKeys[i] = eye.DistanceSquaredTo(n.Position); i++; }
        Array.Sort(_trimKeys, _trimNodes, 0, c);
        long kept = 0;
        int k = 0;
        while (k < c && kept + _trimNodes[k].LetteringWeight <= Budget) kept += _trimNodes[k++].LetteringWeight;
        for (int j = k; j < c; j++) _sweep.Remove(_trimNodes[j]);
        Array.Clear(_trimNodes, 0, c);
    }

    readonly List<PieceNode> _forPicture = new();

    public void Hold(IEnumerable<PieceNode> ns)
    {
        _held = true;
        _sweptAt = -1;
        _forPicture.Clear();
        foreach (var n in ns)
        {
            n.SetLettered(true);
            n.LetterForPicture(true);
            _lettered.Add(n);
            _forPicture.Add(n);
        }
    }

    public void Release()
    {
        foreach (var n in _forPicture) if (GodotObject.IsInstanceValid(n)) n.LetterForPicture(false);
        _forPicture.Clear();
        _held = false;
    }

    public int Count => _lettered.Count;
    public long Weight { get { long s = 0; foreach (var n in _lettered) if (GodotObject.IsInstanceValid(n)) s += n.LetteringWeight; return s; } }
}
