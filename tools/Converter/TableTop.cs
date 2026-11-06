using System.Globalization;
using SkiaSharp;

namespace KTable.Converter;

public sealed record TableTop(double MinX, double MaxX, double MinZ, double MaxZ, double Top, int Turn)
{
    const double Flat = 1e-4, Snug = 0.01, UvSlack = 1e-3;

    public bool Upright => Turn is 0 or 180;

    public static TableTop? Read(string objPath)
    {
        var verts = new List<(double X, double Y, double Z)>();
        var uvs = new List<(double U, double V)>();
        var faces = new List<List<(int V, int T)>>();
        foreach (var line in File.ReadLines(objPath))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) continue;
            if (p[0] == "v" && p.Length >= 4 && Num(p[1]) is { } x && Num(p[2]) is { } y && Num(p[3]) is { } z) verts.Add((x, y, z));
            else if (p[0] == "vt" && Num(p[1]) is { } u && Num(p[2]) is { } v) uvs.Add((u, v));
            else if (p[0] == "f") faces.Add(p.Skip(1).Select(c => Corner(c, verts.Count, uvs.Count)).ToList());
        }
        return verts.Count == 0 ? null : Of(verts, uvs, faces);
    }

    static TableTop? Of(List<(double X, double Y, double Z)> verts, List<(double U, double V)> uvs, List<List<(int V, int T)>> faces)
    {
        double top = verts.Max(v => v.Y);
        var tops = faces.Where(f => f.Count >= 3 && f.All(c => c.V >= 0 && c.V < verts.Count && Math.Abs(verts[c.V].Y - top) < Flat)).ToList();
        if (tops.Count == 0) return null;
        var on = tops.SelectMany(f => f).ToList();
        double minX = on.Min(c => verts[c.V].X), maxX = on.Max(c => verts[c.V].X), minZ = on.Min(c => verts[c.V].Z), maxZ = on.Max(c => verts[c.V].Z);
        double rect = (maxX - minX) * (maxZ - minZ);
        if (rect <= 0 || Math.Abs(tops.Sum(f => Area(f, verts)) - rect) > rect * Snug) return null;
        var at = new Dictionary<(int U, int V), (double X, double Z)>();
        foreach (var (v, t) in on)
        {
            if (t < 0 || t >= uvs.Count) return null;
            var (u, w) = uvs[t];
            if (Math.Abs(u - Math.Round(u)) > UvSlack || Math.Abs(w - Math.Round(w)) > UvSlack || Math.Round(u) is not (0 or 1) || Math.Round(w) is not (0 or 1)) return null;
            at[((int)Math.Round(u), (int)Math.Round(w))] = (-verts[v].X, verts[v].Z);
        }
        if (at.Count != 4 || at.Values.Distinct().Count() != 4) return null;
        var origin = at[(0, 0)];
        var (rx, rz) = Unit(at[(1, 0)], origin);
        var (ux, uz) = Unit(at[(0, 1)], origin);
        if ((rx, rz) != (uz, -ux)) return null;
        int? turn = (ux, uz) switch { (0, -1) => 0, (-1, 0) => 90, (0, 1) => 180, (1, 0) => 270, _ => null };
        return turn is { } quarter ? new TableTop(minX, maxX, minZ, maxZ, top, quarter) : null;
    }

    static (int X, int Z) Unit((double X, double Z) to, (double X, double Z) from)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len <= 0) return (0, 0);
        return (Math.Abs(dx / len) > 1 - UvSlack ? Math.Sign(dx) : 0, Math.Abs(dz / len) > 1 - UvSlack ? Math.Sign(dz) : 0);
    }

    static double Area(List<(int V, int T)> face, List<(double X, double Y, double Z)> verts)
    {
        double twice = 0;
        for (int i = 0; i < face.Count; i++)
        {
            var a = verts[face[i].V];
            var b = verts[face[(i + 1) % face.Count].V];
            twice += a.X * b.Z - b.X * a.Z;
        }
        return Math.Abs(twice) / 2;
    }

    static (int V, int T) Corner(string c, int vertCount, int uvCount)
    {
        var parts = c.Split('/');
        int v = parts.Length > 0 && int.TryParse(parts[0], out var vi) ? Index(vi, vertCount) : -1;
        int t = parts.Length > 1 && int.TryParse(parts[1], out var ti) ? Index(ti, uvCount) : -1;
        return (v, t);
    }

    static int Index(int i, int count) => i > 0 ? i - 1 : count + i;

    const int Probe = 64;
    const double ClothSpread = 4;

    public static bool IsCloth(string picture)
    {
        using var codec = SKCodec.Create(picture);
        if (codec == null) return false;
        var dims = codec.GetScaledDimensions(Math.Min(1f, Probe * 2f / Math.Max(codec.Info.Width, codec.Info.Height)));
        var bmp = SKBitmap.Decode(codec, new SKImageInfo(Math.Max(1, dims.Width), Math.Max(1, dims.Height), SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (bmp == null) return false;
        while (Math.Max(bmp.Width, bmp.Height) > Probe)
        {
            var half = new SKImageInfo(Math.Max(1, (bmp.Width + 1) / 2), Math.Max(1, (bmp.Height + 1) / 2), bmp.ColorType, bmp.AlphaType);
            var next = bmp.Resize(half, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            bmp.Dispose();
            if (next == null) return false;
            bmp = next;
        }
        using (bmp)
        {
            var pixels = bmp.Pixels;
            return new Func<SKColor, double>[] { c => c.Red, c => c.Green, c => c.Blue }.All(channel => Spread(pixels.Select(channel)) < ClothSpread);
        }
    }

    static double Spread(IEnumerable<double> values)
    {
        var list = values.ToList();
        double mean = list.Average();
        return Math.Sqrt(list.Average(v => (v - mean) * (v - mean)));
    }

    static double? Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;
}
