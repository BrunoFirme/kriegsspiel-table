using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed record Mirror(bool X, bool Y, bool Z)
{
    public static Mirror Of(ModuleObj o) => new(o.ScaleX < 0, !o.HeightLocked && o.ScaleY < 0, o.ScaleZ < 0);
    public bool Any => X || Y || Z;
    public bool Flat => X || Z;
    public string Key => Any ? $"|m{(X ? "x" : "")}{(Y ? "y" : "")}{(Z ? "z" : "")}" : "";
    public double SignX => X ? -1 : 1;
    public double SignZ => Z ? -1 : 1;
    public double[] Up(double[] u) => u.Length == 3 ? [X ? -u[0] : u[0], Y ? -u[1] : u[1], Z ? -u[2] : u[2]] : u;
    const double HalfTurn = 180;
    public double Decal(double deg) => (X, Z) switch { (true, false) => -deg, (false, true) => HalfTurn - deg, (true, true) => deg + HalfTurn, _ => deg };
}

public sealed class PieceMirror(string cache)
{
    readonly string _cache = cache;
    readonly Dictionary<(string, Mirror), string?> _done = new();

    public void Apply(PieceDef def, PackOut pack, Mirror m)
    {
        if (!m.Any) return;
        if (def.Kind == "model")
        {
            def.Mesh = Mesh(pack, def.Mesh, m);
            foreach (var s in def.States) s.Mesh = Mesh(pack, s.Mesh, m);
            return;
        }
        if (!m.Flat) return;
        def.Image = Picture(pack, def.Image, m);
        def.Back = Picture(pack, def.Back, m);
        def.Outline = def.Outline?.Select(l => l.Select(p => Flipped(p, m)).ToList()).ToList();
        foreach (var s in def.States)
        {
            s.Image = Picture(pack, s.Image, m);
            s.Back = Picture(pack, s.Back, m);
            s.Outline = s.Outline?.Select(l => l.Select(p => Flipped(p, m)).ToList()).ToList();
        }
    }

    static Vec2 Flipped(Vec2 p, Mirror m) => new(m.X ? 1 - p.X : p.X, m.Z ? 1 - p.Z : p.Z);

    public string? Picture(PackOut pack, string? sha, Mirror m)
    {
        if (sha == null || !m.Flat || !pack.M.Assets.TryGetValue(sha, out var a)) return sha;
        if (_done.TryGetValue((sha, m), out var known)) return known;
        using var bmp = BestEffort.Read(() => SKBitmap.Decode(Path.Combine(pack.Dir, a.File)), fallback: null);
        if (bmp == null) return _done[(sha, m)] = sha;
        using var flipped = new SKBitmap(bmp.Width, bmp.Height, bmp.ColorType, bmp.AlphaType);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(m.X ? -1 : 1, m.Z ? -1 : 1, bmp.Width / 2f, bmp.Height / 2f);
            using var image = SKImage.FromBitmap(bmp);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }
        using var data = flipped.Encode(SKEncodedImageFormat.Png, 100);
        return _done[(sha, m)] = Keep(pack, data.ToArray(), "png", "image", bmp.Width, bmp.Height);
    }

    string? Mesh(PackOut pack, string? sha, Mirror m)
    {
        if (sha == null || !pack.M.Assets.TryGetValue(sha, out var a) || !a.File.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) return sha;
        if (_done.TryGetValue((sha, m), out var known)) return known;
        var lines = BestEffort.Read(() => File.ReadAllLines(Path.Combine(pack.Dir, a.File)), fallback: null);
        if (lines == null) return _done[(sha, m)] = sha;
        return _done[(sha, m)] = Keep(pack, Encoding.UTF8.GetBytes(string.Join('\n', MirroredObj(lines, m)) + "\n"), "obj", "mesh", 0, 0);
    }

    public static IEnumerable<string> MirroredObj(IEnumerable<string> lines, Mirror m)
    {
        bool turnsInside = m.X ^ m.Y ^ m.Z;
        foreach (var line in lines)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4 && p[0] is "v" or "vn")
                yield return string.Join(' ', p.Select((v, i) => i is >= 1 and <= 3 && (i == 1 && m.X || i == 2 && m.Y || i == 3 && m.Z) ? Negated(v) : v));
            else if (turnsInside && p.Length >= 4 && p[0] == "f")
                yield return "f " + string.Join(' ', p.Skip(1).Reverse());
            else yield return line;
        }
    }

    static string Negated(string v) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (-d).ToString("R", CultureInfo.InvariantCulture) : v;

    string Keep(PackOut pack, byte[] bytes, string ext, string kind, int w, int h)
    {
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = Path.Combine(_cache, "mirrored", $"{sha}.{ext}");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Json.WriteAtomic(path, bytes);
        }
        DefFactory.Put(pack, sha, ext, path, kind, w, h);
        return sha;
    }
}
