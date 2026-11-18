using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

static class StudioStateCopyTests
{
    [NodeTest]
    static async Task A_state_duplicated_comes_last_with_its_pictures_shape_and_size_and_undo_takes_it_away(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        s.States.AddCopy();
        s.Apply(x =>
        {
            x.States[1].Name = "Routed";
            x.States[1].Image = "routed-front";
            x.States[1].Back = "routed-back";
            x.States[1].Shape = "round";
            x.States[1].Size = new Size2(0.5, 0.5);
            x.States[1].Module = new ModuleStateData { Type = "Custom_Token", Guid = "abc123" };
        }, what: "a routed look");
        s.States.Add(1);
        Check.Equal(3, d.States.Count, "the copy is a new state");
        var copy = d.States[2];
        Check.Equal(("Routed copy", "routed-front", "routed-back", "round"), (copy.Name, copy.Image, copy.Back, copy.Shape), "the copy is the state it was made from, named as a copy");
        Check.Equal(0.5, copy.Size?.W ?? 0, "with its own size");
        Check.That(copy.Module == null, "and none of the imported module's ties, which belong to the original");
        s.Undo();
        await Until(r, () => d.States.Count == 2);
        Check.Equal(2, d.States.Count, "one undo takes the copy away");
    }

    [NodeTest]
    static async Task A_copy_of_state_1_takes_the_piece_s_own_pictures_where_state_1_leaves_them_to_it(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        s.States.AddCopy();
        s.Apply(x =>
        {
            x.States[0].Image = null;
            x.States[0].Back = null;
            x.Back = "the-back";
        }, what: "a look left to the piece");
        s.States.Add(0);
        await r.Frames(1);
        var copy = d.States[^1];
        Check.Equal((d.Image, "the-back"), (copy.Image, copy.Back), "the copy shows what state 1 shows");
        Check.That(copy.Name.EndsWith(" copy"), $"named as a copy: {copy.Name}");
    }

    static async Task Until(TestRunner r, System.Func<bool> done)
    {
        for (int i = 0; i < 120 && !done(); i++) await r.Frames(1);
    }
}
