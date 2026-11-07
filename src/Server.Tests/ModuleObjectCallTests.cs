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
    public void Time_reads_the_scripts_clock_and_frame_count()
    {
        using var t = new Table();
        t.Server.Scripts.Tick(2.0);
        t.Server.Scripts.Tick(2.25);
        const string lua = "function onLoad()\n  self.setName(Time.time .. '|' .. Time.delta_time .. '|' .. Time.fixed_delta_time .. '|' .. Time.frame_count)\nend\n";
        Assert.Equal("2.25|0.25|0.02|2", Run(t, lua));
    }
}
