using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceNestTests
{
    const string U = Layers.Umpire;

    static bool Under(PieceIndex index, string child, string parent) => index.ItemOf(U, child)?.GetParent() is { } up && up == index.ItemOf(U, parent);

    static string Ticked(PieceIndex index)
    {
        var ids = new System.Collections.Generic.List<string>();
        for (var item = index.Rows.GetNextSelected(null); item != null; item = index.Rows.GetNextSelected(item))
            foreach (var id in new[] { "p1", "p2", "p3" }) if (item == index.ItemOf(U, id)) ids.Add(id);
        return string.Join(",", ids.Order());
    }

    static string OnTable(HudTests.Rig h) => string.Join(",", h.Table.Selected.Order());

    [NodeTest]
    static async Task What_is_selected_on_the_table_is_ticked_under_On_the_table_and_a_row_picked_there_is_selected_on_the_table(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        IHudShell shell = h.Hud;
        shell.TogglePanel("pieces");
        h.Hud.Pieces.ShowIndex();
        await r.Frames(3);
        var index = h.Hud.Pieces.Index;
        Check.That(index.Root.IsVisibleInTree(), "the list is open");

        h.Table.SetSelection(new[] { "p1", "p3" });
        await r.Frames(2);
        Check.Equal("p1,p3", Ticked(index), "rows ticked for the table's selection");

        index.Rows.DeselectAll();
        index.ItemOf(U, "p2")!.Select(0);
        index.Rows.EmitSignal(Tree.SignalName.MultiSelected, index.ItemOf(U, "p2")!, 0, true);
        await r.Frames(2);
        Check.Equal("p2", OnTable(h), "the picked row is the table's selection");
        Check.Equal("p2", Ticked(index), "and the list still shows it");

        h.Table.SetSelection(System.Array.Empty<string>());
        await r.Frames(2);
        Check.Equal("", Ticked(index), "nothing selected, nothing ticked");

        shell.TogglePanel("pieces");
        await r.Frames(2);
        h.Table.SetSelection(new[] { "p3" });
        shell.TogglePanel("pieces");
        h.Hud.Pieces.ShowIndex();
        await r.Frames(3);
        Check.Equal("p3", Ticked(index), "a selection made while the list was shut shows when it opens");
    }

    [NodeTest]
    static async Task Picking_a_row_keeps_selected_the_pieces_the_search_leaves_out_of_the_list(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        IHudShell shell = h.Hud;
        shell.TogglePanel("pieces");
        h.Hud.Pieces.ShowIndex();
        await r.Frames(3);
        var index = h.Hud.Pieces.Index;
        h.S.Do(new SetLabel { Layer = U, Id = "p1", Value = "Hussars" });
        h.S.Do(new SetLabel { Layer = U, Id = "p2", Value = "Grenadiers" });
        await r.Frames(3);
        h.Table.SetSelection(new[] { "p1" });
        var search = index.Root.GetChild<LineEdit>(0);
        search.Text = "Grenadiers";
        search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
        await r.Frames(2);
        index.ItemOf(U, "p2")!.Select(0);
        index.Rows.EmitSignal(Tree.SignalName.MultiSelected, index.ItemOf(U, "p2")!, 0, true);
        await r.Frames(2);
        Check.Equal("p1,p2", OnTable(h), "the hidden hussars stay selected beside the picked grenadiers");
    }

    [NodeTest]
    static async Task A_second_row_dragged_onto_a_row_after_the_first_drag_ended_still_nests_under_it(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        IHudShell shell = h.Hud;
        shell.TogglePanel("pieces");
        h.Hud.Pieces.ShowIndex();
        await r.Frames(3);
        var index = h.Hud.Pieces.Index;
        Vector2 OnRow(string id) => index.Rows.GetItemAreaRect(index.ItemOf(U, id)!).GetCenter();

        Check.That(index.CanDrop(OnRow("p1"), PieceIndex.DragData(new[] { (U, "p2") })), "the first drag can drop onto p1");
        index.Drop(OnRow("p1"), PieceIndex.DragData(new[] { (U, "p2") }));
        index.Rows.Notification((int)Control.NotificationDragEnd);
        await r.Frames(3);
        Check.That(Under(index, "p2", "p1"), "p2 under p1");

        Check.That(index.CanDrop(OnRow("p1"), PieceIndex.DragData(new[] { (U, "p3") })), "after a drag has ended, a row can still be dropped onto p1");
        index.Drop(OnRow("p1"), PieceIndex.DragData(new[] { (U, "p3") }));
        await r.Frames(3);
        Check.That(Under(index, "p3", "p1"), "p3 under p1 as well");
    }

    [NodeTest]
    static async Task Rows_dragged_onto_a_row_nest_under_it_and_ride_with_it_down_the_whole_branch(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        var index = h.Hud.Pieces.Index;
        index.Rebuild();
        index.Nest(index.NestCommands(new() { (U, "p2") }, index.ItemOf(U, "p1"), between: false));
        await r.Frames(2);
        index.Rebuild();
        index.Nest(index.NestCommands(new() { (U, "p3") }, index.ItemOf(U, "p2"), between: false));
        await r.Frames(2);
        index.Rebuild();
        Check.That(Under(index, "p2", "p1") && Under(index, "p3", "p2"), "p3 under p2 under p1");
        Check.Equal(0, index.NestCommands(new() { (U, "p1") }, index.ItemOf(U, "p3"), between: false).Count, "nothing nests under what it carries");
        Check.Equal(0, index.NestCommands(new() { (U, "p3") }, index.ItemOf(U, "p3"), between: true).Count, "dropped beside itself it stays put");

        h.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(11, 0) });
        await r.Frames(2);
        Check.Equal(new Vec2(13, 0), h.S.View.Find(U, "p3")!.Pos, "the grandchild rode with p1");
        h.S.Do(new MovePiece { Layer = U, Id = "p3", Pos = new Vec2(13, 6) });
        await r.Frames(2);
        Check.Equal(new Vec2(11, 0), h.S.View.Find(U, "p1")!.Pos, "a child moved alone leaves its parent where it is");

        index.Nest(index.NestCommands(new() { (U, "p3") }, index.ItemOf(U, "p2"), between: true));
        await r.Frames(2);
        index.Rebuild();
        Check.That(Under(index, "p3", "p1"), "dropped between rows, it joins p2's parent");
        index.Nest(index.NestCommands(new() { (U, "p2"), (U, "p3") }, null, between: false));
        await r.Frames(2);
        index.Rebuild();
        Check.That(index.ItemOf(U, "p2")?.GetParent() == index.Rows.GetRoot() && index.ItemOf(U, "p3")?.GetParent() == index.Rows.GetRoot(), "dropped clear of the rows, both stand alone");
        Check.Equal(0, h.S.View.Find(U, "p1")!.Attached.Count, "p1 carries nothing");
    }

    [NodeTest]
    static async Task A_piece_attached_from_afar_moved_by_hand_alone_stays_attached_and_its_parent_still_carries_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2, arrange: g => g.Find(U, "p2")!.Pos = new Vec2(8, 0));
        await r.Frame();
        t.S.Do(new Attach { Layer = U, Parent = "p1", Child = "p2" });
        await r.Frames(5);
        await Carry(r, t, "p2", new Vec2(8, 0), new Vec2(8, 5));
        Check.Equal(new Vec2(1, 0), t.S.View.Find(U, "p1")!.Pos, "the parent stays");
        Check.That(t.S.View.Find(U, "p1")!.Attached.Contains("p2"), "still attached after moving alone");
        await Carry(r, t, "p1", new Vec2(1, 0), new Vec2(1, 3));
        Check.That((t.S.View.Find(U, "p2")!.Pos - new Vec2(8, 8)).Length < 0.3, $"the parent carries it: {t.S.View.Find(U, "p2")!.Pos}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    static async Task Carry(TestRunner r, OfflineTable t, string id, Vec2 from, Vec2 to)
    {
        t.Table.Selection.Set(new[] { id });
        t.Table.Hand.Start(from);
        t.Table.Hand.Move(to);
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(10);
    }
}
