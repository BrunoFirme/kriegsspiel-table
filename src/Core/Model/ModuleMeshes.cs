using System.Globalization;
using System.Text;

namespace KTable.Core.Model;

public static class ModuleMeshes
{
    static double Turned(double v) => v == 0 ? 0 : -v;

    public static string Placed(IEnumerable<string> obj, double stretch)
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
            p[1] = Turned(x).ToString("R", inv);
            p[2] = y.ToString("R", inv);
            p[3] = Turned(z).ToString("R", inv);
            sb.Append(string.Join(' ', p)).Append('\n');
        }
        return sb.ToString();
    }
}
