using Godot;
using KTable.Core.Model;

namespace KTable.App;

enum Surface
{
    None, Street, Paving, Road, Track, Yard, Flagged, Flags, Gravel, Tile, Barrel, Slate, Thatch, Shingle, Lead, Copper, Mud, Turf, Stone,
    Foliage, Water, Garden, Masonry, Planks, Cloth, Hewn, Rounded,
}

static class SurfaceBook
{
    public const float Mark = 64;
    const float MostMitre = 1.6f, EdgeDark = 0.07f, CrownLight = 0.04f;
    const int WashCode = 32;

    public static readonly Shader Shader = GD.Load<Shader>("res://shaders/built.gdshader");

    public static Color Coded(Color fill, Surface kind, bool wash = false) => fill with { A = (255 - (int)kind - (wash ? WashCode : 0)) / 255f };

    public static Surface Ground(string cls, int manner) => cls switch
    {
        "square" => manner == Manners.Roman ? Surface.Flags : manner is Manners.Egyptian or Manners.Tribal ? Surface.Yard : Surface.Paving,
        "forum" => Surface.Flags,
        "garden" => Surface.Garden,
        "pond" or "moat" => Surface.Water,
        "drymoat" => Surface.Mud,
        "yard" or "pen" or "threshing" or "parade" => Surface.Yard,
        _ => Surface.None,
    };

    public static Surface Lining(string cls, int manner) => cls switch
    {
        "street" => manner == Manners.Roman ? Surface.Flagged : manner is Manners.Egyptian or Manners.Tribal ? Surface.Road : Surface.Street,
        "chaussee" => manner == Manners.Roman ? Surface.Flagged : Surface.Gravel,
        "track" => Surface.Track,
        "lane" or "road" or "causeway" => Surface.Road,
        "jetty" => Surface.Planks,
        "quay" or "mole" => Surface.Flags,
        "wall" or "dam" => Surface.Masonry,
        "moat" or "river" or "stream" or "ditch" => Surface.Water,
        "drymoat" or "dryditch" => Surface.Mud,
        _ => Surface.None,
    };

    public static Surface Roofing(Cover cover, int manner) => cover switch
    {
        Cover.Slate => Surface.Slate,
        Cover.Thatch => Surface.Thatch,
        Cover.Shingle => Surface.Shingle,
        Cover.Lead => Surface.Lead,
        Cover.Copper => Surface.Copper,
        Cover.Mud or Cover.Lime => Surface.Mud,
        Cover.Turf => Surface.Turf,
        Cover.Stone => Surface.Stone,
        _ => manner is Manners.Roman or Manners.Southern ? Surface.Barrel : Surface.Tile,
    };

    public static void Strip(System.Action<int[], Vector2[], Color[], Vector2[]> lay, Vector2[] px, float half, float perMetre, float seed, Color fill, Surface kind)
    {
        int n = px.Length;
        var points = new Vector2[n * 3];
        var colours = new Color[n * 3];
        var uvs = new Vector2[n * 3];
        float run = seed;
        Color edge = Coded(fill.Darkened(EdgeDark), kind), crown = Coded(fill.Lightened(CrownLight), kind);
        var before = Vector2.Right;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) run += (px[i] - px[i - 1]).Length() / perMetre;
            var after = i + 1 < n && (px[i + 1] - px[i]).Length() > 0.001f ? (px[i + 1] - px[i]).Normalized() : before;
            if (i == 0) before = after;
            var along = (before + after).Length() > 0.001f ? (before + after).Normalized() : after;
            var side = along.Orthogonal() * (half * Mathf.Min(MostMitre, 1 / Mathf.Max(0.3f, along.Dot(after))));
            for (int c = 0; c < 3; c++)
            {
                points[i * 3 + c] = px[i] + side * (c - 1);
                colours[i * 3 + c] = c == 1 ? crown : edge;
                uvs[i * 3 + c] = new Vector2(Mark + run, (c - 1) * half / perMetre);
            }
            before = after;
        }
        var indices = new int[(n - 1) * 12];
        for (int i = 0, at = 0; i + 1 < n; i++)
            for (int c = 0; c < 2; c++)
            {
                int a = i * 3 + c, b = a + 1, d = a + 3, e = a + 4;
                foreach (int corner in new[] { a, b, e, a, e, d }) indices[at++] = corner;
            }
        lay(indices, points, colours, uvs);
    }
}
