using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Scenery;

public static class PropAtlas
{
    public enum M { Canvas, CanvasDark, Bark, EndGrain, Char, Stone, Earth, EarthDark, Steel, Sandbag, Straw, Ash, Mud, Plank, Walnut, Iron,
        Water, Wicker, Turf, Cobble, Painted, Brush }

    public static (int x, int y) CellAt(M m) => (int)m < 16 ? ((int)m % 4 * Cell, (int)m / 4 * Cell) : (512 + ((int)m - 16) % 4 * Cell, 256 + ((int)m - 16) / 4 * Cell);
    public const int AtlasW = 1024, AtlasH = 512, Cell = 128, Pad = 6;

    public static float Sm(float a, float b, float x) { float t = Mathf.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    public static Vector2 Uv(M m, float u, float v)
    {
        var (cx, cy) = CellAt(m);
        return new((cx + Pad + Mathf.Clamp(u, 0, 1) * (Cell - 2 * Pad)) / AtlasW, (cy + Pad + Mathf.Clamp(v, 0, 1) * (Cell - 2 * Pad)) / AtlasH);
    }

    public static Vector2 CraterUv(float u, float v) => new((512 + 2 + Mathf.Clamp(u, 0, 1) * 252) / AtlasW, (2 + Mathf.Clamp(v, 0, 1) * 252) / AtlasH);
    public static Vector2 GroundUv(float u, float v) => new((768 + 2 + Mathf.Clamp(u, 0, 1) * 252) / AtlasW, (2 + Mathf.Clamp(v, 0, 1) * 252) / AtlasH);
}
