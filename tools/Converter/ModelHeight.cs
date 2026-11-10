using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KTable.Core;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    const double SameHeight = 0.01;

    double Stretch(ModuleObj m)
    {
        double across = Math.Abs(m.ScaleX) + Math.Abs(m.ScaleZ);
        double stretch = across > 0 ? 2 * Math.Abs(m.ScaleY) / across : 1;
        return Math.Abs(stretch - 1) <= SameHeight || Entry(m.MeshUrl) is not { Ext: "obj" } e || _assets.FilePath(e) == null ? 1 : stretch;
    }

    string? ModelMesh(PackOut pack, ModuleObj m, string where)
    {
        double stretch = Stretch(m);
        if (Entry(m.MeshUrl) is not { Ext: "obj" } e || _assets.FilePath(e) is not { } src)
            return Asset(pack, m.MeshUrl, "mesh", where);
        var file = Path.Combine(_cfg.Cache, "placed", string.Create(CultureInfo.InvariantCulture, $"{e.Sha}-{stretch:0.####}-lit.obj"));
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Json.WriteAtomic(file, Placed(File.ReadLines(src), stretch));
        }
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        Put(pack, sha, "obj", file, "mesh", 0, 0);
        return sha;
    }

    internal static string Placed(IEnumerable<string> obj, double stretch) => KTable.Core.Model.ModuleMeshes.Placed(obj, stretch);
}
