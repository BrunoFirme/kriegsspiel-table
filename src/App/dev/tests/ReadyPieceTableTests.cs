using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ReadyPieceTableTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task A_held_piece_marked_ready_comes_out_with_the_key_and_goes_back_with_it(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p2" });
        h.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p3" });
        await r.Frames(2);
        var root = new Control { Name = "Screen" };
        r.AddChild(root);
        var holder = h.Table.NodeOf(U, "p1")!;
        HolderWindow.Open(root, h.S, h.Table, holder);
        await r.Frames(2);
        var w = root.GetChildren().OfType<HolderWindow>().Single();
        var list = w.FindChildren("*", nameof(ItemList), true, false).OfType<ItemList>().Single();
        Check.Equal("p2", list.GetItemMetadata(0).AsString(), "the first held piece before any is ready");
        var tile = list.GetItemRect(1).GetCenter();
        list.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = tile, GlobalPosition = tile });
        Check.That(w.Menu is { } menu && menu.ItemCount == 1 && menu.GetItemText(0) == "Ready piece", "a right-click on a held tile offers to make it the ready piece");
        w.Menu!.EmitSignal(PopupMenu.SignalName.IdPressed, (long)HolderWindow.ReadyChoice);
        await r.Frames(2);
        Check.Equal("p3", list.GetItemMetadata(0).AsString(), "the ready piece is listed first");
        Check.That(list.GetItemText(0).Contains("Ready"), "the ready piece is marked: " + list.GetItemText(0));

        void PressB() => h.Table.Keys.OnKey(new InputEventKey { Keycode = Key.B, Pressed = true });
        int undos = h.S.MyUndoable.Count;
        h.Table.Selection.Set(new[] { "p1" });
        PressB();
        await r.Frames(3);
        Check.That(h.S.View.Find(U, "p3") != null, "B on the holder releases its ready piece onto the table");
        Check.Equal(undos + 1, h.S.MyUndoable.Count, "things to undo after one press");
        Check.Equal("out:p3", list.GetItemMetadata(0).AsString(), "the released piece stays at the top of the holder's window");

        h.Hud.Menus.Show(Vector2.Zero, holder);
        await r.Frames(1);
        var rows = h.Hud.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).ToList();
        Check.That(rows.Any(t => t.StartsWith("Recall")), "the holder's menu offers to recall it: " + string.Join(" | ", rows.Where(t => t.Length > 0).Take(20)));
        foreach (var p in h.Hud.FindChildren("*", nameof(PopupPanel), true, false).OfType<PopupPanel>()) p.Hide();

        h.Table.Selection.Set(new[] { "p3" });
        PressB();
        await r.Frames(3);
        Check.That(h.S.View.Find(U, "p3") == null, "B on the released piece recalls it");
        Check.Equal("p3", h.S.View.Find(U, "p1")!.Stowed![0].Id, "the recalled piece is first in the holder");
        Check.Equal(undos + 2, h.S.MyUndoable.Count, "things to undo after two presses");
        w.QueueFree();
        root.QueueFree();
    }

    [NodeTest]
    static async Task The_first_piece_put_in_is_ready_and_B_over_the_holder_lets_it_out_attached_and_takes_it_back(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.S.Do(new Batch { Items = KTable.Client.ReadyKey.Stowing(h.S.View, U, h.S.View.Find(U, "p1")!, [new Stow { Layer = U, Holder = "p1", Id = "p3" }]) });
        await r.Frames(2);
        Check.Equal("p3", h.S.View.Find(U, "p1")!.Ready?.Id, "the first piece put into an empty holder is its ready piece");
        h.Table.Selection.Set(System.Array.Empty<string>());
        var over = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(new Vec2(1, 0)));
        h.Table.Hover.Update(over, 0);
        Check.Equal("p1", h.Table.HoveredNode?.Piece.Id, "the pointer is over the holder");
        void PressB() => h.Table.Keys.OnKey(new InputEventKey { Keycode = Key.B, Pressed = true });
        PressB();
        await r.Frames(3);
        Check.That(h.S.View.Find(U, "p3") != null, "B over the holder releases its ready piece with nothing selected");
        Check.That(h.S.View.Find(U, "p1")!.Attached.Contains("p3"), "it comes out attached to its holder");
        h.Table.Hover.Update(over, 0);
        PressB();
        await r.Frames(3);
        Check.That(h.S.View.Find(U, "p3") == null, "B over the holder again recalls it");
    }

    [NodeTest]
    static async Task Held_pieces_are_made_ready_for_a_look_from_their_menu_and_the_number_keys_swap_them(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        var def = h.S.Store.Def("x")!;
        def.States.Add(new PieceStateDef { Name = "Line" });
        def.States.Add(new PieceStateDef { Name = "Column" });
        h.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p2" });
        h.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p3" });
        await r.Frames(2);
        var root = new Control { Name = "Screen" };
        r.AddChild(root);
        HolderWindow.Open(root, h.S, h.Table, h.Table.NodeOf(U, "p1")!);
        await r.Frames(2);
        var w = root.GetChildren().OfType<HolderWindow>().Single();
        var list = w.FindChildren("*", nameof(ItemList), true, false).OfType<ItemList>().Single();
        int Row(string id) => Enumerable.Range(0, list.ItemCount).First(i => list.GetItemMetadata(i).AsString() == id);
        void RightClick(string id)
        {
            var tile = list.GetItemRect(Row(id)).GetCenter();
            list.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = tile, GlobalPosition = tile });
        }
        RightClick("p3");
        var texts = Enumerable.Range(0, w.Menu!.ItemCount).Select(i => w.Menu.GetItemText(i)).ToList();
        Check.That(texts.SequenceEqual(new[] { "Ready piece", "Ready in state 1: Line", "Ready in state 2: Column" }), "a held piece's menu offers each state: " + string.Join(" | ", texts));
        w.Menu.EmitSignal(PopupMenu.SignalName.IdPressed, (long)HolderWindow.LookChoice + 1);
        await r.Frames(2);
        RightClick("p2");
        w.Menu!.EmitSignal(PopupMenu.SignalName.IdPressed, (long)HolderWindow.LookChoice);
        await r.Frames(2);
        Check.Equal("p2", h.S.View.Find(U, "p1")!.ReadyFor(0)?.Id, "ready in state 1");
        Check.Equal("p3", h.S.View.Find(U, "p1")!.ReadyFor(1)?.Id, "ready in state 2");

        var over = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(new Vec2(1, 0)));
        async Task Press(Key key)
        {
            h.Table.Hover.Update(over, 0);
            h.Table.Keys.OnKey(new InputEventKey { Keycode = key, Pressed = true });
            await r.Frames(3);
        }
        await Press(Key.B);
        Check.That(h.S.View.Find(U, "p2") != null, "B brings out state 1's ready piece");
        await Press(Key.Key2);
        Check.Equal(1, h.S.View.Find(U, "p1")!.State, "the holder is in state 2");
        Check.That(h.S.View.Find(U, "p2") == null && h.S.View.Find(U, "p3") == null, "state 1's piece goes back in and state 2's, never out, stays in");
        await Press(Key.B);
        await Press(Key.Key1);
        Check.That(h.S.View.Find(U, "p2") != null && h.S.View.Find(U, "p3") == null, "back in state 1 its piece comes out again and state 2's goes in");
        Check.That(h.S.View.Find(U, "p1")!.Attached.Contains("p2"), "it rides the holder");
        await Press(Key.Key2);
        Check.That(h.S.View.Find(U, "p3") != null && h.S.View.Find(U, "p2") == null, "state 2's piece comes out as it was left");

        RightClick("p2");
        Check.That(w.Menu!.GetItemText(w.Menu.ItemCount - 1) == "Not ready", "a ready piece's menu offers to clear it");
        w.Menu.EmitSignal(PopupMenu.SignalName.IdPressed, (long)HolderWindow.NotReadyChoice);
        await r.Frames(2);
        Check.That(h.S.View.Find(U, "p1")!.ReadyIn?.Values.All(x => x.Id != "p2") ?? true, "Not ready clears it from every state");
        w.QueueFree();
        root.QueueFree();
    }

    [NodeTest]
    static async Task A_copied_holder_brings_its_ready_piece_from_the_table_and_holds_its_own_pieces(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p2" });
        h.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p3" });
        h.S.Do(new SetReady { Layer = U, Holder = "p1", Ready = new ReadyPiece { Id = "p3" }, Move = "p3" });
        h.S.Do(new ReleaseReady { Layer = U, Holder = "p1" });
        await r.Frames(3);
        Check.That(h.S.View.Find(U, "p3") != null, "the ready piece is out before the copy");
        h.Table.Clip.Copy([h.Table.NodeOf(U, "p1")!], cut: false);
        var before = h.S.View.Layer(U).Select(p => p.Id).ToHashSet();
        h.Table.Clip.Paste(new Vec2(20, 20));
        await r.Frames(3);
        var made = h.S.View.Layer(U).Where(p => !before.Contains(p.Id)).ToList();
        Check.Equal(2, made.Count, "pieces pasted: the holder and its ready piece");
        var holder = made.Single(p => p.Ready != null);
        var ready = made.Single(p => p.Ready == null);
        Check.Equal(ready.Id, holder.Ready!.Id, "the pasted holder's ready piece is the pasted copy");
        Check.That(holder.Stowed is [{ Id: not "p2" }], "the pasted holder holds a copy of its piece under its own id");
        h.S.Do(new RecallReady { Layer = U, Holder = holder.Id });
        await r.Frames(3);
        Check.That(h.S.View.Find(U, ready.Id) == null, "the copy's ready piece goes back into the copy");
        Check.That(h.S.View.Find(U, "p3") != null, "the first holder's ready piece stays out");
    }
}
