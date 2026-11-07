using KTable.Core.Model;

namespace KTable.Converter;

public static class StockDice
{
    public static readonly Dictionary<string, double> Radius = new(StringComparer.Ordinal)
    {
        ["Die_4"] = 0.7719, ["Die_6"] = 0.5426, ["Die_6_Rounded"] = 0.4714, ["Die_8"] = 0.5861, ["Die_10"] = 0.5724, ["Die_12"] = 0.5018, ["Die_20"] = 0.5261,
    };

    public static double DrawnRadius(string obj, Size2 size)
    {
        var points = obj.Split('\n').Where(l => l.StartsWith("v ", StringComparison.Ordinal)).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 4).Select(p => (X: Parse(p[1]), Y: Parse(p[2]), Z: Parse(p[3]))).ToList();
        if (points.Count == 0) return 0;
        double ax = points.Max(p => p.X) - points.Min(p => p.X), az = points.Max(p => p.Z) - points.Min(p => p.Z);
        double sx = ax > 0 ? size.W / ax : 1, sz = az > 0 ? size.H / az : 1, sy = (sx + sz) / 2;
        double cx = (points.Max(p => p.X) + points.Min(p => p.X)) / 2, cy = (points.Max(p => p.Y) + points.Min(p => p.Y)) / 2, cz = (points.Max(p => p.Z) + points.Min(p => p.Z)) / 2;
        return points.Max(p => Math.Sqrt(Math.Pow((p.X - cx) * sx, 2) + Math.Pow((p.Y - cy) * sy, 2) + Math.Pow((p.Z - cz) * sz, 2)));
    }

    static double Parse(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
}
