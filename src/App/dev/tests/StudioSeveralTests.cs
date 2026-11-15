using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class StudioSeveralTests
{
    static async Task<(StudioTests.Rig Rig, PieceDef Hussars, PieceDef Uhlans, PieceDef Cuirassiers)> Three(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var ed = h.Studio;
        var hussars = ed.Pack.Pieces[0];
        var uhlans = PackEdit.NewDef(h.Store, ed.Pack, "Uhlans", hussars.Image!, 8, 8, 1.0, "box");
        var cuirassiers = PackEdit.NewDef(h.Store, ed.Pack, "Cuirassiers", hussars.Image!, 8, 8, 1.0, "box");
        ed.SavePack();
        ed.Paper.LayOutAll();
        ed.RebuildList();
        await r.Frames(2);
        return (h, hussars, uhlans, cuirassiers);
    }

    static string Listed(PieceStudio ed)
    {
        var list = ed.Root.FindChildren("*", nameof(ItemList), true, false).OfType<ItemList>().First(l => l.IconMode == ItemList.IconModeEnum.Top);
        return string.Join(", ", Enumerable.Range(0, list.ItemCount).Select(list.GetItemText));
    }

    static Button? Btn(PieceStudio ed, string prefix) =>
        ed.Root.FindChildren("*", nameof(Button), true, false).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text.StartsWith(prefix));

    static async Task Rename(TestRunner r, PieceStudio ed, PieceDef d, string name)
    {
        d.Name = name;
        ed.SavePack();
        await ed.FlushAllAsync();
        await r.Frames(2);
    }

    static ConfirmationDialog? Shown(TestRunner r) =>
        r.GetTree().Root.FindChildren("*", nameof(ConfirmationDialog), true, false).OfType<ConfirmationDialog>().FirstOrDefault(d => d.Visible && d.Title.StartsWith("Change "));

    static async Task<ConfirmationDialog?> Asked(TestRunner r)
    {
        for (int i = 0; i < 30 && Shown(r) == null; i++) await r.Frames(1);
        return Shown(r);
    }

    static async Task Until(TestRunner r, System.Func<bool> done)
    {
        for (int i = 0; i < 60 && !done(); i++) await r.Frames(1);
    }

    [NodeTest]
    static async Task Edit_several_lays_every_kind_out_in_the_pack_s_order_and_lines_up_the_picked_ones(TestRunner r)
    {
        var (h, hussars, uhlans, cuirassiers) = await Three(r);
        var ed = h.Studio;
        Check.Equal("Cuirassiers, Hussars, Uhlans", Listed(ed), "the list is by name outside Edit several");
        ed.ToggleSeveral();
        await r.Frames(3);
        Check.Equal("table", ed.View, "the pieces are shown on the table");
        Check.Equal("Hussars, Uhlans, Cuirassiers", Listed(ed), "the list follows the pack's own order");
        Check.That(Btn(ed, "Line up") is { Disabled: true }, "nothing to line up with one piece picked");
        ed.T.SetSelection(new[] { ed.Paper.Instance(cuirassiers)!.Id, ed.Paper.Instance(hussars)!.Id });
        await r.Frames(3);
        Check.That(Btn(ed, "Line up 2") is { Disabled: false }, "two picked can be lined up");
        await ed.LineUpAsync();
        await r.Frames(3);
        Check.Equal(hussars.Id + "," + cuirassiers.Id, string.Join(",", ed.Line!.Items.Select(i => i.Kind)), "the line goes in the pack's order");
        Check.Equal(hussars.Id, ed.Sel?.Id, "the first is shown");
        Check.That(ed.Root.FindChildren("*", nameof(Label), true, false).OfType<Label>().Any(l => l.Text == "1 of 2"), "the top bar names the place in the line");
        await Rename(r, ed, hussars, "Guard hussars");
        var q = await Asked(r);
        Check.That(q != null, "the first change asks");
        var answers = q!.GetOkButton().GetParent().GetChildren().OfType<Button>().Where(b => b.Visible).Select(b => b.Text).ToList();
        Check.That(answers.Contains("Save as a new kind") && answers.Contains("Change this kind") && answers.Contains("Undo the change"), "the pack's answers: " + string.Join(", ", answers));
        q.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Until(r, () => ed.Line!.Current.Answered);
        var copy = ed.Line!.Current.Kind;
        Check.That(copy != hussars.Id, "a new kind is made");
        Check.Equal("Hussars", hussars.Name, "the kind is as it was");
        Check.Equal("Guard hussars", ed.Pack.Pieces.Single(d => d.Id == copy).Name, "the new kind carries the change");
        Check.Equal(copy, ed.Sel?.Id, "the studio goes on with the new kind");
        ed.Step(1);
        await Until(r, () => ed.Sel?.Id == cuirassiers.Id);
        await Rename(r, ed, cuirassiers, "Heavy cuirassiers");
        q = await Asked(r);
        q!.EmitSignal(AcceptDialog.SignalName.CustomAction, "also0");
        await Until(r, () => ed.Line!.Current.Answered);
        Check.Equal(cuirassiers.Id, ed.Line!.Current.Kind, "changing the kind keeps it");
        ed.FlushPack();
        var disk = h.OnDisk().Pieces.Select(d => d.Name).ToList();
        Check.That(disk.Contains("Hussars") && disk.Contains("Guard hussars") && disk.Contains("Heavy cuirassiers") && !disk.Contains("Cuirassiers"), "the pack on disk: " + string.Join(", ", disk));
        ed.EndLine();
        await r.Frames(2);
        Check.That(ed.Line == null && Btn(ed, "Line up") != null, "stopping the line goes back to picking");
    }

    [NodeTest]
    static async Task In_Edit_several_kinds_picked_in_the_list_are_picked_on_the_table_and_the_other_way_round(TestRunner r)
    {
        var (h, hussars, uhlans, cuirassiers) = await Three(r);
        var ed = h.Studio;
        var list = ed.Root.FindChildren("*", nameof(ItemList), true, false).OfType<ItemList>().First(l => l.IconMode == ItemList.IconModeEnum.Top);
        Check.Equal(ItemList.SelectModeEnum.Single, list.SelectMode, "one at a time outside Edit several");
        ed.ToggleSeveral();
        await r.Frames(3);
        Check.Equal(ItemList.SelectModeEnum.Multi, list.SelectMode, "several at a time in Edit several");
        list.DeselectAll();
        list.Select(0, false);
        list.Select(2, false);
        list.EmitSignal(ItemList.SignalName.MultiSelected, 2, true);
        await r.Frames(3);
        var onTable = ed.T.SelectedNodes.Select(n => n.Piece.Def).ToHashSet();
        Check.That(onTable.SetEquals(new[] { hussars.Id, cuirassiers.Id }), "the kinds picked in the list are picked on the table");
        Check.That(Btn(ed, "Line up 2") is { Disabled: false }, "and can be lined up");
        ed.T.SetSelection(new[] { ed.Paper.Instance(uhlans)!.Id, ed.Paper.Instance(cuirassiers)!.Id });
        await r.Frames(3);
        Check.Equal("1,2", string.Join(",", list.GetSelectedItems().Order()), "the pieces picked on the table are picked in the list");
        await ed.LineUpAsync();
        await r.Frames(3);
        Check.Equal(uhlans.Id + "," + cuirassiers.Id, string.Join(",", ed.Line!.Items.Select(i => i.Kind)), "the line holds what was picked");
    }

    [NodeTest]
    static async Task Dragging_a_portrait_reorders_the_pack_and_one_undo_puts_it_back(TestRunner r)
    {
        var (h, hussars, uhlans, cuirassiers) = await Three(r);
        var ed = h.Studio;
        ed.ToggleSeveral();
        await r.Frames(3);
        ed.Reorder(cuirassiers.Id, hussars.Id);
        await r.Frames(2);
        Check.Equal("Cuirassiers, Hussars, Uhlans", Listed(ed), "moved up, it goes before the piece it was dropped on");
        ed.Reorder(cuirassiers.Id, uhlans.Id);
        await r.Frames(2);
        Check.Equal("Hussars, Uhlans, Cuirassiers", Listed(ed), "moved down, it goes after the piece it was dropped on");
        ed.Reorder(hussars.Id, cuirassiers.Id);
        ed.FlushPack();
        Check.Equal("Uhlans, Cuirassiers, Hussars", string.Join(", ", h.OnDisk().Pieces.Select(d => d.Name)), "the order is saved with the pack");
        var laid = ed.S.View.Layer(Layers.Umpire).OrderBy(p => -p.Pos.Z).ThenBy(p => p.Pos.X).Select(p => ed.Pack.Pieces.Single(d => d.Id == p.Def).Name);
        Check.Equal("Uhlans, Cuirassiers, Hussars", string.Join(", ", laid), "the table is laid out in the new order");
        ed.Undo();
        await Until(r, () => Listed(ed) == "Hussars, Uhlans, Cuirassiers");
        Check.Equal("Hussars, Uhlans, Cuirassiers", Listed(ed), "one undo puts the last move back");
        ed.ToggleSeveral();
        await r.Frames(2);
        Check.Equal("Cuirassiers, Hussars, Uhlans", Listed(ed), "out of Edit several the list is by name again");
    }
}
