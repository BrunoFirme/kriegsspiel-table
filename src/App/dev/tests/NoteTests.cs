using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class NoteTests
{
    const string NoteDef = "note";

    static OfflineTable WithNote(TestRunner r, string title, string body)
    {
        var t = OfflineTable.Make(r, pieces: 1, prepare: store => store.RegisterDef(new PackManifest { Id = "notes", Name = "Notes" }, new PieceDef
        {
            Id = NoteDef, Name = NoteCard.DefaultName, Size = new Size2(NoteCard.Width, NoteCard.Height), Shape = "box", Thickness = NoteCard.Thickness,
            Tags = new() { NoteCard.Tag }, TextLines = NoteCard.TextLines(title, body),
        }));
        t.S.Do(new SpawnPiece { Layer = Layers.Umpire, Def = NoteDef, Pos = new Vec2(4, 0), RotDeg = 180 });
        return t;
    }

    static PieceNode Note(OfflineTable t) => t.Table.Pieces.Nodes.First(n => n.Piece.Def == NoteDef);

    static string[] Written(PieceNode n) => n.FindChildren("*", "Label3D", true, false).OfType<Label3D>().Where(l => l.Font == Fonts.Hand).Select(l => l.Text).ToArray();

    static void Type(NameEditor names, string text)
    {
        foreach (var ch in text)
            names.Key(ch == '\n' ? new InputEventKey { Keycode = Key.Enter, Pressed = true } : new InputEventKey { Keycode = Key.None, Unicode = ch, Pressed = true });
    }

    [NodeTest]
    static async Task A_note_writes_its_title_and_body_on_its_face_line_by_line(TestRunner r)
    {
        var t = WithNote(r, "Orders", "Hold the ford\nuntil dusk");
        await r.Frames(2);
        var written = Written(Note(t));
        Check.That(written.Contains("Orders"), "the title is written: " + string.Join(" / ", written));
        Check.That(written.Contains("Hold the ford") && written.Contains("until dusk"), "each line of the body is written on its own rule: " + string.Join(" / ", written));
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task Typing_into_a_note_s_body_keeps_new_lines_and_ctrl_enter_keeps_it_for_everyone(TestRunner r)
    {
        var t = WithNote(r, "Orders", "");
        await r.Frames(2);
        var names = t.Table.Names;
        var note = Note(t);
        names.BeginNote(note, body: true);
        Type(names, "Turn\nInitiative");
        names.Key(new InputEventKey { Keycode = Key.Enter, CtrlPressed = true, Pressed = true });
        await r.Frames(1);
        Check.Equal("Turn\nInitiative", Note(t).Piece.Text.GetValueOrDefault(NoteCard.BodyLine), "the body as typed");
        Check.That(names.Node == null, "Ctrl+Enter ends the typing");
        names.BeginNote(Note(t), body: true);
        Type(names, " and more");
        names.Key(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await r.Frames(1);
        Check.Equal("Turn\nInitiative", Note(t).Piece.Text.GetValueOrDefault(NoteCard.BodyLine), "Esc leaves the body as it was");
        names.BeginNote(Note(t), body: false);
        Type(names, " now\n");
        await r.Frames(1);
        Check.Equal("Orders now", Note(t).Piece.Text.GetValueOrDefault(NoteCard.TitleLine), "Enter keeps the title");
        Check.That(Written(Note(t)).Contains("Orders now"), "the new title is written on the card");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_note_face_down_shows_no_writing_and_cannot_be_typed_into(TestRunner r)
    {
        var t = WithNote(r, "Secret", "Flank at dawn");
        await r.Frames(2);
        t.S.Do(new FlipPiece { Layer = Layers.Umpire, Id = Note(t).Piece.Id, FaceDown = true });
        await r.Frames(2);
        Check.That(Written(Note(t)).Length == 0, "nothing written shows face down");
        Check.That(Note(t).NotePartAt(Note(t).Piece.Pos) == null, "no part of it takes typing");
        t.Table.QueueFree();
    }

    static Label3D[] Letters(PieceNode n) => n.FindChildren("*", "Label3D", true, false).OfType<Label3D>().ToArray();

    [NodeTest]
    static async Task A_short_note_is_written_a_line_to_a_rule_and_a_longer_one_two_lines_to_a_rule_so_none_of_it_is_lost(TestRunner r)
    {
        const float Twice = 2, Slack = 0.01f;
        var eleven = string.Join("\n", Enumerable.Range(1, NoteCard.Lines + 1).Select(i => "Order " + i));
        var brief = WithNote(r, "Orders", "Hold the ford");
        var full = WithNote(r, "Orders", eleven);
        await r.Frames(2);
        var large = Letters(Note(brief)).Single(l => l.Text == "Hold the ford");
        var small = Letters(Note(full)).Where(l => l.Text.StartsWith("Order ")).ToArray();
        Check.Equal(NoteCard.Lines + 1, small.Length, "every line of the longer note is written");
        Check.That(Mathf.Abs(large.PixelSize / small[0].PixelSize - Twice) < Slack, $"the short note is written twice as large: {large.PixelSize / small[0].PixelSize:0.##}");
        float step = (float)NoteCard.RuleStep * Note(brief).Footprint.Y;
        Check.That(large.FontSize * large.PixelSize > step / Twice, $"its letters fill the rule's line: {large.FontSize * large.PixelSize / step:0.##} of it");
        brief.Table.QueueFree();
        full.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_note_draws_formatting_codes_as_heavy_slanted_coloured_underlined_and_struck_writing_and_shows_them_raw_while_typed(TestRunner r)
    {
        var t = WithNote(r, "[b]Orders[/b]", "[b]Hold[/b] the [ff0000]ford[-] [u]until[/u] [s]dusk[/s] [i]quietly[/i]");
        await r.Frames(2);
        var letters = Letters(Note(t));
        Check.That(letters.All(l => !l.Text.Contains('[')), "no code is written out: " + string.Join(" / ", letters.Select(l => l.Text)));
        var heavy = letters.Single(l => l.Text.Trim() == "Hold");
        Check.That(heavy.Font is FontVariation { VariationEmbolden: > 0 }, "bold writing is heavier");
        Check.That(letters.Single(l => l.Text.Trim() == "quietly").Font is FontVariation slanted && slanted.VariationTransform != Transform2D.Identity, "italic writing leans");
        var red = letters.Single(l => l.Text.Trim() == "ford").Modulate;
        Check.That(red.R > 0.9f && red.G < 0.1f && red.B < 0.1f, $"coloured writing takes its colour: {red}");
        Check.That(letters.Single(l => l.Text.Trim() == "the").Modulate.R < 0.5f, "the colour ends at the dash");
        Check.Equal(2, Note(t).GetChildren().OfType<MeshInstance3D>().Count(m => m.Mesh is QuadMesh { Size.Y: < 0.1f }), "an underline and a strike are drawn");
        Check.That(letters.Any(l => l.Text == "Orders" && l.Font is FontVariation), "the title takes codes too");

        t.Table.Names.BeginNote(Note(t), body: true);
        await r.Frames(1);
        Check.That(Letters(Note(t)).Any(l => l.Text.Contains("[b]Hold[/b]")), "while typed the codes show as written");
        t.Table.Names.Key(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_note_under_the_pointer_tells_its_title_and_all_its_writing_without_the_codes(TestRunner r)
    {
        var words = string.Join("\n", Enumerable.Range(1, 3 * NoteCard.Lines).Select(i => "Order " + i));
        var t = WithNote(r, "[b]Orders[/b]", "[i]Hold the ford[/i]\n" + words);
        await r.Frames(2);
        var told = Note(t).HoverText();
        Check.That(told.Contains("Orders") && told.Contains("Hold the ford") && told.Contains("Order " + 3 * NoteCard.Lines), "the title and every line are told: " + told);
        Check.That(!told.Contains('['), "without the codes: " + told);
        t.S.Do(new SetLabel { Layer = Layers.Umpire, Id = Note(t).Piece.Id, Value = "[b]Orders[/b]" });
        t.S.Do(new SetNote { Layer = Layers.Umpire, Id = Note(t).Piece.Id, Value = "[i]Hold the ford[/i]\r\n" + words });
        await r.Frames(2);
        var named = Note(t).HoverText();
        var lines = named.Split('\n');
        t.S.Do(new SetNote { Layer = Layers.Umpire, Id = Note(t).Piece.Id, Value = "\n" });
        await r.Frames(2);
        Check.That(!Note(t).HoverText().EndsWith('\n'), "a description of nothing but a line break adds no empty line");
        Check.Equal("Orders", lines[0], "its name is told without the codes");
        Check.Equal((1, 1), (lines.Count(l => l == "Orders"), lines.Count(l => l == "Hold the ford")), "a note named and described with its own writing, as a module's is, tells it once: " + named);
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task Resting_the_pointer_on_a_note_brings_up_its_name_and_its_writing(TestRunner r)
    {
        const double Rest = 0.5;
        var t = WithNote(r, "[b]Orders[/b]", "Hold the [ff0000]ford[-]");
        await r.Frames(2);
        string? shown = null;
        t.Table.HoverChanged += (text, _) => shown = text ?? shown;
        var at = t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(Note(t).Piece.Pos));
        t.Table.Hover.Update(at, 0);
        t.Table.Hover.Update(at, Rest);
        Check.That(t.Table.Hover.Node == Note(t), "the note is the piece under the pointer");
        Check.That(shown != null && shown.Contains("Orders") && shown.Contains("Hold the ford") && !shown.Contains('['), "its name and writing come up without codes: " + shown);
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_note_told_to_write_large_or_small_does_so_whatever_its_length_and_undo_gives_the_choice_back(TestRunner r)
    {
        const float Twice = 2, Slack = 0.01f;
        var eleven = string.Join("\n", Enumerable.Range(1, NoteCard.Lines + 1).Select(i => "Order " + i));
        var t = WithNote(r, "Orders", eleven);
        await r.Frames(2);
        Label3D[] Orders() => Letters(Note(t)).Where(l => l.Text.StartsWith("Order ")).ToArray();
        float fitted = Orders()[0].PixelSize;
        Check.Equal(NoteCard.Lines + 1, Orders().Length, "left to itself the note writes small enough to hold every line");

        t.S.Do(new SetNoteWriting { Layer = Layers.Umpire, Id = Note(t).Piece.Id, LinesToARule = 1 });
        await r.Frames(2);
        Check.Equal(NoteCard.Lines, Orders().Length, "written large it holds a line to each rule and no more");
        Check.That(Mathf.Abs(Orders()[0].PixelSize / fitted - Twice) < Slack, $"large writing is twice the small: {Orders()[0].PixelSize / fitted:0.##}");

        t.S.Do(new SetNoteWriting { Layer = Layers.Umpire, Id = Note(t).Piece.Id, LinesToARule = 2 });
        await r.Frames(2);
        Check.That(Mathf.Abs(Orders()[0].PixelSize / fitted - 1) < Slack && Orders().Length == NoteCard.Lines + 1, "written small it is two lines to a rule again");

        t.S.UndoLast();
        await r.Frames(2);
        Check.Equal(1, Note(t).Piece.LinesToARule, "undo gives back the choice before");
        t.Table.QueueFree();
    }
}
