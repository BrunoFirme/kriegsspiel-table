using System.Globalization;
using System.Text;
using KTable.Core.Model;
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
        int baseVertex = 1, baseCorner = 1, baseNormal = 1;
        foreach (var (part, index) in model.Parts.Select((p, i) => (p, i)))
        {
            text.Append((part.Draw ?? Unmade).Statements());
            text.Append(index == 0 ? "usemtl tinted\n" : "usemtl own\n");
            var colour = index == 0 || part.Colour == null ? " 1 1 1" : $" {Six(part.Colour[0])} {Six(part.Colour[1])} {Six(part.Colour[2])}";
            var used = part.Triangles.Distinct().OrderBy(v => v).ToArray();
            var at = new Dictionary<int, int>(used.Length);
            for (int n = 0; n < used.Length; n++) at[used[n]] = n;
            var place = part.Texture != null && places.TryGetValue(part.Texture, out var found) ? found : null;
            var tiling = part.Tiling ?? TextureTiling.Plain;
            bool lit = part.Normals is { } normals && normals.Length >= part.Verts.Length;
            var added = new List<(double[] Point, double[]? Normal)>();
            var faces = new List<(int Vertex, double U, double V)[]>();
            for (int n = 0; n + 2 < part.Triangles.Length; n += 3)
            {
                int[] corners = [part.Triangles[n], part.Triangles[n + 1], part.Triangles[n + 2]];
                var triangle = corners.Select((c, i) => new CutPoint(i == 0 ? 1 : 0, i == 1 ? 1 : 0, i == 2 ? 1 : 0, tiling.U(Uv(part, c, 0)), tiling.V(Uv(part, c, 1)))).ToArray();
                if (place == null)
                {
                    faces.Add(triangle.Select((p, i) => (at[corners[i]], p.U, p.V)).ToArray());
                    continue;
                }
                var made = new Dictionary<(double, double), int>();
                foreach (var piece in TileCut.Cut(triangle, tiling.WrapU, tiling.WrapV))
                    faces.Add(piece.Select(p => (Vertex(p), (p.U * place.Width + place.X) / atlasWidth, (p.V * place.Height + place.Y) / atlasHeight)).ToArray());

                int Vertex(CutPoint p)
                {
                    for (int i = 0; i < corners.Length; i++)
                        if (Weight(p, i) > 1 - Mixed) return at[corners[i]];
                    var key = (Math.Round(p.A, KeyDigits), Math.Round(p.B, KeyDigits));
                    if (made.TryGetValue(key, out int had)) return had;
                    added.Add((Mix(part.Verts, corners, p), lit ? Unit(Mix(part.Normals!, corners, p)) : null));
                    return made[key] = used.Length + added.Count - 1;
                }
            }
            foreach (int vertex in used)
                Line(text, "v ", part.Verts, vertex, colour);
            foreach (var (point, _) in added)
                Line(text, "v ", point, 0, colour);
            if (lit)
            {
                foreach (int vertex in used)
                    Line(text, "vn ", part.Normals!, vertex, "");
                foreach (var (_, normal) in added)
                    Line(text, "vn ", normal!, 0, "");
            }
            foreach (var face in faces)
                foreach (var (_, u, v) in face)
                    text.Append("vt ").Append(Six(u)).Append(' ').Append(Six(v)).Append('\n');
            for (int f = 0; f < faces.Count; f++)
            {
                int corner = baseCorner + f * 3;
                text.Append('f');
                for (int i = 2; i >= 0; i--)
                {
                    int vertex = faces[f][i].Vertex;
                    text.Append(' ').Append(baseVertex + vertex).Append('/').Append(corner + i).Append(Lit(lit, baseNormal + vertex));
                }
                text.Append('\n');
            }
            int vertices = used.Length + added.Count;
            baseVertex += vertices;
            if (lit) baseNormal += vertices;
            baseCorner += faces.Count * 3;
        }
        return text.ToString();
    }

    const double Mixed = 1e-9;
    const int KeyDigits = 9;

    static double Weight(CutPoint p, int corner) => corner switch { 0 => p.A, 1 => p.B, _ => p.C };

    static double[] Mix(double[] values, int[] corners, CutPoint p)
    {
        var mixed = new double[Axes];
        for (int axis = 0; axis < Axes; axis++)
            for (int i = 0; i < corners.Length; i++)
                mixed[axis] += Weight(p, i) * values[corners[i] * Axes + axis];
        return mixed;
    }

    static double[] Unit(double[] v)
    {
        double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        return length > Mixed ? [v[0] / length, v[1] / length, v[2] / length] : v;
    }

    static void Line(StringBuilder text, string head, double[] values, int index, string tail) =>
        text.Append(head).Append(Six(values[index * Axes])).Append(' ')
            .Append(Six(values[index * Axes + 1])).Append(' ')
            .Append(Six(-values[index * Axes + 2])).Append(tail).Append('\n');

    static readonly SurfaceDraw Unmade = MaterialDraw.Drawn(MaterialDraw.Standard, new Dictionary<string, double>());

    static string Lit(bool lit, int normal) => lit ? $"/{normal}" : "";

    static double Uv(ModelPart part, int vertex, int axis) =>
        vertex * UvParts + 1 < part.Uvs.Length ? part.Uvs[vertex * UvParts + axis] : 0;

    static string Six(double v) =>
        (v == 0 ? "0" : v.ToString("G6", CultureInfo.InvariantCulture)).Replace("E", "e", StringComparison.Ordinal);
}
