using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class TtsHelpersTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static string Run(Table t, string lua)
    {
        var r = TtsTranslator.Translate(lua, Ctx());
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        return t.Server.State.Find(U, P)!.Label ?? "";
    }

    [Fact]
    public void Table_colour_tag_and_script_state_helpers_answer_as_TTS_does()
    {
        const string lua = "function onLoad()\n  local t = copyLuaTable({a = {1, 2}, b = 'x'})\n  t.a[1] = 9\n  local h = colorToHex({r=1, g=0, b=0})\n  local c = hexToColor('#00ff00')\n"
                           + "  self.setLuaScriptState('{\"n\":1}')\n  self.addTag('alpha')\n  local any = self.hasAnyTag({'zeta', 'alpha'})\n  local all = #getAllTags()\n"
                           + "  self.setName(h .. '|' .. c.g .. '|' .. self.getLuaScriptState() .. '|' .. tostring(any) .. '|' .. all .. '|' .. t.a[1])\nend\n";
        using var t = new Table();
        Assert.Equal("ff0000|1|{\"n\":1}|true|1|9", Run(t, lua));
    }

    [Fact]
    public void Clearing_the_paint_erases_every_stroke_and_stop_all_ends_every_wait()
    {
        const string lua = "function onLoad()\n  Global.setVectorLines({ { points = { {0,1,0}, {5,1,5} }, color = {1,0,0} } })\n  local before = #board.strokes()\n"
                           + "  clearVectorPaint()\n  Wait.time(function() self.setName('late') end, 0.01)\n  Wait.stopAll()\n  self.setName(before .. '/' .. #board.strokes())\nend\n";
        using var t = new Table();
        var before = t.Server.State.Drawings.GetValueOrDefault(U)?.Count ?? 0;
        Assert.Equal($"{before + 1}/0", Run(t, lua));
        t.Server.Scripts.Tick(1.0);
        Assert.Equal($"{before + 1}/0", t.Server.State.Find(U, P)!.Label);
    }

    [Fact]
    public void A_turn_start_handler_runs_when_the_clock_advances()
    {
        const string lua = "function onPlayerTurnStart(c, p)\n  self.setName('start:' .. tostring(c))\nend\n";
        using var t = new Table();
        Assert.Equal("", Run(t, lua));
        var res = t.Server.Handle(Table.Host, new AdvanceClock());
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("start:White", t.Server.State.Find(U, P)!.Label);
    }
}
