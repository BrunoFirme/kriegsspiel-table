using System.Globalization;
using System.Text;

namespace KTable.Client.Studio;

public static class ModelGroups
{
    public static string? Encode(IReadOnlyList<int> mats, int tris)
    {
        if (mats.Count < tris || tris == 0) return null;
        if (mats.Take(tris).Distinct().Count() < 2) return null;
        var sb = new StringBuilder();
        int run = mats[0], count = 0;
        for (int t = 0; t < tris; t++)
        {
            if (mats[t] == run) { count++; continue; }
            Append(sb, run, count);
            run = mats[t]; count = 1;
        }
        Append(sb, run, count);
        return sb.ToString();
    }

    static void Append(StringBuilder sb, int mat, int count)
    {
        if (sb.Length > 0) sb.Append(',');
        sb.Append(mat.ToString(CultureInfo.InvariantCulture)).Append('*').Append(count.ToString(CultureInfo.InvariantCulture));
    }

    public static List<int>? Decode(string groups, int tris)
    {
        var mats = new List<int>(tris);
        foreach (var piece in groups.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = piece.Split('*');
            if (bits.Length != 2 || !int.TryParse(bits[0], NumberStyles.None, CultureInfo.InvariantCulture, out var mat) || !int.TryParse(bits[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)) return null;
            if (count < 1 || mats.Count + count > tris) return null;
            mats.AddRange(Enumerable.Repeat(mat, count));
        }
        return mats.Count == tris ? mats : null;
    }
}
