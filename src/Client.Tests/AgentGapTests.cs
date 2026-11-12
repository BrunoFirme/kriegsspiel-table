using KTable.Core.Model;

namespace KTable.Client.Tests;

public partial class TableToolsTests
{
    async Task<PieceInstance> Placed(string kind, double x, double z, double turn = 0)
    {
        var r = await Call("place_piece", new { kind, x, z, turn });
        Assert.False(r.IsError, r.Text);
        return _s.View.Layer(Layers.Umpire).Last();
    }

    [Fact]
    public async Task Text_typed_into_a_script_s_input_box_reaches_its_on_change()
    {
        await Call("write_script", new { id = "p-1", text = "function on_load()\n  self.input{ face = 'front', w = 0.4, h = 0.2, value = '', on_change = 'typed' }\nend\nfunction typed(who, value, index, piece, typing) if not typing then print('orders: ' .. value) end end\n" });
        var typed = await Call("type_in_box", new { id = "p-1", text = "Advance" });
        Assert.False(typed.IsError, typed.Text);
        Assert.Contains("orders: Advance", typed.Text);
        Assert.True((await Call("type_in_box", new { id = "p-2", text = "x" })).IsError);
    }

    [Fact]
    public async Task A_number_key_sets_the_look_or_goes_to_on_number_typed_when_the_table_has_a_global_script()
    {
        var bn = await Placed("user.t.bn", 3, 3);
        var set = await Call("press_number", new { ids = new[] { bn.Id }, number = 2 });
        Assert.False(set.IsError, set.Text);
        Assert.Equal(1, _s.View.Find(Layers.Umpire, bn.Id)!.State);
        Assert.True((await Call("press_number", new { ids = new[] { bn.Id }, number = 3 })).IsError);
        await Call("write_script", new { text = "function on_number_typed(piece, who, n) print('typed ' .. n) return true end\n" });
        var heard = await Call("press_number", new { ids = new[] { bn.Id }, number = 1 });
        Assert.Contains("typed 1", heard.Text);
        Assert.Equal(1, _s.View.Find(Layers.Umpire, bn.Id)!.State);
    }

    [Fact]
    public async Task A_script_s_panel_is_read_and_its_button_and_dropdown_used_as_a_player_does()
    {
        await Call("write_script", new
        {
            text = "function on_load() ui.set([[<Button id=\"go\" onClick=\"go\">Go</Button><Dropdown id=\"d\" onValueChanged=\"pick\"><Option>Red</Option><Option>Blue</Option></Dropdown>]]) end\n" +
                   "function go(who, value, id) print('went ' .. id) end\nfunction pick(who, value, id) print('picked ' .. value) end\n",
        });
        Frame();
        var panels = await Call("read_panels", new { });
        Assert.Contains("\"go\"", panels.Text);
        Assert.Contains("Blue", panels.Text);
        Assert.Contains("onClick", panels.Text);
        Assert.Contains("went go", (await Call("use_panel", new { element = "go" })).Text);
        Assert.Contains("went go", (await Call("use_panel", new { element = "0" })).Text);
        Assert.Contains("picked Blue", (await Call("use_panel", new { element = "d", @event = "change", value = "blue" })).Text);
        Assert.True((await Call("use_panel", new { element = "d", @event = "change", value = "Green" })).IsError);
        Assert.True((await Call("use_panel", new { element = "nothing" })).IsError);
        Assert.Contains("No panels", (await Call("read_panels", new { id = "p-2" })).Text);
    }

    [Fact]
    public async Task A_script_s_dialog_is_listed_answered_and_closed()
    {
        await Call("write_script", new
        {
            text = "function on_load()\n  game.dialog(nil, { kind = 'options', title = 'Orders', options = { 'Advance', 'Hold' } }, function(answer) print('order ' .. tostring(answer)) end)\n" +
                   "  game.dialog(nil, { kind = 'confirm', title = 'Retire?' }, function(answer) print('retire ' .. tostring(answer)) end)\nend\n",
        });
        var open = await Call("answer_dialog", new { });
        Assert.Contains("Orders", open.Text);
        Assert.Contains("Retire?", open.Text);
        Assert.True((await Call("answer_dialog", new { answer = "Retreat" })).IsError);
        Assert.Contains("order Hold", (await Call("answer_dialog", new { answer = "hold" })).Text);
        Assert.Contains("retire nil", (await Call("answer_dialog", new { answer = "no" })).Text);
        Assert.Empty(_s.OpenDialogs);
        Assert.Contains("No script window", (await Call("answer_dialog", new { })).Text);
        Assert.True((await Call("answer_dialog", new { cancel = true })).IsError);
    }

    [Fact]
    public async Task An_effect_plays_once_and_stops_and_the_other_right_click_options_are_set()
    {
        var smoke = await Placed("user.t.smoke", 1, 1);
        Assert.False((await Call("play_effect", new { ids = new[] { smoke.Id } })).IsError);
        Assert.True((await Call("play_effect", new { ids = new[] { "p-1" } })).IsError);
        var r = await Call("set_piece_options", new { id = smoke.Id, running = false, carries = false, box_selected = false, shows_move = true });
        Assert.False(r.IsError, r.Text);
        var p = _s.View.Find(Layers.Umpire, smoke.Id)!;
        Assert.Equal((true, true, true, true), (p.EffectOff, p.NotSticky, p.NotBoxSelected, p.MeasuresMoves));
        Assert.True((await Call("set_piece_options", new { id = "p-1", running = true })).IsError);
        Assert.True((await Call("set_piece_options", new { id = "p-1", favourite = true })).IsError);
    }

    [Fact]
    public async Task A_kind_s_markers_go_down_beside_the_piece_turned_with_it()
    {
        var gun = await Placed("user.t.gun", 10, 10, 180);
        var r = await Call("place_markers", new { id = gun.Id });
        Assert.False(r.IsError, r.Text);
        var limber = _s.View.Layer(Layers.Umpire).Last();
        Assert.Equal(("user.t.limber", 10.0, 12.0, 180.0), (limber.Def, Math.Round(limber.Pos.X, 6), Math.Round(limber.Pos.Z, 6), limber.RotDeg));
        Assert.True((await Call("place_markers", new { id = gun.Id, kinds = new[] { "Caisson" } })).IsError);
        Assert.True((await Call("place_markers", new { id = "p-1" })).IsError);
    }

    [Fact]
    public async Task Every_held_piece_comes_out_in_a_row_beside_the_holder()
    {
        var bn = await Placed("user.t.bn", 20, 0);
        await Call("stow_pieces", new { holder = "p-1", ids = new[] { "p-2", bn.Id } });
        var r = await Call("unstow_piece", new { holder = "p-1", all = true });
        Assert.False(r.IsError, r.Text);
        var holder = _s.View.Find(Layers.Umpire, "p-1")!;
        Assert.Null(holder.Stowed is { Count: > 0 } ? holder.Stowed : null);
        Assert.Equal(holder.Pos.X + TakeOut.From, _s.View.Find(Layers.Umpire, "p-2")!.Pos.X, 6);
        Assert.Equal(holder.Pos.X + TakeOut.From + TakeOut.Apart, _s.View.Find(Layers.Umpire, bn.Id)!.Pos.X, 6);
        Assert.True((await Call("unstow_piece", new { holder = "p-1", all = true })).IsError);
    }

    [Fact]
    public async Task A_book_s_page_gets_a_highlight_and_loses_it()
    {
        var book = await Placed("user.t.orders", 0, 0);
        Assert.False((await Call("set_piece_values", new { id = book.Id, book_highlight = new[] { 0.1, 0.2, 0.5, 0.6 } })).IsError);
        Assert.Equal(new[] { 0.1, 0.2, 0.5, 0.6 }, _s.View.Find(Layers.Umpire, book.Id)!.BookHighlight);
        Assert.False((await Call("set_piece_values", new { id = book.Id, book_highlight = Array.Empty<double>() })).IsError);
        Assert.Null(_s.View.Find(Layers.Umpire, book.Id)!.BookHighlight);
        Assert.True((await Call("set_piece_values", new { id = book.Id, book_highlight = new[] { 0.1, 2.0, 0.5, 0.6 } })).IsError);
        Assert.True((await Call("set_piece_values", new { id = "p-1", book_highlight = new[] { 0.1, 0.2, 0.5, 0.6 } })).IsError);
    }

    [Fact]
    public async Task The_pen_s_shapes_are_drawn_and_see_through_ink_keeps_its_opacity()
    {
        var corners = new[] { new[] { 0.0, 0.0 }, new[] { 4.0, 2.0 } };
        Assert.False((await Call("draw", new { points = corners, shape = "rectangle" })).IsError);
        Assert.Equal(5, _s.View.DrawLayer(Layers.Umpire)[^1].Points.Count);
        Assert.False((await Call("draw", new { points = corners, shape = "ellipse", colour = "#336699", opacity = 0.5 })).IsError);
        Assert.Equal("#33669980", _s.View.DrawLayer(Layers.Umpire)[^1].Color);
        var round = new[] { new[] { 0.0, 0.0 }, new[] { 4.0, 0.0 }, new[] { 2.0, 3.0 } };
        Assert.False((await Call("draw", new { points = round, shape = "area" })).IsError);
        Assert.True(_s.View.DrawLayer(Layers.Umpire)[^1].Fill);
        int before = _s.View.DrawLayer(Layers.Umpire).Count;
        Assert.False((await Call("draw", new { points = round, shape = "curved_arrow" })).IsError);
        Assert.Equal(before + 2, _s.View.DrawLayer(Layers.Umpire).Count);
        Assert.True(_s.View.DrawLayer(Layers.Umpire)[^2].Points.Count > round.Length);
        Assert.True((await Call("draw", new { points = round, shape = "rectangle" })).IsError);
        Assert.True((await Call("draw", new { points = corners, shape = "star" })).IsError);
        Assert.True((await Call("draw", new { points = corners, colour = "red", opacity = 0.5 })).IsError);
    }

    [Fact]
    public async Task The_scenario_s_settings_and_the_people_at_the_table_are_read()
    {
        await Call("set_grid", new { kind = "hex", cell = 12.0 });
        await Call("set_zone", new { name = "North", owner = "Ann", polygon = new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 10.0 } } });
        var read = await Call("read_scenario", new { });
        Assert.False(read.IsError, read.Text);
        Assert.Contains("\"hex\"", read.Text);
        Assert.Contains("\"owner\": \"Ann\"", read.Text);
        Assert.Contains("\"minutes_per_turn\"", read.Text);
        var people = await Call("list_people", new { });
        Assert.Contains("\"role\": \"host\"", people.Text);
    }

    [Fact]
    public async Task A_light_preset_is_taken_and_a_backdrop_picture_needs_the_app()
    {
        var lit = await Call("set_light", new { preset = "sundown" });
        Assert.False(lit.IsError, lit.Text);
        Assert.True(LightPresets.Same(_s.View.Lighting, LightPresets.Named(LightPresets.Sundown)!));
        Assert.True((await Call("set_light", new { preset = "noon on mars" })).IsError);
        var picture = await Call("set_light", new { backdrop = "C:/pictures/panorama.png" });
        Assert.True(picture.IsError);
        Assert.Contains("Scenario properties", picture.Text);
    }

    [Fact]
    public async Task The_sun_is_placed_by_bearing_and_height_read_back_and_kept_by_a_sky_backdrop()
    {
        var set = await Call("set_light", new { sun_from = 100, sun_height = 30 });
        Assert.False(set.IsError, set.Text);
        Assert.Equal((100.0, 30.0), (_s.View.Lighting.SunFrom, _s.View.Lighting.SunHeight));
        var read = await Call("read_scenario", new { });
        Assert.Contains("\"sun_from\": 100", read.Text);
        Assert.Contains("\"sun_height\": 30", read.Text);
        await Call("set_light", new { backdrop = "open-sky-night" });
        Assert.Equal((100.0, 30.0), (_s.View.Lighting.SunFrom, _s.View.Lighting.SunHeight));
        await Call("set_light", new { preset = "morning sun" });
        Assert.Equal((90.0, 20.0), (_s.View.Lighting.SunFrom, _s.View.Lighting.SunHeight));
        await Call("set_light", new { sun_from = -45, sun_height = 120 });
        Assert.Equal((315.0, Lighting.HighestSun), (_s.View.Lighting.SunFrom, _s.View.Lighting.SunHeight));
    }
}
