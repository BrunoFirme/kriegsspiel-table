using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleLookSwapTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    const string Lua = "function onLoad(saved)\n  self.setName('loaded ' .. saved)\nend\n"
                       + "function onSave()\n  return 'kept ' .. self.getName()\nend\n";

    static PieceInstance Ready(Table t)
    {
        var def = t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf");
        def.States = new()
        {
            new PieceStateDef { Name = "Line", Module = new ModuleStateData { Type = "Custom_Token", Guid = "aaa111", ScriptState = "line" } },
            new PieceStateDef { Name = "Column", Module = new ModuleStateData { Type = "Custom_Token", Guid = "bbb222", ScriptState = "column" } },
        };
        var p = t.Server.State.Find(U, P)!;
        p.ScriptState = "line";
        var script = ModuleTranslator.Translate(Lua, new TranslateContext()).Script;
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = script }), o => o.Message is ErrorMessage);
        Assert.Equal("loaded line", p.Label);
        return p;
    }

    [Fact]
    public void A_module_piece_changing_look_starts_that_look_s_own_script_state_and_keeps_the_one_it_leaves_as_the_game_swaps_objects()
    {
        using var t = new Table();
        var p = Ready(t);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        Assert.Equal("loaded column", p.Label);
        Assert.Equal(new[] { "kept loaded line", "column" }, p.StateScripts!);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 0 });
        Assert.Equal("loaded kept loaded line", p.Label);
        Assert.Equal("kept loaded column", p.StateScripts![1]);

        var undo = t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log.Last(c => c is SetState).Seq });
        Assert.DoesNotContain(undo, o => o.Message is ErrorMessage);
        Assert.Equal(1, p.State);
        Assert.EndsWith("loaded column", p.Label);
    }
}
