using System.Globalization;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public static class Sanity
{
    public static void Render(Config cfg, string outPath, string? region = null, int maxPx = 4000)
    {
        var store = new PackageStore(cfg.Output);
        var (_, scPath) = store.ListScenarios().First(s => s.id == cfg.Scenario.Dir);
        var sc = Json.Load<Scenario>(scPath);
        store.LoadPacks(sc.Packs);
        var layer = sc.UmpireBoard;

        double minX, maxX, minZ, maxZ;
        if (region != null)
        {
            var p = region.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            (minX, minZ, maxX, maxZ) = (p[0], p[1], p[2], p[3]);
        }
        else
        {
            var xs = sc.Sheets.SelectMany(s => new[] { s.Pos.X - s.Size.W / 2, s.Pos.X + s.Size.W / 2 }).Concat(layer.Select(p => p.Pos.X)).ToList();
            var zs = sc.Sheets.SelectMany(s => new[] { s.Pos.Z - s.Size.H / 2, s.Pos.Z + s.Size.H / 2 }).Concat(layer.Select(p => p.Pos.Z)).ToList();
            (minX, maxX, minZ, maxZ) = (xs.Min() - 5, xs.Max() + 5, zs.Min() - 5, zs.Max() + 5);
        }
        double k = maxPx / Math.Max(maxX - minX, maxZ - minZ);
        using var bmp = new SKBitmap((int)((maxX - minX) * k), (int)((maxZ - minZ) * k));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(40, 40, 40));
        SKPoint Px(Vec2 v) => new((float)((v.X - minX) * k), (float)((maxZ - v.Z) * k));
        var cache = new Dictionary<string, SKBitmap?>();

        foreach (var s in sc.Sheets)
        {
            var src = Directory.GetFiles(Path.Combine(store.PackageDir(s.Package), "assets"), s.Image + ".*").First();
            Draw(canvas, cache, src, Px(s.Pos), s.Size.W * k, s.Size.H * k, s.RotDeg);
        }
        foreach (var p in layer.OrderBy(p => p.Stack))
        {
            var def = store.Def(p.Def);
            if (def == null) continue;
            var size = p.State > 0 && p.State < def.States.Count && def.States[p.State].Size is { } ss ? ss : def.Size;
            var img = p.State < def.States.Count && def.States.Count > 0 ? def.States[p.State].Image ?? def.Image : def.Image ?? def.Texture;
            var path = store.AssetPathForDef(p.Def, img);
            if (path == null || !File.Exists(path)) continue;
            Draw(canvas, cache, path, Px(p.Pos), size.W * p.Scale * k, size.H * p.Scale * k, p.RotDeg);
        }
        canvas.Flush();
        using (var data = bmp.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(outPath, data.ToArray());
        foreach (var b in cache.Values) b?.Dispose();
        Console.WriteLine($"sanity image: {outPath} ({bmp.Width}x{bmp.Height}, {k:0.##} px/unit)");
    }

    static void Draw(SKCanvas canvas, Dictionary<string, SKBitmap?> cache, string path, SKPoint c, double w, double h, double rotDeg)
    {
        if (!cache.TryGetValue(path, out var img)) cache[path] = img = SKBitmap.Decode(path);
        if (img == null) return;
        canvas.Save();
        canvas.Translate(c.X, c.Y);
        canvas.RotateDegrees((float)(180 + rotDeg));
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(SKImage.FromBitmap(img), new SKRect((float)(-w / 2), (float)(-h / 2), (float)(w / 2), (float)(h / 2)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        canvas.Restore();
    }
}
