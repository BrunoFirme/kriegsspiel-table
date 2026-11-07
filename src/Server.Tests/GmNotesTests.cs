using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class GmNotesTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    [Fact]
    public void A_pieces_saved_gm_notes_are_what_a_module_script_reads_until_it_sets_its_own()
    {
        using var t = new Table();
        t.Server.State.Find(U, P)!.GmNotes = "the ford is mined";
        var r = ModuleTranslator.Translate("""
            function onLoad()
              self.setName(self.getGMNotes())
              self.setGMNotes("cleared")
              self.setDescription(self.getGMNotes())
            end
            """, new TranslateContext { ResolveImage = (url, type, scale) => "marker" });

        t.Ok(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });

        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(("the ford is mined", "cleared"), (p.Label, p.Note));
    }
}
