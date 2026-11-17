using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.Server.Tests;

public class ScriptsOffTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    const string Busy = """
        function on_load(saved)
          self.set_counter("str", self.counters.str + 1)
          self.clear_widgets()
          self.button{ face = "back", x = 0.1, z = 0.2, w = 0.2, h = 0.1, label = "Loaded " .. (saved or ""), on_click = "up" }
        end
        function up() self.set_counter("str", self.counters.str + 1) end
        function on_update() self.set_counter("ammo", 1) end
        function on_save() return "kept" end
        """;

    static PieceInstance Piece(GameServer s) => s.State.Find(U, P)!;

    static Table Scripted()
    {
        var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = Busy });
        t.Server.Handle(Table.Host, new SetCounter { Layer = U, Id = P, Name = "ammo", Value = 0 });
        t.Server.Scripts.Save();
        Assert.Equal("kept", Piece(t.Server).ScriptState);
        return t;
    }

    [Fact]
    public void With_scripts_off_no_script_is_loaded_and_on_load_is_not_called()
    {
        using var t = Scripted();
        int str = Piece(t.Server).Counters["str"];

        t.Server.Scripts.Off = true;
        var started = t.Server.Scripts.Load();

        Assert.Empty(started);
        Assert.Equal(0, t.Server.Scripts.Loaded);
        Assert.Equal(str, Piece(t.Server).Counters["str"]);
        Assert.Equal("Loaded ", Assert.Single(Piece(t.Server).Widgets).Label);
        Assert.Equal(ScriptInfo.OffStatus, Assert.Single(t.Server.Scripts.Describe()).Status);
    }

    [Fact]
    public void With_scripts_off_a_click_a_tick_and_a_console_line_run_nothing()
    {
        using var t = Scripted();
        t.Server.Scripts.Off = true;
        t.Server.Scripts.Load();
        int str = Piece(t.Server).Counters["str"];

        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var ticked = t.Server.Scripts.Tick(1);
        t.Server.Scripts.RunSnippet(Table.Host, P, "self.set_counter('str', 1)");
        t.Server.Handle(Table.Host, new AdvanceClock());

        Assert.Empty(ticked);
        Assert.Equal(str, Piece(t.Server).Counters["str"]);
        Assert.Equal(0, Piece(t.Server).Counters["ammo"]);
        Assert.Equal("scripts are off", t.Server.Scripts.Feed[^1].Text);
    }

    [Fact]
    public void A_script_edited_while_scripts_are_off_is_stored_and_not_run()
    {
        using var t = Scripted();
        t.Server.Scripts.Off = true;
        t.Server.Scripts.Load();
        int str = Piece(t.Server).Counters["str"];

        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = Busy + "\n" });

        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal(Busy + "\n", Piece(t.Server).Script);
        Assert.Equal(0, t.Server.Scripts.Loaded);
        Assert.Equal(str, Piece(t.Server).Counters["str"]);
    }

    [Fact]
    public void A_game_saved_with_scripts_off_keeps_its_scripts_and_runs_them_when_opened_as_usual()
    {
        using var t = Scripted();
        t.Server.Scripts.Off = true;
        t.Server.Scripts.Load();
        int str = Piece(t.Server).Counters["str"];
        t.Server.Scripts.SaveAll();
        var path = Path.Combine(t.Root, "off.kstable");
        t.Server.SaveTo(path);

        var again = SaveLoader.Load(path, t.Store);
        Assert.Equal(Busy, Piece(again).Script);
        Assert.Equal("kept", Piece(again).ScriptState);
        Assert.Single(Piece(again).Widgets);
        Assert.False(again.Scripts.Off);

        again.Scripts.Load();

        Assert.Equal(1, again.Scripts.Loaded);
        Assert.Equal(str + 1, Piece(again).Counters["str"]);
        Assert.Equal("Loaded kept", Assert.Single(Piece(again).Widgets).Label);
        again.Scripts.Tick(1);
        Assert.Equal(1, Piece(again).Counters["ammo"]);
    }

    [Fact]
    public void A_save_loaded_at_the_table_while_scripts_are_off_starts_no_script()
    {
        using var t = Scripted();
        int str = Piece(t.Server).Counters["str"];
        var save = t.Server.ToSave();
        t.Server.Scripts.Off = true;

        t.Server.LoadSave(save.Snapshot, save.Turns, "again");

        Assert.Equal(0, t.Server.Scripts.Loaded);
        Assert.Equal(str, Piece(t.Server).Counters["str"]);
        Assert.Empty(t.Server.Scripts.Tick(1));
    }
}
