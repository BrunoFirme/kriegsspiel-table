using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ModelSetScaleTests
{
    [NodeTest]
    static async Task Models_imported_together_keep_their_own_units_and_are_fitted_as_a_set_only_when_far_off_a_piece_s_size(TestRunner r)
    {
        var horse = new Aabb(Vector3.Zero, new Vector3(1.5f, 2, 3));
        var man = new Aabb(Vector3.Zero, new Vector3(0.5f, 1.5f, 0.6f));
        var asMade = ModelStudio.ImportScale(new[] { horse, man });
        Check.That(asMade is { Scale: 1, Fitted: false }, $"a set at a piece's size keeps its own units: {asMade}");

        var millimetres = ModelStudio.ImportScale(new[] { new Aabb(Vector3.Zero, new Vector3(40, 50, 60)), new Aabb(Vector3.Zero, new Vector3(10, 30, 15)) });
        Check.That(millimetres.Fitted && Mathf.IsEqualApprox(60 * millimetres.Scale, 2) && Mathf.IsEqualApprox(15 * millimetres.Scale, 0.5f),
            $"a set far too big is fitted together, the widest two units wide: {millimetres}");

        var tiny = ModelStudio.ImportScale(new[] { new Aabb(Vector3.Zero, new Vector3(0.02f, 0.02f, 0.01f)) });
        Check.That(tiny.Fitted && Mathf.IsEqualApprox(0.02f * tiny.Scale, 2), $"a set far too small is fitted too: {tiny}");
        await r.Frames(1);
    }
}
