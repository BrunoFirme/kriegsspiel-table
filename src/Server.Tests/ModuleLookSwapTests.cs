using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleLookSwapTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    const string LineLua = "function onLoad(saved)\n  self.setDescription('line ' .. saved)\nend\n"
                           + "function onSave()\n  return 'kept ' .. self.getDescription()\nend\n"
                           + "function onDestroy()\n  print('ev left ' .. self.getGUID())\nend\n";

    const string ColumnLua = "function onLoad(saved)\n  self.setDescription('column ' .. saved)\nend\n"
                             + "function onStateChange(old)\n  print('ev own ' .. old)\nend\n";

    const string GlobalLua = "function onObjectDestroy(o) print('ev destroy ' .. o.getGUID()) end\n"
                             + "function onObjectSpawn(o) print('ev spawn ' .. o.getGUID()) end\n"
                             + "function onObjectStateChange(o, old) print('ev state ' .. o.getGUID() .. ' from ' .. old) end\n";

    static string Lua(string lua) => ModuleTranslator.Translate(lua, new TranslateContext()).Script;

    static GameState Looks(Table t, string? columnScript)
    {
        var def = t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf");
        def.States = new()
        {
            new PieceStateDef { Name = "Line", Script = Lua(LineLua) },
            new PieceStateDef { Name = "Column", Script = columnScript ?? "" },
        };
        var state = Json.Clone(t.Server.State);
        var p = state.Find(U, P)!;
        (p.Guid, p.Label, p.ScriptState, p.Tags, p.Locked, p.Script) = ("aaa111", "1st Bn in line", "fresh", ["line"], false, null);
        p.Looks = [PieceLook.Of(p), new PieceLook { Guid = "bbb222", Label = "1st Bn in column", ScriptState = "marching", Tags = ["column"], Locked = true, Memo = "col" }];
        state.Script = ModuleTranslator.Translate(GlobalLua, new TranslateContext { IsGlobal = true }).Script;
        return state;
    }

    static PieceInstance Load(Table t, GameState state)
    {
        t.Server.LoadSave(state, new List<TurnRecord>(), "Loaded");
        Assert.DoesNotContain(t.Server.Log.SelectMany(c => c is Batch b ? b.Items : [c]).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        return t.Server.State.Find(U, P)!;
    }

    [Fact]
    public void A_module_piece_changing_look_wears_that_look_whole_its_name_tags_lock_memo_and_guid_and_keeps_the_look_it_left_as_it_was_left()
    {
        using var t = new Table();
        var p = Load(t, Looks(t, Lua(ColumnLua)));
        Assert.Equal("line fresh", p.Note);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(("1st Bn in column", "bbb222", true, "col"), (p.Label, p.Guid, p.Locked, p.Memo));
        Assert.Equal(["column"], p.Tags!);
        Assert.Equal("kept line fresh", p.Looks![0].ScriptState);
        Assert.Equal("1st Bn in line", p.Looks[0].Label);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 0 });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(("1st Bn in line", "aaa111", false), (p.Label, p.Guid, p.Locked));
        Assert.Equal("line kept line fresh", p.Note);
    }

    [Fact]
    public void Each_look_runs_its_own_script_and_a_look_with_none_runs_none()
    {
        using var t = new Table();
        Load(t, Looks(t, Lua(ColumnLua)));
        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        Assert.Equal("column marching", t.Server.State.Find(U, P)!.Note);

        using var bare = new Table();
        Load(bare, Looks(bare, null));
        bare.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        var q = bare.Server.State.Find(U, P)!;
        Assert.Null(q.Note);
        Assert.Equal("", bare.Store.Def(q.Def)!.ScriptFor(q));
    }

    [Fact]
    public void A_look_change_ends_the_leaving_look_and_spawns_the_arriving_one_then_tells_both_scripts_the_guid_it_came_from()
    {
        using var t = new Table();
        Load(t, Looks(t, Lua(ColumnLua)));
        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        Assert.Equal(["ev left aaa111", "ev destroy aaa111", "ev spawn bbb222", "ev own aaa111", "ev state bbb222 from aaa111"],
            t.Server.Scripts.Console.Where(l => l.StartsWith("ev ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Undoing_a_look_change_puts_back_the_look_it_left_with_its_script_state()
    {
        using var t = new Table();
        Load(t, Looks(t, Lua(ColumnLua)));
        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        var undo = t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log.Last(c => c is SetState).Seq });
        Assert.DoesNotContain(undo, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal((0, "1st Bn in line", "aaa111", false), (p.State, p.Label, p.Guid, p.Locked));
        Assert.Equal("line kept line fresh", p.Note);
        Assert.Equal("1st Bn in column", p.Looks![1].Label);
    }

    [Fact]
    public void A_save_from_before_looks_were_whole_keeps_each_look_s_guid_and_script_state()
    {
        var p = Json.Deserialize<PieceInstance>("""{"id":"p-1","def":"inf","guid":"aaa111","label":"Bn","stateGuids":["aaa111","bbb222"],"stateScripts":["a","b"]}""")!;
        Assert.Equal(["aaa111", "bbb222"], p.Looks!.Select(l => l.Guid));
        Assert.Equal(["a", "b"], p.Looks!.Select(l => l.ScriptState));
        Assert.DoesNotContain("stateGuids", Json.Serialize(p));
    }
}
