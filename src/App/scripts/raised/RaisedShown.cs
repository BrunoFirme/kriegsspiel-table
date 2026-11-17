using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class RaisedShown
{
    public sealed record Piece(List<RaisedChunk.Drawn> Drawn, int Buildings, int Trees, int Near, int Plain, List<(Vector3[] Ring, float Top)> Blockers);

    readonly Dictionary<(int, int), Piece> _pieces = new();
    readonly Dictionary<(int, int), List<RaisedChunk.Drawn>> _groves = new();

    public int Span { get; set; } = 1;

    public int Buildings => _pieces.Values.Sum(p => p.Buildings);

    public int Trees => _pieces.Values.Sum(p => p.Trees);

    public int Near => _pieces.Values.Sum(p => p.Near);

    public int Plain => _pieces.Values.Sum(p => p.Plain);

    public int Pieces => _pieces.Count;

    public int Batches => _groves.Values.Sum(g => g.Count) / 2;

    public IEnumerable<RaisedChunk.Drawn> Drawn => _pieces.Values.SelectMany(p => p.Drawn).Concat(_groves.Values.SelectMany(g => g));

    public IEnumerable<(Vector3[] Ring, float Top)> Blockers => _pieces.Values.SelectMany(p => p.Blockers);

    public void Put((int, int) key, Piece piece)
    {
        Drop(key);
        _pieces[key] = piece;
    }

    public void Drop((int, int) key)
    {
        if (_pieces.Remove(key, out var old)) Free(old.Drawn);
    }

    public void PutGrove((int, int) group, List<RaisedChunk.Drawn> drawn)
    {
        DropGrove(group);
        _groves[group] = drawn;
    }

    public void DropGrove((int, int) group)
    {
        if (_groves.Remove(group, out var old)) Free(old);
    }

    public void Clear()
    {
        foreach (var piece in _pieces.Values) Free(piece.Drawn);
        foreach (var grove in _groves.Values) Free(grove);
        _pieces.Clear();
        _groves.Clear();
    }

    static void Free(IEnumerable<RaisedChunk.Drawn> drawn)
    {
        foreach (var d in drawn)
            if (GodotObject.IsInstanceValid(d.Node)) d.Node.QueueFree();
    }
}
