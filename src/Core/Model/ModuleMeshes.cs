using System.Globalization;
using System.Text;

namespace KTable.Core.Model;

public static class ModuleMeshes
{
    static double Turned(double v) => v == 0 ? 0 : -v;

    const int MostCorners = 4, MostPoints = 30000;

    public static bool KeptFace(string[] p) => p.Length - 1 <= MostCorners;

    public static string? Refusal(IEnumerable<string> obj)
    {
        var points = new HashSet<(string, string)>();
        foreach (var line in obj)
        {
            if (line.Length == 0) continue;
            if (char.IsWhiteSpace(line[0])) return "a line of it starts with a space";
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int numbers = p.Length - 1;
            switch (p[0])
            {
                case "v" when numbers != 3: return "a vertex of it has other than three numbers";
                case "vt" when numbers is < 2 or > 3: return "a texture point of it has one number or more than three";
                case "vn" when numbers != 3: return "a normal of it has other than three numbers";
                case "f" when KeptFace(p):
                    foreach (var corner in p.Skip(1)) points.Add((corner.Split('/')[0], corner.Split('/').ElementAtOrDefault(1) ?? ""));
                    if (points.Count > MostPoints) return $"it has more than {MostPoints} points";
                    break;
            }
        }
        return null;
    }

    public static string Placed(IEnumerable<string> obj, double stretch)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        foreach (var line in obj)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 0 && p[0] == "f" && !KeptFace(p)) continue;
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
            p[1] = Turned(x).ToString("R", inv);
            p[2] = y.ToString("R", inv);
            p[3] = Turned(z).ToString("R", inv);
            sb.Append(string.Join(' ', p)).Append('\n');
        }
        return sb.ToString();
    }
}
