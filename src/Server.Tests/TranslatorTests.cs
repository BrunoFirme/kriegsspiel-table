using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class TranslatorTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "scripts", name));
    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static int WidgetIndex(PieceInstance p, Func<Widget, bool> pick)
    {
        for (int i = 0; i < p.Widgets.Count; i++) if (pick(p.Widgets[i])) return i + 1;
        throw new InvalidOperationException("no such widget");
    }

    [Fact]
    public void The_counter_script_translates_whole()
    {
        var r = ModuleTranslator.Translate(Fixture("unit_block.lua"), Ctx());
        Assert.True(r.Parsed);
        Assert.Empty(r.Untranslated);
        Assert.True(r.DirectCalls >= 2, "direct calls");
        Assert.True(r.CompatCalls > 10, "compat calls");
        var s = r.Script;
        Assert.Contains("function on_load(saved)", s);
        Assert.Contains("function on_save()", s);
        Assert.Contains("json.encode(state)", s);
        Assert.Contains("compat.create_button(self, markFor(form), _G)", s);
        Assert.Contains("compat.edit_button(self, {index = 1, label = ammo}, _G)", s);
        Assert.Contains("function ammoUp(__who, __alt, __index, __piece)", s);
        Assert.Contains("function rename(__who, __value, __index, __piece, __typing)", s);
        Assert.Contains("local obj, color, value = __piece or self, compat.color_of(__who), __value", s);
        Assert.Contains("compat.scale_by(self, 1.25)", s);
        Assert.Contains("compat.position_to_world(self, {0, -0.25, -1.75})", s);
        Assert.Contains("compat.spawn_object({ type = 'Custom_Token'", s);
        Assert.Contains("compat.set_name(tile, name)", s);
        Assert.Contains("image = \"marker\"", s);
        Assert.DoesNotContain("steamusercontent", s);
        Assert.Contains("---adjust these values", s);
        Assert.DoesNotContain("createButton", s.Replace("compat.create_button", ""));
    }

    [Fact]
    public void The_translated_counter_piece_behaves_as_in_its_module()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate(Fixture("unit_block.lua"), Ctx());
        var p = t.Server.State.Find(U, P)!;
        p.ScriptState = "{\"ammo\":9,\"morale\":9,\"strength\":9,\"form\":1,\"name\":\"Infantry\"}";
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Equal(16, p.Widgets.Count);
        Assert.Equal(15, p.Widgets.Count(w => w.Kind == "button"));
        Assert.Equal("Infantry", p.Widgets[0].Label);
        Assert.Equal("front", p.Widgets[0].Face);
        var ammo = WidgetIndex(p, w => w.Tooltip == "Ammo");
        Assert.Equal("back", p.Widgets[ammo - 1].Face);
        Assert.Equal("9", p.Widgets[ammo - 1].Label);
        Assert.Equal("fireSmoke", p.Widgets[ammo - 1].OnClick);
        Assert.Equal("#000000", p.Widgets[ammo - 1].Color);
        Assert.Equal("#FFFFFF", p.Widgets[ammo - 1].TextColor);
        Assert.Equal(-0.5, p.Widgets[ammo - 1].X, 3);
        Assert.Equal(53 * 0.00624 * 2.5 * 0.5, p.Widgets[ammo - 1].W, 6);
        var minus = WidgetIndex(p, w => w.OnClick == "ammoDown");
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = minus });
        Assert.Equal("8", p.Widgets[ammo - 1].Label);
        var square = WidgetIndex(p, w => w.OnClick == "formSquare");
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = square });
        var mark = WidgetIndex(p, w => w.Label == "■");
        Assert.Equal("front", p.Widgets[mark - 1].Face);
        Assert.Equal("#FF0000", p.Widgets[square - 1].Color);
        Assert.Equal("#000000", p.Widgets[WidgetIndex(p, w => w.OnClick == "formLine") - 1].Color);
        int before = t.Server.State.Layer(U).Count;
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = ammo });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Equal(before + 1, t.Server.State.Layer(U).Count);
        var smoke = t.Server.State.Layer(U).Last();
        Assert.Equal("marker", smoke.Def);
        Assert.Equal("7", p.Widgets[ammo - 1].Label);
        Assert.NotEqual(p.Pos, smoke.Pos);
        var mp = WidgetIndex(p, w => w.OnClick == "strengthUp");
        p.ScriptState = "";
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = mp });
        Assert.Equal(1.0, p.Scale);
        var input = WidgetIndex(p, w => w.Kind == "input");
        t.Server.Handle(Table.Ump3, new EditWidget { Layer = U, Id = P, Index = input, Value = "1st Foot" });
        Assert.Equal("Infantry", p.Widgets[0].Label);
        t.Server.Handle(Table.Ump3, new EditWidget { Layer = U, Id = P, Index = input, Value = "2nd Foot" });
        Assert.Equal("1st Foot", p.Widgets[0].Label);
        t.Server.Scripts.Save();
        Assert.Contains("\"ammo\":7", p.ScriptState);
        Assert.Contains("\"name\":\"2nd Foot\"", p.ScriptState);
        Assert.Contains("\"form\":2", p.ScriptState);
    }

    [Fact]
    public void Widget_geometry_is_in_the_pieces_own_units_whatever_its_scale()
    {
        var geometry = new List<(double x, double z, double w, double font)>();
        foreach (var scale in new[] { 1.0, 0.64, 2.5 })
        {
            using var t = new Table();
            var r = ModuleTranslator.Translate(Fixture("unit_block.lua"), Ctx());
            var p = t.Server.State.Find(U, P)!;
            p.Scale = scale;
            p.ScriptState = "{\"ammo\":9,\"morale\":9,\"strength\":9,\"form\":1,\"name\":\"Infantry\"}";
            var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
            Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
            var ammo = p.Widgets[WidgetIndex(p, w => w.Tooltip == "Ammo") - 1];
            geometry.Add((ammo.X, ammo.Z, ammo.W, ammo.Font));
        }
        Assert.All(geometry, g => Assert.Equal(geometry[0], g));
        Assert.Equal(-0.5, geometry[0].x, 3);
        Assert.Equal(53 * 0.00624 * 2.5 * 0.5, geometry[0].w, 6);
    }

    [Fact]
    public void A_name_typed_letter_by_letter_reaches_the_face()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate(Fixture("unit_block.lua"), Ctx());
        var p = t.Server.State.Find(U, P)!;
        p.ScriptState = "{\"ammo\":9,\"morale\":9,\"strength\":9,\"form\":1,\"name\":\"Infantry\"}";
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        var input = WidgetIndex(p, w => w.Kind == "input");
        const string typed = "1st Foot";
        for (int i = 1; i <= typed.Length; i++)
            t.Server.Handle(Table.Ump3, new EditWidget { Layer = U, Id = P, Index = input, Value = typed[..i] });
        Assert.Equal(typed[..^1], p.Widgets[0].Label);
        t.Server.Handle(Table.Ump3, new EditWidget { Layer = U, Id = P, Index = input, Value = typed });
        Assert.Equal(typed, p.Widgets[0].Label);
        Assert.Equal(typed, p.Widgets[input - 1].Value);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
    }

    [Fact]
    public void The_name_plate_translates_and_runs()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate(Fixture("name_plate.lua"), Ctx());
        Assert.Empty(r.Untranslated);
        var p = t.Server.State.Find(U, P)!;
        p.ScriptState = "{\"regiment\":\"53rd\",\"division\":\"Div\",\"brigade\":\"Bde\"}";
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Equal(6, p.Widgets.Count);
        Assert.Equal(3, p.Widgets.Count(w => w.Kind == "input" && w.Face == "back"));
        Assert.Equal("53rd", p.Widgets[0].Label);
        Assert.Equal("53rd", p.Widgets[3].Value);
        Assert.Equal("Regiment", p.Widgets[3].Placeholder);
        Assert.Equal("#FFCC00", p.Widgets[3].Color);
        t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 4, Value = "4th Foot" });
        Assert.Equal("4th Foot", p.Widgets[3].Value);
        Assert.Equal("53rd", p.Widgets[0].Label);
        t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 4, Value = "5th Foot" });
        Assert.Equal("4th Foot", p.Widgets[0].Label);
        t.Server.Scripts.Save();
        Assert.Contains("\"regiment\":\"5th Foot\"", p.ScriptState);
    }

    [Fact]
    public void What_has_no_rule_is_listed_with_its_line()
    {
        var r = ModuleTranslator.Translate("function onLoad()\n  self.addForce({0, 1, 0})\n  Tables.getTable()\nend\nfunction onCollisionEnter(info) end\n");
        Assert.True(r.Parsed);
        Assert.Contains(r.Untranslated, u => u.Line == 2 && u.What == "addForce");
        Assert.Contains(r.Untranslated, u => u.Line == 3 && u.What == "Tables.getTable");
        Assert.Contains(r.Untranslated, u => u.What == "event" && u.Text.StartsWith("onCollisionEnter"));
        Assert.Contains("compat.todo(self, \"addForce\")", r.Script);
    }

    [Fact]
    public void A_script_that_does_not_parse_is_kept_and_reported()
    {
        var r = ModuleTranslator.Translate("function broken(\n  x = = 1\n");
        Assert.False(r.Parsed);
        Assert.NotEmpty(r.Untranslated);
        Assert.Equal("syntax", r.Untranslated[0].What);
    }

    [Fact]
    public void A_memo_set_on_a_plain_table_stays_a_field_of_that_table()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate("""
            function onLoad()
              local unit = {}
              unit.memo = "hold"
              self.memo = "orders: " .. unit.memo
            end
            """, Ctx());
        t.Ok(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.Empty(Errors(t));
        Assert.Equal("orders: hold", t.Server.State.Find(U, P)!.Memo);
    }

    [Fact]
    public void Removing_an_attachment_by_its_index_detaches_it_and_gives_it_back()
    {
        using var t = new Table();
        const string M = "p-000003";
        t.Ok(Table.Host, new Attach { Layer = U, Parent = P, Child = M });
        var r = ModuleTranslator.Translate("""
            function onLoad()
              local first = self.getAttachments()[1]
              local loose = self.removeAttachment(first.index)
              loose.setName("loose " .. first.index)
            end
            """, Ctx());
        t.Ok(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.Empty(Errors(t));
        Assert.Empty(t.Server.State.Find(U, P)!.Attached);
        Assert.Equal("loose 0", t.Server.State.Find(U, M)!.Label);
    }

    [Fact]
    public void Tags_and_memo_translate_to_the_pieces_own()
    {
        var r = ModuleTranslator.Translate("""
            function onLoad()
              self.addTag("Source")
              if self.hasTag("Source") then self.memo = "1" end
              local m = self.memo
              self.setMemo(m .. "x")
              local any = getObjectsWithAnyTags({"a", "b"})
              local all = getObjectsWithAllTags({"a"})
              self.removeTag("Source")
              self.setTags({"France"})
              local d = self.getData()
              spawnObjectData({ data = d })
              log(logString(d, "data"))
              addContextMenuItem("Report", report)
            end
            """, Ctx());
        var s = r.Script;
        Assert.Contains("compat.add_tag(self, \"Source\")", s);
        Assert.Contains("compat.has_tag(self, \"Source\")", s);
        Assert.Contains("compat.set_prop(self, \"memo\", \"1\")", s);
        Assert.Contains("local m = self.memo", s);
        Assert.Contains("compat.set_memo(self, m .. \"x\")", s);
        Assert.Contains("compat.pieces_with_any_tags({\"a\", \"b\"})", s);
        Assert.Contains("compat.pieces_with_all_tags({\"a\"})", s);
        Assert.Contains("compat.remove_tag(self, \"Source\")", s);
        Assert.Contains("compat.set_tags(self, {\"France\"})", s);
        Assert.Contains("compat.get_data(self)", s);
        Assert.Contains("compat.spawn_object_data({ data = d })", s);
        Assert.Contains("compat.log_string(d, \"data\")", s);
        Assert.Contains("compat.add_global_context_menu_item(_ENV, \"Report\", report)", s);
        Assert.Empty(r.Untranslated);
    }

    [Fact]
    public void Object_ui_and_pictures_named_by_address_translate()
    {
        var ctx = Ctx();
        ctx.ResolveAsset = url => url.EndsWith("icon.png") ? "asset-icon" : null;
        var r = ModuleTranslator.Translate("""
            icons = { [1] = { imageURL = "https://example.test/icon.png" }, [2] = { imageURL = "https://example.test/not-fetched.bin" } }
            function onLoad()
              self.UI.setXmlTable({ { tag = "Panel" } })
              self.UI.setAttribute("g1", "image", icons[1].imageURL)
              local x = self.UI.getAttribute("g1", "image")
              self.UI.show("g1")
              local obj = getObjectFromGUID("abc123")
              obj.UI.setXml("<Panel/>")
              obj.UI.setCustomAssets({})
              local spawned = spawnObject({ type = "Custom_Token" })
              spawned.setCustomObject({ image = "https://example.test/icon.png" })
            end
            """, ctx);
        var s = r.Script;
        Assert.Contains("imageURL = \"asset-icon\"", s);
        Assert.Contains("imageURL = \"https://example.test/not-fetched.bin\"", s);
        Assert.Contains("self.ui.set({ { tag = \"Panel\" } })", s);
        Assert.Contains("self.ui.set_attribute(\"g1\", \"image\", icons[1].imageURL)", s);
        Assert.Contains("local x = self.ui.get_attribute(\"g1\", \"image\")", s);
        Assert.Contains("self.ui.show(\"g1\")", s);
        Assert.Contains("obj.ui.set(\"<Panel/>\")", s);
        Assert.Contains("obj.ui.set_pictures({})", s);
        Assert.Contains("image = \"marker\"", s);
        Assert.Empty(r.Untranslated);
    }

    [Fact]
    public void Module_hotkeys_and_the_global_context_menu_translate_and_run()
    {
        var ctx = Ctx();
        ctx.IsGlobal = true;
        var r = ModuleTranslator.Translate("""
            function onLoad()
              addHotkey("Magic Hotkey #1", function(playerColor, hoveredObject, pointerPosition, isKeyUp)
                if hoveredObject then hoveredObject.setName(playerColor .. " " .. pointerPosition.x .. " " .. #Player[playerColor].getSelectedObjects() .. " " .. tostring(Player[playerColor].getHoverObject() == hoveredObject)) end
              end)
              addContextMenuItem("Casualty Report", function(player_color, menu_position) broadcastToAll("report " .. player_color .. " " .. menu_position.z) end)
              clearHotkeys()
              addHotkey("Again", function(playerColor, hoveredObject, pointerPosition, isKeyUp) end)
            end
            """, ctx);
        var s = r.Script;
        Assert.Contains("compat.add_hotkey(_ENV, \"Magic Hotkey #1\", function(playerColor, hoveredObject, pointerPosition, isKeyUp)", s);
        Assert.Contains("compat.add_global_context_menu_item(_ENV, \"Casualty Report\", function(player_color, menu_position)", s);
        Assert.Contains("compat.clear_hotkeys()", s);
        Assert.Empty(r.Untranslated);
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = s });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), x => x.Text.StartsWith("Script error"));
        Assert.Equal(new[] { "Again" }, t.Server.State.Hotkeys.Select(h => h.Label));
        Assert.Equal("Casualty Report", Assert.Single(t.Server.State.MenuItems).Label);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = s.Replace("clearHotkeys()", "").Replace("compat.clear_hotkeys()", "") });
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 1, Layer = U, Piece = P, Pos = new Vec2(3, 0), Selected = new() { P } });
        var label = t.Server.State.Find(U, P)!.Label;
        Assert.NotNull(label);
        Assert.EndsWith(" 3 1 true", label);
        t.Server.Handle(Table.Ump3, new PickGlobalMenuItem { Index = 1, Pos = new Vec2(0, 7) });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), x => x.Text.StartsWith("report ") && x.Text.EndsWith(" 7"));
    }

    [Fact]
    public void A_number_typed_over_a_piece_reaches_its_own_script_before_the_global_one()
    {
        var piece = ModuleTranslator.Translate("""
            hp = 100
            function onNumberTyped(player_color, number, alt)
              hp = hp - number * 10
              self.setName("piece " .. number .. " " .. player_color .. " " .. tostring(alt) .. " hp " .. hp)
              return number == 4
            end
            function onObjectNumberTyped(obj, player_color, n) self.setName("never") end
            """, Ctx());
        Assert.Contains("function on_number_typed(__who, __n)", piece.Script);
        Assert.Empty(piece.Untranslated);
        var gctx = Ctx();
        gctx.IsGlobal = true;
        var global = ModuleTranslator.Translate("""
            function onObjectNumberTyped(obj, player_color, n, alt)
              obj.setName(obj.getName() .. " then global " .. tostring(alt))
            end
            function onNumberTyped(player_color, number) broadcastToAll("never") end
            """, gctx);
        Assert.Empty(global.Untranslated);
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = global.Script });
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Pos = new Vec2(5, 5), Id = "p-000090" });
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000090", Text = piece.Script });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), x => x.Text.StartsWith("Script error"));
        t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = "p-000090", Number = 3 });
        var die = t.Server.State.Find(U, "p-000090")!;
        Assert.StartsWith("piece 3 ", die.Label);
        Assert.EndsWith(" false hp 70 then global false", die.Label);
        Assert.Equal(2, die.ShownFace);
        t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = "p-000090", Number = 4 });
        Assert.Contains(" hp 30 ", die.Label);
        Assert.Equal(2, die.ShownFace);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), x => x.Text == "never" || x.Text.StartsWith("Script error"));
    }

    [Fact]
    public void The_long_tail_translates_and_runs()
    {
        var ctx = Ctx();
        ctx.IsGlobal = true;
        var r = ModuleTranslator.Translate("""
            function onObjectNumberTyped(obj, player_color, n)
              obj.setName("typed " .. n .. " " .. player_color)
              obj.removeFromPlayerSelection(player_color)
              return n == 9
            end
            function onChat(message, player) broadcastToAll("chat " .. message .. " " .. player.color) end
            function onScriptingButtonDown(index, player_color) broadcastToAll("sb " .. index .. " " .. player_color) end
            function onLoad()
              local copy = getObjectFromGUID("p-000003").clone({ position = { 1, 0, 2 } })
            end
            """, ctx);
        var s = r.Script;
        Assert.Contains("function on_number_typed(__piece, __who, __n)", s);
        Assert.Contains("local obj, player_color, n = __piece, compat.color_of(__who), __n", s);
        Assert.Contains("compat.select(obj, player_color, false)", s);
        Assert.Contains("function on_chat(__text, __who)", s);
        Assert.Contains("function on_scripting_button_down(__index, __who)", s);
        Assert.Contains("compat.clone(board.find(\"p-000003\"), { position = { 1, 0, 2 } })", s);
        Assert.Empty(r.Untranslated);
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = s });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), x => x.Text.StartsWith("Script error"));
        Assert.Contains(t.Server.State.Layer(U), p => p.Def == "marker" && p.Pos == new Vec2(1, 2));
        t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = P, Number = 5 });
        Assert.StartsWith("typed 5 ", t.Server.State.Find(U, P)!.Label);
        Assert.Single(Flat(t.Server.Log).OfType<SelectPiece>());
        t.Server.Handle(Table.Ump3, new Say { Text = "hi" });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), x => x.Text.StartsWith("chat hi "));
        var sb3 = t.Server.State.Hotkeys.FindIndex(h => h.Label == "Scripting button 3") + 1;
        Assert.True(sb3 > 0);
        t.Server.Handle(Table.Ump3, new Hotkey { Index = sb3, Layer = U, Piece = P });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), x => x.Text.StartsWith("sb 3 "));
    }

    static IEnumerable<string> Errors(Table t) => Flat(t.Server.Log).OfType<Say>().Where(s => s.Text.StartsWith("Script error")).Select(s => s.Text);

    [Fact]
    public void A_reloaded_script_makes_its_module_buttons_once()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate("""
            function onLoad()
              self.createButton({ click_function = "count", function_owner = self, label = "A", position = {0, 0.1, 0}, width = 300, height = 300 })
              self.createButton({ click_function = "count", function_owner = self, label = "B", position = {0, 0.1, 0.5}, width = 300, height = 300 })
            end
            function count(obj, color, alt)
              obj.setName("buttons " .. #obj.getButtons())
              obj.removeButton(1)
            end
            """, Ctx());
        for (int i = 0; i < 3; i++) t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(2, p.Widgets.Count);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Equal("buttons 2", p.Label);
        Assert.Equal(new[] { "A" }, p.Widgets.Select(w => w.Label));
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void A_repeating_wait_stops_when_told_and_a_condition_times_out()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate("""
            function onLoad()
              n = 0
              id = Wait.time(function()
                n = n + 1
                self.setName("n " .. n)
                if n == 2 then Wait.stop(id) end
              end, 1, -1)
              Wait.condition(function() self.setDescription("met") end, function() return false end, 3, function() self.setDescription("timed out") end)
            end
            """, Ctx());
        Assert.Contains("compat.wait_stop(id)", r.Script);
        var ctx = Ctx();
        ctx.IsGlobal = true;
        var g = ModuleTranslator.Translate("""
            function onLoad() Timer.create({ identifier = "tick", function_name = "tock", delay = 1, repetitions = 0 }) end
            function tock() m = (m or 0) + 1 if m == 3 then Timer.destroy("tick") end end
            function onSave() return tostring(m) end
            """, ctx);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = g.Script });
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        for (int s = 1; s <= 8; s++) t.Server.Scripts.Tick(s + 0.5);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal("n 2", p.Label);
        Assert.Equal("timed out", p.Note);
        t.Server.Scripts.Save();
        Assert.Equal("3", t.Server.State.ScriptState);
        Assert.Empty(Errors(t));
    }

    [Theory]
    [InlineData("alpha", 0.1924284)]
    [InlineData("box", 0.36)]
    public void A_module_button_is_placed_and_lettered_by_the_piece_shape_whatever_its_own_tags(string shape, double unit)
    {
        using var t = new Table();
        var def = t.Store.Def("marker")!;
        def.Shape = shape;
        def.Size = new Size2(0.72, 0.72);
        var o = t.Server.State.Find(U, "p-000003")!;
        o.Tags = new();
        var r = ModuleTranslator.Translate("""
            function onLoad()
              self.createButton({ click_function = "none", function_owner = self, label = "Lt. Gen.", position = {0, 0.05, -0.93}, width = 0, height = 0, font_size = 165 })
            end
            function none() end
            """, Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = r.Script });
        Assert.Empty(Errors(t));
        Assert.Equal(-0.93 * unit, o.Widgets.Single().Z, 4);
        Assert.Equal(55 * 0.00624 * unit, o.Widgets.Single().Font, 4);
    }

    [Fact]
    public void A_module_tile_letters_its_band_and_its_title_at_the_size_the_game_prints_them()
    {
        using var t = new Table();
        var def = t.Store.Def("marker")!;
        def.Shape = "box";
        def.Size = new Size2(2.245, 1.3466);
        var o = t.Server.State.Find(U, "p-000003")!;
        var r = ModuleTranslator.Translate("""
            function onLoad()
              self.createButton({ click_function = "none", function_owner = self, label = "Irregulars", position = {0, 0.26, -0.72}, width = 0, height = 0, font_size = 220 })
              self.createButton({ click_function = "none", function_owner = self, label = "Irregulars", position = {0, 0.25, -1.4}, width = 0, height = 0, font_size = 400 })
              self.createButton({ click_function = "none", function_owner = self, label = "3", position = {0.6, 0, 0}, rotation = {0, 0, 180}, scale = {2.5, 2.5, 2.5}, width = 160, height = 160, font_size = 160 })
            end
            function none() end
            """, Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = r.Script });
        Assert.Empty(Errors(t));
        var (band, title, count) = (o.Widgets[0], o.Widgets[1], o.Widgets[2]);
        double unit = 1.3466 / 2;
        Assert.Equal(0.228, band.Font / def.Size.H, 3);
        Assert.Equal(0.415, title.Font / def.Size.H, 3);
        Assert.Equal(-0.72 * unit, band.Z, 4);
        Assert.True(title.Z < -def.Size.H / 2);
        Assert.Equal(53 * 0.00624 * 2.5 * unit, count.W, 4);
        Assert.Equal(count.W, count.H, 6);
        Assert.Equal(count.W, count.Font, 6);
    }

    [Fact]
    public void A_module_widget_counts_its_sizes_in_whole_steps_of_three_and_letters_grow_with_its_height_scale()
    {
        using var t = new Table();
        var def = t.Store.Def("marker")!;
        def.Shape = "box";
        def.Size = new Size2(2, 2);
        var o = t.Server.State.Find(U, "p-000003")!;
        var r = ModuleTranslator.Translate("""
            function onLoad()
              self.createButton({ click_function = "none", function_owner = self, label = "a", width = 2, height = 101, font_size = 100 })
              self.createButton({ click_function = "none", function_owner = self, label = "b", scale = {3, 1, 1}, width = 99, height = 99, font_size = 100 })
              self.createButton({ click_function = "none", function_owner = self, label = "c", scale = {1, 1, 3}, width = 99, height = 99, font_size = 100 })
            end
            function none() end
            """, Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = r.Script });
        Assert.Empty(Errors(t));
        var (a, b, c) = (o.Widgets[0], o.Widgets[1], o.Widgets[2]);
        Assert.Equal(0, a.W);
        Assert.Equal(33 * 0.00624, a.H, 6);
        Assert.Equal(33 * 0.00624, a.Font, 6);
        Assert.Equal(3 * c.W, b.W, 6);
        Assert.Equal(a.Font, b.Font, 6);
        Assert.Equal(3 * a.Font, c.Font, 6);
    }

    [Fact]
    public void Handlers_get_the_piece_their_widget_is_on()
    {
        using var t = new Table();
        t.Server.State.Find(U, "p-000003")!.Guid = "b00b00";
        var ctx = Ctx();
        ctx.IsGlobal = true;
        var global = ModuleTranslator.Translate("""
            function onLoad()
              local o = getObjectFromGUID("p-000003")
              o.createButton({ click_function = "hit", function_owner = Global, label = "x", width = 300, height = 300 })
              o.addContextMenuItem("Mark", mark)
            end
            function hit(obj, color, alt) obj.setName("hit " .. obj.getGUID() .. " by " .. color) end
            function mark(player_color, menu_position, obj) obj.setDescription("marked by " .. player_color) end
            """, ctx);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = global.Script });
        Assert.Empty(Errors(t));
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = "p-000003", Index = 1 });
        var o = t.Server.State.Find(U, "p-000003")!;
        var colour = o.Label?.Split(' ').Last();
        Assert.Equal("hit b00b00 by " + colour, o.Label);
        Assert.Contains(colour, new[] { "Red", "Blue", "Green" });
        t.Server.Handle(Table.Ump3, new PickMenuItem { Layer = U, Id = "p-000003", Index = 1 });
        Assert.Equal("marked by " + colour, o.Note);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "" });
        var controller = ModuleTranslator.Translate("""
            function onLoad()
              getObjectFromGUID("b00b00").createButton({ click_function = "report", function_owner = self, label = "r", width = 300, height = 300 })
            end
            function report(obj, color, alt) obj.setName("reported to " .. self.getGUID()) end
            """, Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = controller.Script });
        o = t.Server.State.Find(U, "p-000003")!;
        var button = o.Widgets.FindIndex(w => w.Label == "r") + 1;
        Assert.Equal(P + "/report", o.Widgets[button - 1].OnClick);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = "p-000003", Index = button });
        Assert.Equal("reported to " + P, t.Server.State.Find(U, "p-000003")!.Label);
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void A_spawned_model_takes_its_mesh_its_spawn_scale_and_its_own_script()
    {
        var ctx = Ctx();
        var asked = new List<string>();
        ctx.ResolveModel = (mesh, diffuse, sx, sy) =>
        {
            asked.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mesh}|{diffuse}|{sx}|{sy}"));
            return "pack.brigade";
        };
        var r = ModuleTranslator.Translate("""
            function spawnBrigade()
              local spawnparams = { type = 'Custom_Model', position = {0, 1, 0}, scale = {x = 1.3, y = 1, z = 1.3} }
              object = spawnObject(spawnparams)
              params = { mesh = "https://example.test/column.obj", diffuse = "https://example.test/blue.png", material = 3 }
              object.setCustomObject(params)
              createUnit()
            end
            function createUnit()
              local code_string = [[
                function onLoad()
                  self.addContextMenuItem("Battle Line", setLine)
                end
                function setLine()
                  params = { mesh = "https://example.test/line.obj" }
                  self.setCustomObject(params)
                  self.reload()
                end
                function spawnSkirmishers()
                  local sk = { type = 'Custom_Model', scale = {x = 1.3, y = 1, z = 1.3} }
                  local o = spawnObject(sk)
                  o.setCustomObject({ mesh = "https://example.test/skirmish.obj" })
                end
              ]]
              object.setLuaScript(code_string)
            end
            function spawnSmoke()
              local puff = { type = 'Custom_Token', scale = {x = 0.16, y = 1, z = 0.16} }
              local token = spawnObject(puff)
              token.setCustomObject({ image = "https://example.test/smoke.png" })
            end
            """, ctx);
        var s = r.Script;
        Assert.True(r.Parsed);
        Assert.Contains("def = \"pack.brigade\"", s);
        Assert.DoesNotContain("mesh = \"https://example.test/column.obj\"", s);
        Assert.Equal("https://example.test/column.obj|https://example.test/blue.png|1.3|1", asked[0]);
        Assert.Equal("https://example.test/line.obj||1.3|1", asked[1]);
        Assert.Equal("https://example.test/skirmish.obj||1.3|1", asked[2]);
        Assert.Contains("compat.add_context_menu_item(_ENV, self, \"Battle Line\", setLine)", s);
        Assert.Contains("compat.set_custom_object(self, params)", s);
        Assert.Contains("image = \"marker\"", s);
    }

    [Fact]
    public void A_spawn_reads_its_type_and_scale_through_the_variable_it_was_given()
    {
        var ctx = Ctx();
        var asked = new List<(string type, double scale)>();
        ctx.ResolveImage = (url, type, scale) => { asked.Add((type, scale)); return "marker"; };
        ModuleTranslator.Translate("""
            function spawnSmoke()
              local puff = { type = 'Custom_Token', scale = {x = 0.16, y = 1, z = 0.16} }
              local token = spawnObject(puff)
              token.setCustomObject({ image = "https://example.test/smoke.png" })
            end
            function spawnFlag()
              flagparams = { type = 'Custom_Tile', scale = {2, 1, 2} }
              local flag = spawnObject(flagparams)
              flag.setCustomObject({ image = "https://example.test/flag.png" })
            end
            """, ctx);
        Assert.Equal(("Custom_Token", 0.16), asked[0]);
        Assert.Equal(("Custom_Tile", 2.0), asked[1]);
    }
}
