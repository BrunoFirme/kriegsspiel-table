using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class LetteringTurnTests
{
    [NodeTest]
    static async Task Module_text_saved_at_turn_zero_reads_upright_from_the_south_as_the_game_shows_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
            store.RegisterDef(new PackManifest { Id = "test.text", Name = "Text" }, new PieceDef { Id = "label", Name = "Label", Size = new Size2(4, 1), Thickness = 0.02 }),
            arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
            {
                new() { Id = "a", Def = "label", Pos = new Vec2(0, 0), RotDeg = 0, Lettering = new KTable.Core.Model.Lettering { Text = "Bull Run campaign" } },
            });
        await r.Frames(3);
        var label = t.Node("a").GetChildren().OfType<Label3D>().FirstOrDefault() ?? throw new TestFailure("the text is drawn live");
        var up = label.GlobalBasis * Vector3.Up;
        var north = Coords.ToWorld(new Vec2(0, 1), 0) - Coords.ToWorld(new Vec2(0, 0), 0);
        Check.That(up.Normalized().Dot(north.Normalized()) > 0.99f, $"the top of the text points north (+Z on the map): {up}");
        t.Table.QueueFree();
    }
}
