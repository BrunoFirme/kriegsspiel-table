using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class StickyTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static void Load(Table t, string lua)
    {
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = ModuleTranslator.Translate(lua, Ctx()).Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
    }

    [Fact]
    public void A_module_script_reads_a_piece_as_sticky_until_it_turns_that_off()
    {
        using var t = new Table();
        Load(t, "function onLoad()\n  local was = self.sticky\n  self.sticky = false\n  self.setName(tostring(was) .. '/' .. tostring(self.sticky) .. '/' .. tostring(self.tooltip))\nend\n");
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(("true/false/true", true), (p.Label, p.NotSticky));
    }

    [Fact]
    public void Undo_gives_a_piece_back_its_stickiness()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Host, new SetPieceOption { Layer = U, Id = Marker, Name = PieceOptions.NotSticky, Value = true });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.True(t.Server.State.Find(U, Marker)!.NotSticky);
        t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq });
        Assert.False(t.Server.State.Find(U, Marker)!.NotSticky);
    }
}
