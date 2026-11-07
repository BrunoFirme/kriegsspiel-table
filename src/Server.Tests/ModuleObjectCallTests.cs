using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleObjectCallTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static string Run(Table t, string lua, bool errorsExpected = false)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            var r = ModuleTranslator.Translate(lua, Ctx());
            Assert.True(r.Parsed);
            var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
            Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
            if (!errorsExpected) Assert.DoesNotContain(t.Server.Scripts.Console, l => l.StartsWith("Script error"));
            return t.Server.State.Find(U, P)!.Label ?? "";
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }


    static string Run(string lua)
    {
        using var t = new Table();
        return Run(t, lua);
    }

    [Fact]
    public void Object_setters_answer_true_as_module_code_checks()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              local r = { m.setName('x'), m.setDescription('d'), m.setGMNotes('g'), m.setMemo('m'), m.setLock(true), m.setColorTint('Red'),
                m.setPosition({1, 0, 2}), m.setRotation({0, 90, 0}), m.setScale({1, 1, 1}), m.scale(1), m.translate({0, 0, 1}), m.rotate({0, 10, 0}),
                m.highlightOn('Red', 1), m.highlightOff(), m.flip(), m.clearContextMenu(), m.setVar('a', 1), m.setTable('b', {}),
                m.setTags({'t'}), m.addTag('u') }
              local out = {}
              for i = 1, 20 do out[i] = tostring(r[i]) end
              self.setName(table.concat(out, ','))
            end
            """;
        Assert.Equal(string.Join(",", Enumerable.Repeat("true", 20)), Run(lua));
    }

    [Fact]
    public void A_vector_argument_takes_x_y_z_names_first_then_three_numbers_then_two_and_otherwise_fails()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setPosition({x = 4, y = 0, z = 5, 9, 9, 9})
              local named = m.getPosition()
              m.setPosition({3, 7})
              local two = m.getPosition()
              local ok, err = pcall(function() m.setPosition({x = 1, z = 2}) end)
              self.setName(named.x .. ',' .. named.z .. '|' .. two.x .. ',' .. two.z .. '|' .. tostring(ok) .. '|' .. tostring(err))
            end
            """;
        var got = Run(lua);
        Assert.StartsWith("4,5|3,0|false|", got);
        Assert.EndsWith("Error converting Table to Vector. (x, y, z or 1, 2, 3)", got);
    }

    [Fact]
    public void A_colour_argument_takes_a_seat_name_exactly_or_r_g_b_and_alpha_defaults_to_one()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setColorTint({1, 0, 0})
              local a = m.getColorTint()
              m.setColorTint({r = 0, g = 0, b = 1, 0.5})
              local b = m.getColorTint()
              local ok, err = pcall(function() m.setColorTint('red') end)
              self.setName(a.r .. ',' .. a.a .. '|' .. b.b .. ',' .. b.a .. '|' .. tostring(ok) .. '|' .. tostring(err))
            end
            """;
        var got = Run(lua);
        Assert.StartsWith("1,1|1,1|false|", got);
        Assert.EndsWith("Error converting String to Color. (White, Red, Blue, etc.)", got);
    }

    [Fact]
    public void Turning_over_by_x_or_z_lays_the_piece_face_down_and_rotation_reads_back_in_0_to_360()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setRotation({0, -90, 180})
              local a = m.getRotation()
              local up = m.getTransformUp()
              m.setRotation({180, 30, 0})
              local b = m.getRotation()
              m.setRotation({0, 0, 0})
              self.setName(a.y .. ',' .. a.z .. ',' .. up.y .. '|' .. b.y .. ',' .. b.z .. '|' .. tostring(m.is_face_down))
            end
            """;
        Assert.Equal("270,180,-1|210,180|false", Run(lua));
    }

    [Fact]
    public void A_piece_without_states_has_state_id_minus_one_no_states_and_refuses_set_state()
    {
        const string lua = """
            function onLoad()
              self.setName(self.getStateId() .. '|' .. tostring(self.getStates()) .. '|' .. tostring(self.setState(2)))
            end
            """;
        Assert.Equal("-1|nil|nil", Run(lua));
    }

    [Fact]
    public void Tags_match_without_case_and_with_spaces_as_underscores()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.addTag('Red Team')
              local has = m.hasTag('RED_team')
              local found = #getObjectsWithTag('red_team')
              local again = m.addTag('RED TEAM')
              local n = #m.getTags()
              local gone = m.removeTag('red team')
              local twice = m.removeTag('red team')
              self.setName(tostring(has) .. '|' .. found .. '|' .. tostring(again) .. '|' .. n .. '|' .. tostring(gone) .. '|' .. tostring(twice))
            end
            """;
        Assert.Equal("true|1|true|1|true|false", Run(lua));
    }

    [Fact]
    public void A_call_into_another_script_copies_its_table_both_ways_and_an_error_there_is_logged_and_answers_nil()
    {
        using var t = new Table();
        const string other = """
            function grow(t) t.n = t.n + 1 kept = t return t end
            function bad() error('boom') end
            function peek() return kept.n end
            """;
        var r = ModuleTranslator.Translate(other, Ctx());
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Marker, Text = r.Script }), o => o.Message is ErrorMessage);
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              local mine = { n = 1 }
              local back = m.call('grow', mine)
              back.n = 10
              local bad = m.call('bad')
              local missing = m.call('nothing')
              self.setName(mine.n .. '|' .. m.call('peek') .. '|' .. tostring(bad) .. '|' .. tostring(missing))
            end
            """;
        Assert.Equal("1|2|nil|nil", Run(t, lua, errorsExpected: true));
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("boom"));
    }

    [Fact]
    public void A_piece_that_is_not_a_bag_counts_minus_one_lists_nothing_and_takes_nothing_in()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              self.setName(self.getQuantity() .. '|' .. tostring(self.getObjects()) .. '|' .. tostring(self.putObject(m)) .. '|' .. tostring(self.takeObject({})))
            end
            """;
        Assert.Equal("-1|nil|nil|nil", Run(t, lua));
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("does not support getObjects()"));
        Assert.NotNull(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void Printing_to_a_seat_nobody_holds_is_an_error_and_seat_names_are_exact()
    {
        const string lua = """
            function onLoad()
              local ok, err = pcall(function() broadcastToColor('hi', 'Teal', {1, 1, 1}) end)
              local c = stringColorToRGB('Red')
              self.setName(tostring(ok) .. '|' .. tostring(err) .. '|' .. tostring(stringColorToRGB('red')) .. '|' .. c.r .. '|' .. tostring(printToAll('x')))
            end
            """;
        var got = Run(lua);
        Assert.StartsWith("false|", got);
        Assert.Contains("Error in BroadcastToColor: Player Teal does not exist.|nil|0.856|true", got);
    }

    [Fact]
    public void Seated_players_leave_out_the_game_master_seat()
    {
        const string lua = "function onLoad()\n  self.setName(table.concat(getSeatedPlayers(), ','))\nend\n";
        Assert.Equal("White,Red", Run(lua));
    }

    [Fact]
    public void Log_writes_a_table_as_indented_key_lines_under_its_label_and_answers_true()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local ok = log({ a = 1, b = { c = true } }, 'here')
              self.setName(tostring(ok) .. '|' .. logString(0.5) .. '|' .. logString(nil))
            end
            """;
        Assert.Equal("true|0.5|nil", Run(t, lua));
        var line = t.Server.Scripts.Console.Single(l => l.StartsWith("here"));
        Assert.Contains("here\n", line);
        Assert.Contains("  a: 1", line);
        Assert.Contains("  b: \n    c: true", line);
    }

    [Fact]
    public void A_module_function_named_like_a_tabletop_global_is_the_one_its_script_calls()
    {
        const string lua = """
            function log(x) self.setName('own ' .. x) end
            function onLoad() log('call') end
            """;
        Assert.Equal("own call", Run(lua));
    }

    [Fact]
    public void Wait_stop_answers_whether_a_timer_was_waiting_and_a_nil_function_is_an_error()
    {
        const string lua = """
            function onLoad()
              local id = Wait.frames(function() end, 5)
              local first = Wait.stop(id)
              local second = Wait.stop(id)
              local ok, err = pcall(function() Wait.frames(nil, 1) end)
              local okc, errc = pcall(function() Wait.condition(function() end, function() return 1 end) end)
              self.setName(tostring(first) .. '|' .. tostring(second) .. '|' .. tostring(err) .. '|' .. tostring(errc))
            end
            """;
        var got = Run(lua);
        Assert.StartsWith("true|false|", got);
        Assert.Contains("Error in Wait.frames: function is nil|", got);
        Assert.EndsWith("Error in Wait.condition: condition_function does not return bool", got);
    }

    [Fact]
    public void In_a_frame_waits_on_frames_fire_before_waits_on_seconds_and_a_failing_condition_fires_its_function()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              order = ''
              Wait.time(function() order = order .. 't' end, 0.5)
              Wait.frames(function() order = order .. 'f' end, 1)
              Wait.condition(function() order = order .. 'c' end, function() if order == '' then return false end error('no') end)
            end
            function onSave() self.setName(order) return '' end
            """;
        Run(t, lua);
        t.Server.Scripts.Tick(1.0);
        t.Server.Scripts.Tick(1.1);
        var r = ModuleTranslator.Translate("function onLoad() self.setName(getObjectFromGUID('p-000002').getVar('order')) end", Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Marker, Text = r.Script });
        Assert.Equal("ftc", t.Server.State.Find(U, Marker)!.Label);
    }

    [Fact]
    public void A_timer_calls_the_named_function_of_the_script_that_made_it_and_refuses_a_name_in_use()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              n = 0
              local a = Timer.create({ identifier = 'x', function_name = 'tick', delay = 1, repetitions = 2, parameters = { by = 2 } })
              local b = Timer.create({ identifier = 'x', function_name = 'tick' })
              local c = Timer.create({ function_name = 'tick' })
              self.setName(tostring(a) .. tostring(b) .. tostring(c))
            end
            function tick(p) n = n + p.by self.setDescription(tostring(n)) end
            """;
        Assert.Equal("truefalsefalse", Run(t, lua));
        for (int s = 1; s <= 5; s++) t.Server.Scripts.Tick(s + 0.5);
        Assert.Equal("4", t.Server.State.Find(U, P)!.Note);
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("Timer already contains identifier: x"));
    }

    [Fact]
    public void The_same_menu_function_under_the_same_label_is_one_row_and_a_missing_function_is_refused()
    {
        using var t = new Table();
        const string lua = """
            function act(color, pos, obj) obj.setDescription(color .. ' ' .. pos.x) end
            function onLoad()
              local a = self.addContextMenuItem('Act', act)
              local b = self.addContextMenuItem('Act', act)
              local c = self.addContextMenuItem('Other', nil)
              self.setName(tostring(a) .. tostring(b) .. tostring(c))
            end
            """;
        Assert.Equal("truetruefalse", Run(t, lua));
        Assert.Single(t.Server.State.Find(U, P)!.MenuItems);
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("Function required for context menu (label = \"Other\""));
    }

    [Fact]
    public void Scale_reads_one_local_unit_and_setting_it_back_keeps_the_piece_the_same_size()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              local s = m.getScale()
              m.setScale(s)
              local again = m.getScale()
              m.scale(2)
              local twice = m.getScale()
              self.setName(s.x .. ',' .. s.z .. '|' .. again.x .. '|' .. twice.x)
            end
            """;
        Assert.Equal("0.5,0.5|0.5|1", Run(t, lua));
        Assert.Equal(2, t.Server.State.Find(U, Marker)!.Scale);
    }

    [Fact]
    public void A_local_point_goes_to_the_world_with_its_height_scaled_and_turned_over_with_the_piece()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              local up = m.positionToWorld({0, 2, 0})
              local back = m.positionToLocal(m.positionToWorld({1, 2, 3}))
              m.flip()
              local down = m.positionToWorld({0, 2, 0})
              local back2 = m.positionToLocal(m.positionToWorld({1, 2, 3}))
              local function f(v) return string.format('%.4g,%.4g,%.4g', v.x, v.y, v.z) end
              self.setName(up.y .. '|' .. f(back) .. '|' .. down.y .. '|' .. f(back2))
            end
            """;
        Assert.Equal("1|1,2,3|-1|1,2,3", Run(lua));
    }

    [Fact]
    public void Bounds_of_a_turned_piece_hold_its_turned_outline_and_normalized_bounds_ignore_the_turn()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setScale({1, 1, 2})
              m.setRotation({0, 90, 0})
              local b = m.getBounds()
              local n = m.getBoundsNormalized()
              self.setName(string.format('%.2f,%.2f|%.2f,%.2f', b.size.x, b.size.z, n.size.x, n.size.z))
            end
            """;
        Assert.Equal("2.00,2.00|2.00,2.00", Run(lua));
    }

    [Fact]
    public void Vector_lines_read_back_nil_until_set_and_then_as_given_with_vectors_and_colours()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local before = self.getVectorLines()
              local ok = self.setVectorLines({ { points = { {0, 0, 0}, {1, 0, 1} }, color = 'Red', thickness = 0.2, rotation = {0, 90, 0}, square = true } })
              local l = self.getVectorLines()[1]
              self.setName(tostring(before) .. '|' .. tostring(ok) .. '|' .. l.points[2].x .. '|' .. l.color.r .. '|' .. l.rotation.y .. '|' .. tostring(l.square) .. '|' .. tostring(l.loop))
            end
            """;
        Assert.Equal("nil|true|1|0.856|90|true|false", Run(t, lua));
        Assert.Single(t.Server.State.Find(U, P)!.Lines);
    }

    [Fact]
    public void Decals_need_a_name_and_a_link_and_read_back_nil_until_set()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local before = self.getDecals()
              local noName = self.setDecals({ { url = 'arms', position = {0, 1, 0} } })
              local none = self.setDecals(nil)
              local noUrl = self.addDecal({ name = 'x' })
              self.setName(tostring(before) .. '|' .. tostring(noName) .. '|' .. tostring(none) .. '|' .. tostring(noUrl))
            end
            """;
        Assert.Equal("nil|false|false|false", Run(t, lua));
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("name was null or an empty string"));
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("Decal table is empty!"));
    }

    [Fact]
    public void Snap_points_read_back_with_their_rotation_only_when_they_snap_rotation()
    {
        const string lua = """
            function onLoad()
              local before = #self.getSnapPoints()
              self.setSnapPoints({ { position = {1, 0, 2}, rotation = {0, 45, 0} }, { position = {0, 0, 0}, rotation = {0, 30, 0}, rotation_snap = true } })
              local s = self.getSnapPoints()
              self.setName(before .. '|' .. s[1].position.z .. ',' .. s[1].rotation.y .. ',' .. tostring(s[1].rotation_snap) .. '|' .. s[2].rotation.y)
            end
            """;
        Assert.Equal("0|2,0,false|30", Run(lua));
    }

    [Fact]
    public void Json_writes_sorted_keys_empty_tables_as_arrays_text_as_it_is_and_reads_past_trailing_text()
    {
        const string lua = """
            function onLoad()
              local a = JSON.encode({ b = 1, a = { 1, nil, 3 }, [2] = 'n', e = {}, s = 'é "q"', f = 0 / 0 })
              local d = JSON.decode('{"x": [1, null, 3], "1": true} trailing')
              local ok = pcall(JSON.encode, { f = function() end })
              local p = JSON.encode_pretty({ k = { 1, 2 } })
              local u = JSON.decode('"\\a\\u0041"')
              self.setName(a .. '|' .. tostring(d.x[3]) .. tostring(d.x[2]) .. tostring(d['1']) .. tostring(d[1]) .. '|' .. tostring(ok) .. '|' .. p .. '|' .. u)
            end
            """;
        Assert.Equal("{\"a\":[1,null,3],\"b\":1,\"e\":[],\"f\":null,\"s\":\"é \\\"q\\\"\",\"2\":\"n\"}|3niltruenil|false|{\n  \"k\": [ 1, 2 ]\n}|aA", Run(lua));
    }

    [Fact]
    public void Module_vectors_follow_the_tabletop_class_two_numbers_make_zero_equality_is_within_a_squared_thousandth_and_moves_change_the_vector()
    {
        const string lua = """
            function onLoad()
              local two = Vector(1, 2)
              local near = Vector(0, 0, 0) == Vector(0.03, 0, 0)
              local far = Vector(0, 0, 0) == Vector(0.04, 0, 0)
              local m = Vector(0, 0, 0)
              m:moveTowards(Vector(10, 0, 0), 3)
              local p = Vector(2, 5, 0)
              p:project(Vector(1, 0, 0))
              local div = pcall(function() return Vector(1, 1, 1) / 2 end)
              local h = Vector(0, 1, 0):heading('x')
              self.setName(tostring(two) .. '|' .. tostring(near) .. tostring(far) .. '|' .. m.x .. '|' .. tostring(p) .. '|' .. tostring(div) .. '|' .. h .. '|' .. tostring(Vector(1.5, -2, 0.25)))
            end
            """;
        Assert.Equal("Vector: { 0, 0, 0 }|truefalse|3|Vector: { 2, 0, 0 }|false|90|Vector: { 1.5, -2, 0.25 }", Run(lua));
    }

    [Fact]
    public void Module_colours_follow_the_tabletop_class_named_channels_only_clamped_and_strict_about_names_and_hex()
    {
        const string lua = """
            function onLoad()
              local positional = Color({ 1, 0, 0 })
              local clamped = Color(2, -1, 0.5)
              local bad = pcall(Color.fromString, 'mauve')
              local badHex = pcall(Color.fromHex, '#12345')
              local half = Color(0.5, 0.5, 0.5):toHex()
              local seeThrough = Color(0.856, 0.1, 0.094, 0.5):toString()
              local upper = Color.RED:toString()
              self.setName(positional.r .. ',' .. clamped.r .. clamped.g .. '|' .. tostring(bad) .. tostring(badHex) .. '|' .. half .. '|' .. tostring(seeThrough) .. '|' .. upper .. '|' .. tostring(Color.Red))
            end
            """;
        Assert.Equal("0,10|falsefalse|7f7f7f|nil|Red|Color: Red { r = 0.856, g = 0.1, b = 0.094 }", Run(lua));
    }

    [Fact]
    public void A_coroutine_runs_to_its_first_yield_at_once_goes_on_each_frame_while_it_yields_zero_and_stops_on_any_other_number()
    {
        using var t = new Table();
        const string lua = """
            function count()
              n = n + 1
              coroutine.yield(0)
              n = n + 1
              coroutine.yield(1)
              n = n + 100
            end
            function onLoad()
              n = 0
              local ok = startLuaCoroutine(self, 'count')
              local missing = startLuaCoroutine(self, 'nothing')
              self.setName(n .. tostring(ok) .. tostring(missing))
            end
            """;
        Assert.Equal("1truetrue", Run(t, lua));
        for (int f = 1; f <= 4; f++) t.Server.Scripts.Tick(f);
        var r = ModuleTranslator.Translate("function onLoad() self.setName(tostring(getObjectFromGUID('p-000002').getVar('n'))) end", Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Marker, Text = r.Script });
        Assert.Equal("2", t.Server.State.Find(U, Marker)!.Label);
    }

    [Fact]
    public void A_spawned_piece_is_spawning_until_the_next_frame_when_its_callback_runs()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local o = spawnObject({ type = 'Custom_Token', position = {3, 1, 4}, callback_function = function(obj) obj.setName('ready ' .. tostring(obj.spawning)) end })
              o.setCustomObject({ image = 'http://example.com/a.png' })
              self.setName(tostring(o.spawning))
            end
            """;
        Assert.Equal("true", Run(t, lua));
        Assert.DoesNotContain(t.Server.State.Layer(U), p => p.Label == "ready false");
        t.Server.Scripts.Tick(1);
        Assert.Contains(t.Server.State.Layer(U), p => p.Label == "ready false");
    }

    [Fact]
    public void The_tabletops_own_global_names_reach_the_global_script()
    {
        using var t = new Table();
        var g = ModuleTranslator.Translate("score = 3\nfunction double(p) return { v = p.v * 2 } end", new TranslateContext { IsGlobal = true });
        t.Server.Handle(Table.Host, new SetGlobalScript { Text = g.Script });
        const string lua = """
            function onLoad()
              local before = getGlobalScriptVar('score')
              setGlobalScriptVar('score', 4)
              setGlobalScriptTable('kept', { a = 1 })
              local kept = getGlobalScriptTable('kept')
              local d = callLuaFunctionInOtherScriptWithParams(Global, 'double', { v = 5 })
              local c = color(1, 0, 0)
              local v = vector(1, 2, 3)
              local bad = pcall(color, 1, 0)
              self.setName(before .. getGlobalScriptVar('score') .. kept.a .. d.v .. c.r .. v.z .. tostring(bad) .. tostring(setLookingForPlayers(true)) .. logString(true))
            end
            """;
        Assert.Equal("3411013falsetruetrue", Run(t, lua));
    }

    [Fact]
    public void Plain_reads_and_small_calls_answer_as_the_tabletop_does()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setGMNotes('secret')
              local a = { m.getDescription(), m.getGMNotes(), tostring(m.getLock()), tostring(m.guid), tostring(m.reload() == m) }
              local b = { tostring(clearPixelPaint()), tostring(debugLog('x')), tostring(pcall(function() return Timer.timers end)), tostring(Global.getVectorLines()) }
              local gone = m.destruct()
              self.setName(table.concat(a, ',') .. '|' .. table.concat(b, ',') .. '|' .. tostring(gone) .. tostring(m.isDestroyed()))
            end
            """;
        Assert.Equal(",secret,false,p-000003,true|false,true,false,nil|truetrue", Run(t, lua));
        Assert.Null(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void Notebook_tabs_need_a_title_adding_one_answers_the_new_count_and_notes_refuse_nil()
    {
        const string lua = """
            function onLoad()
              local none = Notes.addNotebookTab({ body = 'x' })
              local n = Notes.addNotebookTab({ title = 'Orders', body = 'advance', color = 'Red' })
              local tab = Notes.getNotebookTabs()[1]
              local edited = Notes.editNotebookTab({ index = 5, title = 'no' })
              local ok = pcall(Notes.setNotes, nil)
              self.setName(none .. '|' .. n .. '|' .. tab.title .. ',' .. tab.body .. ',' .. tab.color .. ',' .. tab.index .. '|' .. tostring(edited) .. '|' .. tostring(ok))
            end
            """;
        Assert.Equal("-1|1|Orders,advance,Red,0|false|false", Run(lua));
    }

    [Fact]
    public void A_menu_row_calls_its_function_with_the_colour_the_pieces_position_and_the_piece()
    {
        using var t = new Table();
        const string lua = """
            function act(color, pos, obj) obj.setDescription(color .. ' ' .. pos.x .. ' ' .. obj.getGUID()) end
            function onLoad() self.addContextMenuItem('Act', act) end
            """;
        Run(t, lua);
        Assert.DoesNotContain(t.Server.Handle(Table.Ump3, new PickMenuItem { Layer = U, Id = P, Index = 1 }), o => o.Message is ErrorMessage);
        Assert.Equal("Red 0 " + P, t.Server.State.Find(U, P)!.Note);
    }

    [Fact]
    public void Object_lists_tag_lists_seat_names_and_world_lines_answer_as_the_tabletop_does()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setTags({ 'Foot', 'Guard' })
              local all, every = #getObjects(), #getAllObjects()
              local any = #getObjectsWithAnyTags({ 'guard', 'none' })
              local both = #getObjectsWithAllTags({ 'foot', 'GUARD' })
              local none = #getObjectsWithAllTags({ 'foot', 'none' })
              local who = colorToPlayerName('Red')
              local noone = pcall(colorToPlayerName, 'Teal')
              local drew = Global.setVectorLines({ { points = { {0, 0, 0}, {2, 0, 2} } } })
              local back = #Global.getVectorLines()
              local gone = destroyObject(m)
              self.setName(all .. every .. any .. both .. none .. '|' .. who .. '|' .. tostring(noone) .. '|' .. tostring(drew) .. back .. '|' .. tostring(gone) .. '|' .. Time.fixed_time)
            end
            """;
        Assert.Equal("22110|Umpire Three|false|true1|true|0", Run(t, lua));
        Assert.Null(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void A_piece_of_a_kind_tagged_bag_is_a_bag_even_with_tags_of_its_own()
    {
        using var t = new Table();
        t.Store.Def("marker")!.Tags.Add("bag");
        t.Server.State.Find(U, Marker)!.Tags = new() { "Memory" };
        const string lua = "function onLoad()\n  local m = getObjectFromGUID('p-000003')\n  self.setName(m.getQuantity() .. '|' .. #m.getObjects())\nend\n";
        Assert.Equal("0|0", Run(t, lua));
    }

    [Fact]
    public void Plain_object_properties_start_at_the_tabletops_defaults_and_keep_what_a_script_sets()
    {
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              local a = { m.auto_raise, m.tooltip, m.sticky, m.drag_selectable, m.measure_movement, m.max_typed_number, m.value, m.use_rotation_value_flip,
                m.resting, m.is_face_down, m.held_flip_index, m.held_spin_index, m.memo }
              m.auto_raise = false
              m.tooltip = false
              m.max_typed_number = 9
              m.memo = 'kept'
              m.sticky = false
              local b = { m.auto_raise, m.tooltip, m.max_typed_number, m.memo, m.sticky, m.getMemo(), m.setMemo('x') }
              local out = {}
              for i = 1, 13 do out[#out + 1] = tostring(a[i]) end
              for i = 1, 7 do out[#out + 1] = tostring(b[i]) end
              self.setName(table.concat(out, ','))
            end
            """;
        Assert.Equal("true,true,true,true,false,-1,0,false,true,false,0,0,,false,false,9,kept,false,kept,true", Run(lua));
    }

    [Fact]
    public void Attachments_answer_true_list_their_saved_fields_and_come_off_together_as_a_list()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.setName('Flag')
              local ok = self.addAttachment(m)
              local a = self.getAttachments()[1]
              local off = self.removeAttachments()
              self.setName(tostring(ok) .. '|' .. a.name .. ',' .. a.guid .. ',' .. a.index .. ',' .. type(a.tags) .. '|' .. #off .. ',' .. off[1].getGUID() .. '|' .. #self.getAttachments())
            end
            """;
        Assert.Equal("true|Flag,p-000003,0,table|1,p-000003|0", Run(t, lua));
    }

    [Fact]
    public void A_bags_container_answers_as_the_tabletops_bag_container_which_does_nothing()
    {
        using var t = new Table();
        t.Server.State.Find(U, Marker)!.Holds = true;
        const string lua = """
            function onLoad()
              local c = getObjectFromGUID('p-000003').Container
              local r = { c.putObject(self), c.takeObject({}), c.getData(), c.setData({}), c.addData({}), c.removeData(0), c.getInfo(), c.randomize(), c.getCount() }
              local out = {}
              for i = 1, 9 do out[i] = tostring(r[i]) end
              self.setName(table.concat(out, ','))
            end
            """;
        Assert.Equal("nil,nil,nil,true,true,nil,nil,true,0", Run(t, lua));
        Assert.NotNull(t.Server.State.Find(U, P));
    }

    [Fact]
    public void A_highlight_rings_the_piece_for_its_seconds_and_interactable_off_keeps_hands_off()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              m.highlightOn('Red', 2)
              self.highlightOn({0, 0, 1})
              self.highlightOff()
              m.interactable = false
              self.setName(tostring(m.interactable))
            end
            """;
        Assert.Equal("false", Run(t, lua));
        var m = t.Server.State.Find(U, Marker)!;
        Assert.Equal(("#DA1A18", true), (m.Highlight, m.NotInteractable));
        Assert.Null(t.Server.State.Find(U, P)!.Highlight);
        t.Server.Scripts.Tick(1);
        t.Server.Scripts.Tick(3);
        Assert.Null(t.Server.State.Find(U, Marker)!.Highlight);
    }

    [Fact]
    public void A_smooth_move_takes_the_tabletops_time_reads_its_target_meanwhile_and_lands_at_the_end()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              m = getObjectFromGUID('p-000003')
              m.setPositionSmooth({ 6, 0, 0 })
              local target = m.getPositionSmooth()
              local turn = m.getRotationSmooth()
              local short = getObjectFromGUID('p-000002')
              short.translate({ 0.01, 0, 0 })
              self.setName(tostring(m.isSmoothMoving()) .. '|' .. target.x .. '|' .. tostring(turn) .. '|' .. tostring(short.isSmoothMoving()))
            end
            function watch() return string.format('%s|%.2f', tostring(m.isSmoothMoving()), m.getPosition().x) end
            """;
        Assert.Equal("true|6|nil|false", Run(t, lua));
        Assert.Equal(new Vec2(1, 0), t.Server.State.Find(U, Marker)!.Pos);
        t.Server.Scripts.Tick(0.2);
        var r = ModuleTranslator.Translate("function onLoad() self.setName(getObjectFromGUID('p-000002').call('watch')) end", Ctx());
        t.Server.Scripts.Tick(0.4);
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = r.Script });
        var mid = t.Server.State.Find(U, Marker)!.Label!;
        Assert.StartsWith("true|", mid);
        var x = double.Parse(mid.Split('|')[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(x, 1.5, 5.99);
        for (int f = 1; f <= 10; f++) t.Server.Scripts.Tick(0.4 + f * 0.1);
        Assert.Equal(new Vec2(6, 0), t.Server.State.Find(U, Marker)!.Pos);
    }

    [Fact]
    public void Matching_tags_a_removed_attachment_the_global_menu_clear_and_an_outside_message_answer_as_the_tabletop_does()
    {
        using var t = new Table();
        const string lua = """
            function onLoad()
              local m = getObjectFromGUID('p-000003')
              self.setTags({ 'Guard' })
              m.setTags({ 'guard', 'Foot' })
              local match = self.hasMatchingTag(m)
              m.setTags({ 'Foot' })
              local none = self.hasMatchingTag(m)
              self.addAttachment(m)
              local off = self.removeAttachment(0)
              ticks = 0
              self.setName(tostring(match) .. tostring(none) .. '|' .. off.getGUID() .. '|' .. tostring(clearContextMenu()) .. tostring(sendExternalMessage({ a = 1 })))
            end
            function onUpdate() ticks = ticks + 1 end
            """;
        Assert.Equal("truefalse|p-000003|truetrue", Run(t, lua));
        for (int f = 1; f <= 3; f++) t.Server.Scripts.Tick(f * 0.016);
        var r = ModuleTranslator.Translate("function onLoad() self.setName(tostring(getObjectFromGUID('p-000002').getVar('ticks'))) end", Ctx());
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Marker, Text = r.Script });
        Assert.Equal("3", t.Server.State.Find(U, Marker)!.Label);
    }

    [Fact]
    public void A_converted_piece_answers_its_module_type_kind_links_and_covers_widgets_below_its_top()
    {
        using var t = new Table();
        t.Store.RegisterDef(new PackManifest { Id = "testpack" }, new PieceDef
        {
            Id = "tok", Name = "Token", Tags = { "Infantry", "token" }, ModuleType = "Custom_Token", ModuleTop = 0.1, ModuleObjectTag = "Figurine", ModuleTags = ["Infantry"],
            Sources = new() { ["ImageURL"] = "https://example.test/a.png" },
        });
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "tok", Id = "p-000060", Pos = new(2, 2) });
        const string lua = """
            function onLoad()
              local c = self.getCustomObject()
              local d = self.getData()
              local own = { name = 'mine' }
              self.createButton({ click_function = 'x', function_owner = self, position = {0, 0.05, 0}, width = 300, height = 100 })
              self.createButton({ click_function = 'x', function_owner = self, position = {0, 0.15, 0}, width = 300, height = 100 })
              local spawned = spawnObject({ type = 'Custom_Token' })
              spawned.setCustomObject({ image = 'tok' })
              self.setName(self.name .. '|' .. own.name .. '|' .. c.image .. ',' .. c.thickness .. '|' .. d.Name .. ',' .. d.CustomImage.ImageURL)
              self.setDescription(self.type .. ',' .. self.tag .. ',' .. tostring(c.stand_up) .. ',' .. spawned.type .. ',' .. ({ type = 'own' }).type .. ',' .. table.concat(self.getTags(), ' ') .. ',' .. tostring(self.hasTag('token')))
            end
            function x() end
            """;
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var r = ModuleTranslator.Translate(lua, Ctx());
        var answers = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000060", Text = r.Script });
        System.Globalization.CultureInfo.CurrentCulture = saved;
        Assert.DoesNotContain(answers, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, "p-000060")!;
        Assert.Equal("Custom_Token|mine|https://example.test/a.png,0.2|Custom_Token,https://example.test/a.png", p.Label);
        Assert.Equal("Figurine,Figurine,true,Figurine,own,Infantry,false", p.Note);
        Assert.Single(p.Widgets);
        var spawned = t.Server.State.Layer(U).Last(q => q.Def == "tok");
        Assert.Empty(spawned.Tags ?? ["kind tags leaked"]);
    }

    [Fact]
    public void Links_made_at_run_time_find_the_kind_made_from_them_and_raw_data_spawns_that_kind()
    {
        using var t = new Table();
        var pack = new PackManifest { Id = "testpack" };
        t.Store.RegisterDef(pack, new PieceDef { Id = "foot", Name = "Foot", ModuleType = "Custom_Token", Sources = new() { ["ImageURL"] = "https://example.test/foot.png" } });
        t.Store.RegisterDef(pack, new PieceDef { Id = "gun", Name = "Gun", ModuleType = "Custom_Model", Sources = new() { ["MeshURL"] = "https://example.test/gun.obj", ["DiffuseURL"] = "https://example.test/gun.png" } });
        var picture = $"pictures/{new string('c', 64)}.png";
        Directory.CreateDirectory(Path.Combine(t.Store.PackageDir("sheets"), "pictures"));
        File.WriteAllBytes(Path.Combine(t.Store.PackageDir("sheets"), picture), [1]);
        t.Server.Scripts.FetchPicture = link => Task.FromResult<string?>(link == "https://example.test/other.png" ? picture : null);
        var label = Run(t, """
            function onLoad()
              local base = 'https://example.test/'
              if self.getDescription() == 'reloaded' then return end
              self.setDescription('reloaded')
              local a = spawnObjectData({ data = { Name = 'Custom_Token', Nickname = 'raw', CustomImage = { ImageURL = base .. 'foot.png' }, Transform = { posX = 2, posY = 1, posZ = 3 } } })
              local b = spawnObjectJSON({ json = JSON.encode({ Name = 'Custom_Token', Nickname = 'json', CustomImage = { ImageURL = base .. 'other.png' } }), position = { 4, 1, 4 } })
              local c = spawnObjectJSON({ json = 'not json' })
              self.setName(tostring(c == nil))
              self.setCustomObject({ mesh = base .. 'gun.obj', diffuse = base .. 'gun.png' })
              self.reload()
            end
            """);
        Assert.Equal("true", label);
        t.Server.Scripts.Tick(0.1);
        Assert.Equal("gun", t.Server.State.Find(U, P)!.Picture);
        var raw = t.Server.State.Layer(U).Single(q => q.Label == "raw");
        Assert.Equal(("foot", new Vec2(2, 3)), (raw.Def, raw.Pos));
        var fallback = t.Server.State.Layer(U).Single(q => q.Label == "json");
        Assert.Equal(("foot", picture), (fallback.Def, fallback.FacePicture));
    }

    [Fact]
    public void Selecting_players_are_the_seated_colours_whose_selection_holds_the_piece()
    {
        using var t = new Table();
        var label = Run(t, """
            function onLoad()
              local before = #self.getSelectingPlayers()
              self.addToPlayerSelection('Red')
              local after = self.getSelectingPlayers()
              self.setName(before .. ' ' .. #after .. ' ' .. tostring(after[1]))
            end
            """);
        Assert.Equal("0 1 Red", label);
    }

    [Fact]
    public void Time_reads_the_scripts_clock_and_frame_count()
    {
        using var t = new Table();
        t.Server.Scripts.Tick(2.0);
        t.Server.Scripts.Tick(2.25);
        const string lua = "function onLoad()\n  self.setName(Time.time .. '|' .. Time.delta_time .. '|' .. Time.fixed_delta_time .. '|' .. Time.frame_count)\nend\n";
        Assert.Equal("2.25|0.25|0.02|2", Run(t, lua));
    }
}
