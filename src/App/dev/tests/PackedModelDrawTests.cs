using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PackedModelDrawTests
{
    static readonly string[] Gun =
    [
        "o Gun",
        "cull back", "alpha opaque", "usemtl tinted",
        "v 0 0 0 1 1 1", "v 1 0 0 1 1 1", "v 0 1 0 1 1 1", "v 1 1 0 1 1 1",
        "vn 0 0 1", "vn 0 0 1", "vn 0.6 0 0.8", "vn 0 0 1",
        "vt 0.1 0.1", "vt 0.9 0.1", "vt 0.1 0.9",
        "f 3/3/3 2/2/2 1/1/1", "f 3/3/3 4/2/4 2/2/2",
        "cull none", "alpha blend", "usemtl own",
        "v 0 0 1 0.5 0.25 0", "v 1 0 1 0.5 0.25 0", "v 0 1 1 0.5 0.25 0",
        "vt 0.6 0.6", "vt 0.7 0.6",
        "f 7/5 6/4 5/4",
    ];

    static string Drawn(ArrayMesh mesh, int surface)
    {
        var arrays = mesh.SurfaceGetArrays(surface);
        string Listed<[MustBeVariant] T>(Mesh.ArrayType kind, System.Func<Variant, T[]> read) =>
            arrays[(int)kind].VariantType == Variant.Type.Nil ? "none" : string.Join(' ', read(arrays[(int)kind]).Select(v => v!.ToString()));
        return $"{mesh.SurfaceGetName(surface)} | {Listed(Mesh.ArrayType.Vertex, v => v.AsVector3Array())} | {Listed(Mesh.ArrayType.Normal, v => v.AsVector3Array())}"
            + $" | {Listed(Mesh.ArrayType.TexUV, v => v.AsVector2Array())} | {Listed(Mesh.ArrayType.Color, v => v.AsColorArray())} | {Listed(Mesh.ArrayType.Index, v => v.AsInt32Array())}";
    }

    [NodeTest]
    static async Task A_packed_model_is_drawn_point_for_point_as_the_same_model_kept_as_text_and_a_damaged_one_is_left_out(TestRunner r)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ktable-packed-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            string text = System.IO.Path.Combine(dir, "gun.obj"), packed = System.IO.Path.Combine(dir, "gun" + PackedModel.Ext), cut = System.IO.Path.Combine(dir, "cut" + PackedModel.Ext);
            System.IO.File.WriteAllLines(text, Gun);
            var bytes = PackedModel.Pack(Gun) ?? throw new TestFailure("the model packs");
            System.IO.File.WriteAllBytes(packed, bytes);
            System.IO.File.WriteAllBytes(cut, bytes[..(bytes.Length / 2)]);

            var asText = ObjLoader.Load(text) ?? throw new TestFailure("the model kept as text reads");
            var asPacked = ObjLoader.Load(packed) ?? throw new TestFailure("the packed model reads");

            Check.Equal(asText.GetSurfaceCount(), asPacked.GetSurfaceCount(), "both have the same surfaces");
            Check.Equal(2, asPacked.GetSurfaceCount(), "the tinted part and the part in its own colour");
            for (int s = 0; s < asText.GetSurfaceCount(); s++)
                Check.Equal(Drawn(asText, s), Drawn(asPacked, s), $"surface {s} is the same points, light, picture places and colours");
            Check.That(ObjLoader.Load(cut) == null, "a packed model cut short is not drawn");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(dir); }
        await r.Frames(1);
    }
}
