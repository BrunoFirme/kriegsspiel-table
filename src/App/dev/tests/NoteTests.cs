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
}
