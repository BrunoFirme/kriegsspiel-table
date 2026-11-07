using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class BookNotesScriptTests
{
    const string U = Layers.Umpire;

    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    [Fact]
    public void A_module_script_turns_a_book_by_its_printed_page_numbers_and_hears_the_page_turn()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef
        {
            Id = "rules", Name = "Rules", Tags = { "book" }, PageOffset = 2, States = { new() { Name = "Page 1" }, new() { Name = "Page 2" }, new() { Name = "Page 3" } },
        });
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "rules", Id = "p-000030", Pos = new(2, 2) });
        var r = ModuleTranslator.Translate("""
            function onLoad()
              local before = self.Book.page_offset
              self.Book.page_offset = 4
              self.Book.setPage(6, true)
              self.setName(before .. ":" .. self.Book.getPage() .. ":" .. self.Book.getPage(true))
            end
            function onPageChange() self.setDescription("turned") end
            """, new TranslateContext());
        Assert.DoesNotContain(r.Untranslated, u => u.What.StartsWith("Book"));
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000030", Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var book = t.Server.State.Find(U, "p-000030")!;
        Assert.Equal((1, "2:1:6", "turned"), (book.State, book.Label, book.Note));
    }

    [Fact]
    public void A_module_script_reads_and_writes_the_notes_and_notebook_tabs_on_the_note_cards()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "card", Name = "Note", Tags = { NoteCard.Tag }, TextLines = NoteCard.TextLines() });
        void Card(string id, string tag, string title, string body)
        {
            t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "card", Id = id, Pos = new(9, 0), Tags = [tag] });
            t.Server.Handle(Table.Host, new SetText { Layer = U, Id = id, Name = NoteCard.TitleLine, Value = title });
            t.Server.Handle(Table.Host, new SetText { Layer = U, Id = id, Name = NoteCard.BodyLine, Value = body });
        }
        Card("p-000040", "notes", "Notes", "Turn 1: rain");
        Card("p-000041", "notebook", "Orders", "Hold the bridge");
        var r = ModuleTranslator.Translate("""
            function onLoad()
              local tabs = Notes.getNotebookTabs()
              local was = Notes.getNotes() .. "|" .. #tabs .. "|" .. tabs[1].title .. "|" .. tabs[1].body
              Notes.setNotes("Turn 2: fog")
              Notes.editNotebookTab({ index = 0, body = "Cross the river" })
              local added = Notes.addNotebookTab({ title = "Supply", body = "Two days" })
              broadcastToAll(was .. "|" .. added)
            end
            """, new TranslateContext { IsGlobal = true });
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "Turn 1: rain|1|Orders|Hold the bridge|1");
        Assert.Equal("Turn 2: fog", t.Server.State.Find(U, "p-000040")!.Text[NoteCard.BodyLine]);
        Assert.Equal("Cross the river", t.Server.State.Find(U, "p-000041")!.Text[NoteCard.BodyLine]);
        var supply = Assert.Single(t.Server.State.Layer(U), p => p.Tags?.Contains("notebook") == true && p.Id != "p-000041");
        Assert.Equal(("Supply", "Two days"), (supply.Text[NoteCard.TitleLine], supply.Text[NoteCard.BodyLine]));
    }

    [Fact]
    public void An_effect_a_module_script_or_an_umpire_stops_and_starts_answers_as_a_looping_effect()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef { Id = "smoke", Name = "Powder smoke", Tags = { "effect" }, Effect = new EffectDef() });
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "smoke", Id = "p-000050", Pos = new(1, 1) });
        var global = ModuleTranslator.Translate("function onObjectLoopingEffect(obj, index) obj.setDescription('effect ' .. index) end", new TranslateContext { IsGlobal = true });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = global.Script });
        var own = ModuleTranslator.Translate("""
            function onLoad()
              local list = self.AssetBundle.getLoopingEffects()
              self.setName(#list .. ":" .. list[1].name .. ":" .. self.AssetBundle.getLoopingEffectIndex())
            end
            """, new TranslateContext());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000050", Text = own.Script });
        Assert.Equal("1:Powder smoke:0", t.Server.State.Find(U, "p-000050")!.Label);
        t.Server.Handle(Table.Ump3, new SetPieceOption { Layer = U, Id = "p-000050", Name = PieceOptions.EffectOff, Value = true });
        Assert.True(t.Server.State.Find(U, "p-000050")!.EffectOff);
        Assert.Equal("effect -1", t.Server.State.Find(U, "p-000050")!.Note);
    }

    [Fact]
    public void A_module_script_rewrites_3d_text_its_size_and_its_colour()
    {
        using var t = new Table();
        const string P = "p-000002";
        t.Server.Handle(Table.Host, new SetLettering { Layer = U, Id = P, Lettering = new Lettering { Text = "HQ", Size = 48, Color = "#FF0000FF" } });
        var r = ModuleTranslator.Translate("""
            function onLoad()
              local was = self.TextTool.getValue() .. ":" .. self.TextTool.getFontSize() .. ":" .. self.TextTool.getFontColor().r
              self.TextTool.setValue("Corps HQ")
              self.TextTool.setFontSize(72)
              self.TextTool.setFontColor({ 0, 0, 1 })
              self.setDescription(was .. ":" .. self.getValue())
            end
            """, new TranslateContext());
        Assert.DoesNotContain(r.Untranslated, u => u.What.Contains("TextTool"));
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal("HQ:48:1:Corps HQ", p.Note);
        Assert.Equal(("Corps HQ", 72.0, "#0000ffff"), (p.Lettering!.Text, p.Lettering.Size, p.Lettering.Color));
    }
}
