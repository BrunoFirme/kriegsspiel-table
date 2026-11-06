using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Scenery;
using static KTable.Client.Scenery.PropAtlas;

namespace KTable.App;

public static class Props
{
    static Image? _atlas;
    static byte[]? _px;

    public static Image Atlas()
    {
        if (_atlas != null) return _atlas;
        _px = PaintAtlas();
        return _atlas = Image.CreateFromData(AtlasW, AtlasH, false, Image.Format.Rgba8, _px);
    }

    static byte[] PaintAtlas() => new AtlasPainter().Paint();

    readonly record struct Texel(int X, int Y, float Gx, float Gy, float B, float Md, float F, float V);

    sealed class AtlasPainter
    {
        readonly byte[] px = new byte[AtlasW * AtlasH * 4];
        readonly FastNoiseLite big = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Seed = 1812, Frequency = 0.03f, FractalOctaves = 4 };
        readonly FastNoiseLite mid = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Seed = 1813, Frequency = 0.12f, FractalOctaves = 3 };
        readonly FastNoiseLite fine = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Value, Seed = 1815, Frequency = 0.9f };
        readonly FastNoiseLite cells =
            new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Seed = 1809, Frequency = 0.07f, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Sub };

        void Put(int x, int y, Color c)
        {
            int i = (y * AtlasW + x) * 4;
            px[i] = (byte)Mathf.Clamp(c.R * 255, 0, 255);
            px[i + 1] = (byte)Mathf.Clamp(c.G * 255, 0, 255);
            px[i + 2] = (byte)Mathf.Clamp(c.B * 255, 0, 255);
            px[i + 3] = (byte)Mathf.Clamp(c.A * 255, 0, 255);
        }
        float B(float x, float y) => big.GetNoise2D(x, y) * 0.5f + 0.5f;
        float Md(float x, float y) => mid.GetNoise2D(x, y) * 0.5f + 0.5f;
        float F(float x, float y) => fine.GetNoise2D(x, y) * 0.5f + 0.5f;

        public byte[] Paint()
        {
            Materials();
            Crater();
            TrampledGround();
            return px;
        }

        void Materials()
        {
            foreach (M m in Enum.GetValues<M>())
            {
                var (cx, cy) = CellAt(m);
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                    {
                        float gx = cx + x, gy = cy + y, b = B(gx, gy), md = Md(gx, gy), f = F(gx, gy);
                        float v = y / (float)Cell;
                        float a = 1;
                        var c = Material(m, new Texel(x, y, gx, gy, b, md, f, v), ref a);
                        Put(cx + x, cy + y, c with { A = a });
                    }
            }
        }

        Color Material(M m, Texel t, ref float a) => m switch
        {
            M.Bark or M.EndGrain or M.Char or M.Plank or M.Walnut or M.Painted => Wood(m, t, ref a),
            M.Stone or M.Earth or M.EarthDark or M.Ash or M.Mud or M.Water or M.Turf or M.Cobble => Soil(m, t, ref a),
            M.Steel or M.Iron => Metal(m, t, ref a),
            _ => Cloth(m, t, ref a),
        };

        Color Cloth(M m, Texel t, ref float a)
        {
            var (x, y, gx, gy, b, md, f, v) = t;
            Color c;
            switch (m)
            {
                case M.Canvas:
                case M.CanvasDark:
                {
                    var basec = m == M.Canvas ? new Color("#c9bfa3") : new Color("#a89c80");
                    float weave = (Mathf.Sin(x * 2.2f) * Mathf.Sin(y * 2.2f)) * 0.025f;
                    c = basec.Darkened(weave + 0.06f * f + 0.18f * Sm(0.55f, 0.85f, b)).Lerp(new Color("#7d7058"), 0.35f * Mathf.Pow(v, 3));
                    bool onSeam = x % 42 < 2;
                    if (onSeam) c = c.Darkened(0.08f);
                    break;
                }
                case M.Straw:
                {
                    float strand = Mathf.Abs(Mathf.Sin(gx * 1.3f + mid.GetNoise2D(gx * 0.5f, gy * 0.2f) * 6));
                    c = new Color("#a98d4f").Lerp(new Color("#7a6332"), strand * 0.6f).Darkened(0.1f * f).Lerp(new Color("#5a4a2c"), 0.25f * Sm(0.6f, 0.9f, b));
                    float fringe = 0.86f + 0.1f * (mid.GetNoise2D(gx * 4, 0) * 0.5f + 0.5f);
                    if (v > fringe && Mathf.Abs(Mathf.Sin(gx * 1.9f)) < 0.75f) a = 0;
                    break;
                }
                case M.Sandbag:
                {
                    float weave = ((x / 2 + y / 2) % 2 == 0 ? 0.04f : 0) + ((x % 6 == 0) ? 0.05f : 0);
                    c = new Color("#94835e").Darkened(weave + 0.12f * md + 0.05f * f).Lerp(new Color("#5a4a34"), 0.3f * Sm(0.6f, 0.9f, b));
                    break;
                }
                case M.Wicker:
                {
                    int row = y / 9, col = (x + (row % 2) * 8) / 16;
                    float rod = Mathf.Sin(y / 9f * Mathf.Pi);
                    c = new Color("#7a6342").Lerp(new Color("#a08458"), rod * 0.6f).Darkened((col % 2 == 0 ? 0.18f : 0) + 0.08f * f);
                    if (x % 16 < 2) c = new Color("#4a3a26");
                    break;
                }
                default:
                {
                    float twig = Mathf.Abs(Mathf.Sin(gy * 1.4f + mid.GetNoise2D(gx * 0.2f, gy) * 4));
                    c = new Color("#4e3e2c").Lerp(new Color("#6e5a40"), twig * 0.7f).Darkened(0.1f * f + (y % 30 < 3 ? 0.25f : 0));
                    break;
                }
            }
            return c;
        }

        Color Wood(M m, Texel t, ref float a)
        {
            var (x, y, gx, gy, b, md, f, v) = t;
            Color c;
            switch (m)
            {
                case M.Bark:
                {
                    float fib = mid.GetNoise2D(gx * 3f, gy * 0.35f) * 0.5f + 0.5f;
                    c = new Color("#4f3a2a").Lerp(new Color("#6b5340"), fib).Darkened(0.35f * Sm(0.6f, 0.75f, fib) * (f > 0.5f ? 1 : 0.4f) + 0.08f * f);
                    break;
                }
                case M.EndGrain:
                {
                    float r = Mathf.Sqrt(Sq(x - 64) + Sq(y - 64)) / 64;
                    float ring = Mathf.Sin(r * 38 + md * 4) * 0.5f + 0.5f;
                    c = new Color("#b08d62").Lerp(new Color("#8a6a45"), ring * 0.6f).Darkened(0.05f * f);
                    if (r > 0.86f) c = new Color("#4a3627");
                    if (r < 0.05f) c = c.Darkened(0.2f);
                    break;
                }
                case M.Char:
                {
                    c = new Color("#1f1a17").Lerp(new Color("#3a322c"), md * 0.7f);
                    if (Sm(0.7f, 0.74f, cells.GetNoise2D(gx * 2, gy * 2) * 0.5f + 0.5f) > 0.5f) c = new Color("#5a2512").Lerp(new Color("#a2441a"), f);
                    if (b > 0.68f) c = c.Lerp(new Color("#8a857e"), 0.5f);
                    break;
                }
                case M.Plank:
                {
                    int board = x / 32;
                    float grain = Mathf.Sin(y * 0.4f + mid.GetNoise2D(gx * 0.3f, gy * 2) * 5 + board * 3);
                    c = new Color("#7a6a55").Lerp(new Color("#5d4f3e"), 0.3f + 0.2f * grain + 0.2f * (board % 2)).Darkened(0.06f * f);
                    if (x % 32 < 2) c = new Color("#2e261e");
                    break;
                }
                case M.Walnut:
                    c = new Color("#4a2f1d").Lerp(new Color("#5e3c25"), Mathf.Sin(gx * 0.25f + md * 3) * 0.5f + 0.5f).Lightened(0.04f * f);
                    break;
                case M.Painted:
                {
                    float wear = Sm(0.55f, 0.75f, b + 0.2f * (f - 0.5f));
                    c = new Color("#55636a").Lerp(new Color("#6a5a45"), wear).Darkened((x % 26 < 2 ? 0.3f : 0) + 0.06f * f);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(m));
            }
            return c;
        }

        Color Soil(M m, Texel t, ref float a)
        {
            var (x, y, gx, gy, b, md, f, v) = t;
            Color c;
            switch (m)
            {
                case M.Stone:
                {
                    float cr = cells.GetNoise2D(gx, gy) * 0.5f + 0.5f;
                    c = new Color("#88847a").Lerp(new Color("#6d6a63"), md).Darkened(0.06f * f + 0.25f * Sm(0.15f, 0.0f, cr));
                    if (f > 0.93f) c = c.Lightened(0.15f);
                    bool lichen = Sm(0.7f, 0.78f, b) > 0.5f && f > 0.4f;
                    if (lichen) c = c.Lerp(new Color("#7f8a5c"), 0.35f);
                    break;
                }
                case M.Earth:
                case M.EarthDark:
                {
                    float cr = cells.GetNoise2D(gx * 1.5f, gy * 1.5f) * 0.5f + 0.5f;
                    c = (m == M.Earth ? new Color("#6f5a40") : new Color("#4a3a2a")).Lerp(new Color("#57452f"), md * 0.5f)
                        .Darkened(0.1f * f + 0.18f * Sm(0.2f, 0.0f, cr)).Lightened(f > 0.9f ? 0.08f : 0);
                    break;
                }
                case M.Ash:
                {
                    float r = Mathf.Sqrt(Sq(x - 64) + Sq(y - 64)) / 64;
                    c = new Color("#7a756f").Darkened(0.25f * md + 0.1f * f).Lerp(new Color("#1d1917"), Sm(0.75f, 0.1f, r) * 0.85f);
                    if (r < 0.5f && f > 0.94f) c = new Color("#c4531c");
                    break;
                }
                case M.Mud:
                    c = new Color("#3a2f24").Lerp(new Color("#2b2a2a"), Sm(0.55f, 0.8f, b) * 0.7f).Lightened(Sm(0.72f, 0.8f, b) * 0.12f).Darkened(0.06f * f);
                    break;
                case M.Water:
                {
                    float ripple = Mathf.Sin(gx * 0.18f + mid.GetNoise2D(gx * 0.4f, gy * 0.4f) * 5) * 0.5f + 0.5f;
                    c = new Color("#2b3a3f").Lerp(new Color("#4c5f63"), Sm(0.7f, 1f, ripple) * 0.6f).Lerp(new Color("#26302a"), Sm(0.55f, 0.8f, b) * 0.5f);
                    break;
                }
                case M.Turf:
                {
                    float blade = fine.GetNoise2D(gx * 2.5f, gy * 2.5f) * 0.5f + 0.5f;
                    c = new Color("#4f5534").Lerp(new Color("#666a42"), blade * 0.8f).Lerp(new Color("#6e5e40"), Sm(0.5f, 0.8f, b) * 0.7f).Darkened(0.1f * f + 0.08f * (1 - md));
                    break;
                }
                case M.Cobble:
                {
                    float cr = cells.GetNoise2D(gx * 1.6f, gy * 1.6f) * 0.5f + 0.5f;
                    c = new Color("#7d776c").Lerp(new Color("#605b52"), md).Darkened(0.4f * Sm(0.12f, 0.0f, cr) + 0.06f * f);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(m));
            }
            return c;
        }

        Color Metal(M m, Texel t, ref float a)
        {
            var (x, y, gx, gy, b, md, f, v) = t;
            Color c;
            switch (m)
            {
                case M.Steel:
                {
                    float rust = Sm(0.45f, 0.75f, b + 0.25f * (mid.GetNoise2D(gx * 0.5f, gy * 3) * 0.5f + 0.5f) - 0.12f);
                    c = new Color("#5a5e60").Lerp(new Color("#7a4426"), rust).Lerp(new Color("#8a4b25"), rust * f * 0.6f).Darkened(0.06f * f);
                    break;
                }
                case M.Iron:
                    c = new Color("#2d2f31").Lerp(new Color("#44474a"), md * 0.6f).Lerp(new Color("#4f3424"), Sm(0.65f, 0.8f, b) * 0.4f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(m));
            }
            return c;
        }

        void Crater()
        {
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = (x + 0.5f) / 256 * 2 - 1, v = (y + 0.5f) / 256 * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v) * 1.35f;
                    float gx = 512 + x, gy = y, b = B(gx * 1.5f, gy * 1.5f), md = Md(gx, gy), f = F(gx, gy);
                    var c = r < 0.32f ? new Color("#211c18").Lerp(new Color("#2b3138"), Sm(0.6f, 0.75f, b) * 0.6f)
                        : r < 0.8f ? new Color("#33281d").Lerp(new Color("#5f4a33"), (r - 0.32f) / 0.48f)
                        : new Color("#6a553a").Lerp(new Color("#7e6646"), md);
                    c = c.Darkened(0.14f * f + (r > 0.8f && f > 0.85f ? 0.22f : 0)).Darkened(0.15f * Sm(0.3f, 0.0f, Mathf.Abs(r - 0.55f)) * (md > 0.5f ? 1 : 0));
                    float edge = 1.1f + 0.22f * (Md(gx * 0.6f, gy * 0.6f) - 0.5f) * 2;
                    float a = r < edge ? 1 : 0;
                    if (a == 0 && r < 1.33f && f > 0.88f && b > 0.45f) { a = 1; c = new Color("#5f4a33"); }
                    Put(512 + x, y, c with { A = a });
                }
        }

        void TrampledGround()
        {
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float u = (x + 0.5f) / 256 * 2 - 1, v = (y + 0.5f) / 256 * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float gx = 768 + x, gy = y, b = B(gx, gy), md = Md(gx * 1.3f, gy * 1.3f), f = F(gx, gy);
                    var c = new Color("#6b5940").Lerp(new Color("#4f412f"), Sm(0.45f, 0.75f, b)).Darkened(0.08f * f);
                    if (Mathf.Abs(Mathf.Sin(gx * 0.9f + gy * 0.45f + md * 9)) > 0.996f && f > 0.75f && b < 0.5f) c = c.Lerp(new Color("#9c8650"), 0.55f);
                    bool ashAndWear = md > 0.78f;
                    if (ashAndWear) c = c.Lerp(new Color("#5c5752"), 0.4f);
                    float edge = 0.86f + 0.14f * (Md(gx * 0.5f, gy * 0.5f) - 0.5f) * 2 + 0.04f * (f - 0.5f);
                    float a = r < edge ? 1 : (r < edge + 0.06f && f > 0.8f ? 1 : 0);
                    Put(768 + x, y, c with { A = a });
                }
        }
    }

    static float Sq(float a) => a * a;

    static readonly PropMaker Maker = new(Perlin);

    static IPropNoise Perlin(int seed, float frequency) =>
        new EngineNoise(new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Seed = seed, Frequency = frequency });

    sealed class EngineNoise(FastNoiseLite noise) : IPropNoise
    {
        public float GetNoise2D(float x, float y) => noise.GetNoise2D(x, y);
        public float GetNoise3D(float x, float y, float z) => noise.GetNoise3D(x, y, z);
    }

    public static PropMade Bivouac() => Maker.Bivouac();
    public static PropMade PicketPost() => Maker.PicketPost();
    public static PropMade CampFire() => Maker.CampFire();
    public static PropMade Hedgehog() => Maker.Hedgehog();
    public static PropMade Crater(float radius, int seed) => Maker.Crater(radius, seed);
    public static PropMade Trench(Vector2[] path) => Maker.Trench(path.Select(q => new System.Numerics.Vector2(q.X, q.Y)).ToArray());
    public static PropMade Breastwork() => Maker.Breastwork();
    public static PropMade Earthwork() => Maker.Earthwork();
    public static PropMade ChevauxDeFrise() => Maker.ChevauxDeFrise();
    public static PropMade Stakes() => Maker.Stakes();
    public static PropMade Palisade() => Maker.Palisade();
    public static PropMade Moat() => Maker.Moat();
    public static PropMade Barricade() => Maker.Barricade();
    public static PropMade Pavises() => Maker.Pavises();
    public static PropMade TateShields() => Maker.TateShields();
    public static PropMade CampCurtain() => Maker.CampCurtain();
    public static PropMade Box() => Maker.SmallBox();
    public static PropMade Chest() => Maker.Chest();
    public static PropMade Crate() => Maker.Crate();

    public static Image Thumb(PropMesh mesh)
    {
        Atlas();
        return Image.CreateFromData(PropThumb.Out, PropThumb.Out, false, Image.Format.Rgba8, PropThumb.Render(mesh, _px!));
    }
}
