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

    const string Lettered = """
        function on_load()
          self.button{ x = 0, z = 0, w = 0, h = 0, font = 0.2, label = "button", on_click = "pressed" }
          self.input{ x = 0, z = 0, w = 1.5, h = 0.3, font = 0.2, value = "box", on_change = "typed" }
        end
        function pressed() end
        function typed() end
        """;

    [NodeTest]
    static async Task A_button_s_letters_sit_a_tenth_of_their_size_above_its_anchor_as_the_game_centres_them_and_a_box_s_do_not(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        var node = t.Table.Pieces.Nodes.First();
        t.S.Do(new SetScript { Layer = Layers.Umpire, Id = node.Piece.Id, Text = Lettered });
        await r.Frames(3);
        var labels = node.GetChildren().OfType<Label3D>().ToList();
        var button = labels.FirstOrDefault(l => l.Text == "button") ?? throw new TestFailure("the button is lettered");
        var box = labels.FirstOrDefault(l => l.Text == "box") ?? throw new TestFailure("the box is lettered");
        Check.That(button.Font == Fonts.Widget && box.Font == Fonts.Widget, "buttons and boxes letter in the widget typeface");
        float em = button.PixelSize * Fonts.FaceSize;
        var lift = button.Basis.Y * em * PieceWidgets.ButtonBaselineLift;
        Check.That((button.Position - box.Position).IsEqualApprox(lift), $"the button's letters rise {PieceWidgets.ButtonBaselineLift} of their size along their own up, the box's stay on the anchor");
        Check.That(lift.Length() > 0.01f, "the rise is there to see");
        t.Table.QueueFree();
    }
}
