using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RaisedChanges
{
    public const int BlockCells = 32;
    const ulong Basis = 14695981039346656037UL, Prime = 1099511628211UL;

    readonly Dictionary<string, (int Cols, int Rows, long[] Blocks)> _grids = new();
    readonly Dictionary<string, (long Key, Rect2 Box)> _things = new();

    public readonly record struct Grid(string Sheet, int Cols, int Rows, long[] Blocks);

    public static long[] Blocks(ReliefGrid grid)
    {
        int across = (grid.Cols + BlockCells - 1) / BlockCells, down = (grid.Rows + BlockCells - 1) / BlockCells;
        var blocks = new ulong[across * down];
        Array.Fill(blocks, Basis);
        for (int y = 0; y < grid.Rows; y++)
        {
            int row = y / BlockCells * across;
            for (int x = 0; x < grid.Cols; x++)
            {
                int i = y * grid.Cols + x, b = row + x / BlockCells;
                ulong h = blocks[b];
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(grid.H[i])) * Prime;
                h = (h ^ (ulong)(grid.Kind[i] | grid.Detail[i] << 8 | grid.Salt[i] << 16)) * Prime;
                blocks[b] = h;
            }
        }
        return Array.ConvertAll(blocks, h => unchecked((long)h));
    }

    public static IEnumerable<Rect2I> Changed(long[] was, long[] now, int cols, int rows)
    {
        int across = (cols + BlockCells - 1) / BlockCells;
        for (int b = 0; b < now.Length; b++)
        {
            if (was[b] == now[b]) continue;
            int x = b % across * BlockCells, y = b / across * BlockCells;
            yield return new Rect2I(x, y, Math.Min(BlockCells, cols - x), Math.Min(BlockCells, rows - y));
        }
    }

    public bool Known => _grids.Count > 0 || _things.Count > 0;

    public bool Covers(IReadOnlyCollection<Grid> grids) => grids.Count == _grids.Count && grids.All(g => _grids.ContainsKey(g.Sheet));

    public List<Rect2I> GroundChanged(Grid grid, out bool whole)
    {
        whole = !_grids.TryGetValue(grid.Sheet, out var was) || was.Cols != grid.Cols || was.Rows != grid.Rows;
        return whole ? [] : [.. Changed(was.Blocks, grid.Blocks, grid.Cols, grid.Rows)];
    }

    public IEnumerable<Rect2> ThingsChanged(IReadOnlyDictionary<string, (long Key, Rect2 Box)> now)
    {
        foreach (var (id, thing) in now)
        {
            if (!_things.TryGetValue(id, out var was)) yield return thing.Box;
            else if (was.Key != thing.Key) yield return was.Box.Merge(thing.Box);
        }
        foreach (var (id, was) in _things)
            if (!now.ContainsKey(id)) yield return was.Box;
    }

    public void Keep(IEnumerable<Grid> grids, IReadOnlyDictionary<string, (long Key, Rect2 Box)> things)
    {
        _grids.Clear();
        foreach (var grid in grids) _grids[grid.Sheet] = (grid.Cols, grid.Rows, grid.Blocks);
        _things.Clear();
        foreach (var (id, thing) in things) _things[id] = thing;
    }

    public void Forget()
    {
        _grids.Clear();
        _things.Clear();
    }
}
