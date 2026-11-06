using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class BoardDrawingTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    [Fact]
    public void A_script_draws_on_the_board_lists_the_lines_and_erases_one()
    {
        using var t = new Table();
        const string lua = "function on_load()\n  local id = board.draw({ points = { {x=1,z=1}, {x=4,z=1} }, color = '#FF0000', width = 0.3 })\n"
                           + "  local n = #board.strokes()\n  board.erase(id)\n  self.set_name(tostring(id ~= nil) .. '/' .. n .. '/' .. #board.strokes())\nend\n";
        var before = t.Server.State.Drawings.GetValueOrDefault(U)?.Count ?? 0;
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = lua });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal($"true/{before + 1}/{before}", t.Server.State.Find(U, P)!.Label);
    }

    [Fact]
    public void A_module_global_script_sets_and_reads_the_table_s_vector_lines()
    {
        using var t = new Table();
        const string lua = "function onLoad()\n  Global.setVectorLines({ { points = { {0,1,0}, {5,1,5} }, color = {1,0,0}, thickness = 0.2, loop = true } })\n"
                           + "  local n = #Global.getVectorLines()\n  Global.setVectorLines({ { points = { {2,1,2}, {3,1,3} }, color = {0,0,1} } })\n"
                           + "  getObjectFromGUID('" + P + "').setName(n .. '/' .. #Global.getVectorLines())\nend\n";
        var r = ModuleTranslator.Translate(lua, new TranslateContext { IsGlobal = true, ResolveImage = (url, type, scale) => "marker" });
        Assert.Empty(r.Untranslated);
        var before = t.Server.State.Drawings.GetValueOrDefault(U)?.Count ?? 0;
        var res = t.Server.Handle(Table.Host, new SetGlobalScript { Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("1/1", t.Server.State.Find(U, P)!.Label);
        var strokes = t.Server.State.Drawings[U];
        Assert.Equal(before + 1, strokes.Count);
        var last = strokes[^1];
        Assert.Equal((new Vec2(2, 2), new Vec2(3, 3), "#0000FF"), (last.Points[0], last.Points[1], last.Color));
    }
}
