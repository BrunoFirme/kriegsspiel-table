using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ModelFinishTests
{
    [ScreenTest]
    static async Task A_shape_made_metal_shines_in_the_studio_and_on_the_table_and_its_neighbours_stay_matte(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece("Pikeman", ModelImport.Primitive("box"), "box");
        var m = h.Studio.Model!;
        var coat = m.Doc.Parts.Single();
        m.SelPartId = null;
        m.AddShape("cylinder", "Pike");
        var pike = m.SelPart!;
        m.Edits.SetFinish(pike, Finishes.Metal);
        Check.Equal((Finishes.Metal, (string?)null), (pike.Finish, coat.Finish), "only the pike is metal");
        Check.That(m.Views[pike.Id].Mat.GetShaderParameter("metallic").AsSingle() > 0.5f, "the pike shines in the studio");
        Check.Equal(0f, m.Views[coat.Id].Mat.GetShaderParameter("metallic").AsSingle(), "the coat does not");
        await m.Bake();
        Check.That(m.Problem == null, $"the model saves: {m.Problem}");
        var d = h.Studio.Sel!;
        var pack = h.Store.Pack(h.PackId)!;
        var obj = File.ReadAllText(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[d.Mesh!].File));
        Check.That(obj.Contains(SurfaceDraw.MetalStatement.Trim()), "the saved model marks the metal faces");
        int metalAt = obj.IndexOf(SurfaceDraw.MetalStatement.Trim(), System.StringComparison.Ordinal);
        Check.That(obj.IndexOf("\nf ", System.StringComparison.Ordinal) < metalAt && obj.IndexOf("\nf ", metalAt, System.StringComparison.Ordinal) > metalAt, "matte faces come before the mark and metal ones after");

        var mesh = ObjLoader.Load(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[d.Mesh!].File))!;
        var draws = Enumerable.Range(0, mesh.GetSurfaceCount()).Select(s => ObjLoader.Surface(mesh, s)).ToList();
        Check.That(draws.Count(x => x.Draw.Metal) == 1 && draws.Count(x => !x.Draw.Metal) == 1, $"the table reads one metal surface and one matte: {string.Join(", ", draws)}");
        Check.That(draws.All(x => x.Tinted), "both still take the piece's tint");
        var basis = new StandardMaterial3D();
        var shine = PieceArt.Drawn(basis, draws.First(x => x.Draw.Metal).Draw);
        Check.Equal((PieceFinish.Metal.Metallic, PieceFinish.Metal.Roughness), (shine.Metallic, shine.Roughness), "the table dresses the metal surface as metal");

        m.Edits.SetFinish(pike, null);
        await m.Bake();
        Check.That(m.Problem == null, $"the model saves again: {m.Problem}");
        var plain = File.ReadAllText(Path.Combine(h.Store.PackageDir(pack.Id), pack.Assets[d.Mesh!].File));
        Check.That(!plain.Contains(SurfaceDraw.MetalStatement.Trim()), "made matte again, nothing is marked");
    }
}
