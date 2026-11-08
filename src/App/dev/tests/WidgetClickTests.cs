using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class WidgetClickTests
{
    const float Raised = 3, Beside = 1.4f, Lift = 0.008f;

    const string Script = """
        function on_load()
          self.button{ x = 0, z = 0, y = 3, w = 0.6, h = 0.3, label = "high", on_click = "pressed" }
          self.button{ x = 1.2, z = 0, w = 0.8, h = 0.3, label = "beside", on_click = "pressed" }
        end
        function pressed() end
        """;

    [NodeTest]
    static async Task A_button_takes_the_click_where_it_is_drawn_even_raised_above_its_piece_or_hanging_past_its_edge(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        var node = t.Table.Pieces.Nodes.First();
        t.S.Do(new SetScript { Layer = Layers.Umpire, Id = node.Piece.Id, Text = Script });
        var rig = t.Table.Rig;
        rig.Target = node.GlobalPosition;
        rig.Distance = 12;
        rig.Apply();
        await r.Frames(3);
        Check.Equal(2, node.Piece.Widgets.Count, "two buttons drawn");
        var cam = rig.Cam;
        Vector2 Seen(Vector3 local) => cam.UnprojectPosition(node.GlobalTransform * local);
        var high = Seen(new Vector3(0, Raised, 0));
        Check.Equal(1, node.WidgetUnder(high)?.index ?? 0, "the raised button answers where it shows");
        Check.That(t.Table.Pieces.Pick(node.Piece.Pos, screen: high) == node, "pointing at the raised button picks its piece");
        var below = cam.UnprojectPosition(new Vector3(node.GlobalPosition.X, node.TopY + Lift, node.GlobalPosition.Z));
        Check.That(node.WidgetUnder(below)?.index != 1, "the face under the raised button does not press it from an angle");
        var past = cam.UnprojectPosition(node.GlobalTransform * new Vector3(Beside, 0, 0) with { Y = node.TopY + Lift });
        Check.That(!node.Contains(Coords.FromWorld(node.GlobalTransform * new Vector3(Beside, 0, 0))), "the point is past the piece's edge");
        Check.Equal(2, node.WidgetUnder(past)?.index ?? 0, "the part of a button past the edge answers");
        Check.That(t.Table.Pieces.Pick(node.Piece.Pos, screen: past) == node, "pointing past the edge at the button picks its piece");
        t.Table.QueueFree();
    }
}
