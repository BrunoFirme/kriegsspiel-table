using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Panels;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class PanelTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });
    static List<Say> Said(Table t) => Flat(t.Server.Log).OfType<Say>().ToList();
    static double _now;

    static void Frame(Table t, int n = 1)
    {
        for (int i = 0; i < n; i++) t.Server.Scripts.Tick(_now += 0.02);
    }

    static Table Global(string script, int frames = 1)
    {
        var t = new Table();
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Frame(t, frames);
        return t;
    }

    static string Text(Table t) => t.Server.State.UiLayout!.ById("out")!.Shown ?? "";

    [Fact]
    public void Panels_land_on_the_next_frame_and_until_then_the_script_reads_the_old_ones()
    {
        using var t = Global("""
            function on_load(s)
              ui.set('<Text id="a">One</Text>')
              seen = tostring(ui.loading) .. "|" .. ui.xml() .. "|" .. tostring(ui.get_attribute("a", "id"))
            end
            function later() return tostring(ui.loading) .. "|" .. ui.xml() end
            """, frames: 0);
        Assert.Empty(t.Server.State.Ui);
        Frame(t);
        Assert.Equal("One", t.Server.State.Ui[0].Value);
        Assert.Equal("a", t.Server.State.UiLayout!.ById("a")!.Get("id"));
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) ui.set_value('a', tostring(ui.loading) .. ' ' .. tostring(ui.get_value('a'))) end" });
        Frame(t, 2);
        Assert.Equal("false One", t.Server.State.Ui[0].Value);
    }

    [Fact]
    public void A_change_to_an_element_only_the_new_panels_hold_is_refused_and_one_both_hold_lands_after_the_load()
    {
        using var t = Global("""
            function on_load(s)
              ui.set('<Text id="old">x</Text><Text id="both">x</Text>')
            end
            function swap()
              ui.set('<Text id="new">x</Text><Text id="both">x</Text>')
              answers = tostring(ui.set_attribute("new", "color", "red")) .. tostring(ui.set_attribute("both", "color", "blue"))
              return answers
            end
            """);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = t.Server.State.Script + "\nfunction on_chat(m) game.say(swap()) end" });
        Frame(t);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Contains(Said(t), s => s.Text == "falsetrue");
        Frame(t);
        Assert.Null(t.Server.State.UiLayout!.ById("both")!.Get("color"));
        Frame(t);
        Assert.Equal("blue", t.Server.State.UiLayout!.ById("both")!.Get("color"));
        Assert.Null(t.Server.State.UiLayout!.ById("new")!.Get("color"));
    }

    [Fact]
    public void A_change_lands_when_the_call_ends_so_the_same_call_still_reads_the_old_value()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Text id="a" color="red"/><Text id="out"/>') end
            function on_chat(m)
              ui.set_attribute("a", "color", "blue")
              ui.set_value("out", ui.get_attribute("a", "color"))
            end
            """);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Equal("red", Text(t));
        Assert.Equal("blue", t.Server.State.UiLayout!.ById("a")!.Get("color"));
    }

    [Fact]
    public void Attributes_read_the_live_element_with_its_defaults_and_values_read_the_written_panels()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Button id="b">Go</Button><Text id="t">Hi</Text><Text id="out"/>') end
            function on_chat(m)
              ui.set_value("out", ui.get_attribute("b", "onClickSound") .. "|" .. ui.get_attribute("b", "text") .. "|" .. tostring(ui.get_attribute("t", "text"))
                .. "|" .. ui.get_value("b") .. "|" .. ui.get_value("t") .. "|" .. tostring(ui.get_attributes("nope")))
            end
            """);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Equal("Defaults/Sounds/ClickUI|Go|nil|Go|Hi|nil", Text(t));
    }

    [Fact]
    public void Booleans_and_numbers_given_to_an_element_become_the_words_the_tabletop_writes()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Text id="a"/>') end
            function on_chat(m) ui.set_attributes("a", { active = false, fontSize = 14, scale = 0.5 }) ui.set_attribute("a", "raycastTarget", true) end
            """);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        var a = t.Server.State.UiLayout!.ById("a")!;
        Assert.Equal("False", a.Get("active"));
        Assert.Equal("14", a.Get("fontSize"));
        Assert.Equal("0.5", a.Get("scale"));
        Assert.Equal("True", a.Get("raycastTarget"));
        Assert.Equal(PanelOps.Off, a.Active);
    }

    [Fact]
    public void Panels_given_as_tables_must_be_a_list_and_their_booleans_are_written_in_lower_case()
    {
        using var t = Global("""
            function on_load(s)
              single = ui.set({ tag = "Text", attributes = { id = "x" } })
              ui.set({ { tag = "Text", value = 5, attributes = { id = "a", active = false, fontSize = 12 } }, { tag = "Panel", children = { tag = "Image", attributes = { id = "i" } } } })
            end
            function on_chat(m) game.say(tostring(single)) end
            """);
        Assert.Contains(Said(t), s => s.Text.Contains("table was null"));
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Contains(Said(t), s => s.Text == "false");
        Assert.Equal("false", t.Server.State.Ui[0].Attributes["active"]);
        Assert.Equal("5", t.Server.State.Ui[0].Value);
        Assert.NotNull(t.Server.State.UiLayout!.ById("i"));
    }

    [Fact]
    public void A_click_on_an_element_without_an_id_still_calls_its_function_with_the_pointer_and_no_id()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Panel><Button onClick="go">Go</Button><Button onClick="go(left)">Two</Button></Panel><Text id="out"/>') end
            function go(who, value, id) ui.set_value("out", who.name .. " " .. tostring(value) .. " " .. tostring(id)) end
            """);
        t.Server.Handle(Table.Ump3, new UiEvent { Path = new() { 0, 0 }, Event = "onClick", Value = "-2" });
        Frame(t);
        Assert.Equal("Umpire Three -2 nil", Text(t));
        t.Server.Handle(Table.Ump3, new UiEvent { Path = new() { 0, 1 }, Event = "onClick", Value = "-1" });
        Frame(t);
        Assert.Equal("Umpire Three left nil", Text(t));
    }

    [Fact]
    public void A_piece_s_panel_calls_its_own_script_only_and_reaches_the_global_one_through_global_slash()
    {
        using var t = new Table();
        t.Server.State.Find(U, P)!.Guid = "abc123";
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function hit(who, value, id) self.set_text('name', 'piece ' .. id) end" });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = """
            function on_load(s)
              board.find("p-000003").ui.set('<Button id="lost" onClick="hit">x</Button><Button id="glob" onClick="Global/hit">y</Button><Button id="far" onClick="abc123/hit">z</Button><Button id="gone" onClick="zzz/hit">w</Button>')
            end
            function hit(who, value, id) game.say("global " .. id) end
            """ });
        Frame(t);
        foreach (var id in new[] { "lost", "glob", "far", "gone" })
            t.Server.Handle(Table.Ump3, new UiEvent { Layer = U, Piece = "p-000003", Id = id, Event = "onClick", Value = "-1" });
        Assert.DoesNotContain(Said(t), s => s.Text == "global lost");
        Assert.Contains(Said(t), s => s.Text == "global glob");
        Assert.Equal("piece far", t.Server.State.Find(U, P)!.Text["name"]);
        Assert.Contains(Said(t), s => s.Text == "Xml UI couldn't find Object with guid: zzz");
    }

    [Fact]
    public void Changed_values_arrive_as_the_tabletop_words_them_for_each_kind_of_element()
    {
        using var t = Global("""
            function on_load(s)
              ui.set([[<Toggle id="t" onValueChanged="got">Lock</Toggle><Dropdown id="d" onValueChanged="got"><Option>Red</Option><Option>Blue</Option></Dropdown>
                <Dropdown id="di" onValueChanged="got(selectedIndex)"><Option>A</Option></Dropdown><InputField id="i" onValueChanged="got" onEndEdit="got(done)"/>
                <Slider id="s" onValueChanged="got"/><Text id="out"/>]])
            end
            function got(who, value, id) ui.set_value("out", id .. "=" .. tostring(value)) end
            """);
        void Send(string id, string ev, string value, string want)
        {
            t.Server.Handle(Table.Ump3, new UiEvent { Id = id, Event = ev, Value = value });
            Frame(t);
            Assert.Equal(want, Text(t));
        }
        Send("t", "onValueChanged", "True", "t=True");
        Send("d", "onValueChanged", "1", "d=Blue");
        Send("di", "onValueChanged", "0", "di=0");
        Send("i", "onValueChanged", "Blu", "i=Blu");
        Send("i", "onEndEdit", "Blue", "i=done");
        Send("s", "onValueChanged", "0.25", "s=0.25");
    }

    [Fact]
    public void What_someone_types_or_ticks_stays_on_their_screen_and_does_not_change_the_shared_panels()
    {
        using var t = Global("function on_load(s) ui.set('<Toggle id=\"t\" isOn=\"false\" onValueChanged=\"x\"/><InputField id=\"i\" text=\"a\"/>') end");
        var before = Json.Serialize(t.Server.State.UiLayout);
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "t", Event = "onValueChanged", Value = "True" });
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "i", Event = "onEndEdit", Value = "typed" });
        Assert.Equal(before, Json.Serialize(t.Server.State.UiLayout));
        Assert.Equal("false", t.Server.State.Ui[0].Attributes["isOn"]);
    }

    [Fact]
    public void Panels_that_do_not_build_keep_the_old_ones_say_why_and_stay_loading()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Text id="a">x</Text>') end
            function on_chat(m)
              if m == "break" then ui.set('<Panel><Text>') else game.say(tostring(ui.loading) .. " " .. tostring(ui.set_attribute("a", "color", "red"))) end
            end
            """);
        t.Server.Handle(Table.Ump3, new Say { Text = "break" });
        Frame(t, 3);
        Assert.Contains(Said(t), s => s.Text.StartsWith("Error Building Xml UI on Global: "));
        Assert.Equal("x", t.Server.State.Ui[0].Value);
        t.Server.Handle(Table.Ump3, new Say { Text = "ask" });
        Frame(t, 3);
        Assert.Contains(Said(t), s => s.Text == "true false");
        Assert.Null(t.Server.State.UiLayout!.ById("a")?.Get("color"));
    }

    [Fact]
    public void An_unknown_tag_and_a_class_set_as_an_attribute_are_named_in_the_chat()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Fancy/><Text id="a"/>') end
            function on_chat(m) ui.set_attribute("a", "class", "big") end
            """);
        Assert.Contains(Said(t), s => s.Text.StartsWith("[XmlLayout] Unknown tag 'Fancy'."));
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Contains(Said(t), s => s.Text == PanelOps.ClassWarning);
        Assert.Equal("big", t.Server.State.Ui[1].Attributes["class"]);
    }

    [Fact]
    public void New_pictures_are_listed_at_once_and_build_the_panels_again_with_earlier_changes_landing_after()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Image id="i" image="crest"/><Text id="out"/>') end
            function on_chat(m)
              ui.set_value("out", "changed")
              ui.set_pictures({ { name = "crest", url = "pic-1", type = ui.assetType.image } })
              local p = ui.pictures()
              game.say(p[1].name .. " " .. p[1].url .. " " .. p[1].type .. " " .. ui.assetType.AssetBundle)
            end
            """);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Contains(Said(t), s => s.Text == "crest pic-1 0 1");
        Assert.Null(t.Server.State.UiLayout!.ById("out")!.Shown);
        Frame(t);
        Assert.Null(t.Server.State.UiLayout!.ById("out")!.Shown);
        Assert.Equal("pic-1", Assert.Single(t.Server.State.UiAssets!).Url);
        Frame(t);
        Assert.Equal("changed", t.Server.State.UiLayout!.ById("out")!.Shown);
        Assert.Equal("changed", t.Server.State.Ui[1].Value);
    }

    [Fact]
    public void A_piece_carries_panels_of_its_own_and_undo_gives_back_the_element_as_it_was()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = """
            function on_load(s) self.ui.set('<Panel width="100"><Image id="g1" active="false"><Text id="g1t">3</Text></Image><Button id="b" onClick="hit">x</Button></Panel>') end
            function hit(who, value, id) self.ui.show("g1") self.ui.set_attributes("g1t", { color = "#ff0000" }) end
            """ });
        Frame(t);
        var p = t.Server.State.Find(U, P)!;
        Assert.Empty(t.Server.State.Ui);
        Assert.Equal(PanelOps.Off, p.UiLayout!.ById("g1")!.Active);
        t.Server.Handle(Table.Ump3, new UiEvent { Layer = U, Piece = P, Id = "b", Event = "onClick", Value = "-1" });
        Assert.Equal(PanelOps.On, p.UiLayout!.ById("g1")!.Active);
        Assert.Equal("#ff0000", p.UiLayout!.ById("g1t")!.Get("color"));
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
        t.Server.Handle(Table.Ump3, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        p = t.Server.State.Find(U, P)!;
        Assert.Null(p.UiLayout!.ById("g1t")!.Get("color"));
        Assert.Equal(PanelOps.Off, p.UiLayout!.ById("g1")!.Active);
    }

    [Theory]
    [InlineData("local t = { tag = 'Panel' } t.children = { t } ui.set({ t })", "nests deeper")]
    [InlineData("local a = { tag = 'Panel' } for i = 1, 22 do a = { tag = 'Panel', children = { a, a } } end ui.set({ a })", "elements")]
    public void A_panel_table_without_end_is_refused_not_walked_for_ever(string lua, string why)
    {
        using var t = Global("function on_load(s) " + lua + " end");
        Assert.Contains(Said(t), s => s.Text.StartsWith("Script error") && s.Text.Contains(why));
        Assert.Empty(t.Server.State.Ui);
    }

    [Fact]
    public void Panel_xml_nested_without_end_is_refused_when_it_lands()
    {
        using var t = Global("function on_load(s) ui.set(string.rep('<Panel>', 100000) .. string.rep('</Panel>', 100000)) end");
        Assert.Contains(Said(t), s => s.Text.StartsWith("Error Building Xml UI") && s.Text.Contains("nest deeper"));
        Assert.Empty(t.Server.State.Ui);
    }

    [Fact]
    public void A_dialog_goes_to_one_umpire_and_its_answer_to_the_script()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Button id="ask" onClick="ask">Ask</Button>') end
            function ask(who, value, id)
              game.dialog(who, { kind = "input", title = "Name?", text = "x" }, function(answer, w) game.say("got " .. tostring(answer) .. " from " .. w.name) end)
            end
            """);
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "ask", Event = "click", Value = "-1" });
        var dlg = Assert.Single(Flat(t.Server.Log).OfType<ShowDialog>());
        Assert.Equal($"p:{Table.Ump3}", dlg.To);
        t.Server.Handle(Table.Ump3, new DialogResult { Token = dlg.Token, Value = "Blue" });
        Assert.Contains(Said(t), s => s.Text == "got Blue from Umpire Three");
        t.Server.Handle(Table.Ump3, new DialogResult { Token = dlg.Token, Value = "again" });
        Assert.DoesNotContain(Said(t), s => s.Text.Contains("again"));
    }

    [Fact]
    public void Module_ui_calls_and_player_dialogs_translate()
    {
        var r = ModuleTranslator.Translate("""
            function onLoad()
              UI.setXml([[<Panel><Button id="b" onClick="]] .. self.getGUID() .. [[/pressed">Go</Button></Panel>]])
              UI.setAttribute("b", "text", "Stop")
              UI.show("b")
              local x = UI.getXml()
              local busy = UI.loading
              Player["Red"].showMemoDialog("Edit", "a: 1", function(text, color) end)
              self.UI.setXmlTable({})
              self.UI.setXml("<Text/>", {})
            end
            function pressed(player, value, id) print(player.color, value, id) end
            """);
        var s = r.Script;
        Assert.Contains("ui.set([[<Panel>", s);
        Assert.Contains("ui.set_attribute(\"b\", \"text\", \"Stop\")", s);
        Assert.Contains("ui.show(\"b\")", s);
        Assert.Contains("local x = ui.xml()", s);
        Assert.Contains("local busy = ui.loading", s);
        Assert.Contains("compat.player(\"Red\").showMemoDialog(", s);
        Assert.Contains("self.ui.set({})", s);
        Assert.Contains("self.ui.set(\"<Text/>\", {})", s);
        Assert.Contains("local player, value, id = compat.player(compat.color_of(__who)), __value, __id", s);
        Assert.DoesNotContain(r.Untranslated, u => u.What.StartsWith("object UI") || u.What.StartsWith("UI"));
    }

    [Fact]
    public void A_global_xml_set_with_pictures_takes_both_at_once()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Image id="i" image="crest"/>', { { name = "crest", url = "pic-2" } }) end
            """);
        Assert.Equal("crest", t.Server.State.UiLayout!.ById("i")!.Get("image"));
        Assert.Equal("pic-2", Assert.Single(t.Server.State.UiAssets!).Url);
    }

    [Fact]
    public void The_panels_as_tables_hold_tag_value_attributes_and_children_with_no_value_where_none_was_written()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Panel id="p" width="4"><Text id="t">Hi</Text></Panel>') end
            function on_chat(m)
              local tree = ui.tree()
              local p, c = tree[1], tree[1].children[1]
              game.say(p.tag .. "|" .. tostring(p.value) .. "|" .. p.attributes.width .. "|" .. #p.children .. "|" .. c.tag .. "|" .. c.value .. "|" .. c.attributes.id .. "|" .. #c.children)
            end
            """);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Contains(Said(t), s => s.Text == "Panel|nil|4|1|Text|Hi|t|0");
    }

    [Fact]
    public void A_panel_event_with_no_id_or_name_is_passed_over()
    {
        using var t = Global("function on_load(s) ui.set('<Button id=\"b\" onClick=\"go\">Go</Button>') end function go() game.say('went') end");
        t.Server.Handle(Table.Ump3, new UiEvent { Id = null!, Event = null!, Value = null! });
        t.Server.Handle(Table.Ump3, new UiEvent { Id = "b", Event = null!, Value = null! });
        t.Server.Handle(Table.Ump3, new UiEvent { Path = new() { 7, 3 }, Event = "onClick", Value = "-1" });
        Assert.DoesNotContain(Said(t), s => s.Text.StartsWith("Script error") || s.Text == "went");
    }

    [Fact]
    public void Panels_whose_pictures_name_a_type_the_tabletop_does_not_load_never_land()
    {
        using var t = Global("""
            function on_load(s) ui.set('<Text id="a"/>', { { name = "odd", url = "pic", type = 5 } }) end
            function on_chat(m) game.say(tostring(ui.loading)) end
            """, frames: 4);
        Assert.Empty(t.Server.State.Ui);
        t.Server.Handle(Table.Ump3, new Say { Text = "go" });
        Assert.Contains(Said(t), s => s.Text == "true");
    }
}
