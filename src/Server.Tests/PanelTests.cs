using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class PanelTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    [Fact]
    public void Panels_come_from_xml_and_events_reach_the_global_script()
    {
        using var t = new Table();
        const string script = """
            function on_load(s)
              ui.set([[<Panel id="main" width="300" height="200" color="#202020"><VerticalLayout spacing="4"><Text id="title" fontSize="18">Orders</Text><Button id="go" onClick="go">Go</Button><InputField id="name" onEndEdit="named" text="x"/><Toggle id="t" onValueChanged="tog" isOn="False">Lock</Toggle></VerticalLayout></Panel>]])
            end
            function go(who, value, id) ui.set_attribute("title", "text", "Gone " .. value .. " " .. who.name .. " " .. id) end
            function named(who, value, id) ui.set_value("title", "Named " .. value) end
            function tog(who, value, id) ui.set_attribute("title", "text", "Toggled " .. value) end
            """;
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        var ui = t.Server.State.Ui;
        var panel = Assert.Single(ui);
        Assert.Equal("Panel", panel.Tag);
        Assert.Equal("300", panel.Attributes["width"]);
        Assert.Equal("Orders", t.Server.State.UiNode("title")!.Attributes["text"]);
        Assert.Equal("18", t.Server.State.UiNode("title")!.Attributes["fontSize"]);
        Assert.Single(Flat(t.Server.Log).OfType<SetUi>());
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "go", Event = "click", Value = "-1" });
        Assert.Equal("Gone -1 Umpire Three go", t.Server.State.UiNode("title")!.Attributes["text"]);
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "name", Event = "end", Value = "Blue Corps" });
        Assert.Equal("Blue Corps", t.Server.State.UiNode("name")!.Attributes["text"]);
        Assert.Equal("Named Blue Corps", t.Server.State.UiNode("title")!.Attributes["text"]);
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "t", Event = "change", Value = "True" });
        Assert.Equal("True", t.Server.State.UiNode("t")!.Attributes["isOn"]);
        Assert.Equal("Toggled True", t.Server.State.UiNode("title")!.Attributes["text"]);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) ui.set(ui.xml() .. '<Text id=\"more\">More</Text>') ui.hide('main') end" });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Equal(2, t.Server.State.Ui.Count);
        Assert.Equal("More", t.Server.State.UiNode("more")!.Attributes["text"]);
        Assert.Equal("Blue Corps", t.Server.State.UiNode("name")!.Attributes["text"]);
        Assert.Equal("false", t.Server.State.UiNode("main")!.Attributes["active"]);
        Assert.Contains(res, o => o.PeerId == Table.Obs && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<SetUi>().Any());
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
    }

    [Fact]
    public void A_handler_named_guid_slash_fn_runs_in_that_piece()
    {
        using var t = new Table();
        t.Server.State.Find(U, P)!.Guid = "abc123";
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function hit(who, value, id) self.set_text('name', 'hit ' .. id .. ' by ' .. who.name) end" });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) ui.set({ tag = 'Button', attributes = { id = 'b', onClick = 'abc123/hit' }, value = 'Hit' }) end" });
        Assert.Equal("Hit", t.Server.State.UiNode("b")!.Attributes["text"]);
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "b", Event = "click", Value = "-1" });
        Assert.Equal("hit b by Umpire Three", t.Server.State.Find(U, P)!.Text["name"]);
    }

    [Fact]
    public void A_dialog_goes_to_one_umpire_and_its_answer_to_the_script()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = """
            function on_load(s) ui.set('<Button id="ask" onClick="ask">Ask</Button>') end
            function ask(who, value, id)
              game.dialog(who, { kind = "input", title = "Name?", text = "x" }, function(answer, w) game.say("got " .. tostring(answer) .. " from " .. w.name) end)
            end
            """ });
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "ask", Event = "click", Value = "-1" });
        var dlg = Assert.Single(Flat(t.Server.Log).OfType<ShowDialog>());
        Assert.Equal($"p:{Table.Ump3}", dlg.To);
        Assert.Equal("input", dlg.Kind);
        Assert.Equal("Name?", dlg.Title);
        t.Server.Handle(Table.Ump3, new DialogResult { Token = dlg.Token, Value = "Blue" });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "got Blue from Umpire Three");
        t.Server.Handle(Table.Ump3, new DialogResult { Token = dlg.Token, Value = "again" });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.Contains("again"));
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "ask", Event = "click", Value = "-1" });
        var dlg2 = Flat(t.Server.Log).OfType<ShowDialog>().Last();
        t.Server.Handle(Table.Ump3, new DialogResult { Token = dlg2.Token, Value = null });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "got nil from Umpire Three");
    }

    [Fact]
    public void TTS_ui_calls_and_player_dialogs_translate()
    {
        var r = TtsTranslator.Translate("""
            function onLoad()
              UI.setXml([[<Panel><Button id="b" onClick="]] .. self.getGUID() .. [[/pressed">Go</Button></Panel>]])
              UI.setAttribute("b", "text", "Stop")
              UI.show("b")
              local x = UI.getXml()
              Player["Red"].showMemoDialog("Edit", "a: 1", function(text, color) end)
              self.UI.setXmlTable({})
            end
            function pressed(player, value, id) print(player.color, value, id) end
            """);
        var s = r.Script;
        Assert.Contains("ui.set([[<Panel>", s);
        Assert.Contains("ui.set_attribute(\"b\", \"text\", \"Stop\")", s);
        Assert.Contains("ui.show(\"b\")", s);
        Assert.Contains("local x = ui.xml()", s);
        Assert.Contains("compat.player(\"Red\").showMemoDialog(", s);
        Assert.Contains("self.ui.set({})", s);
        Assert.Contains("function pressed(__who, __value, __id)", s);
        Assert.Contains("local player, value, id = compat.player(compat.color_of(__who)), __value, __id", s);
        Assert.DoesNotContain(r.Untranslated, u => u.What.StartsWith("object UI"));
    }

    [Fact]
    public void A_piece_carries_panels_of_its_own()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = """
            function on_load(s)
              self.ui.set([[<Panel width="1750" height="150" rotation="0 0 180"><GridLayout cellSize="150 150" spacing="50">
                <Image id="g1" image="pic-1" active="false"><Text id="g1t" fontSize="50">3</Text></Image>
                <Image id="g2" image=""><Text id="g2t"></Text></Image>
                <Button id="b" onClick="hit">x</Button></GridLayout></Panel>]])
              self.ui.set_attribute("g1", "active", "true")
              self.ui.set_attributes("g1t", { text = "4", color = "#ff0000" })
            end
            function hit(who, value, id) self.ui.set_value("g2t", "hit " .. id .. " by " .. who.name) end
            """ });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        var p = t.Server.State.Find(U, P)!;
        var panel = Assert.Single(p.Ui);
        Assert.Equal("1750", panel.Attributes["width"]);
        Assert.Empty(t.Server.State.Ui);
        Assert.Equal("true", t.Server.State.UiNode(U, P, "g1")!.Attributes["active"]);
        Assert.Equal("4", t.Server.State.UiNode(U, P, "g1t")!.Attributes["text"]);
        Assert.Equal("#ff0000", t.Server.State.UiNode(U, P, "g1t")!.Attributes["color"]);
        var set = Assert.Single(Flat(t.Server.Log).OfType<SetUi>());
        Assert.Equal(P, set.Piece);
        t.Server.Handle(Table.Ump3, new UiEvent { Layer = U, Piece = P, Id = "b", Event = "click", Value = "-1" });
        Assert.Equal("hit b by Umpire Three", t.Server.State.UiNode(U, P, "g2t")!.Attributes["text"]);
        Assert.Null(t.Server.State.UiNode("b"));
        Assert.Single(p.Clone().Ui);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
        t.Server.Handle(Table.Ump3, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Equal("", t.Server.State.UiNode(U, P, "g2t")!.Attributes.GetValueOrDefault("text", ""));
    }

    [Theory]
    [InlineData("local t = { tag = 'Panel' } t.children = { t } ui.set(t)", "nests deeper")]
    [InlineData("local a = { tag = 'Panel' } for i = 1, 22 do a = { tag = 'Panel', children = { a, a } } end ui.set(a)", "elements")]
    [InlineData("ui.set(string.rep('<Panel>', 100000) .. string.rep('</Panel>', 100000))", "nests deeper")]
    public void A_panel_tree_without_end_is_refused_not_walked_for_ever(string lua, string why)
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) " + lua + " end" });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error") && s.Text.Contains(why));
        Assert.Empty(t.Server.State.Ui);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) local a = { tag = 'Text', value = 'deep' } for i = 1, 12 do a = { tag = 'Panel', children = { a } } end ui.set(a) end" });
        Assert.Single(t.Server.State.Ui);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(Json.Clone(t.Server.State)));
    }

    [Fact]
    public void A_global_script_may_put_panels_on_a_scriptless_piece_and_answer_their_clicks()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = """
            function on_load(s)
              local p = board.find("p-000003")
              p.ui.set('<Panel width="400" height="100"><Button id="plate" onClick="plate_hit">Go</Button></Panel>')
            end
            function plate_hit(who, value, id) board.find("p-000003").ui.set_attribute("plate", "text", "Gone " .. id) end
            """ });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Single(t.Server.State.Find(U, "p-000003")!.Ui);
        t.Server.Handle(Table.Ump3, new UiEvent { Layer = U, Piece = "p-000003", Id = "plate", Event = "click", Value = "-1" });
        Assert.Equal("Gone plate", t.Server.State.UiNode(U, "p-000003", "plate")!.Attributes["text"]);
    }
}
