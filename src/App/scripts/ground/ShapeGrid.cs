using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class ShapeGrid
{
    const int ShapesPerCell = 8, MostAcross = 128, WideFromCells = 64;

    readonly Rect2[] _boxes;
    readonly Rect2 _over;
    readonly int _across;
    readonly List<int>?[] _cells;
    readonly List<int> _wide = new();
    readonly int[] _asked;
    int _asking;

    public long Weighed { get; private set; }

    public ShapeGrid(Rect2[] boxes)
    {
        _boxes = boxes;
        _asked = new int[boxes.Length];
        _across = Math.Clamp((int)Math.Ceiling(Math.Sqrt(boxes.Length / (double)ShapesPerCell)), 1, MostAcross);
        _cells = new List<int>?[_across * _across];
        if (boxes.Length == 0) return;
        _over = boxes[0];
        foreach (var box in boxes) _over = _over.Merge(box);
        for (int i = 0; i < boxes.Length; i++)
        {
            var (x0, y0, x1, y1) = Cells(boxes[i]);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) > WideFromCells)
            {
                _wide.Add(i);
                continue;
            }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) (_cells[y * _across + x] ??= new()).Add(i);
        }
    }

    int Cell(float at, float from, float size) => size <= 0 ? 0 : Math.Clamp((int)Math.Floor((at - from) / size * _across), 0, _across - 1);

    int Across(float x) => Cell(x, _over.Position.X, _over.Size.X);

    int Down(float y) => Cell(y, _over.Position.Y, _over.Size.Y);

    (int X0, int Y0, int X1, int Y1) Cells(Rect2 box) => (Across(box.Position.X), Down(box.Position.Y), Across(box.End.X), Down(box.End.Y));

    bool Reaches(int shape, Rect2 reach)
    {
        Weighed++;
        return _boxes[shape].Intersects(reach, true);
    }

    public List<int> Among(Rect2 reach, bool firstOnly = false)
    {
        var found = new List<int>();
        if (_boxes.Length == 0 || !_over.Intersects(reach, true)) return found;
        var (x0, y0, x1, y1) = Cells(reach);
        if ((x1 - x0 + 1) * (y1 - y0 + 1) * 2 >= _cells.Length)
        {
            for (int i = 0; i < _boxes.Length && !(firstOnly && found.Count > 0); i++)
                if (Reaches(i, reach)) found.Add(i);
            return found;
        }
        if (++_asking == int.MaxValue)
        {
            Array.Clear(_asked);
            _asking = 1;
        }
        foreach (int i in _wide)
        {
            if (firstOnly && found.Count > 0) return found;
            if (Reaches(i, reach)) found.Add(i);
        }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (_cells[y * _across + x] is not { } cell) continue;
                foreach (int i in cell)
                {
                    if (firstOnly && found.Count > 0) return found;
                    if (_asked[i] == _asking) continue;
                    _asked[i] = _asking;
                    if (Reaches(i, reach)) found.Add(i);
                }
            }
        found.Sort();
        return found;
    }
}
