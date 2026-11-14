using System;
using Godot;

namespace KTable.App;

sealed class SightRows
{
    public const int Rows = 256, MostEdges = 128;
    const int Across = MostEdges + 1, Floats = 4, Slack = 1;
    readonly float[] _cells = new float[Across * Rows * Floats];
    readonly byte[] _bytes = new byte[Across * Rows * Floats * sizeof(float)];
    ImageTexture? _texture;

    public Vector2 From { get; private set; }

    public int Edges(int row) => (int)_cells[row * Across * Floats];

    public ImageTexture Lay(Vector2[] outline)
    {
        Array.Clear(_cells);
        float low = float.MaxValue, high = float.MinValue;
        foreach (var p in outline)
        {
            low = Math.Min(low, p.Y);
            high = Math.Max(high, p.Y);
        }
        if (outline.Length == 0) low = high = 0;
        float perUnit = Rows / Math.Max(high - low, 1e-6f);
        From = new Vector2(low, perUnit);
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i], b = outline[(i + outline.Length - 1) % outline.Length];
            int first = Math.Max(Row(Math.Min(a.Y, b.Y), low, perUnit) - Slack, 0), last = Math.Min(Row(Math.Max(a.Y, b.Y), low, perUnit) + Slack, Rows - 1);
            for (int row = first; row <= last; row++) Add(row, a, b);
        }
        Buffer.BlockCopy(_cells, 0, _bytes, 0, _bytes.Length);
        var image = Image.CreateFromData(Across, Rows, false, Image.Format.Rgbaf, _bytes);
        if (_texture == null) _texture = ImageTexture.CreateFromImage(image);
        else _texture.Update(image);
        return _texture;
    }

    static int Row(float y, float low, float perUnit) => (int)MathF.Floor((y - low) * perUnit);

    void Add(int row, Vector2 a, Vector2 b)
    {
        int head = row * Across * Floats, held = (int)_cells[head];
        if (held >= MostEdges)
        {
            _cells[head] = MostEdges + 1;
            return;
        }
        int at = head + (held + 1) * Floats;
        _cells[at] = a.X;
        _cells[at + 1] = a.Y;
        _cells[at + 2] = b.X;
        _cells[at + 3] = b.Y;
        _cells[head] = held + 1;
    }

    public bool Holds(Vector2 p)
    {
        float r = (p.Y - From.X) * From.Y;
        if (r < 0 || r >= Rows) return false;
        int head = (int)r * Across * Floats, held = (int)_cells[head];
        if (held > MostEdges) throw new InvalidOperationException("the row holds more edges than it keeps");
        bool inside = false;
        for (int i = 1; i <= held; i++)
        {
            float ax = _cells[head + i * Floats], ay = _cells[head + i * Floats + 1], bx = _cells[head + i * Floats + 2], by = _cells[head + i * Floats + 3];
            if ((ay > p.Y) != (by > p.Y) && p.X < (bx - ax) * (p.Y - ay) / (by - ay) + ax) inside = !inside;
        }
        return inside;
    }
}
