using System.Linq;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

static class StudioStateLookTests
{
    [NodeTest]
    static async Task A_shape_built_on_one_state_stays_off_the_others(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        await s.SetViewAsync("model");
        s.States.AddCopy();
        await Until(r, () => s.Model is { } open && open.Holds(d) && open.ShownState == 1);
        Check.Equal((2, 1), (d.States.Count, s.State), "the new state is a copy, chosen at once");
        var m = s.Model!;
        m.AddShape("cylinder", "Flag pole");
        await m.Bake();
        Check.That(StateLooks.AddOnOf(d, 1).Mesh != null, "the shape is built on state 2");
        Check.That(StateLooks.AddOnOf(d, 0).Mesh == null, "state 1 has none");
        await s.States.ChooseAsync(0);
        await Until(r, () => m.ShownState == 0);
        Check.Equal(0, m.Doc.Parts.Count, "state 1 opens without the pole");
        await s.States.ChooseAsync(1);
        await Until(r, () => m.ShownState == 1);
        Check.Equal("Flag pole", m.Doc.Parts.Single().Name, "state 2 still has it");
        await s.SetViewAsync("paint");
        await s.States.ChooseAsync(0);
        Check.That(!PieceFaces.Of(d).Any(f => PieceFaces.IsShapeFace(f.Key)), "in 2D state 1 lists no shape's faces");
        await s.States.ChooseAsync(1);
        Check.That(PieceFaces.Of(d).Any(f => PieceFaces.IsShapeFace(f.Key)), "and state 2 lists the pole's");
    }

    [NodeTest]
    static async Task Shapes_painted_on_a_copy_do_not_reach_the_state_it_was_copied_from(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        await s.SetViewAsync("model");
        await Until(r, () => s.Model is { } open && open.Holds(d));
        var m = s.Model!;
        m.AddShape("box", "Crate");
        await m.Bake();
        var first = StateLooks.AddOnOf(d, 0).Mesh;
        s.States.AddCopy();
        await Until(r, () => m.ShownState == 1);
        Check.Equal(first, StateLooks.AddOnOf(d, 1).Mesh, "the copy starts with the same shapes");
        var crate = m.Views[m.Doc.Parts.Single().Id];
        crate.Paint[0] = 200;
        crate.Paint[3] = 255;
        crate.PaintDirty = true;
        m.Changed(mesh: false);
        await m.Bake();
        var copy = StateLooks.AddOnOf(d, 1).Mesh;
        Check.That(copy != null && copy != first, "the painted copy keeps its own model");
        Check.Equal(first, StateLooks.AddOnOf(d, 0).Mesh, "state 1 keeps its own");
        Check.That(ModelStudio.LoadDoc(s.Store, s.Pack.Id, first)!.Parts.Single().Paint == null, "and state 1's saved shapes carry no paint");
        Check.That(ModelStudio.LoadDoc(s.Store, s.Pack.Id, copy)!.Parts.Single().Paint != null, "while the copy's do");
    }

    [NodeTest]
    static async Task Each_state_keeps_its_own_size_outline_and_tint(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        s.States.AddCopy();
        await r.Frames(2);
        var was = d.Size;
        var tint = d.Tint;
        s.Apply(x => StateLooks.SetSize(x, 0, new Size2(3, 3)), what: "the size");
        s.Apply(x => StateLooks.SetTint(x, 1, "#336699"), what: "the tint");
        await s.Framer.SetOutline(d, 1, "Round");
        Check.Equal((new Size2(3, 3), was), (StateLooks.SizeOf(d, 0), StateLooks.SizeOf(d, 1)), "state 1's new size leaves state 2's");
        Check.Equal((tint, "#336699"), (StateLooks.TintOf(d, 0), StateLooks.TintOf(d, 1)), "state 2's tint leaves state 1's");
        Check.Equal(("box", "circle"), (StateLooks.ShapeOf(d, 0), StateLooks.ShapeOf(d, 1)), "state 2's outline leaves state 1's");
    }

    [NodeTest]
    static async Task Choosing_a_state_in_2D_opens_its_face_on_the_side_that_was_open(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        s.States.AddCopy();
        await s.SetViewAsync("paint");
        await r.Frames(4);
        Check.Equal(("s1", 1), (s.FaceKey, s.State), "2D opens the chosen state's front");
        await s.SwitchFace("s1b");
        await s.States.ChooseAsync(0);
        await r.Frames(2);
        Check.Equal(("back", 0), (s.FaceKey, s.State), "choosing state 1 opens its back");
    }

    [NodeTest]
    static async Task Taking_the_last_state_away_chooses_the_one_before(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        s.States.AddCopy("Shaken");
        s.States.AddCopy("Routed");
        await r.Frames(2);
        Check.Equal((3, 2), (d.States.Count, s.State), "three states, the last chosen");
        await s.RemoveStateAsync(2);
        Check.Equal((2, 1, "Shaken"), (d.States.Count, s.State, d.States[1].Name), "the one before is chosen");
        await s.RemoveStateAsync(0);
        Check.Equal((0, 0), (d.States.Count, s.State), "with one look left the piece has no states");
    }

    [NodeTest]
    static async Task The_layers_tab_holds_the_states_and_the_piece_tab_does_not(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        await s.SetViewAsync("model");
        s.States.AddCopy();
        await r.Frames(4);
        var layers = s.Inspector.Tabs.GetTabControl(s.Inspector.TabIndex("Layers"));
        var buttons = layers.FindChildren("*", "Button", true, false).OfType<Godot.Button>().Where(b => !b.IsQueuedForDeletion()).ToList();
        Check.That(buttons.Any(b => b.TooltipText == "A new state, a copy of this one"), "the Layers tab adds states");
        Check.Equal("1 2", string.Join(" ", buttons.Where(b => b.Text is "1" or "2").Select(b => b.Text)), "one chip a state");
        var piece = s.Inspector.Tabs.GetTabControl(s.Inspector.TabIndex("Piece"));
        Check.That(!piece.FindChildren("*", "Button", true, false).OfType<Godot.Button>().Any(b => b.Text.Contains("STATES")), "the Piece tab has no States section");
    }

    [NodeTest]
    static async Task A_counter_moved_on_one_state_stays_put_on_the_others(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        s.Prints.AddCounter();
        s.States.AddCopy();
        await r.Frames(2);
        var c = d.Counters.Single();
        var first = StateLooks.PrintOf(d, 0, c)!;
        s.Prints.Moved("c0", new FaceBox { X = 0.2, Y = 0.6, W = 0.6, H = 0.2 }, PrintPlace.Back);
        Check.Equal((PrintPlace.Back, 0.6), (StateLooks.PrintOf(d, 1, c)!.Side, StateLooks.PrintOf(d, 1, c)!.Y), "state 2's counter prints on its back");
        Check.Equal((first.Side, first.Y), (StateLooks.PrintOf(d, 0, c)!.Side, StateLooks.PrintOf(d, 0, c)!.Y), "state 1's stays where it was");
        s.Inspector.Tabs.CurrentTab = s.Inspector.TabIndex("Piece");
        s.RebuildInspector();
        await r.Frames(2);
        var piece = s.Inspector.Tabs.GetTabControl(s.Inspector.TabIndex("Piece"));
        Check.That(piece.FindChildren("*", "Button", true, false).OfType<Godot.Button>().Any(b => b.Text.Contains("COUNTERS") && b.Text.Contains("state 2")), "the Counters head names the state it prints on");
    }

    [NodeTest]
    static async Task The_piece_tab_holds_pieces_with_off_and_on_and_offers_markers_with_pictures(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var d = s.Sel!;
        await s.DuplicateSelAsync();
        s.Select(d, frame: false);
        await r.Frames(3);
        s.Inspector.Tabs.CurrentTab = s.Inspector.TabIndex("Piece");
        s.RebuildInspector();
        await r.Frames(2);
        var piece = s.Inspector.Tabs.GetTabControl(s.Inspector.TabIndex("Piece"));
        var buttons = piece.FindChildren("*", "Button", true, false).OfType<Godot.Button>().Where(b => !b.IsQueuedForDeletion()).ToList();
        Check.That(!buttons.OfType<Godot.CheckBox>().Any(b => b.Text == "Container"), "no lone Container tick");
        buttons.First(b => b.Text == "On").EmitSignal(Godot.BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(d.Holds, "On makes it hold pieces");
        piece = s.Inspector.Tabs.GetTabControl(s.Inspector.TabIndex("Piece"));
        var add = piece.FindChildren("*", "MenuButton", true, false).OfType<Godot.MenuButton>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.Text.StartsWith("Add a marker"));
        Check.That(add != null && add.GetPopup().ItemCount == 1, "the marker menu lists the other piece");
    }

    static async Task Until(TestRunner r, System.Func<bool> done)
    {
        for (int i = 0; i < 240 && !done(); i++) await r.Frames(1);
    }
}
