using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class PieceGrid
{
    const float CellSize = 8f;
    readonly Dictionary<(int x, int z), List<PieceNode>> _grid = new();
    readonly Dictionary<(int x, int z), float> _cellReach = new();
    readonly List<PieceNode> _loose = new();
    public int Filings { get; private set; }
    public float LetterReach { get; private set; }

    static (int x, int z) CellAt(double x, double z) => ((int)Math.Floor(x / CellSize), (int)Math.Floor(z / CellSize));
    static readonly (int x, int z) Loose = (int.MinValue, int.MinValue);

    public void Index(PieceNode n)
    {
        var cell = n.IsDie || n.Footprint.Length() > CellSize ? Loose : CellAt(n.Piece.Pos.X, n.Piece.Pos.Z);
        if (n.Cell == cell) return;
        Deindex(n);
        n.Cell = cell;
        Filings++;
        if (cell == Loose) _loose.Add(n);
        else
        {
            if (!_grid.TryGetValue(cell, out var l)) _grid[cell] = l = new List<PieceNode>();
            l.Add(n);
            _cellReach[cell] = Mathf.Max(_cellReach.GetValueOrDefault(cell), n.LetteringRange());
            LetterReach = Mathf.Max(LetterReach, n.LetteringRange());
        }
    }

    void Deindex(PieceNode n)
    {
        if (n.Cell is not { } was) return;
        if (was == Loose) _loose.Remove(n);
        else if (_grid.TryGetValue(was, out var l)) l.Remove(n);
        n.Cell = null;
    }

    static IEnumerable<PieceNode> Filed(List<PieceNode> l)
    {
        for (int i = l.Count - 1; i >= 0; i--)
        {
            var n = l[i];
            if (!GodotObject.IsInstanceValid(n) || n.IsQueuedForDeletion()) { l.RemoveAt(i); n.Cell = null; continue; }
            yield return n;
        }
    }

    public IEnumerable<PieceNode> Near(Vec2 p)
    {
        var (cx, cz) = CellAt(p.X, p.Z);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
                if (_grid.TryGetValue((x, z), out var l))
                    foreach (var n in Filed(l)) yield return n;
        foreach (var n in Filed(_loose)) yield return n;
    }

    public IEnumerable<PieceNode> NearAny(IEnumerable<Vector3> places, float radius)
    {
        var cells = new HashSet<(int x, int z)>();
        int r = Mathf.CeilToInt(radius / CellSize) + 1;
        foreach (var w in places)
        {
            var p = Coords.FromWorld(w);
            var (cx, cz) = CellAt(p.X, p.Z);
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++) cells.Add((x, z));
        }
        foreach (var c in cells)
            if (_grid.TryGetValue(c, out var l))
                foreach (var n in Filed(l)) yield return n;
        foreach (var n in Filed(_loose)) yield return n;
    }

    public void InLetteringReach(Vector3 eye, Vec2 ground, Action<PieceNode> look)
    {
        float far = LetterReach * 1.15f;
        int r = Mathf.CeilToInt(far / CellSize) + 1;
        var (cx, cz) = CellAt(ground.X, ground.Z);
        for (int x = cx - r; x <= cx + r; x++)
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (!_grid.TryGetValue((x, z), out var l) || l.Count == 0) continue;
                float reach = _cellReach.GetValueOrDefault((x, z)) * 1.15f;
                float dx = Mathf.Max(0, Mathf.Max(x * CellSize - (float)ground.X, (float)ground.X - (x + 1) * CellSize));
                float dz = Mathf.Max(0, Mathf.Max(z * CellSize - (float)ground.Z, (float)ground.Z - (z + 1) * CellSize));
                if (dx * dx + dz * dz + eye.Y * eye.Y > reach * reach) continue;
                foreach (var n in Filed(l)) look(n);
            }
        foreach (var n in Filed(_loose)) look(n);
    }

    public void RefileReach()
    {
        _cellReach.Clear(); LetterReach = 0;
        foreach (var (cell, l) in _grid)
            foreach (var n in Filed(l))
            {
                _cellReach[cell] = Mathf.Max(_cellReach.GetValueOrDefault(cell), n.LetteringRange());
                LetterReach = Mathf.Max(LetterReach, n.LetteringRange());
            }
        Filings++;
    }

    public void Clear()
    {
        _grid.Clear(); _cellReach.Clear(); _loose.Clear();
        LetterReach = 0;
        Filings++;
    }

    public string Debug() => $"{_grid.Count} cells, {_grid.Values.Sum(l => l.Count)} filed, {_loose.Count} loose, busiest cell {(_grid.Count > 0 ? _grid.Values.Max(l => l.Count) : 0)}";
}
