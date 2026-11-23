using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedForest
{
    const float OpenApartM = 16, CloseApartM = 9, CrownShare = 0.3f, Jitter = 0.55f, StandM = 250, HouseClearM = 5, WayClearM = 2.5f, FelledM = 12, FelledShare = 0.55f;
    const float SaltLift = 3.3f, TownTreeM = 3.4f;

    public static IEnumerable<bool> Plant(SheetDef sheet, Ground ground, float top, float upm, float chunk, Dictionary<(int, int), RaisedChunk> chunks, IReadOnlyList<BuiltPainter.Shape> shapes,
        int culture = Manners.Continental, IReadOnlySet<(int, int)>? only = null)
    {
        var grid = ground.GridOf(sheet.Id);
        if (grid == null || !grid.Painted || upm <= 0) yield break;
        double fullWidth = Ground.FullWidth(sheet), fullHeight = BuiltGeometry.FullHeight(sheet);
        var near = BuiltNear.Of(shapes, (float)fullWidth, (float)fullHeight, upm);
        int cols = grid.Cols, rows = grid.Rows;
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
                if (Trees(grid, cx, cy) is { } trees) Cell(sheet, ground, top, upm, chunk, chunks, near, grid, cx, cy, trees, culture, only);
            yield return true;
        }
    }

    static bool Wood(ReliefGrid grid, int x, int y) =>
        grid.Kind[Mathf.Clamp(y, 0, grid.Rows - 1) * grid.Cols + Mathf.Clamp(x, 0, grid.Cols - 1)] == GroundKinds.Wood;

    static float? Trees(ReliefGrid grid, int cx, int cy)
    {
        float? most = null;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= grid.Cols || y >= grid.Rows || !Wood(grid, x, y)) continue;
                float trees = (grid.Detail[y * grid.Cols + x] & GroundKinds.LevelSteps) / (float)GroundKinds.LevelSteps;
                if (dx == 0 && dy == 0) return trees;
                most = Mathf.Max(most ?? 0, trees);
            }
        return most;
    }

    static float Woodness(ReliefGrid grid, double u, double v)
    {
        float x = (float)(u * grid.Cols - 0.5), y = (float)(v * grid.Rows - 0.5);
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        float W(int dx, int dy) => Wood(grid, x0 + dx, y0 + dy) ? 1 : 0;
        return Mathf.Lerp(Mathf.Lerp(W(0, 0), W(1, 0), fx), Mathf.Lerp(W(0, 1), W(1, 1), fx), fy);
    }

    static void Cell(SheetDef sheet, Ground ground, float top, float upm, float chunk, Dictionary<(int, int), RaisedChunk> chunks, BuiltNear.Field near, ReliefGrid grid, int cx, int cy,
        float trees, int culture, IReadOnlySet<(int, int)>? only)
    {
        int cols = grid.Cols, rows = grid.Rows, i = cy * cols + cx, forest = GroundKinds.ForestOf(grid.Detail[i]);
        float salt = grid.Salt[i] + SaltLift;
        float apart = Mathf.Lerp(OpenApartM, CloseApartM, trees) * upm, keep = Mathf.Lerp(0.6f, 0.97f, trees);
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var (u, v) in new[] { (cx, cy), (cx + 1, cy), (cx, cy + 1), (cx + 1, cy + 1) })
        {
            var w = Coords.ToWorld(Ground.FromUv(sheet, u / (double)cols, v / (double)rows));
            x0 = Mathf.Min(x0, w.X);
            x1 = Mathf.Max(x1, w.X);
            z0 = Mathf.Min(z0, w.Z);
            z1 = Mathf.Max(z1, w.Z);
        }
        if (only != null && !Touches(only, chunk, x0, x1, z0, z1)) return;
        for (int row = Mathf.FloorToInt(z0 / apart) - 1; row <= Mathf.FloorToInt(z1 / apart) + 1; row++)
        {
            float shift = Mod(row, 2) * 0.5f;
            for (int col = Mathf.FloorToInt(x0 / apart - shift) - 1; col <= Mathf.FloorToInt(x1 / apart - shift) + 1; col++)
            {
                var id = new Vector2(col + salt * 13.71f, row + salt * 7.33f);
                var o = (new Vector2(col + shift, row) + Vector2.One * 0.5f + (Hash2(id) - Vector2.One * 0.5f) * Jitter) * apart;
                if (o.X < x0 || o.X > x1 || o.Y < z0 || o.Y > z1) continue;
                var on = Coords.FromWorld(new Vector3(o.X, 0, o.Y));
                var (uu, vv) = Ground.UvOf(sheet, on);
                if ((int)System.Math.Floor(uu * cols) != cx || (int)System.Math.Floor(vv * rows) != cy || Woodness(grid, uu, vv) < 0.5f) continue;
                if (Hash(id + Vector2.One * 2.9f) < 1 - keep) continue;
                float size = 0.6f + 0.8f * Hash(id + Vector2.One * 3.7f), crownM = apart / upm * CrownShare * size;
                if (Felled(near, (float)uu, (float)vv, crownM, Hash(id + Vector2.One * 7.7f))) continue;
                bool conifer = Hash(id + Vector2.One * 6.1f) <= ConiferShare(o, salt, upm);
                var at = new Vector3(o.X, top + (float)ground.DrawnAt(on), o.Y);
                Add(chunks, chunk, at, Kind(forest, culture, conifer, Hash(id + Vector2.One * 5.3f)), crownM * upm, Hash(id + Vector2.One * 9.1f), only);
            }
        }
    }

    static bool Touches(IReadOnlySet<(int, int)> only, float chunk, float x0, float x1, float z0, float z1)
    {
        for (int x = Mathf.FloorToInt(x0 / chunk); x <= Mathf.FloorToInt(x1 / chunk); x++)
            for (int z = Mathf.FloorToInt(z0 / chunk); z <= Mathf.FloorToInt(z1 / chunk); z++)
                if (only.Contains((x, z))) return true;
        return false;
    }

    public static void Add(Dictionary<(int, int), RaisedChunk> chunks, float chunk, Vector3 at, TreeKind kind, float crown, float pick, IReadOnlySet<(int, int)>? only = null)
    {
        var key = ((int)Mathf.Floor(at.X / chunk), (int)Mathf.Floor(at.Z / chunk));
        if (only != null && !only.Contains(key)) return;
        if (!chunks.TryGetValue(key, out var into)) chunks[key] = into = new RaisedChunk(new Vector3((key.Item1 + 0.5f) * chunk, 0, (key.Item2 + 0.5f) * chunk));
        into.Tree(at, kind, crown, pick);
    }

    public static TreeKind TownKind(int culture, float pick) =>
        culture is Manners.Egyptian or Manners.NorthAfrican or Manners.Pacific && pick < 0.8f || culture == Manners.Saracen && pick < 0.4f ? TreeKind.Palm
        : Manners.Kin(culture) == Manners.Northern && pick < 0.5f ? TreeKind.Conifer
        : Manners.Kin(culture) == Manners.Roman && pick < 0.35f || culture == Manners.Persian && pick < 0.6f || culture == Manners.Ottoman && pick < 0.7f
            || culture == Manners.Byzantine && pick < 0.45f || culture == Manners.Saracen && pick < 0.75f ? TreeKind.Cypress : TreeKind.Broad;

    public static TreeKind Kind(int forest, int culture, bool conifer, float pick) => forest switch
    {
        GroundKinds.Broadleaf => TreeKind.Broad,
        GroundKinds.Conifer => TreeKind.Conifer,
        GroundKinds.Mixed => pick < MixedConifers ? TreeKind.Conifer : TreeKind.Broad,
        GroundKinds.Birch => pick < BirchShare ? TreeKind.Birch : TreeKind.Conifer,
        GroundKinds.PalmAndOlive => pick < PalmShare ? TreeKind.Palm : TreeKind.Olive,
        _ => Regional(culture, conifer, pick),
    };

    const float MixedConifers = 0.5f, BirchShare = 0.8f, PalmShare = 0.5f;

    public static TreeKind Regional(int culture, bool conifer, float pick)
    {
        int kin = Manners.Kin(culture), seat = Manners.Named(culture).Seat;
        if (culture is Manners.Egyptian or Manners.NorthAfrican or Manners.Pacific) return pick < 0.55f ? TreeKind.Palm : conifer ? TreeKind.Cypress : TreeKind.Olive;
        if (seat is Manners.South or Manners.NearEast || kin == Manners.Roman)
            return conifer ? pick < 0.5f ? TreeKind.Cypress : TreeKind.Conifer : pick < 0.3f ? TreeKind.Olive : TreeKind.Broad;
        if (seat == Manners.North || kin is Manners.Northern or Manners.Rus or Manners.Soviet) return conifer ? TreeKind.Conifer : pick < 0.45f ? TreeKind.Birch : TreeKind.Broad;
        return conifer ? TreeKind.Conifer : TreeKind.Broad;
    }

    public static float TownCrownM(float pick) => TownTreeM * (0.8f + 0.4f * pick);

    static bool Felled(BuiltNear.Field near, float u, float v, float crownM, float pick)
    {
        if (near.WideM <= 0) return false;
        int x = Mathf.Clamp((int)(u * near.Cols), 0, near.Cols - 1), y = Mathf.Clamp((int)(v * near.Rows), 0, near.Rows - 1);
        float house = near.Texels[(y * near.Cols + x) * 2] / 255f * BuiltNear.ReachM, way = near.Texels[(y * near.Cols + x) * 2 + 1] / 255f * BuiltNear.ReachM;
        float room = Mathf.Min(house - HouseClearM, way - WayClearM) - crownM;
        return room < 0 || room < FelledM && pick < FelledShare;
    }

    static float ConiferShare(Vector2 o, float salt, float upm)
    {
        float n = Noise(o / Mathf.Max(StandM * upm, 1e-3f) + Vector2.One * (salt * 0.37f));
        return Mathf.SmoothStep(0.42f, 0.58f, n);
    }

    static float Mod(int a, int b) => (a % b + b) % b;

    static float Fract(float x) => x - Mathf.Floor(x);

    static float Hash(Vector2 p)
    {
        var q = new Vector3(Fract(p.X * 0.1031f), Fract(p.Y * 0.1031f), Fract(p.X * 0.1031f));
        float d = q.Dot(new Vector3(q.Y + 33.33f, q.Z + 33.33f, q.X + 33.33f));
        q += Vector3.One * d;
        return Fract((q.X + q.Y) * q.Z);
    }

    static Vector2 Hash2(Vector2 p) => new(Hash(p), Hash(p + new Vector2(17.31f, 41.97f)));

    static float Noise(Vector2 p)
    {
        var i = new Vector2(Mathf.Floor(p.X), Mathf.Floor(p.Y));
        var f = p - i;
        var u = f * f * (Vector2.One * 3 - f * 2);
        float a = Hash(i), b = Hash(i + new Vector2(1, 0)), c = Hash(i + new Vector2(0, 1)), d = Hash(i + Vector2.One);
        return Mathf.Lerp(Mathf.Lerp(a, b, u.X), Mathf.Lerp(c, d, u.X), u.Y);
    }
}
