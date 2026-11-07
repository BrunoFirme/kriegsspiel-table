using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class CastPreviewTests
{
    const string U = Layers.Umpire;

    const string Script = """
        function on_load()
          board.show_cast{ shape = "box", from = { x = 0, y = 1, z = 0 }, to = { x = 4, y = 1, z = 0 }, size = { x = 1, y = 1, z = 1 }, turn = { x = 0, y = 30, z = 0 }, seconds = 0.6 }
          board.show_cast{ shape = "sphere", from = { x = 0, y = 2, z = 0 }, to = { x = 0, y = 2, z = 3 }, size = { x = 2, y = 2, z = 2 }, seconds = 0.6 }
          board.show_cast{ from = { x = 0, y = 5, z = 0 }, to = { x = 3, y = 5, z = 0 }, seconds = 0.6 }
        end
        """;

    static CastPreview[] Shown(Node scene) => scene.GetChildren().OfType<CastPreview>().ToArray();

    [NodeTest]
    static async Task A_debug_cast_slides_a_see_through_shape_along_its_path_and_goes_away(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        t.S.Do(new SetScript { Layer = U, Id = "p1", Text = Script });
        await r.Frames(2);
        var shown = Shown(t.Table);
        Check.Equal(3, shown.Length, "a ray, a sphere and a box are shown");
        Check.That(shown.Any(p => p.Mesh is SphereMesh) && shown.Count(p => p.Mesh is BoxMesh) == 2, "each in its own shape");
        await r.Seconds(0.35);
        var box = shown.First(p => p.Mesh is BoxMesh { Size.X: 1 });
        Check.That(box.GlobalPosition.X > box.From.X + 0.1f && box.GlobalPosition.X < box.To.X, $"it is on its way ({box.GlobalPosition.X})");
        await r.Seconds(0.6);
        Check.Equal(0, Shown(t.Table).Length, "it goes when its time is up");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }
}
