using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ShapeTurnTests
{
    static async Task<ModelStudio> Model(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        await h.Studio.NewModelPiece("Turned", ModelImport.Primitive("box"), "box");
        var m = h.Studio.Model!;
        m.SetTool(TurnTool.Name);
        await r.Frames(2);
        return m;
    }

    static void Drag(ModelStudio m, Vector2 from, Vector2 to, bool steps)
    {
        m.Tools.OnInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        m.Tools.OnInput(new InputEventMouseMotion { Position = to, Relative = to - from, ButtonMask = MouseButtonMask.Left, ShiftPressed = steps });
    }

    static void Let(ModelStudio m, Vector2 at) => m.Tools.OnInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });

    static bool Near(float a, float b, float tolerance = 0.01f) => Mathf.Abs(Mathf.Wrap(a - b, -180, 180)) <= tolerance;

    [ScreenTest]
    static async Task A_ring_dragged_along_itself_turns_the_shape_about_that_axis_in_steps_and_undo_takes_the_turn_back(TestRunner r)
    {
        var m = await Model(r);
        var box = m.Doc.Parts.Single();
        var centre = m.BoundsOf(box)!.Value.GetCenter();
        float front = m.Camera.Yaw;
        var grip = m.Turning.RingPoint(TurnTool.Upright, front)!.Value;
        var further = m.Turning.RingPoint(TurnTool.Upright, front + 0.3f)!.Value;
        var to = grip + (further - grip).Normalized() * 80;
        Drag(m, grip, to, steps: true);
        Check.That(m.Turning.Held && m.Turning.LitRing == TurnTool.Upright, "the upright ring is held");
        float turned = box.Rot[1];
        Check.That(turned >= 15 && Near(turned / 15, Mathf.Round(turned / 15)), $"dragged the way the ring runs it turns that way, in steps of 15°: {turned}");
        Check.That(Near(box.Rot[0], 0) && Near(box.Rot[2], 0), $"only about the upright: {string.Join(",", box.Rot)}");
        Check.That(Near(m.Turning.Turned, turned), $"the ring reads the turn: {m.Turning.Turned}");
        Check.That(m.BoundsOf(box)!.Value.GetCenter().DistanceTo(centre) < 0.01f, "it turns where it stands");
        Let(m, to);
        Check.That(!m.Turning.Held, "letting go ends the turn");
        m.Undo();
        Check.That(Near(m.Doc.Parts.Single().Rot[1], 0), "undo takes the whole turn back in one step");
    }

    [ScreenTest]
    static async Task A_shape_dragged_sideways_spins_where_it_stands_and_a_click_on_it_turns_nothing(TestRunner r)
    {
        var m = await Model(r);
        var box = m.Doc.Parts.Single();
        var mid = (Vector2)m.Vp.Size / 2;
        Check.That(m.Tools.Ray(mid) != null, "the middle of the view is on the shape");
        int steps = m.History.Texts.Count();
        Drag(m, mid, mid, steps: false);
        Let(m, mid);
        Check.That(Near(box.Rot[1], 0) && m.History.Texts.Count() == steps, "a click alone turns nothing and leaves no step to undo");
        Drag(m, mid, mid + new Vector2(60, 0), steps: false);
        Let(m, mid + new Vector2(60, 0));
        Check.That(box.Rot[1] > 1 && Near(box.Rot[0], 0) && Near(box.Rot[2], 0), $"dragged to the right its near side goes right: {string.Join(",", box.Rot)}");
    }

    [ScreenTest]
    static async Task A_joined_shape_dragged_sideways_spins_whole_about_its_middle(TestRunner r)
    {
        var m = await Model(r);
        var box = m.Doc.Parts.Single();
        box.Pos = new[] { 0f, 0.5f, 0f };
        box.Scl = new[] { 1f, 1f, 1f };
        m.SelPartId = null;
        m.AddShape("box", "Wing");
        var wing = m.SelPart!;
        wing.Pos = new[] { 1f, 0.5f, 0f };
        wing.Scl = new[] { 1f, 1f, 1f };
        m.RefreshSelection();
        m.Edits.SetJoin(wing, ModelTree.Add);
        await m.Settle();
        m.Frame();
        await r.Frames(2);
        var middle = new Vector3(0.5f, 0.5f, 0);
        var on = m.Cam.UnprojectPosition(m.Stand.Transform * new Vector3(0.5f, 1, 0));
        Check.That(m.Tools.Ray(on) is { } hit && ModelTree.IsSolidKey(hit.part), "the press lands on the joined solid");
        Drag(m, on, on + new Vector2(70, 0), steps: true);
        Let(m, on + new Vector2(70, 0));
        box = m.Doc.Parts.First(p => p.Name != "Wing");
        wing = m.Doc.Parts.First(p => p.Name == "Wing");
        Check.That(box.Rot[1] >= 15 && Near(box.Rot[1], wing.Rot[1]), $"both shapes take the same turn: {box.Rot[1]} and {wing.Rot[1]}");
        var now = m.BoundsOf(box)!.Value.Merge(m.BoundsOf(wing)!.Value).GetCenter();
        Check.That(now.DistanceTo(middle) < 0.02f, $"the solid turns about its own middle: {now}");
        Check.That(Near(new Vector3(wing.Pos[0], wing.Pos[1], wing.Pos[2]).DistanceTo(middle), 0.5f), "the wing stays half a unit from the middle");
    }

    [ScreenTest]
    static async Task Q_and_E_turn_the_chosen_shape_left_and_right_as_seen_by_fifteen_degrees_and_Shift_by_one(TestRunner r)
    {
        var m = await Model(r);
        var box = m.Doc.Parts.Single();
        Check.That(m.Key(new InputEventKey { Keycode = Key.Q, Pressed = true }), "Q is taken by the tool");
        Check.That(Near(box.Rot[1], 15), $"seen from above Q turns it to the left: {box.Rot[1]}");
        Check.That(m.Key(new InputEventKey { Keycode = Key.E, Pressed = true, ShiftPressed = true }) && m.Tool == TurnTool.Name, "E turns, it does not pick the rubber");
        Check.That(Near(box.Rot[1], 14), $"Shift+E turns it back by one degree: {box.Rot[1]}");
        m.Undo();
        m.Undo();
        Check.That(Near(m.Doc.Parts.Single().Rot[1], 0), "each key is a step to undo");
    }

    [ScreenTest]
    static async Task A_shape_inside_a_turned_group_turns_about_the_ring_s_axis_not_the_group_s(TestRunner r)
    {
        var m = await Model(r);
        var box = m.Doc.Parts.Single();
        m.Edits.Group(box);
        var group = ModelTree.ParentOf(m.Doc, box)!;
        group.Rot = new[] { 90f, 0f, 0f };
        box.Scl = new[] { 2f, 1f, 1f };
        m.RefreshSelection();
        var before = ModelTree.WorldXf(m.Doc, box);
        m.Turning.By(box, TurnTool.Upright, 90);
        var after = ModelTree.WorldXf(m.Doc, box);
        var wanted = new Basis(Vector3.Up, Mathf.Pi / 2) * before.Basis;
        Check.That(after.Basis.IsEqualApprox(wanted), $"its length now runs where a quarter turn about the upright puts it: {after.Basis.X}");
        Check.That(after.Origin.DistanceTo(before.Origin) < 0.001f, "and it stays in its place");
        var middle = m.BoundsOf(group)!.Value.GetCenter();
        m.Turning.By(group, TurnTool.Upright, 45);
        Check.That(m.BoundsOf(group)!.Value.GetCenter().DistanceTo(middle) < 0.01f, "a group turns about the middle of its shapes");
    }
}
