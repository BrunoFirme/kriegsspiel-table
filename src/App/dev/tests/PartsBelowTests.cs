using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class PartsBelowTests
{
    [NodeTest]
    static async Task A_piece_glued_on_top_of_a_part_below_it_rests_on_that_part_as_the_game_rests_the_whole_stack(TestRunner r)
    {
        const float Below = 0.1f;
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
        {
            var pack = new PackManifest { Id = "test.below", Name = "Below" };
            store.RegisterDef(pack, new PieceDef { Id = "base", Name = "Base", Size = new Size2(1, 1), Thickness = 0.1 });
            store.RegisterDef(pack, new PieceDef { Id = "top", Name = "Top", Size = new Size2(1, 1), Thickness = 0.1, Parts = new() { new PartDef { Def = "base", Y = -Below } } });
        }, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
        {
            new() { Id = "a", Def = "top", Pos = new Vec2(0, 0), Scale = 2 },
            new() { Id = "b", Def = "base", Pos = new Vec2(4, 0), Scale = 2 },
        });
        await r.Frames(3);
        var top = t.Node("a");
        var part = top.Parts.Count == 1 ? top.Parts[0] : throw new TestFailure("the piece carries its glued part");
        float table = t.Node("b").GlobalPosition.Y;
        Check.That(Mathf.Abs(part.GlobalPosition.Y - table) < 1e-3f, $"the part below stands on the table as a lone piece does, not under it: {part.GlobalPosition.Y} against {table}");
        Check.That(Mathf.Abs(top.GlobalPosition.Y - table - 2 * Below) < 1e-3f, $"the piece is raised by the part's depth at its scale: {top.GlobalPosition.Y}");
        t.Table.QueueFree();
    }
}
