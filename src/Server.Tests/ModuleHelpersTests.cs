using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleHelpersTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static string Run(Table t, string lua)
    {
        var r = ModuleTranslator.Translate(lua, Ctx());
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        return t.Server.State.Find(U, P)!.Label ?? "";
    }

    [Fact]
    public void Tag_and_script_state_helpers_answer_as_modules_expect()
    {
        const string lua = """
            function onLoad()
              local before = self.hasAnyTag()
              self.script_state = '{"n":1}'
              self.addTag('alpha')
              local any = self.hasAnyTag()
              local all = #getAllTags()
              self.setName(self.script_state .. '|' .. tostring(before) .. '|' .. tostring(any) .. '|' .. all)
            end
            """;
        using var t = new Table();
        Assert.Equal("{\"n\":1}|false|true|1", Run(t, lua));
    }

    [Fact]
    public void Helper_names_the_tabletop_never_installs_are_not_there_for_a_module()
    {
        const string lua = """
            function onLoad()
              local t = {}
              local names = {'copyLuaTable', 'colorToHex', 'hexToColor', 'stringColorToHex', 'constructColor', 'constructVector', 'getGlobalVar', 'getGlobalTable', 'setGlobalTable', 'logFormat', 'getScriptState'}
              for _, n in ipairs(names) do
                t[#t + 1] = tostring(pcall(function() return _ENV[n]() end))
              end
              self.setName(table.concat(t, ','))
            end
            """;
        using var t = new Table();
        Assert.Equal(string.Join(",", Enumerable.Repeat("false", 11)), Run(t, lua));
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
