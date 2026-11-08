using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Hand;

public sealed class HeightGrid
{
    public const int Cells = 72;
    readonly float[] _h = new float[Cells * Cells];
    readonly Vector3 _position, _size;

    HeightGrid(Vector3 position, Vector3 size)
    {
        _position = position;
        _size = size;
        Array.Fill(_h, float.NaN);
    }

    public static HeightGrid? Of(Vector3 position, Vector3 size, IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> triangles)
    {
        if (size.X <= 0 || size.Z <= 0) return null;
        var g = new HeightGrid(position, size);
        float cx = size.X / (Cells - 1), cz = size.Z / (Cells - 1);
        foreach (var (a, b, c) in triangles)
        {
            float span = Mathf.Max(Mathf.Max((b - a).Norm(), (c - a).Norm()), (c - b).Norm());
            int steps = Math.Clamp((int)(span / Mathf.Min(cx, cz) * 1.5f) + 1, 1, 200);
            for (int p = 0; p <= steps; p++)
                for (int q = 0; q <= steps - p; q++)
                {
                    var pt = a + (b - a) * (p / (float)steps) + (c - a) * (q / (float)steps);
                    int gx = Math.Clamp((int)MathF.Round((pt.X - position.X) / cx), 0, Cells - 1), gz = Math.Clamp((int)MathF.Round((pt.Z - position.Z) / cz), 0, Cells - 1);
                    ref float h = ref g._h[gz * Cells + gx];
                    if (float.IsNaN(h) || pt.Y > h) h = pt.Y;
                }
        }
        return g;
    }

    public float? At(float x, float z)
    {
        float fx = (x - _position.X) / _size.X * (Cells - 1), fz = (z - _position.Z) / _size.Z * (Cells - 1);
        if (fx < -0.5f || fz < -0.5f || fx > Cells - 0.5f || fz > Cells - 0.5f) return null;
        int gx = Math.Clamp((int)MathF.Round(fx), 0, Cells - 1), gz = Math.Clamp((int)MathF.Round(fz), 0, Cells - 1);
        float h = _h[gz * Cells + gx];
        return float.IsNaN(h) ? null : h;
    }
}
