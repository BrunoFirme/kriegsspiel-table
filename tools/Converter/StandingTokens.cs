using System.Globalization;
using System.Text;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    void StandUp(PieceDef def, PackOut pack)
    {
        if (def.Image is not { } picture) return;
        double across = def.Size.W, tall = def.Size.H, deep = def.Thickness;
        def.Kind = "model";
        def.Shape = "box";
        def.Texture = picture;
        def.Image = null;
        def.Mesh = Generated(pack, Encoding.UTF8.GetBytes(StandingObj(across, tall, deep)), "obj", "mesh", 0, 0);
        def.Size = new Size2(across, Math.Max(deep, ThinnestStand));
        def.Thickness = Math.Round(tall, 3);
        def.LocalUnit = new Size2(1, 1);
        def.Tags = def.Tags.Append("figurine").Distinct().ToList();
    }

    const double ThinnestStand = 0.02;

    static string StandingObj(double across, double tall, double deep)
    {
        var inv = CultureInfo.InvariantCulture;
        double x = across / 2, z = Math.Max(deep, ThinnestStand) / 2;
        var lines = new List<string>
        {
            string.Format(inv, "v {0} 0 {1}", -x, -z), string.Format(inv, "v {0} 0 {1}", x, -z), string.Format(inv, "v {0} {1} {2}", x, tall, -z), string.Format(inv, "v {0} {1} {2}", -x, tall, -z),
            string.Format(inv, "v {0} 0 {1}", -x, z), string.Format(inv, "v {0} 0 {1}", x, z), string.Format(inv, "v {0} {1} {2}", x, tall, z), string.Format(inv, "v {0} {1} {2}", -x, tall, z),
            "vt 0 0", "vt 1 0", "vt 1 1", "vt 0 1",
            "f 1/1 2/2 3/3 4/4", "f 6/2 5/1 8/4 7/3",
        };
        return string.Join('\n', lines) + "\n";
    }
}
