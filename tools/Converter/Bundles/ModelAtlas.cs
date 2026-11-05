using System.Globalization;
using System.Text;
using SkiaSharp;

namespace KTable.Converter.Bundles;

public sealed record AtlasPlace(int X, int Y, int Width, int Height);

public sealed record Atlas(int Width, int Height, byte[] Png, Dictionary<string, AtlasPlace> Places);

public static class ModelAtlas
{
    const int Channels = 4;
    const int Blank = 4;
    const int Axes = 3;
    const int UvParts = 2;
    const int Opaque = 255;

    public static Atlas Pack(IReadOnlyList<(string Sha, Picture Picture)> textures, int most)
    {
        if (textures.Count == 0) return new Atlas(Blank, Blank, Png(White(Blank), Blank, Blank), new Dictionary<string, AtlasPlace>());
        var items = textures.ToList();
        while (items.Sum(i => (long)i.Picture.Width * i.Picture.Height) > (long)most * most || items.Max(i => i.Picture.Width) > most)
            items = items.Select(i => (i.Sha, Halved(i.Picture))).ToList();
        items = items.OrderByDescending(i => i.Picture.Height).ThenByDescending(i => i.Picture.Width).ToList();

        long area = items.Sum(i => (long)i.Picture.Width * i.Picture.Height);
        int width = 1;
        while ((long)width * width < area) width *= 2;
        width = Math.Max(width, items.Max(i => i.Picture.Width));

        var places = new Dictionary<string, AtlasPlace>();
        int x = 0, y = 0, shelf = 0;
        foreach (var (sha, picture) in items)
        {
            if (x + picture.Width > width)
            {
                x = 0;
                y += shelf;
                shelf = 0;
            }
            places[sha] = new AtlasPlace(x, y, picture.Width, picture.Height);
            x += picture.Width;
            shelf = Math.Max(shelf, picture.Height);
        }
        int height = y + shelf;

        var canvas = new byte[(long)width * height * Channels];
        foreach (var (sha, picture) in items)
        {
            var place = places[sha];
            int top = height - place.Y - place.Height;
            for (int row = 0; row < place.Height; row++)
            {
                long from = (long)row * place.Width * Channels;
                long into = ((long)(top + row) * width + place.X) * Channels;
                if (into < 0 || into + (long)place.Width * Channels > canvas.Length) continue;
                Array.Copy(picture.Rgba, from, canvas, into, (long)place.Width * Channels);
            }
        }
        return new Atlas(width, height, Png(canvas, width, height), places);
    }

    static byte[] White(int side)
    {
        var white = new byte[side * side * Channels];
        Array.Fill(white, (byte)Opaque);
        return white;
    }

    static Picture Halved(Picture picture)
    {
        int width = Math.Max(1, picture.Width / 2), height = Math.Max(1, picture.Height / 2);
        using var source = Bitmap(picture);
        using var small = source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                                        new SKSamplingOptions(SKCubicResampler.Mitchell));
        var bytes = new byte[width * height * Channels];
        System.Runtime.InteropServices.Marshal.Copy(small.GetPixels(), bytes, 0, bytes.Length);
        return new Picture(width, height, bytes);
    }

    static SKBitmap Bitmap(Picture picture)
    {
        var bitmap = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(picture.Rgba, 0, bitmap.GetPixels(), Math.Min(picture.Rgba.Length, bitmap.ByteCount));
        return bitmap;
    }

    static byte[] Png(byte[] rgba, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), Math.Min(rgba.Length, bitmap.ByteCount));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static string Obj(BundleModel model, IReadOnlyDictionary<string, AtlasPlace> places, int atlasWidth, int atlasHeight)
    {
        var text = new StringBuilder();
        text.Append("o ").Append(model.Name).Append('\n');
        int baseIndex = 1;
        foreach (var part in model.Parts)
        {
            var used = part.Triangles.Distinct().OrderBy(v => v).ToArray();
            var at = new Dictionary<int, int>(used.Length);
            for (int n = 0; n < used.Length; n++) at[used[n]] = n;
            var place = part.Texture != null && places.TryGetValue(part.Texture, out var found) ? found : null;
            foreach (int vertex in used)
                text.Append("v ").Append(Six(-part.Verts[vertex * Axes])).Append(' ')
                    .Append(Six(part.Verts[vertex * Axes + 1])).Append(' ')
                    .Append(Six(part.Verts[vertex * Axes + 2])).Append('\n');
            foreach (int vertex in used)
            {
                double u = vertex * UvParts + 1 < part.Uvs.Length ? part.Uvs[vertex * UvParts] : 0;
                double v = vertex * UvParts + 1 < part.Uvs.Length ? part.Uvs[vertex * UvParts + 1] : 0;
                if (place != null)
                {
                    u = (Fraction(u) * place.Width + place.X) / atlasWidth;
                    v = (Fraction(v) * place.Height + place.Y) / atlasHeight;
                }
                text.Append("vt ").Append(Six(u)).Append(' ').Append(Six(v)).Append('\n');
            }
            for (int n = 0; n + 2 < part.Triangles.Length; n += 3)
            {
                int a = baseIndex + at[part.Triangles[n]], b = baseIndex + at[part.Triangles[n + 1]], c = baseIndex + at[part.Triangles[n + 2]];
                text.Append("f ").Append(c).Append('/').Append(c).Append(' ')
                    .Append(b).Append('/').Append(b).Append(' ')
                    .Append(a).Append('/').Append(a).Append('\n');
            }
            baseIndex += used.Length;
        }
        return text.ToString();
    }

    static double Fraction(double v) => v - Math.Floor(v);

    static string Six(double v) =>
        (v == 0 ? "0" : v.ToString("G6", CultureInfo.InvariantCulture)).Replace("E", "e", StringComparison.Ordinal);
}
