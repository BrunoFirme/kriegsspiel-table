using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GlideTests
{
    const string U = Layers.Umpire;

    const string Script = """
        function on_load()
          self.show_glide{ x = 6, z = 0, rot = 90 }
        end
        """;

    [NodeTest]
    static async Task A_piece_shown_gliding_moves_on_screen_towards_its_target_and_a_real_move_ends_the_glide(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frames(2);
        var node = t.Node("p1");
        float startX = node.Position.X;
        t.S.Do(new SetScript { Layer = U, Id = "p1", Text = Script });
        await r.Seconds(0.15);
        Check.That(node.Gliding, "the piece glides");
        float midX = node.Position.X;
        Check.That(midX > startX + 0.01f, $"it has moved on screen ({startX} to {midX})");
        Check.Equal(1.0, t.S.View.Find(U, "p1")!.Pos.X, "the piece itself has not moved yet");
        await r.Seconds(2.5);
        Check.That(!node.GlideShown, "a glide nothing lands lets the piece settle back where it is");
        t.S.Do(new SetScript { Layer = U, Id = "p2", Text = Script });
        await r.Seconds(0.1);
        var other = t.Node("p2");
        Check.That(other.Gliding, "the second piece glides");
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(3, 3) });
        await r.Frames(2);
        Check.That(!other.GlideShown, "a real move ends the glide");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }
}
