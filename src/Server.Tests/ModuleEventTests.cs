using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleEventTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    static void Load(Table t, string id, string lua, bool global = false)
    {
        var r = ModuleTranslator.Translate(lua, new TranslateContext { IsGlobal = global, ResolveImage = (url, type, scale) => "marker" });
        Assert.True(r.Parsed);
        var res = global ? t.Server.Handle(Table.Host, new SetGlobalScript { Text = r.Script }) : t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = id, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
    }

    [Fact]
    public void A_piece_script_hears_every_piece_picked_up_with_the_colour_first_and_the_piece_second()
    {
        using var t = new Table();
        Load(t, P, "function onObjectPickUp(color, obj) self.setName(color .. ' ' .. obj.getGUID()) end");
        t.Server.Handle(Table.Ump3, new HoldPiece { Layer = U, Id = Marker, Held = true });
        Assert.Equal("Red " + Marker, t.Server.State.Find(U, P)!.Label);
    }

    [Fact]
    public void Chat_reaches_piece_scripts_as_well_as_the_global_one()
    {
        using var t = new Table();
        Load(t, P, "function onChat(message, player) self.setName('heard ' .. message) end");
        t.Server.Handle(Table.Ump3, new Say { Text = "advance" });
        Assert.Equal("heard advance", t.Server.State.Find(U, P)!.Label);
    }

    [Fact]
    public void A_try_enter_function_that_answers_nothing_keeps_the_piece_out_and_true_lets_it_in()
    {
        using var t = new Table();
        t.Server.State.Find(U, P)!.Holds = true;
        Load(t, "", "function tryObjectEnterContainer(container, object) end", global: true);
        var kept = t.Server.Handle(Table.Host, new Stow { Layer = U, Holder = P, Id = Marker });
        Assert.Contains(kept.Select(o => o.Message).OfType<ErrorMessage>(), e => e.Text.Contains("keeps " + Marker + " out"));
        Load(t, "", "function tryObjectEnterContainer(container, object) return true end", global: true);
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new Stow { Layer = U, Holder = P, Id = Marker }), o => o.Message is ErrorMessage);
        Assert.Null(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void A_piece_script_asked_about_a_player_action_can_refuse_it_with_false()
    {
        using var t = new Table();
        Load(t, P, "function onPlayerAction(player, action, targets) return not (action == Player.Action.Delete and targets[1].getGUID() == 'p-000003') end");
        var refused = t.Server.Handle(Table.Host, new DeletePiece { Layer = U, Id = Marker });
        Assert.Contains(refused.Select(o => o.Message).OfType<ErrorMessage>(), e => e.Text.Contains("does not allow"));
        Assert.NotNull(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void An_input_function_hears_selected_true_while_typing_and_false_when_the_umpire_leaves_the_box()
    {
        using var t = new Table();
        Load(t, P, """
            function typed(obj, color, text, selected) log(text .. ' ' .. tostring(selected)) end
            function onLoad() self.createInput({ input_function = 'typed', function_owner = self, position = {0, 1, 0}, width = 400, height = 100, validation = 2, tab = 3 }) end
            """);
        var w = t.Server.State.Find(U, P)!.Widgets[0];
        Assert.Equal(("integer", "spaces"), (w.Validation, w.Tab));
        t.Server.Handle(Table.Ump3, new EditWidget { Layer = U, Id = P, Index = 1, Value = "4", Typing = true });
        t.Server.Handle(Table.Ump3, new EditWidget { Layer = U, Id = P, Index = 1, Value = "4" });
        Assert.Contains(t.Server.Scripts.Console, l => l == "4 true");
        Assert.Contains(t.Server.Scripts.Console, l => l == "4 false");
    }

    [Fact]
    public void A_save_answer_that_is_not_text_clears_the_saved_state_and_nothing_keeps_it()
    {
        Assert.Equal("", Saved("function onSave() return 5 end"));
        Assert.Equal("kept", Saved("function onSave() end"));
        Assert.Equal("new", Saved("function onSave() return 'new' end"));
    }

    static string Saved(string lua)
    {
        using var t = new Table();
        t.Server.State.Find(U, P)!.ScriptState = "kept";
        Load(t, P, lua + "\nfunction onLoad() self.setName('x') end");
        t.Server.Scripts.SaveAll();
        return t.Server.State.Find(U, P)!.ScriptState;
    }
}
