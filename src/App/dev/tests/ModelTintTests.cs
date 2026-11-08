using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ModelTintTests
{
    [NodeTest]
    static async Task A_model_takes_its_tint_on_its_first_group_only_even_where_a_later_group_names_the_same_material(TestRunner r)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ktable-tint-{System.Guid.NewGuid():N}.obj");
        System.IO.File.WriteAllLines(path,
        [
            "v 0 0 0", "v 1 0 0", "v 0 1 0", "v 0 0 1", "v 1 0 1", "v 0 1 1",
            "o hull", "usemtl paint", "f 1 2 3",
            "o mast", "usemtl paint", "f 4 5 6",
        ]);
        try
        {
            var mesh = ObjLoader.Load(path) ?? throw new TestFailure("the model reads");
            Check.That(mesh.GetSurfaceCount() == 2, $"the first group is a tinted surface of its own: {mesh.GetSurfaceCount()} surfaces");
            Check.That(mesh.SurfaceGetArrayLen(0) == 3, "only the first group's triangle takes the tint");
        }
        finally { KTable.Core.BestEffort.DeleteFile(path); }
        await r.Frames(1);
    }
}
