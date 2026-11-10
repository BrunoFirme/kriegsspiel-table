using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class FigureSurfaceTests
{
    const string U = Layers.Umpire;

    static readonly string[] Triangle = ["v 0 0 0", "v 1 0 0", "v 0 1 0", "v 0 0 1", "v 1 0 1", "v 0 1 1", "vt 0 0", "vt 1 0", "vt 0 1"];

    static void Figures(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "figures", "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "drawn.obj"), Triangle.Concat(
        [
            "cull back", "alpha opaque", "usemtl tinted", "f 1/1 2/2 3/3",
            "cull none", "alpha blend", "usemtl own", "f 4/1 5/2 6/3",
        ]));
        File.WriteAllLines(Path.Combine(dir, "plain.obj"), Triangle.Concat(["f 1/1 2/2 3/3", "usemtl own", "f 4/1 5/2 6/3"]));
        var pack = new PackManifest { Id = "figures", Name = "Figures" };
        pack.Assets["drawn"] = new AssetEntry { File = "assets/drawn.obj", Kind = "mesh" };
        pack.Assets["plain"] = new AssetEntry { File = "assets/plain.obj", Kind = "mesh" };
        store.RegisterDef(pack, new PieceDef { Id = "drawn", Name = "Drawn", Kind = "model", Mesh = "drawn", Size = new Size2(1, 1), Thickness = 1, Shape = "box" });
        store.RegisterDef(pack, new PieceDef { Id = "plain", Name = "Plain", Kind = "model", Mesh = "plain", Size = new Size2(1, 1), Thickness = 1, Shape = "box" });
    }

    static StandardMaterial3D Worn(PieceNode n, int surface)
    {
        var model = n.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m => m.Mesh is ArrayMesh) ?? throw new TestFailure("the model is drawn");
        return (StandardMaterial3D)model.GetSurfaceOverrideMaterial(surface);
    }

    [NodeTest]
    static async Task A_model_s_surfaces_are_drawn_one_sided_opaque_or_both_sided_blended_as_its_file_says_and_a_file_that_says_nothing_draws_as_before(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: Figures, arrange: g =>
        {
            g.Layers[U].Add(new PieceInstance { Id = "a", Def = "drawn", Pos = new Vec2(-2, 0) });
            g.Layers[U].Add(new PieceInstance { Id = "b", Def = "plain", Pos = new Vec2(2, 0) });
        });
        await r.Frames(5);
        var a = t.Node("a");
        var b = t.Node("b");

        Check.That(Worn(a, 0).CullMode == BaseMaterial3D.CullModeEnum.Back && Worn(a, 0).Transparency == BaseMaterial3D.TransparencyEnum.Disabled,
            "a one-sided opaque surface hides its back and ignores its picture's alpha");
        Check.That(Worn(a, 1).CullMode == BaseMaterial3D.CullModeEnum.Disabled && Worn(a, 1).Transparency == BaseMaterial3D.TransparencyEnum.Alpha,
            "a blended surface shows both sides and blends");
        Check.That(Worn(b, 0).CullMode == BaseMaterial3D.CullModeEnum.Disabled && Worn(b, 0).Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor,
            "a model whose file names no draw keeps both sides, cut by its picture");
        var whole = PieceArt.Drawn(new StandardMaterial3D(), new SurfaceDraw(true, SurfaceAlpha.Cut, 1));
        Check.That(whole.AlphaScissorThreshold < 1 && whole.AlphaScissorThreshold > 0.9f,
            $"a cut that keeps only whole opacity still keeps a solid picture its filtering leaves a hair under one: {whole.AlphaScissorThreshold}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_module_model_s_front_faces_are_its_lit_outsides_so_hiding_back_faces_hides_nothing_seen_from_outside(TestRunner r)
    {
        string[] cube =
        [
            "v 0 0 0", "v 1 0 0", "v 1 1 0", "v 0 1 0", "v 0 0 1", "v 1 0 1", "v 1 1 1", "v 0 1 1",
            "f 1 4 3 2", "f 5 6 7 8", "f 1 2 6 5", "f 4 8 7 3", "f 1 5 8 4", "f 2 3 7 6",
        ];
        var path = Path.Combine(Path.GetTempPath(), $"ktable-cube-{System.Guid.NewGuid():N}.obj");
        File.WriteAllText(path, ModuleMeshes.Placed(cube, 1));
        try
        {
            var mesh = ObjLoader.Load(path) ?? throw new TestFailure("the placed cube reads");
            Check.That(ObjLoader.Surface(mesh, 0).Draw.OneSided, "a module model is drawn one-sided");
            var arrays = mesh.SurfaceGetArrays(0);
            var points = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
            var normals = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
            var order = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? Enumerable.Range(0, points.Length).ToArray() : (int[])arrays[(int)Mesh.ArrayType.Index];
            var middle = points.Aggregate(Vector3.Zero, (s, p) => s + p) / points.Length;
            for (int i = 0; i + 2 < order.Length; i += 3)
            {
                var (p0, p1, p2) = (points[order[i]], points[order[i + 1]], points[order[i + 2]]);
                var front = (p2 - p0).Cross(p1 - p0);
                var centre = (p0 + p1 + p2) / 3;
                Check.That(front.Dot(centre - middle) > 0, $"triangle {i / 3} faces out of the cube");
                Check.That(front.Dot(normals[order[i]]) > 0, $"triangle {i / 3} is lit on its front");
            }
        }
        finally { KTable.Core.BestEffort.DeleteFile(path); }
        await r.Frames(1);
    }
}
