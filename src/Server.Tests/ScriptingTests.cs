using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class ScriptingTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static List<Outgoing> SetScript(Table t, string script, long by = Table.Host) => t.Server.Handle(by, new SetScript { Layer = U, Id = P, Text = script });

    const string Counter = """
        function on_load(saved)
          self.clear_widgets()
          self.button{ face = "back", x = 0.1, z = 0.2, w = 0.2, h = 0.1, label = "Ammo " .. self.counters.str, on_click = "up", tooltip = "Ammo" }
          self.input{ face = "back", x = 0, z = -0.2, w = 0.3, h = 0.05, value = self.text.name or "", on_change = "rename" }
        end
        function up(who, alt, index)
          self.set_counter("str", self.counters.str + (alt and -1 or 1))
          self.edit(1, { label = "Ammo " .. self.counters.str })
        end
        function rename(who, value, index)
          self.set_text("name", value)
        end
        function on_save() return json.encode({ n = self.counters.str }) end
        """;

    [Fact]
    public void A_script_is_asked_for_its_state_only_after_it_has_run()
    {
        using var t = new Table();
        SetScript(t, Counter);
        t.Server.Scripts.Save();
        Assert.Equal(1, t.Server.Scripts.AskedLast);
        var state = t.Server.State.Find(U, P)!.ScriptState;
        Assert.Contains("\"n\"", state);

        t.Server.Scripts.Save();
        Assert.Equal(0, t.Server.Scripts.AskedLast);
        Assert.Equal(state, t.Server.State.Find(U, P)!.ScriptState);

        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 1 });
        t.Server.Scripts.Save();
        Assert.Equal(1, t.Server.Scripts.AskedLast);
        Assert.NotEqual(state, t.Server.State.Find(U, P)!.ScriptState);
    }

    [Fact]
    public void A_script_draws_its_widgets_when_it_loads()
    {
        using var t = new Table();
        var res = SetScript(t, Counter);
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(2, p.Widgets.Count);
        Assert.Equal("Ammo 8", p.Widgets[0].Label);
        Assert.Equal("back", p.Widgets[0].Face);
        Assert.Equal("input", p.Widgets[1].Kind);
        Assert.Equal("UMPIRE-SECRET-NAME", p.Widgets[1].Value);
        var sw = Assert.Single(Flat(t.Server.Log).OfType<SetWidgets>());
        Assert.Equal(Table.Host, t.Server.Log[^1].Actor);
        Assert.Equal(2, sw.Widgets.Count);
        Assert.Contains(res, o => o.PeerId == Table.Obs && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<SetWidgets>().Any());
    }

    [Fact]
    public void What_a_script_draws_belongs_to_the_state_it_drew_it_on()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        SetScript(t, Counter);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(1, p.State);
        Assert.Equal(2, p.Widgets.Count);
        Assert.Equal(0, p.DrawnOnState);

        SetScript(t, """
            function on_load(saved) self.clear_widgets() self.button{ label = "base" } end
            function on_state(old, new) self.clear_widgets() self.button{ label = "state " .. new } end
            """);
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(0, p.DrawnOnState);
        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 2 });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal("state 3", p.Widgets[0].Label);
        Assert.Equal(2, p.DrawnOnState);
    }

    [Fact]
    public void A_click_runs_the_handler_and_its_effects_are_one_batch_under_the_clicker()
    {
        using var t = new Table();
        SetScript(t, Counter);
        var p = t.Server.State.Find(U, P)!;
        int before = p.Counters["str"];
        var logBefore = t.Server.Log.Count;
        var res = t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal(before + 1, p.Counters["str"]);
        Assert.Equal("Ammo 9", p.Widgets[0].Label);
        Assert.Equal(logBefore + 2, t.Server.Log.Count);
        var batch = Assert.IsType<Batch>(t.Server.Log[^1]);
        Assert.Equal(Table.Ump3, batch.Actor);
        Assert.Contains(batch.Items, c => c is SetCounter);
        Assert.Contains(batch.Items, c => c is SetWidgets);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1, Alt = true });
        Assert.Equal(before, p.Counters["str"]);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var seq = t.Server.Log[^1].Seq;
        t.Server.Handle(Table.Ump3, new Undo { TargetSeq = seq });
        Assert.Equal(before, p.Counters["str"]);
        Assert.Equal("Ammo " + before, p.Widgets[0].Label);
    }

    [Fact]
    public void Typing_into_an_input_sets_its_value_and_runs_on_change()
    {
        using var t = new Table();
        SetScript(t, Counter);
        var p = t.Server.State.Find(U, P)!;
        t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 2, Value = "2nd Bn" });
        Assert.Equal("2nd Bn", p.Widgets[1].Value);
        Assert.Equal("2nd Bn", p.Text["name"]);
    }

    [Fact]
    public void On_save_writes_the_state_once_and_on_load_gets_it_back()
    {
        using var t = new Table();
        SetScript(t, Counter);
        var p = t.Server.State.Find(U, P)!;
        var res = t.Server.Scripts.Save();
        Assert.Equal("{\"n\":8}", p.ScriptState);
        Assert.Single(Flat(t.Server.Log).OfType<SetScriptState>());
        Assert.Empty(t.Server.Scripts.Save());
        SetScript(t, "function on_load(saved) self.set_text('name', saved) end");
        Assert.Equal("{\"n\":8}", p.Text["name"]);
    }

    [Fact]
    public void Replay_without_the_runtime_reproduces_the_table()
    {
        using var t = new Table();
        SetScript(t, Counter);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 2, Value = "2nd Bn" });
        t.Server.Scripts.Save();
        var replayed = SaveLoader.Replay(t.Scenario, t.Server.Log);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(replayed));
    }

    [Fact]
    public void A_runaway_script_is_stopped_and_switched_off()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) self.clear_widgets() self.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = 'go', on_click = 'go' } end function go() while true do end end");
        var res = t.Server.Handle(Table.Lead, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var say = Assert.Single(Flat(t.Server.Log).OfType<Say>());
        Assert.Contains("stopped", say.Text);
        Assert.Contains(P, say.Text);
        Assert.Equal("u:", say.To);
        Assert.DoesNotContain(res, o => o.PeerId == Table.Obs && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<Say>().Any());
        Assert.Contains(res, o => o.PeerId == Table.Lead && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<Say>().Any());
    }

    [Fact]
    public void A_hostile_script_is_refused_before_it_enters_the_state()
    {
        using var t = new Table();
        var res = SetScript(t, "function on_load(s) WebRequest.get('https://example.invalid/', function() end) end");
        var err = Assert.IsType<ErrorMessage>(Assert.Single(res).Message);
        Assert.Contains("script refused", err.Text);
        Assert.Null(t.Server.State.Find(U, P)!.Script);
        Assert.DoesNotContain(t.Server.Log, c => c is SetScript);
    }

    [Fact]
    public void An_error_becomes_a_transcript_line_naming_the_piece()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) error('boom') end");
        var say = Assert.Single(Flat(t.Server.Log).OfType<Say>());
        Assert.Contains("boom", say.Text);
        Assert.Contains(P, say.Text);
        Assert.StartsWith("Script error", say.Text);
    }

    [Fact]
    public void The_sandbox_has_no_load_io_require_or_system_os()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) self.set_text('name', tostring(load == nil and loadstring == nil and dofile == nil and require == nil and io == nil and debug == nil and os.execute == nil and os.time ~= nil and json ~= nil)) end");
        Assert.Equal("true", t.Server.State.Find(U, P)!.Text["name"]);
    }

    [Fact]
    public void Spawn_and_timers()
    {
        using var t = new Table();
        SetScript(t, """
            function on_load(s)
              local m = board.spawn{ def = "marker", x = 3, z = 4, label = "smoke", guid = "feed01" }
              self.set_text("name", m.id .. " " .. tostring(board.find("feed01") == m))
              wait.seconds(1, function() m.delete() end)
            end
            """);
        var p = t.Server.State.Find(U, P)!;
        var smoke = Assert.Single(t.Server.State.Layer(U), x => x.Label == "smoke");
        Assert.Equal("feed01", smoke.Guid);
        Assert.Equal(smoke.Id + " true", p.Text["name"]);
        Assert.Empty(t.Server.Scripts.Tick(0.5));
        Assert.NotNull(t.Server.State.Find(U, smoke.Id));
        var res = t.Server.Scripts.Tick(1.5);
        Assert.Null(t.Server.State.Find(U, smoke.Id));
        Assert.Equal(0, t.Server.Log[^1].Actor);
        Assert.Contains(res, o => o.Message is CommandMessage cm && cm.Command is DeletePiece);
    }

    [Fact]
    public void The_global_script_sees_turns_and_keeps_its_own_state()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "n = 0 function on_turn(turn) n = turn game.say('turn ' .. turn) end function on_save() return tostring(n) end" });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        t.Server.Handle(Table.Host, new AdvanceClock());
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "turn 1");
        t.Server.Scripts.Save();
        Assert.Equal("1", t.Server.State.ScriptState);
        var denied = t.Server.Handle(Table.Ump3, new SetGlobalScript { Text = "x = 1" });
        Assert.Contains("not allowed", Assert.IsType<ErrorMessage>(Assert.Single(denied).Message).Text);
    }

    [Fact]
    public void A_tts_global_on_object_rotate_hears_every_piece_turned_in_place()
    {
        var tts = "function onObjectRotate(object, spin, flip, player_color, old_spin, old_flip) print(object.getGUID() .. ' ' .. spin .. ' ' .. old_spin) end";
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate(tts, new KTable.Server.Scripting.Translate.TranslateContext { IsGlobal = true });
        Assert.Contains("function on_rotate(__piece, __rot, __old, __who)", tr.Script);
        using var t = new Table();
        Assert.DoesNotContain(t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_rotate(piece, rot, old_rot, who) game.say(piece.id .. ' ' .. rot .. ' ' .. old_rot) end" }), o => o.Message is ErrorMessage);
        var p = t.Server.State.Layer(Layers.Umpire)[0];
        t.Server.Handle(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = p.Id, Pos = p.Pos, RotDeg = 90 });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), x => x.Text == $"{p.Id} 90 0");
    }

    [Fact]
    public void Scripts_start_from_the_state_on_load_and_after_a_save_is_loaded()
    {
        using var t = new Table();
        SetScript(t, Counter);
        t.Server.Handle(Table.Host, new AdvanceClock());
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Equal(9, t.Server.State.Find(U, P)!.Counters["str"]);
        t.Server.LoadSave(t.Server.Turns[0].Unpack(), new List<TurnRecord>(), "Turn 0");
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(8, p.Counters["str"]);
        Assert.Equal("Ammo 8", p.Widgets[0].Label);
        var again = new GameServer(Json.Clone(t.Server.State), t.Store);
        again.Scripts.Load();
        Assert.Equal("Ammo 8", again.State.Find(U, P)!.Widgets[0].Label);
    }

    [Fact]
    public void Set_script_is_allowed_only_from_an_action_or_on_a_spawned_piece()
    {
        using var t = new Table();
        SetScript(t, """
            function on_load(s)
              local m = board.spawn{ def = "marker", x = 1, z = 1, label = "child", script = "function on_load(s) self.set_text('name', 'installed') end" }
              self.menu_add("Rewrite", "rewrite")
              local other = board.find("p-000003")
              local ok, err = pcall(function() other.set_script("x = 1") end)
              self.set_text("name", ok and "allowed" or "refused")
            end
            function rewrite(who) board.find("p-000003").set_script("function on_load(s) self.set_text('name', 'rewritten') end") end
            """);
        var child = Assert.Single(t.Server.State.Layer(U), x => x.Label == "child");
        Assert.Equal("installed", child.Text["name"]);
        Assert.Equal("refused", t.Server.State.Find(U, P)!.Text["name"]);
        t.Server.Handle(Table.Host, new PickMenuItem { Layer = U, Id = P, Index = 1 });
        Assert.Equal("rewritten", t.Server.State.Find(U, "p-000003")!.Text["name"]);
    }

    [Fact]
    public void Decals_go_on_a_face_from_the_game_s_pictures_and_an_unknown_one_is_an_error()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) self.set_decals{ { picture = 'arms', name = 'Arms', x = 0.3, z = -0.2, w = 0.25, h = 0.2, rot = 15, face = 'back' } } end");
        var d = Assert.Single(t.Server.State.Find(U, P)!.Decals);
        Assert.Equal(("arms", "Arms", "back", 0.3, -0.2, 0.25, 0.2, 15.0), (d.Picture, d.Name, d.Face, d.X, d.Z, d.W, d.H, d.Rot));
        SetScript(t, "function on_load(s) local ok, err = pcall(self.set_decals, { { picture = 'nowhere' } }); self.set_text('name', tostring(err)) end");
        Assert.Contains("no picture 'nowhere'", t.Server.State.Find(U, P)!.Text["name"]);
        Assert.Single(t.Server.State.Find(U, P)!.Decals);
    }

    [Fact]
    public void A_TTS_input_is_drawn_as_a_button_scaled_up_and_its_decals_mirror_its_buttons_in_x()
    {
        using var t = new Table();
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("""
            function onLoad()
              self.createButton({ click_function = "none", function_owner = self, label = "B", position = {0.5, 0.2, 0}, width = 400, height = 200, font_size = 200 })
              self.createInput({ input_function = "none", function_owner = self, value = "I", position = {0.5, 0.2, 0}, width = 400, height = 200, font_size = 200 })
              self.setDecals({ { name = "D", url = "arms", position = {0.5, 0.2, 0}, rotation = {90, 180, 0}, scale = {1, 1, 1} } })
            end
            """);
        SetScript(t, tr.Script);
        var p = t.Server.State.Find(U, P)!;
        var (b, i) = (p.Widgets[0], p.Widgets[1]);
        Assert.Equal(2.2 * b.W, i.W, 6);
        Assert.Equal(2.2 * b.Font, i.Font, 6);
        Assert.Equal(0.87 * b.W / 2, b.Font, 6);
        Assert.Equal(b.X, i.X, 6);
        Assert.Equal(-b.X, Assert.Single(p.Decals).X, 6);
    }

    [Fact]
    public void A_TTS_input_turned_over_on_top_is_locked_text_on_the_front_and_turning_it_back_unlocks_it()
    {
        using var t = new Table();
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("""
            function onLoad()
              self.createInput({ input_function = "typed", function_owner = self, value = "4", position = {0.5, 0.2, 0}, width = 400, height = 200, font_size = 200 })
              self.createInput({ input_function = "typed", function_owner = self, value = "U", position = {0.5, -0.2, 0}, rotation = {0, 0, 180}, width = 400, height = 200 })
              self.createButton({ click_function = "lock", function_owner = self, label = "L", position = {0, 0.2, 0}, width = 400, height = 200 })
              self.createButton({ click_function = "unlock", function_owner = self, label = "O", position = {0, 0.2, 0.5}, width = 400, height = 200 })
            end
            function lock() self.editInput({ index = 0, rotation = {0, 0, 180}, scale = {-1, 1, 1} }) end
            function unlock() self.editInput({ index = 0, rotation = {0, 0, 0}, scale = {1, 1, 1} }) end
            function typed(obj, color, value) end
            """);
        SetScript(t, tr.Script);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(("back", false), (p.Widgets[1].Face, p.Widgets[1].Locked));
        var x = p.Widgets[0].X;
        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 3 });
        Assert.Equal(("front", true, x), (p.Widgets[0].Face, p.Widgets[0].Locked, p.Widgets[0].X));
        var refused = t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 1, Value = "9" });
        Assert.Contains(refused, o => o.Message is ErrorMessage);
        Assert.Equal("4", p.Widgets[0].Value);
        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 4 });
        t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 1, Value = "9" });
        Assert.Equal(("front", false, "9"), (p.Widgets[0].Face, p.Widgets[0].Locked, p.Widgets[0].Value));
    }

    [Fact]
    public void TTS_handlers_named_at_run_time_get_TTS_arguments_and_getInputs_holds_what_was_typed()
    {
        using var t = new Table();
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("""
            function onLoad()
              local typed = "typed_1"
              _G[typed] = function(obj, color, value, selected) obj.setName(value .. "/" .. color .. "/" .. tostring(selected)) end
              self.createInput({ input_function = typed, function_owner = self, value = "a", position = {0, 0.2, 0}, width = "400", height = "200", font_size = "100" })
              local click = "pressed"
              self.createButton({ click_function = click, function_owner = self, label = "B", position = {0, 0.2, 0.5}, width = 400, height = 200 })
              self.createButton({ click_function = "named", function_owner = self, label = "N", position = {0, 0.2, -0.5}, width = 400, height = 200 })
            end
            function pressed(obj, color, alt) obj.setName(obj.getInputs()[1].value .. "!" .. color .. tostring(alt)) end
            function named(obj, color, alt) obj.setName("named " .. color) end
            """);
        SetScript(t, tr.Script);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(2.2 * p.Widgets[1].W, p.Widgets[0].W, 6);
        Assert.Equal(2.2 * p.Widgets[1].H, p.Widgets[0].H, 6);
        t.Server.Handle(Table.Host, new EditWidget { Layer = U, Id = P, Index = 1, Value = "typed" });
        Assert.Equal("typed/White/false", p.Label);
        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 2 });
        Assert.Equal("typed!Whitefalse", p.Label);
        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 3 });
        Assert.Equal("named White", p.Label);
    }

    [Fact]
    public void A_TTS_script_s_decals_come_across_in_piece_units_and_addDecal_adds_one()
    {
        using var t = new Table();
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("""
            function onLoad()
              self.setDecals({ { name = "Arms", url = "arms", position = {0.5, 0.2, -0.4}, rotation = {90, 180, 0}, scale = {0.6, 0.4, 1} } })
              self.addDecal({ name = "Under", url = "arms", position = {0.5, -0.2, 0}, rotation = {270, 200, 0}, scale = {1, 1, 1} })
              self.addDecal({ name = "Web", url = "https://example.com/x.png", position = {0, 0.2, 0}, rotation = {90, 180, 0}, scale = {1, 1, 1} })
            end
            """);
        Assert.Empty(tr.Untranslated);
        SetScript(t, tr.Script);
        var ds = t.Server.State.Find(U, P)!.Decals;
        Assert.Equal(2, ds.Count);
        Assert.Equal(("front", 0.0), (ds[0].Face, ds[0].Rot));
        Assert.Equal(ds[0].W / 0.6 * 0.4, ds[0].H, 6);
        Assert.Equal(-ds[0].X, ds[1].X, 6);
        Assert.Equal(("back", 20.0), (ds[1].Face, ds[1].Rot));
    }

    [Fact]
    public void A_TTS_script_s_decal_fitted_to_an_unevenly_scaled_token_covers_the_token()
    {
        const double TokenArea = 14, ScaleX = 0.56, ScaleZ = 0.3928227, Fit = 0.02;
        const int PictureW = 678, PictureH = 103;
        using var t = new Table();
        var def = t.Store.Def("inf")!;
        double aspect = PictureW / (double)PictureH;
        def.Shape = "alpha";
        def.Image = "line";
        def.Size = new Size2(Math.Sqrt(TokenArea * aspect) * ScaleX, Math.Sqrt(TokenArea / aspect) * ScaleZ);
        t.Store.PackOfDef("inf")!.Assets["line"] = new AssetEntry { File = "assets/line.png", W = PictureW, H = PictureH };
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("""
            function onLoad()
              self.setDecals({ { name = "Outline", url = "arms", position = {0, 0.055, 0}, rotation = {90, 180, 0}, scale = {9.6, 1.45, 1} } })
            end
            """);
        SetScript(t, tr.Script);
        var d = Assert.Single(t.Server.State.Find(U, P)!.Decals);
        Assert.InRange(d.W / def.Size.W, 1 - Fit, 1 + Fit);
        Assert.InRange(d.H / def.Size.H, 1 - Fit, 1 + Fit);
    }

    [Fact]
    public void A_TTS_decal_on_the_web_is_fetched_and_a_picture_dialog_answers_with_a_fetched_link()
    {
        using var t = new Table();
        var name = $"pictures/{new string('a', 64)}.png";
        Directory.CreateDirectory(Path.Combine(t.Store.PackageDir("sheets"), "pictures"));
        File.WriteAllBytes(Path.Combine(t.Store.PackageDir("sheets"), name), [1]);
        var asked = new List<string>();
        t.Server.Scripts.FetchPicture = link => { asked.Add(link); return Task.FromResult<string?>(link.EndsWith("good.png") ? name : null); };
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("""
            function onLoad()
              self.setDecals({ { name = "Arms", url = "arms", position = {0, 0.2, 0}, rotation = {90, 180, 0}, scale = {1, 1, 1} },
                               { name = "Web", url = "https://example.com/good.png", position = {0, 0.2, 0}, rotation = {90, 180, 0}, scale = {1, 1, 1} } })
              self.createButton({ click_function = "ask", function_owner = self, label = "B", position = {0, 0.2, 0}, width = 400, height = 200 })
            end
            function ask(obj, color)
              Player[color].showInputDialog("Set image URL", function(text) self.setDecals({ { name = "New", url = text, position = {0, 0.2, 0}, rotation = {90, 180, 0}, scale = {1, 1, 1} } }) end)
            end
            """);
        SetScript(t, tr.Script);
        Assert.Equal(["arms"], t.Server.State.Find(U, P)!.Decals.Select(d => d.Picture));
        t.Server.Scripts.Tick(0.1);
        Assert.Equal(["arms", name], t.Server.State.Find(U, P)!.Decals.Select(d => d.Picture));
        var logged = t.Server.Log.Count;
        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var dialog = Assert.Single(Flat(t.Server.Log.Skip(logged)).OfType<ShowDialog>());
        Assert.Equal("picture", dialog.Kind);
        t.Server.Handle(Table.Host, new DialogResult { Token = dialog.Token, Value = "https://example.com/good.png" });
        t.Server.Scripts.Tick(0.2);
        Assert.Equal([name], t.Server.State.Find(U, P)!.Decals.Select(d => d.Picture));
        t.Server.Handle(Table.Host, new ClickWidget { Layer = U, Id = P, Index = 1 });
        t.Server.Handle(Table.Host, new DialogResult { Token = Flat(t.Server.Log).OfType<ShowDialog>().Last().Token, Value = "https://example.com/bad.png" });
        var said = t.Server.Scripts.Tick(0.3);
        Assert.Equal([name], t.Server.State.Find(U, P)!.Decals.Select(d => d.Picture));
        Assert.Contains(Flat(said.Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command)).OfType<Say>(), x => x.Text == "No picture came from that link.");
    }

    [Fact]
    public void Lines_are_drawn_in_the_pieces_frame_and_translated_from_TTS_units()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) self.set_lines{ { points = { {x=0,z=1}, {1,0}, {x=0,z=-1} }, color = 'red', width = 0.2, loop = true } } end");
        var p = t.Server.State.Find(U, P)!;
        var ln = Assert.Single(p.Lines);
        Assert.Equal(3, ln.Points.Count);
        Assert.Equal(new KTable.Core.Model.Vec2(1, 0), ln.Points[1]);
        Assert.Equal("#DA1917", ln.Color);
        Assert.Equal(0.2, ln.Width);
        Assert.True(ln.Loop);
        Assert.Single(Flat(t.Server.Log).OfType<SetLines>());
        var tr = KTable.Server.Scripting.Translate.TtsTranslator.Translate("function onLoad() self.setVectorLines({ { points = { {2,0,0}, {0,0,2}, {-2,0,0} }, color = {1,0,0,0.5}, thickness = 0.3 } }) end");
        Assert.Empty(tr.Untranslated);
        SetScript(t, tr.Script);
        ln = Assert.Single(p.Lines);
        Assert.Equal(new KTable.Core.Model.Vec2(-1, 0), ln.Points[0]);
        Assert.Equal(new KTable.Core.Model.Vec2(0, 1), ln.Points[1]);
        Assert.Equal("#FF000080", ln.Color);
        Assert.Equal(0.3, ln.Width);
    }

    [Fact]
    public void Tags_and_memo_are_the_pieces_own_and_a_data_copy_keeps_them()
    {
        using var t = new Table();
        var res = SetScript(t, """
            function on_load(s)
              self.add_tag("Source") self.add_tag("France") self.add_tag("France")
              self.set_memo('{"Unit":{"StateID":1}}')
              self.clear_widgets()
              self.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = "Copy", on_click = "copy" }
            end
            function copy(who, alt, index)
              local d = compat.get_data(self)
              d.Tags = { "France" }  d.Memo = "2"  d.LuaScript = ""
              local c = compat.spawn_object_data({ data = d, position = { 3, 0, 4 } })
              c.set_name("copy of " .. tostring(#board.pieces{ tag = "France" }))
              self.remove_tag("Source")
            end
            """);
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(new[] { "Source", "France" }, p.Tags);
        Assert.Equal("{\"Unit\":{\"StateID\":1}}", p.Memo);
        Assert.Null(t.Server.State.Find(U, "p-000003")!.Tags);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        var copy = Assert.Single(t.Server.State.Layer(U), x => x.Label?.StartsWith("copy of") == true);
        Assert.Equal("inf", copy.Def);
        Assert.Equal(new[] { "France" }, copy.Tags);
        Assert.Equal("2", copy.Memo);
        Assert.Null(copy.Script);
        Assert.Equal(new Vec2(3, 4), copy.Pos);
        Assert.Equal("copy of 2", copy.Label);
        Assert.Equal(new[] { "France" }, p.Tags);
        Assert.Equal(Table.Ump3, t.Server.Log[^1].Actor);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
    }

    [Fact]
    public void Json_encode_reads_the_same_on_every_machine()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            using var t = new Table();
            SetScript(t, """
                function on_load(s)
                  local text = json.encode({ h = 0.05, n = -2, list = { 1, 2.5, "x", true }, sparse = { [3] = "c" }, deep = { a = { b = {} } } })
                  print(text)
                  local back = json.decode(text)
                  print(tostring(back.h) .. " " .. tostring(back.list[2]) .. " " .. tostring(back.sparse[3]) .. " " .. tostring(back.sparse[1]))
                end
                """);
            var lines = t.Server.Scripts.Console.Where(l => !l.StartsWith("Script error")).ToList();
            Assert.DoesNotContain(t.Server.Scripts.Console, l => l.StartsWith("Script error"));
            var text = Assert.Single(lines, l => l.StartsWith("{"));
            Assert.Contains("\"h\":0.05", text);
            Assert.Contains("\"n\":-2", text);
            Assert.Contains("\"list\":[1,2.5,\"x\",true]", text);
            Assert.Contains("\"sparse\":[null,null,\"c\"]", text);
            Assert.Contains("0.05 2.5 c nil", lines[^1]);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Hotkeys_and_the_table_menu_reach_the_script()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = """
            function on_load(s)
              game.hotkey("Magic Hotkey #3", function(who, piece, pos, up)
                if piece then piece.set_name(who.name .. " " .. tostring(#game.selected(who)) .. " " .. tostring(pos.x) .. " " .. tostring(up)) end
              end)
              game.hotkey("Release too", function(who, piece, pos, up) game.say("release " .. tostring(up)) end, true)
              game.menu_add("Casualty Report", function(who, pos) game.say("report at " .. tostring(pos.z) .. " by " .. who.name) end)
            end
            """ });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        Assert.Equal(new[] { "Magic Hotkey #3", "Release too" }, t.Server.State.Hotkeys.Select(h => h.Label));
        Assert.False(t.Server.State.Hotkeys[0].OnUp);
        Assert.True(t.Server.State.Hotkeys[1].OnUp);
        Assert.Equal("Casualty Report", Assert.Single(t.Server.State.MenuItems).Label);
        Assert.Single(Flat(t.Server.Log).OfType<SetHotkeys>());
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 1, Layer = U, Piece = P, Pos = new Vec2(4, 5), Selected = new() { P, "p-000003" } });
        Assert.Equal("Umpire Three 2 4 false", t.Server.State.Find(U, P)!.Label);
        Assert.Equal(Table.Ump3, t.Server.Log[^1].Actor);
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 1, Up = true, Layer = U, Piece = P, Pos = new Vec2(9, 9) });
        Assert.Equal("Umpire Three 2 4 false", t.Server.State.Find(U, P)!.Label);
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 2, Up = true });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "release true");
        t.Server.Handle(Table.Ump3, new PickGlobalMenuItem { Index = 1, Pos = new Vec2(1, 2) });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "report at 2 by Umpire Three");
        Assert.Contains(t.Server.Handle(Table.Ump3, new Hotkey { Index = 9 }), o => o.Message is ErrorMessage);
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) game.hotkey('Only one', function() end) end" });
        Assert.Equal(new[] { "Only one" }, t.Server.State.Hotkeys.Select(h => h.Label));
        Assert.Empty(t.Server.State.MenuItems);
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
    }

    [Fact]
    public void Number_keys_chat_selection_and_scripting_buttons_reach_the_global_script()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = """
            function on_number_typed(piece, who, n)
              piece.set_text("name", "typed " .. n .. " by " .. who.name)
              if n == 9 then return true end   -- keep the state
              if n == 2 then game.select(who, piece, false) end
            end
            function on_chat(text, who) game.say("heard " .. text .. " from " .. who.name, { to = who }) end
            function on_scripting_button_down(index, who) game.say("button " .. index .. " down by " .. who.name) end
            """ });
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("Script error"));
        t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = P, Number = 3 });
        Assert.Equal("typed 3 by Umpire Three", t.Server.State.Find(U, P)!.Text["name"]);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<SetState>(), s => s.Id == P);
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Pos = new Vec2(5, 5), Id = "p-000090" });
        t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = "p-000090", Number = 4 });
        Assert.Equal(3, t.Server.State.Find(U, "p-000090")!.State);
        Assert.Equal(Table.Ump3, t.Server.Log[^1].Actor);
        t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = "p-000090", Number = 9 });
        Assert.Equal(3, t.Server.State.Find(U, "p-000090")!.State);
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 1, Layer = U, Piece = P, Selected = new() { P, "p-000003" } });
        var res = t.Server.Handle(Table.Ump3, new NumberTyped { Layer = U, Id = P, Number = 2 });
        var sel = Assert.Single(Flat(t.Server.Log).OfType<SelectPiece>());
        Assert.Equal($"p:{Table.Ump3}", sel.To);
        Assert.False(sel.Selected);
        Assert.Contains(res, o => o.PeerId == Table.Ump3 && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<SelectPiece>().Any());
        Assert.DoesNotContain(res, o => o.PeerId == Table.Obs && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<SelectPiece>().Any());
        t.Server.Handle(Table.Ump3, new Say { Text = "hello" });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "heard hello from Umpire Three" && s.To == $"p:{Table.Ump3}");
        Assert.Single(Flat(t.Server.Log).OfType<Say>(), s => s.Text.StartsWith("heard "));
        Assert.Equal(10, t.Server.State.Hotkeys.Count(h => h.Label.StartsWith("Scripting button ")));
        var two = t.Server.State.Hotkeys.FindIndex(h => h.Label == "Scripting button 2") + 1;
        t.Server.Handle(Table.Ump3, new Hotkey { Index = two, Layer = U, Piece = P });
        Assert.Contains(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "button 2 down by Umpire Three");
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
    }

    static IEnumerable<string> Errors(Table t) => Flat(t.Server.Log).OfType<Say>().Where(s => s.Text.StartsWith("Script error")).Select(s => s.Text);

    const string Button = "function on_load(s) self.clear_widgets() self.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = 'go', on_click = 'go' } end ";

    [Fact]
    public void After_a_click_the_autosave_asks_that_script_and_the_count_is_exact()
    {
        using var t = new Table();
        SetScript(t, Counter);
        t.Server.Scripts.Save();
        Assert.False(t.Server.Scripts.AnyScriptRan);
        Assert.Equal(0, t.Server.Scripts.Backlog);
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.True(t.Server.Scripts.AnyScriptRan);
        Assert.Equal(1, t.Server.Scripts.Backlog);
        t.Server.Scripts.Save();
        Assert.Equal(1, t.Server.Scripts.AskedLast);
        Assert.Equal(0, t.Server.Scripts.Backlog);
        Assert.Equal("{\"n\":9}", t.Server.State.Find(U, P)!.ScriptState);
        for (int i = 0; i < 3; i++) SetScript(t, Counter);
        Assert.Equal(1, t.Server.Scripts.Backlog);
        t.Server.Handle(Table.Host, new DeletePiece { Layer = U, Id = P });
        Assert.Equal(0, t.Server.Scripts.Backlog);
        Assert.False(t.Server.Scripts.AnyScriptRan);
    }

    [Fact]
    public void Save_all_asks_every_script_that_ran_in_one_round()
    {
        using var t = new Table();
        const string slow = "function on_load(s) gen = (tonumber(s:match('%d+$') or '0') or 0) + 1 end function on_save() local n = 0 for i = 1, 40000 do n = n + 1 end return 'generation ' .. gen end";
        for (int i = 0; i < 40; i++) t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(i, 3), Id = $"p-{100 + i:D6}" });
        foreach (var p in t.Server.State.Layer(U).Where(p => p.Def == "marker").ToList()) t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = p.Id, Text = slow });
        t.Server.Scripts.SaveAll();
        Assert.Equal(0, t.Server.Scripts.Backlog);
        t.Server.Scripts.Load();
        Assert.True(t.Server.Scripts.Backlog >= 40);
        t.Server.Scripts.Save();
        Assert.True(t.Server.Scripts.Backlog > 0, "one sliced round should leave some for the next");
        t.Server.Scripts.SaveAll();
        Assert.Equal(0, t.Server.Scripts.Backlog);
        Assert.False(t.Server.Scripts.AnyScriptRan);
        Assert.All(t.Server.State.Layer(U).Where(p => p.Def == "marker"), p => Assert.Equal("generation 2", p.ScriptState));
    }

    [Fact]
    public void A_script_calling_itself_through_the_host_is_stopped_not_the_host()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "depth = 0 function on_load(s) rec() end function rec() depth = depth + 1 game.call('rec') end" });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Contains(Errors(t), e => e.Contains("calls inside calls"));
        SetScript(t, Button + "function go() self.call('go') end");
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Contains(Errors(t), e => e.Contains(P) && e.Contains("calls inside calls"));
        SetScript(t, Button + "function go() local function r() coroutine.resume(coroutine.create(r)) end r() end");
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        SetScript(t, Button + "function go() local list = {} for i = 1, 100000 do list[i] = coroutine.create(function() end) end end");
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Contains(Errors(t), e => e.Contains("coroutines in one call"));
        SetScript(t, "function on_load(s) self.set_text('name', 'still here') end");
        Assert.Equal("still here", t.Server.State.Find(U, P)!.Text["name"]);
    }

    [Fact]
    public void A_dotnet_failure_in_a_script_switches_it_off_and_keeps_what_it_did()
    {
        using var t = new Table();
        SetScript(t, Button + "function go() self.set_counter('str', 5) local function f(n) return 1 + f(n + 1) end pcall(f, 1) self.set_counter('str', 6) end");
        var logBefore = t.Server.Log.Count;
        var res = t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(5, p.Counters["str"]);
        Assert.Contains(Errors(t), e => e.Contains("stopped") && e.Contains("off until"));
        Assert.True(t.Server.Log.Count > logBefore + 1);
        Assert.Contains(res, o => o.PeerId == Table.Lead && o.Message is CommandMessage cm && Flat(new[] { cm.Command }).OfType<SetCounter>().Any());
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Equal(5, p.Counters["str"]);
    }

    [Fact]
    public void One_failing_script_in_a_frame_leaves_the_others_their_turn()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(5, 5), Id = "p-000090" });
        SetScript(t, "function on_load(s) wait.seconds(1, function() local function f() return 1 + f() end f() end) end");
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = "function on_load(s) wait.seconds(1, function() self.set_text('name', 'fired') end) end" });
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000090", Text = "function on_load(s) coroutine.yield() coroutine.yield() self.set_text('name', 'resumed') end" });
        t.Server.Scripts.Tick(0.5);
        t.Server.Scripts.Tick(1.5);
        Assert.Equal("fired", t.Server.State.Find(U, "p-000003")!.Text["name"]);
        Assert.Equal("resumed", t.Server.State.Find(U, "p-000090")!.Text["name"]);
        Assert.Contains(Errors(t), e => e.Contains(P) && e.Contains("stopped"));
    }

    [Fact]
    public void Tonumber_with_a_base_and_string_rep_answer_as_lua_does()
    {
        using var t = new Table();
        SetScript(t, """
            function on_load(s)
              local ok, err = pcall(function() return ("x"):rep(3e9) end)
              self.set_text("name", table.concat({ tostring(tonumber("zz", 16)), tostring(tonumber("1.5", 16)), tostring(tonumber("", 16)),
                tostring(tonumber("ff", 16)), tostring(tonumber("0x1F", 16)), tostring(tonumber(" -101 ", 2)), tostring(tonumber("z", 36)),
                tostring(tonumber("12")), tostring(ok), tostring(err and err:find("characters long") ~= nil or err) }, " "))
            end
            """);
        Assert.Empty(Errors(t));
        Assert.Equal("nil nil nil 255 31 -5 35 12 false true", t.Server.State.Find(U, P)!.Text["name"]);
    }

    [Theory]
    [InlineData("table.sort({ 3, 1, 2 }, function(a, b) while true do end end)")]
    [InlineData("string.gsub('abc', '%w', function(c) while true do end end)")]
    [InlineData("coroutine.wrap(function() while true do end end)()")]
    [InlineData("local co = coroutine.create(function() while true do end end) coroutine.resume(co)")]
    [InlineData("xpcall(function() error('x') end, function(m) while true do end end)")]
    [InlineData("pcall(table.sort, { 3, 1, 2 }, function(a, b) while true do end end)")]
    public void A_loop_inside_a_library_callback_is_stopped_within_the_budget(string lua)
    {
        using var t = new Table();
        SetScript(t, Button + "function go() " + lua + " end");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Contains(Errors(t), e => e.Contains(P) && e.Contains("stopped"));
    }

    [Fact]
    public void Sort_and_gsub_written_in_lua_give_lua_s_answers()
    {
        using var t = new Table();
        SetScript(t, """
            function on_load(s)
              local out = {}
              local function add(...) local p = {} for i = 1, select("#", ...) do p[#p + 1] = tostring((select(i, ...))) end out[#out + 1] = table.concat(p, ",") end
              local list = {}
              for i = 1, 200 do list[i] = (i * 7919) % 200 end
              table.sort(list, function(a, b) return a > b end)
              add(list[1], list[200], #list)
              local words = { { n = "c" }, { n = "a" }, { n = "b" } }
              table.sort(words, function(x, y) return x.n < y.n end)
              add(words[1].n, words[2].n, words[3].n)
              local mt = { __lt = function(x, y) return x.v < y.v end }
              local objs = { setmetatable({ v = 3 }, mt), setmetatable({ v = 1 }, mt), setmetatable({ v = 2 }, mt) }
              table.sort(objs)
              add(objs[1].v, objs[3].v)
              local nums = { 5, 2, 9 } add(table.sort(nums) == nums, nums[1], nums[3])
              add(("abc"):gsub("%w", function(c) return c:upper() end))
              add(("abc"):gsub("", function() return "-" end))
              add(("hello world"):gsub("o", { o = "0" }))
              add(("abc"):gsub("%w", function(c) if c == "b" then return false end return c:upper() end))
              add(("aaa"):gsub("^a", function() return "b" end))
              add(("aaa"):gsub("a", function() return "b" end, 2))
              add(("abc"):gsub("()", function(p) return p end))
              add(("key=val, k2=v2"):gsub("(%w+)=(%w+)", function(k, v) return v .. "=" .. k end))
              add(("abc"):gsub("%w*", function(m) return "<" .. m .. ">" end))
              add(pcall(string.gsub, "abc", "%w", function() return {} end))
              self.set_text("name", table.concat(out, " | "))
            end
            """);
        Assert.Empty(Errors(t));
        var lines = t.Server.State.Find(U, P)!.Text["name"].Split(" | ");
        Assert.Equal("199,0,200", lines[0]);
        Assert.Equal("a,b,c", lines[1]);
        Assert.Equal("1,3", lines[2]);
        Assert.Equal("true,2,9", lines[3]);
        Assert.Equal("ABC,3", lines[4]);
        Assert.Equal("-a-b-c-,4", lines[5]);
        Assert.Equal("hell0 w0rld,2", lines[6]);
        Assert.Equal("AbC,3", lines[7]);
        Assert.Equal("baa,1", lines[8]);
        Assert.Equal("bba,2", lines[9]);
        Assert.Equal("1a2b3c4,4", lines[10]);
        Assert.Equal("val=key, v2=k2,2", lines[11]);
        Assert.Equal("<abc><>,2", lines[12]);
        Assert.StartsWith("false,", lines[13]);
        Assert.Contains("invalid replacement value", lines[13]);
    }

    [Fact]
    public void Pieces_sharing_one_compiled_script_keep_their_own_variables()
    {
        using var t = new Table();
        const string tally = Button + "n = 0 function go() n = n + 1 self.set_text('name', 'n ' .. n) end";
        SetScript(t, tally);
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = tally });
        for (int i = 0; i < 3; i++) t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = "p-000003", Index = 1 });
        Assert.Equal("n 3", t.Server.State.Find(U, P)!.Text["name"]);
        Assert.Equal("n 1", t.Server.State.Find(U, "p-000003")!.Text["name"]);
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000003", Text = "function on_load(s)\n  local x = nil\n  return x.y\nend" });
        Assert.Contains(Errors(t), e => e.Contains("p-000003") && e.Contains("(3,"));
    }

    [Fact]
    public void One_compiled_chunk_serves_every_piece_with_the_same_script()
    {
        using var t = new Table();
        var big = string.Concat(Enumerable.Range(0, 200).Select(i => $"function f{i}(a) return a + {i} end\n"));
        for (int i = 0; i < 30; i++)
        {
            t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(i, 3), Id = $"p-{100 + i:D6}" });
            t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = $"p-{100 + i:D6}", Text = big });
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(true);
        for (int i = 0; i < 10; i++) t.Server.Scripts.Load();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var kept = GC.GetTotalMemory(true) - before;
        Assert.True(kept < 20L * 1024 * 1024, $"{kept / 1024 / 1024} MB kept by 300 loads");
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void A_replaced_global_script_leaves_no_timers_running()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) local function again() game.say('old') wait.seconds(1, again) end wait.seconds(1, again) end" });
        t.Server.Scripts.Tick(1.5);
        Assert.Single(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "old");
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) end" });
        for (int i = 2; i < 6; i++) t.Server.Scripts.Tick(i + 0.5);
        Assert.Single(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "old");
    }

    [Fact]
    public void Calls_reuse_their_coroutines()
    {
        using var t = new Table();
        SetScript(t, Button + "n = 0 function go() n = n + 1 if n % 10 == 0 then coroutine.yield() end end");
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 }); t.Server.Scripts.Tick(i); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 100L * 1024 * 1024, $"{allocated / 1024 / 1024} MB for 200 calls");
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void Undo_takes_back_the_state_a_script_keeps_in_lua()
    {
        using var t = new Table();
        SetScript(t, Button + "n = 0 function on_load(s) n = tonumber(s) or 0 self.clear_widgets() self.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = tostring(n), on_click = 'go' } end function go() n = n + 1 self.edit(1, { label = tostring(n) }) end function on_save() return tostring(n) end");
        var p = t.Server.State.Find(U, P)!;
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        var clickSeq = t.Server.Log[^1].Seq;
        Assert.Equal("1", p.Widgets[0].Label);
        t.Server.Scripts.Save();
        t.Server.Handle(Table.Ump3, new Undo { TargetSeq = clickSeq });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal("0", p.Widgets[0].Label);
        t.Server.Scripts.Save();
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Equal("1", t.Server.State.Find(U, P)!.Widgets[0].Label);
    }

    [Fact]
    public void Undoing_an_edit_to_the_global_script_puts_the_old_script_back_to_work()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_turn(turn) game.say('old ' .. turn) end" });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_turn(turn) game.say('new ' .. turn) end" });
        t.Server.Handle(Table.Lead, new Undo { TargetSeq = t.Server.Log.Last(c => c is SetGlobalScript).Seq });
        Assert.Equal("function on_turn(turn) game.say('old ' .. turn) end", t.Server.State.Script);
        t.Server.Handle(Table.Host, new AdvanceClock());
        var said = Flat(t.Server.Log).OfType<Say>().Select(s => s.Text).ToList();
        Assert.Contains("old 1", said);
        Assert.DoesNotContain("new 1", said);
    }

    [Fact]
    public void Undo_takes_back_the_state_the_global_script_keeps_in_lua()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = $"n = 0 function on_load(s) n = tonumber(s) or 0 game.hotkey('Count', function() n = n + 1 board.find('{P}').set_text('name', tostring(n)) end) end function on_save() return tostring(n) end" });
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 1 });
        var countSeq = t.Server.Log[^1].Seq;
        Assert.Equal("1", t.Server.State.Find(U, P)!.Text["name"]);
        t.Server.Scripts.Save();
        t.Server.Handle(Table.Ump3, new Undo { TargetSeq = countSeq });
        Assert.Equal("UMPIRE-SECRET-NAME", t.Server.State.Find(U, P)!.Text["name"]);
        t.Server.Scripts.Save();
        t.Server.Handle(Table.Ump3, new Hotkey { Index = 1 });
        Assert.Equal("1", t.Server.State.Find(U, P)!.Text["name"]);
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void A_piece_that_changes_kind_runs_its_new_kinds_script_and_undo_brings_the_old_one_back()
    {
        using var t = new Table();
        t.Store.Def("inf")!.Script = "function on_turn(turn) game.say(self.id .. ' inf ' .. turn) end";
        t.Store.Def("marker")!.Script = "function on_turn(turn) game.say(self.id .. ' marker ' .. turn) end";
        t.Server.Scripts.Load();
        t.Server.Request(Table.Host, new RunLua { Target = "", Code = $"board.find('{P}').set_def('marker')" });
        var kindSeq = t.Server.Log.Last(c => Flat([c]).Any(x => x is SetDef)).Seq;
        Assert.Equal("marker", t.Server.State.Find(U, P)!.Def);
        t.Server.Handle(Table.Host, new AdvanceClock());
        var said = Flat(t.Server.Log).OfType<Say>().Select(s => s.Text).ToList();
        Assert.Contains($"{P} marker 1", said);
        Assert.DoesNotContain($"{P} inf 1", said);

        t.Server.Handle(Table.Host, new Undo { TargetSeq = kindSeq });
        Assert.Equal("inf", t.Server.State.Find(U, P)!.Def);
        t.Server.Handle(Table.Host, new AdvanceClock());
        said = Flat(t.Server.Log).OfType<Say>().Select(s => s.Text).ToList();
        Assert.Contains($"{P} inf 2", said);
        Assert.DoesNotContain($"{P} marker 2", said);
    }

    [Fact]
    public void A_piece_a_script_deletes_hears_on_destroy_as_one_an_umpire_deletes()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetGlobalScript { Text = "function on_destroy(p) game.say('global saw ' .. p.id .. ' go') end" });
        SetScript(t, "function on_destroy() game.say(self.id .. ' says goodbye') end");
        t.Server.Request(Table.Host, new RunLua { Target = "", Code = $"board.find('{P}').delete()" });
        Assert.Null(t.Server.State.Find(U, P));
        var said = Flat(t.Server.Log).OfType<Say>().Select(s => s.Text).ToList();
        Assert.Contains($"{P} says goodbye", said);
        Assert.Contains($"global saw {P} go", said);
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void Undoing_a_deletion_starts_the_pieces_script_again()
    {
        using var t = new Table();
        SetScript(t, Counter);
        t.Server.Handle(Table.Host, new DeletePiece { Layer = U, Id = P });
        t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        var p = t.Server.State.Find(U, P)!;
        int before = p.Counters["str"];
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = P, Index = 1 });
        Assert.Equal(before + 1, t.Server.State.Find(U, P)!.Counters["str"]);
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "marker", Pos = new Vec2(5, 5), Id = "p-000090" });
        var spawnSeq = t.Server.Log[^1].Seq;
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000090", Text = "function on_load(s) wait.seconds(1, function() game.say('ghost') end) end" });
        t.Server.Handle(Table.Host, new Undo { TargetSeq = spawnSeq });
        Assert.Null(t.Server.State.Find(U, "p-000090"));
        t.Server.Scripts.Tick(5);
        Assert.DoesNotContain(Flat(t.Server.Log).OfType<Say>(), s => s.Text == "ghost");
    }

    [Fact]
    public void A_condition_that_times_out_calls_its_timeout_function()
    {
        using var t = new Table();
        SetScript(t, "function on_load(s) wait.condition(function() return false end, function() self.set_text('name', 'met') end, 1, function() self.set_text('name', 'timed out') end) end");
        t.Server.Scripts.Tick(0.5);
        Assert.Equal("UMPIRE-SECRET-NAME", t.Server.State.Find(U, P)!.Text["name"]);
        t.Server.Scripts.Tick(1.5);
        Assert.Equal("timed out", t.Server.State.Find(U, P)!.Text["name"]);
        SetScript(t, "function on_load(s) wait.condition(function() error('broken') end, function() end) end");
        for (int i = 0; i < 5; i++) t.Server.Scripts.Tick(2 + i);
        Assert.Single(Errors(t), e => e.Contains("broken"));
    }

    const string Bare = "p-000003";

    const string GlobalDraws = """
        function on_load(s)
          local p = board.find("p-000003")
          p.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = "Create Unit", on_click = "create" }
          p.menu_add("Casualties", "create")
        end
        function create(who, alt, index, piece) piece.set_name("created by " .. who.name) end
        """;

    static string Drawing(GameServer s, string id)
    {
        var p = s.State.Find(U, id)!;
        return string.Join(",", p.Widgets.Select(w => w.Label)) + " | " + string.Join(",", p.MenuItems.Select(m => m.Label));
    }

    [Fact]
    public void Every_reload_leaves_one_set_of_what_the_global_script_draws()
    {
        using var t = new Table();
        t.Server.State.Script = GlobalDraws;
        t.Server.Scripts.Load();
        const string one = "Create Unit | Casualties";
        Assert.Equal(one, Drawing(t.Server, Bare));
        t.Server.Scripts.Load();
        t.Server.Scripts.Load();
        Assert.Equal(one, Drawing(t.Server, Bare));
        t.Server.Handle(Table.Host, new AdvanceClock());
        t.Server.LoadSave(t.Server.Turns[0].Unpack(), new List<TurnRecord>(), "Turn 0");
        Assert.Equal(one, Drawing(t.Server, Bare));
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = GlobalDraws });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = GlobalDraws });
        Assert.Equal(one, Drawing(t.Server, Bare));
        var resumed = new GameServer(Json.Clone(t.Server.State), t.Store);
        resumed.Scripts.Load();
        resumed.Scripts.Load();
        Assert.Equal(one, Drawing(resumed, Bare));
        t.Server.Handle(Table.Ump3, new ClickWidget { Layer = U, Id = Bare, Index = 1 });
        Assert.Equal("created by Umpire Three", t.Server.State.Find(U, Bare)!.Label);
        Assert.Empty(Errors(t));
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) end" });
        Assert.Equal(" | ", Drawing(t.Server, Bare));
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = GlobalDraws });
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_load(s) error('broken') end" });
        Assert.Equal(one, Drawing(t.Server, Bare));
        Assert.Equal(Json.Serialize(t.Server.State), Json.Serialize(SaveLoader.Replay(t.Scenario, t.Server.Log)));
    }

    [Fact]
    public void A_pieces_own_drawing_is_unaffected_and_shares_the_piece_with_the_global_scripts()
    {
        using var t = new Table();
        SetScript(t, Counter);
        t.Server.State.Script = GlobalDraws.Replace("local p = board.find(\"p-000003\")", "local p = board.find(\"p-000003\") board.find(\"p-000002\").button{ label = \"Orders\" }");
        foreach (var round in Enumerable.Range(0, 3))
        {
            if (round < 2) t.Server.Scripts.Load();
            else { t.Server.Handle(Table.Host, new AdvanceClock()); t.Server.LoadSave(t.Server.Turns[^1].Unpack(), t.Server.Turns.Take(t.Server.Turns.Count - 1).ToList(), "Turn"); }
            var p = t.Server.State.Find(U, P)!;
            Assert.Equal(new[] { "Ammo 8", "UMPIRE-SECRET-NAME" }, p.Widgets.Select(w => w.Kind == "input" ? w.Value : w.Label));
            Assert.Equal("Create Unit | Casualties", Drawing(t.Server, Bare));
        }
        SetScript(t, "function on_load(s) self.button{ label = 'own' } end function on_save() return 'kept' end");
        t.Server.Scripts.Load();
        Assert.Equal("Orders,own | ", Drawing(t.Server, P));
        t.Server.Scripts.Load();
        Assert.Equal("Orders,own | ", Drawing(t.Server, P));
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = t.Server.State.Script });
        Assert.Equal("Orders,own | ", Drawing(t.Server, P));
        Assert.Equal("Create Unit | Casualties", Drawing(t.Server, Bare));
        Assert.Equal("kept", t.Server.State.Find(U, P)!.ScriptState);
        Assert.Empty(Errors(t));
    }

    [Fact]
    public void A_piece_whose_script_fails_on_a_reload_keeps_what_it_was_saved_with()
    {
        using var t = new Table();
        SetScript(t, """
            function on_load(s)
              self.clear_widgets() self.menu_clear()
              if s == "fail early" then error("broken before drawing") end
              self.button{ x = 0, z = 0, w = 0.2, h = 0.1, label = "one", on_click = "go" }
              if s == "fail late" then error("broken half way") end
              self.button{ x = 0, z = 0.2, w = 0.2, h = 0.1, label = "two", on_click = "go" }
              self.menu_add("Row", "go")
            end
            function go() end
            """);
        const string saved = "one,two | Row";
        Assert.Equal(saved, Drawing(t.Server, P));
        foreach (var state in new[] { "fail early", "fail late" })
        {
            t.Server.State.Find(U, P)!.ScriptState = state;
            t.Server.Scripts.Load();
            Assert.Contains(Errors(t), e => e.Contains("broken"));
            Assert.Equal(saved, Drawing(t.Server, P));
        }
        t.Server.State.Find(U, P)!.ScriptState = "";
        t.Server.Scripts.Load();
        Assert.Equal(saved, Drawing(t.Server, P));
        t.Server.State.Find(U, P)!.Script = "function on_load(s) self.clear_widgets() this is not lua";
        t.Server.Scripts.Load();
        Assert.Equal(saved, Drawing(t.Server, P));
        t.Server.State.Find(U, P)!.Script = "function on_load(s) self.clear_widgets() local function f() return 1 + f() end f() end";
        t.Server.Scripts.Load();
        Assert.Contains(Errors(t), e => e.Contains("stopped"));
        Assert.Equal(saved, Drawing(t.Server, P));
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Bare, Text = "function on_load(s) self.button{ label = 'first' } error('then broken') end" });
        t.Server.State.Find(U, Bare)!.Widgets.Clear();
        t.Server.Scripts.Load();
        Assert.Equal("first | ", Drawing(t.Server, Bare));
    }

    [Fact]
    public void A_script_moves_a_piece_to_a_place_or_another_piece_given_whole()
    {
        using var t = new Table();
        SetScript(t, "function on_load() self.set_pos({ x = 7, z = -3 }) end");
        Assert.Equal(new Vec2(7, -3), t.Server.State.Find(U, P)!.Pos);
        SetScript(t, "function on_load() self.move({ x = 2, z = 5 }, 45) end");
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(new Vec2(2, 5), p.Pos);
        Assert.Equal(45, p.RotDeg);
    }

    [Fact]
    public void A_script_that_moves_a_piece_to_something_that_is_not_a_place_is_told_so()
    {
        using var t = new Table();
        var before = t.Server.State.Find(U, P)!.Pos;
        SetScript(t, "function on_load() self.set_pos({ north = 3 }) end");
        SetScript(t, "function on_load() self.move('here', 0) end");
        Assert.Equal(before, t.Server.State.Find(U, P)!.Pos);
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("set_pos: a place is"));
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("move: string is not a number"));
    }

    [Fact]
    public void A_sheet_s_editor_changes_the_symbol_on_the_look_it_is_in_and_the_copy_it_restores_at_load_leaves_the_looks_their_own()
    {
        using var t = new Table();
        var pack = t.Store.PackOfDef("inf")!;
        pack.Assets["flag"] = new AssetEntry { File = "assets/arms.png", W = 64, H = 64 };
        pack.Assets["drawn"] = new AssetEntry { File = "assets/arms.png", W = 64, H = 64 };
        var def = t.Store.Def("inf")!;
        def.States =
        [
            new PieceStateDef { Name = "Line", Decals = [new DecalDef { Picture = "arms", W = 1, H = 0.25 }] },
            new PieceStateDef { Name = "Column", Decals = [new DecalDef { Picture = "flag", W = 0.25, H = 1 }] },
        ];

        SetScript(t, """
            function restore() self.set_decals{ { picture = "drawn", w = 0.5, h = 0.5 } } end
            function on_load(s)
              restore()
              self.menu_add("Redraw", "restore")
              coroutine.yield(0)
              restore()
            end
            """);

        var p = t.Server.State.Find(U, P)!;
        Assert.Empty(p.Decals);
        t.Server.Scripts.Tick(0.1);
        Assert.Empty(t.Server.State.Find(U, P)!.Decals);
        Assert.Equal("arms", Assert.Single(DecalDef.ShownOn(p, def)).Picture);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        Assert.Equal("flag", Assert.Single(DecalDef.ShownOn(t.Server.State.Find(U, P)!, def)).Picture);

        t.Server.Handle(Table.Host, new PickMenuItem { Layer = U, Id = P, Index = 1 });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(1, p.DecalsOnState);
        Assert.Equal("drawn", Assert.Single(DecalDef.ShownOn(p, def)).Picture);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 0 });
        Assert.Equal("arms", Assert.Single(DecalDef.ShownOn(t.Server.State.Find(U, P)!, def)).Picture);

        t.Server.Handle(Table.Host, new SetState { Layer = U, Id = P, State = 1 });
        Assert.Equal("drawn", Assert.Single(DecalDef.ShownOn(t.Server.State.Find(U, P)!, def)).Picture);
    }
    [Fact]
    public void A_coroutine_still_yielding_long_after_its_load_stops_counting_as_loading_and_its_symbols_take()
    {
        using var t = new Table();
        t.Store.PackOfDef("inf")!.Assets["drawn"] = new AssetEntry { File = "assets/arms.png", W = 64, H = 64 };
        var def = t.Store.Def("inf")!;
        def.States = [new PieceStateDef { Name = "Line", Decals = [new DecalDef { Picture = "arms", W = 1, H = 0.25 }] }];

        SetScript(t, """
            function on_load(s)
              while true do
                coroutine.yield(0)
                self.set_decals{ { picture = "drawn", w = 0.5, h = 0.5 } }
              end
            end
            """);

        Assert.Empty(t.Server.State.Find(U, P)!.Decals);
        for (int i = 0; i < 40; i++) t.Server.Scripts.Tick(i * 0.01);
        Assert.Empty(t.Server.State.Find(U, P)!.Decals);

        for (int i = 0; i < 700; i++) t.Server.Scripts.Tick(1 + i * 0.01);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal("drawn", Assert.Single(p.Decals).Picture);
        Assert.Equal(0, p.DecalsOnState);
        Assert.Equal("drawn", Assert.Single(DecalDef.ShownOn(p, def)).Picture);
    }
}
