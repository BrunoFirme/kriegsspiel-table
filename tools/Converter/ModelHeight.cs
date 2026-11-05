using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KTable.Core;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    const double SameHeight = 0.01;

    string? ModelMesh(PackOut pack, TtsObj m, string where)
    {
        double across = Math.Abs(m.ScaleX) + Math.Abs(m.ScaleZ);
        double stretch = across > 0 ? 2 * Math.Abs(m.ScaleY) / across : 1;
        if (Math.Abs(stretch - 1) <= SameHeight || Entry(m.MeshUrl) is not { Ext: "obj" } e || _assets.FilePath(e) is not { } src)
            return Asset(pack, m.MeshUrl, "mesh", where);
        var file = Path.Combine(_cfg.Cache, "stretched", string.Create(CultureInfo.InvariantCulture, $"{e.Sha}-{stretch:0.####}.obj"));
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Json.WriteAtomic(file, Stretched(File.ReadLines(src), stretch));
        }
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        Put(pack, sha, "obj", file, "mesh", 0, 0);
        return sha;
    }

    internal static string Stretched(IEnumerable<string> obj, double stretch)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        foreach (var line in obj)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool vertex = p.Length >= 4 && p[0] is "v" or "vn";
            double x = 0, y = 0, z = 0;
            bool read = vertex && double.TryParse(p[1], NumberStyles.Float, inv, out x) && double.TryParse(p[2], NumberStyles.Float, inv, out y);
            if (!read || !double.TryParse(p[3], NumberStyles.Float, inv, out z))
            {
                sb.Append(line).Append('\n');
                continue;
            }
            if (p[0] == "v") y *= stretch;
            else
            {
                y /= stretch;
                double length = Math.Sqrt(x * x + y * y + z * z);
                if (length > 0) (x, y, z) = (x / length, y / length, z / length);
            }
            p[1] = x.ToString("R", inv);
            p[2] = y.ToString("R", inv);
            p[3] = z.ToString("R", inv);
            sb.Append(string.Join(' ', p)).Append('\n');
        }
        return sb.ToString();
    }
}
