using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using static KTable.App.StudioInPlaceTests;

namespace KTable.App;

static class StudioLineTests
{
    const string U = Layers.Umpire;

    static List<LinePiece> Both(Hosted h) => new() { new(U, "p1", h.Hussars, "1st Hussars"), new(U, "p3", h.Hussars, "") };

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

    static List<string> Buttons(ConfirmationDialog q) => q.GetOkButton().GetParent().GetChildren().OfType<Button>().Where(b => b.Visible).Select(b => b.Text).ToList();

    static PieceDef Kind(Studio st, string id) => st.Ed.Pack.Pieces.Single(d => d.Id == id);

    static async Task<bool> Confirm(TestRunner r, Studio st)
    {
        st.Ed.AskConfirm();
        await Until(r, st.Back);
        return st.Back();
    }

    static async Task<string> Card(TestRunner r, Hosted h, params string[] ids)
    {
        foreach (var p in h.Hud.FindChildren("*", nameof(PopupPanel), true, false).OfType<PopupPanel>()) p.Hide();
        h.Table.Selection.Set(ids);
        h.Hud.Menus.Show(new Vector2(300, 300), h.Table.Pieces.NodeOf(U, ids[0]));
        await r.Frames(2);
        var card = h.Hud.FindChildren("*", nameof(PopupPanel), true, false).OfType<PopupPanel>().First(p => p.Visible);
        return string.Join(" | ", card.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).Where(t => t.Length > 0));
    }

    [NodeTest]
    static async Task Several_pieces_of_the_host_s_own_pack_go_to_the_studio_together_but_not_with_a_standard_one_among_them(TestRunner r)
    {
        var h = await Host(r);
        var mine = await Card(r, h, "p1", "p3");
        Check.That(mine.Contains("Edit in the piece studio…"), "two pieces of the host's own pack: " + mine);
        var mixed = await Card(r, h, "p1", "p2");
        Check.That(!mixed.Contains("Edit in the piece studio"), "a standard kind among them: " + mixed);
    }

    [NodeTest]
    static async Task A_new_kind_for_one_piece_of_the_line_leaves_its_kind_and_the_other_pieces_as_they_were(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h, line: Both(h));
        var labels = st.Ed.Root.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).ToList();
        Check.That(labels.Contains("1 of 2 · 1st Hussars"), "the top bar names the place in the line: " + string.Join(", ", labels));
        var buttons = st.Ed.Root.FindChildren("*", nameof(Button), true, false).OfType<Button>().Select(b => b.Text).ToList();
        Check.That(!buttons.Contains("Save as a copy"), "the line asks instead of offering a copy");
        await Rename(r, st, h.Hussars, "Guard hussars");
        var q = await Asked(r);
        Check.That(q != null, "the first change to a piece asks");
        Check.Equal("New kind for this piece, Change all 2, Undo the change", string.Join(", ", Buttons(q!).OrderBy(b => b.StartsWith("New") ? 0 : b.StartsWith("Change") ? 1 : 2)), "the answers");
        q!.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Until(r, () => st.Place.Current.Answered);
        var copy = st.Place.Current.Kind;
        Check.That(copy != h.Hussars, "the piece takes a kind of its own");
        Check.Equal("Hussars", Kind(st, h.Hussars).Name, "its old kind is as it was");
        Check.Equal("Guard hussars · 1st Hussars", Kind(st, copy).Name, "the new kind carries the change and the piece's name");
        Check.Equal(copy, st.Ed.Sel?.Id, "the studio goes on with the new kind");
        st.Ed.FlushPack();
        Check.Equal(copy, StudioDraft.Find(h.Pack)?.Line?.GetValueOrDefault(U + "/p1"), "the draft remembers which piece took it");
        st.Ed.Step(1);
        await Until(r, () => st.Place.Line.At == 1 && st.Ed.Sel?.Id == h.Hussars);
        Check.Equal(h.Hussars, st.Ed.Sel?.Id, "the next piece shows its own kind");
        Check.That(Shown(r) == null, "moving on asks nothing");
        Check.That(await Confirm(r, st), "Confirm goes back to the table");
        Check.Equal(copy, h.S.View.Find(U, "p1")?.Def, "the edited piece has its new kind");
        Check.Equal(h.Hussars, h.S.View.Find(U, "p3")?.Def, "the other piece keeps the old one");
        Check.Equal("Hussars", h.S.Store.Def(h.Hussars)?.Name, "which is unchanged");
        h.S.UndoLast();
        await Until(r, () => h.S.View.Find(U, "p1")?.Def == h.Hussars);
        Check.Equal(h.Hussars, h.S.View.Find(U, "p1")?.Def, "one undo at the table puts the old kind back");
    }

    [NodeTest]
    static async Task Changing_the_kind_from_the_line_changes_every_piece_of_it(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h, line: Both(h));
        await Rename(r, st, h.Hussars, "Guard hussars");
        var q = await Asked(r);
        Check.That(q != null, "the first change asks");
        q!.EmitSignal(AcceptDialog.SignalName.CustomAction, "also0");
        await Until(r, () => st.Place.Current.Answered);
        Check.Equal(h.Hussars, st.Place.Current.Kind, "the piece keeps its kind");
        Check.Equal(1, st.Ed.Pack.Pieces.Count(d => d.Name.StartsWith("Guard")), "no new kind is made");
        await Rename(r, st, h.Hussars, "Guard hussars of 1813");
        await r.Frames(5);
        Check.That(Shown(r) == null, "the answer holds for the rest of this piece's changes");
        Check.That(await Confirm(r, st), "Confirm goes back to the table");
        Check.Equal("Guard hussars of 1813", h.S.Store.Def(h.Hussars)?.Name, "the kind changed");
        Check.That(h.S.View.Find(U, "p1")?.Def == h.Hussars && h.S.View.Find(U, "p3")?.Def == h.Hussars, "both pieces keep the kind");
    }

    [NodeTest]
    static async Task Undoing_from_the_question_puts_the_kind_back_and_asks_again_on_the_next_change(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h, line: Both(h));
        await Rename(r, st, h.Hussars, "Guard hussars");
        var q = await Asked(r);
        q!.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Until(r, () => Kind(st, h.Hussars).Name == "Hussars");
        Check.Equal("Hussars", Kind(st, h.Hussars).Name, "the change is taken back");
        Check.That(!st.Place.Current.Answered, "the piece is still to be asked about");
        await r.Frames(5);
        Check.That(Shown(r) == null, "taking it back asks nothing");
        await Rename(r, st, h.Hussars, "Line hussars");
        Check.That(await Asked(r) != null, "the next change asks again");
    }

    [NodeTest]
    static async Task Same_for_the_rest_of_the_line_answers_the_next_pieces_without_asking(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h, line: Both(h));
        await Rename(r, st, h.Hussars, "Guard hussars");
        var q = await Asked(r);
        var rest = q!.FindChildren("*", nameof(CheckBox), true, false).OfType<CheckBox>().Single();
        Check.Equal("Same for the rest of the line", rest.Text, "the dialog offers it while pieces are left to ask about");
        rest.ButtonPressed = true;
        q.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Until(r, () => st.Place.Current.Answered);
        var first = st.Place.Current.Kind;
        st.Ed.Step(1);
        await Until(r, () => st.Place.Line.At == 1 && st.Ed.Sel?.Id == h.Hussars);
        await Rename(r, st, h.Hussars, "Line hussars");
        await Until(r, () => st.Place.Current.Answered);
        Check.That(Shown(r) == null, "nothing is asked");
        var second = st.Place.Current.Kind;
        Check.That(second != h.Hussars && second != first, "the second piece also takes a kind of its own");
        Check.That(await Confirm(r, st), "Confirm goes back to the table");
        Check.Equal(first, h.S.View.Find(U, "p1")?.Def, "the first piece");
        Check.Equal(second, h.S.View.Find(U, "p3")?.Def, "the second piece");
        Check.Equal("Hussars", h.S.Store.Def(h.Hussars)?.Name, "their old kind is unchanged");
        Check.Equal("Line hussars · 2", h.S.Store.Def(second)?.Name, "a piece with no name of its own is named by its place in the line");
    }
}
