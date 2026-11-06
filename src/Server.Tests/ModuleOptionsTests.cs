using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleOptionsTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    [Fact]
    public void A_module_script_reads_and_sets_whether_a_box_takes_a_piece_and_whether_it_shows_its_move()
    {
        using var t = new Table();
        const string lua = "function onLoad()\n  local was = tostring(self.drag_selectable) .. '/' .. tostring(self.measure_movement)\n"
                           + "  self.drag_selectable = false\n  self.measure_movement = true\n"
                           + "  self.setName(was .. '/' .. tostring(self.drag_selectable) .. '/' .. tostring(self.measure_movement))\nend\n";
        var text = ModuleTranslator.Translate(lua, new TranslateContext()).Script;
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = text });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(("true/false/false/true", true, true), (p.Label, p.NotBoxSelected, p.MeasuresMoves));
    }
}
