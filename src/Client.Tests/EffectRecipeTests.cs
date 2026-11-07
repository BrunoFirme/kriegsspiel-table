using System.Numerics;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class EffectRecipeTests
{
    static readonly Rgba Grey = new(0.5f, 0.5f, 0.5f);

    static EffectPlan Plan(EffectDef e, float w = 2, float h = 1, float phase = 0.3f, bool own = false) => EffectRecipe.Plan(e, w, h, phase, Grey, own);

    [Fact]
    public void A_see_through_colour_thins_the_foam_and_a_new_colour_keeps_how_clear_it_is()
    {
        var solid = EffectRecipe.Plan(new EffectDef { Kind = "wake" }, 0.3f, 10, 0.3f, Grey, false);
        var light = EffectRecipe.Plan(new EffectDef { Kind = "wake" }, 0.3f, 10, 0.3f, Grey with { A = 0.6f }, false);
        Assert.Equal(solid.Layers.SelectMany(l => l.Ramp).Select(r => r.c.A * 0.6f), light.Layers.SelectMany(l => l.Ramp).Select(r => r.c.A));
        Assert.Equal("#102030", EffectKinds.Recolour("#102030", null));
        Assert.Equal("#10203099", EffectKinds.Recolour("#102030", "#F1F5F699"));
        Assert.Equal("#102030", EffectKinds.Recolour("#102030FF", "#F1F5F6"));
    }

    [Fact]
    public void Every_kind_the_studio_offers_makes_something()
    {
        foreach (var (id, _, _) in EffectKinds.Kinds)
        {
            var plan = Plan(new EffectDef { Kind = id });
            Assert.True(plan.Layers.Count > 0 || plan.Lamps != null || plan.Projectiles is { Shots.Count: > 0 }, id);
            Assert.All(plan.Layers, l => Assert.InRange(l.Amount, 1, 2400));
        }
    }

    [Fact]
    public void The_same_effect_is_laid_out_the_same_on_every_screen_and_a_neighbour_differently()
    {
        static string Points(EffectPlan p) => string.Join(";", p.Layers[0].Points!.Select(v => $"{v.X:R},{v.Z:R}"));
        var camp = new EffectDef { Kind = "camp" };
        Assert.Equal(Points(Plan(camp, phase: 0.4f)), Points(Plan(camp, phase: 0.4f)));
        Assert.NotEqual(Points(Plan(camp, phase: 0.4f)), Points(Plan(camp, phase: 0.41f)));
    }

    [Fact]
    public void A_firing_layer_starts_part_way_into_its_cycle()
    {
        var plan = Plan(new EffectDef { Kind = "cannon" }, phase: 0.25f);
        var fired = plan.Layers.Where(l => l.Explosive > 0).ToList();
        Assert.NotEmpty(fired);
        Assert.All(fired, l => Assert.Equal((0.25f / l.Cycles + l.Offset) % 1 * l.Life, l.Start));
        Assert.All(Plan(new EffectDef { Kind = "smoke" }).Layers, l => Assert.Equal(-1f, l.Start));
    }

    [Fact]
    public void A_rolling_volley_fires_section_after_section_down_the_line()
    {
        var together = Plan(new EffectDef { Kind = "powder", Volley = "together" }, w: 6);
        Assert.Equal(new[] { 0f, 0.5f, 0f }, together.Layers.Select(l => l.Offset));
        var rolling = Plan(new EffectDef { Kind = "powder", Volley = "rolling" }, w: 6);
        var smoke = rolling.Layers.Where(l => l.Sprite == "puff").ToList();
        var offsets = smoke.Where((_, i) => i % 2 == 0).Select(l => l.Offset).ToList();
        Assert.Equal(7, offsets.Count);
        Assert.Equal(offsets.OrderBy(o => o), offsets);
        Assert.Equal(0f, offsets[0]);
        Assert.True(offsets[^1] < 0.25f);
        Assert.All(offsets.Zip(smoke.Where((_, i) => i % 2 == 1).Select(l => l.Offset)), p => Assert.Equal(p.First + 0.5f, p.Second, 5));
    }

    [Theory]
    [InlineData("cannon", "together", 0.37f)]
    [InlineData("cannon", "rolling", 0.81f)]
    [InlineData("powder", "together", 0.12f)]
    [InlineData("powder", "rolling", 0.64f)]
    [InlineData("broadside", "together", 0.93f)]
    [InlineData("broadside", "rolling", 0.5f)]
    public void Every_layer_of_a_volley_fires_with_a_muzzle_flash(string kind, string volley, float phase)
    {
        var e = new EffectDef { Kind = kind, Volley = volley };
        float every = (float)EffectKinds.Interval(e);
        float FiresAt(EffectLayer l) => ((l.Life - l.Start) % every + every) % every;
        var fired = Plan(e, w: 4, phase: phase).Layers.Where(l => l.Start >= 0).ToList();
        var flashes = fired.Where(l => l.Sprite is "muzzle" or "flash").Select(FiresAt).ToList();
        Assert.NotEmpty(flashes);
        Assert.All(fired, l => Assert.Contains(flashes, f => Math.Abs(f - FiresAt(l)) < 1e-3f || Math.Abs(Math.Abs(f - FiresAt(l)) - every) < 1e-3f));
    }

    [Fact]
    public void Volley_smoke_lasts_two_volleys_while_the_guns_keep_their_rhythm_and_blast()
    {
        var plan = Plan(new EffectDef { Kind = "cannon", Every = 5 });
        var smoke = plan.Layers.Where(l => l.Sprite == "puff").ToList();
        Assert.Equal(2 * plan.Layers.Count(l => l.Sprite == "muzzle"), smoke.Count);
        Assert.All(smoke, l => Assert.Equal(10f, l.Life));
        Assert.All(smoke, l => Assert.Equal(0.12f * 5, l.Brake * l.Life, 4));
        Assert.All(plan.Layers.Where(l => l.Sprite != "puff"), l => Assert.Equal(5f, l.Life));
    }

    [Theory]
    [InlineData("together", false)]
    [InlineData("rolling", false)]
    [InlineData("rolling", true)]
    public void Each_gun_s_smoke_leaves_its_own_muzzle_with_its_flash(string volley, bool lingers)
    {
        var air = new SmokeAir(2, 1 / SmokeDrift.UncalibratedMetresPerUnit);
        var e = new EffectDef { Kind = "cannon", Volley = volley, Every = 4, Lingers = lingers };
        var plan = EffectRecipe.Plan(e, 4, 1, 0.3f, Grey, false, air);
        var flashes = plan.Layers.Where(l => l.Sprite == "muzzle").ToList();
        var smoke = plan.Layers.Where(l => l.Sprite == "puff").ToList();
        Assert.True(flashes.Count >= 2, $"{flashes.Count} guns");
        Assert.All(flashes, f => Assert.Equal(1, f.Amount));
        float FiresAt(EffectLayer l) => ((l.Life - l.Start) % 4 + 4) % 4;
        foreach (var puff in smoke)
        {
            var gun = Assert.Single(flashes, f => Math.Abs(f.At.X - puff.At.X) < 1e-4f);
            Assert.True(puff.Box.X < 0.5f * Math.Abs(flashes[1].At.X - flashes[0].At.X), "a puff stays at its own gun");
            float apart = Math.Abs(FiresAt(gun) - FiresAt(puff));
            Assert.True(apart < 1e-3f || Math.Abs(apart - 4) < 1e-3f, $"flash at {FiresAt(gun)}, smoke at {FiresAt(puff)}");
            Assert.Equal((1 - 0.92f) * 4, (1 - puff.Explosive) * puff.Life, 3);
        }
    }

    [Fact]
    public void The_piece_s_own_picture_takes_the_first_layer_s_place_keeping_its_fade()
    {
        var e = new EffectDef { Kind = "smoke", OwnPicture = true };
        var plain = Plan(e).Layers[0];
        var own = Plan(e, own: true).Layers[0];
        Assert.False(plain.Picture);
        Assert.True(own.Picture);
        Assert.Equal(plain.Ramp.Select(r => (r.at, r.c.A)), own.Ramp.Select(r => (r.at, r.c.A)));
        Assert.All(own.Ramp, r => Assert.Equal(Rgba.White with { A = r.c.A }, r.c));
        Assert.False(Plan(e, own: false).Layers[0].Picture);
    }

    [Fact]
    public void A_bivouac_burns_at_its_pits_and_lights_the_first_four_fires()
    {
        var e = new EffectDef { Kind = "camp", At = new List<double> { 0.5, -0.2, -0.4, 0.3 } };
        var plan = Plan(e);
        Assert.Equal(new[] { new Vector3(0.5f, 0, -0.2f), new Vector3(-0.4f, 0, 0.3f) }, plan.Layers[0].Points);
        Assert.Equal(2, plan.Lights.Count);
        Assert.Null(plan.Light);
        Assert.Equal(4, Plan(new EffectDef { Kind = "camp", Amount = 3 }).Lights.Count);
        Assert.Empty(Plan(new EffectDef { Kind = "camp", Light = false }).Lights);
    }

    [Fact]
    public void Burning_and_firing_effects_light_the_table_and_a_shell_pulses_at_each_shot()
    {
        Assert.Equal(2, Plan(new EffectDef { Kind = "burst" }).Light!.Mode);
        Assert.Equal(1, Plan(new EffectDef { Kind = "fire" }).Light!.Mode);
        Assert.Null(Plan(new EffectDef { Kind = "fire", Light = false }).Light);
        Assert.Null(Plan(new EffectDef { Kind = "smoke" }).Light);
        Assert.Null(Plan(new EffectDef { Kind = "flare" }).Light);
        Assert.All(Plan(new EffectDef { Kind = "flare" }).Projectiles!.Shots, s => Assert.Equal(Grey, s.LightColor));
    }

    [Fact]
    public void Rockets_fly_forward_across_the_piece_and_burst_where_they_land()
    {
        var plan = Plan(new EffectDef { Kind = "rocket" }, w: 4, h: 6);
        var p = plan.Projectiles!;
        Assert.False(p.Flare);
        Assert.Equal(3, p.Shots.Count);
        Assert.All(p.Shots, s =>
        {
            Assert.True(s.From.Z > 0 && s.To.Z < 0);
            Assert.Equal(2, s.Burst.Count);
            Assert.Null(s.LightRange);
            Assert.InRange(s.Phase, 0, 1);
        });
        Assert.Equal(6f, plan.TrailSpan);
        Assert.Empty(plan.Layers);
    }

    [Fact]
    public void A_salvo_throws_up_a_column_per_shell_together_or_one_after_another()
    {
        static List<float> Columns(EffectPlan p) => p.Layers.Where(l => l.Sprite == "puff" && l.Spread < 5).Select(l => l.Offset).ToList();
        Assert.Equal(3, Columns(Plan(new EffectDef { Kind = "column" }, w: 3)).Count);
        Assert.Single(Columns(Plan(new EffectDef { Kind = "column", Amount = 0.3 })));
        Assert.All(Columns(Plan(new EffectDef { Kind = "column", Amount = 3 }, w: 6)), o => Assert.InRange(o, 0, 0.03f));
        var rolling = Columns(Plan(new EffectDef { Kind = "column", Amount = 2, Volley = "rolling" }, w: 3));
        Assert.Equal(rolling.OrderBy(o => o), rolling);
        Assert.True(rolling[^1] > 0.3f);
        Assert.All(Plan(new EffectDef { Kind = "column" }).Layers, l => Assert.True(l.Start >= 0));
    }

    [Fact]
    public void Naval_guns_fire_off_the_front_with_a_glowing_fireball_and_a_flash_of_light()
    {
        var plan = Plan(new EffectDef { Kind = "broadside" }, w: 4, h: 1.2f);
        var fireballs = plan.Layers.Where(l => l.Glow).ToList();
        Assert.Equal(3, fireballs.Count);
        Assert.All(fireballs, l => Assert.True(l.Dir.Z < 0 && l.At.Z < 0));
        Assert.Equal(2, plan.Light!.Mode);
        Assert.DoesNotContain(Plan(new EffectDef { Kind = "broadside", Smoke = false }).Layers, l => l.Sprite == "puff" && !l.Glow);
    }

    [Fact]
    public void A_smoke_trail_is_densest_and_lowest_at_the_ship_s_end()
    {
        var trail = Plan(new EffectDef { Kind = "screen" }, w: 1.2f, h: 12).Layers.Where(l => l.Fixed).ToList();
        Assert.True(trail.Count > 2);
        Assert.Equal(trail.OrderBy(l => l.At.Z).Select(l => l.At.Y), trail.Select(l => l.At.Y));
        Assert.True(trail[0].Ramp[1].c.A > trail[^1].Ramp[1].c.A);
        Assert.Single(Plan(new EffectDef { Kind = "screen" }, w: 10, h: 1.2f).Layers);
    }

    [Fact]
    public void A_torpedo_spread_runs_one_track_per_torpedo_fanning_out_to_the_front()
    {
        var heads = Plan(new EffectDef { Kind = "wake", Amount = 4 }, w: 3, h: 10).Layers.Where(l => l.Sprite == "drop").Select(l => l.At).ToList();
        Assert.Equal(4, heads.Count);
        Assert.All(heads, h => Assert.True(h.Z < -4));
        Assert.True(heads.Max(h => h.X) - heads.Min(h => h.X) > 2);
    }

    [Fact]
    public void The_footprint_s_figures_are_the_engine_s_floats()
    {
        var l = Plan(new EffectDef { Kind = "powder" }, w: 3, h: 1, phase: 0.37f).Layers[0];
        Assert.Equal("puff", l.Sprite);
        Assert.Equal(60, l.Amount);
        Assert.Equal(new Vector3(1.425f, 0.242487103f, 0.3f), l.Box);
        Assert.Equal(1.21243548f, l.VMax);
        Assert.Equal(new Rgba(0.55f, 0.55f, 0.55f, 0.45f), l.Ramp[3].c);
    }

    [Theory]
    [InlineData("powder", "together")]
    [InlineData("powder", "random")]
    [InlineData("cannon", "rolling")]
    [InlineData("broadside", "together")]
    public void Black_powder_smoke_is_braked_only_at_the_blast_and_then_goes_with_the_wind(string kind, string volley)
    {
        var smoke = Plan(new EffectDef { Kind = kind, Volley = volley }).Layers.Where(l => l.Sprite == "puff" && !l.Glow).ToList();

        Assert.NotEmpty(smoke);
        Assert.All(smoke, l => Assert.InRange(l.Brake, 0.05f, 0.25f));
        Assert.All(smoke, l => Assert.True(l.Drift >= 0.9f, $"drift {l.Drift}"));
    }

    [Theory]
    [InlineData("Congreve rocket", "rocket")]
    [InlineData("Battery of guns", "cannon")]
    [InlineData("Musket volley", "powder")]
    [InlineData("Burning farm", "fire")]
    [InlineData("Campfire with smoke", "fire")]
    [InlineData("Morning mist", "fog")]
    [InlineData("Something else", "sparks")]
    [InlineData("Torpedo hit", "column")]
    [InlineData("Torpedo track", "wake")]
    [InlineData("Funnel smoke screen", "screen")]
    [InlineData("Main battery salvo", "broadside")]
    [InlineData("Ship on fire", "oil")]
    [InlineData("Burning village", "fire")]
    [InlineData("Longbow volley", "arrows")]
    [InlineData("Flaming arrows", "firearrows")]
    [InlineData("Trebuchet", "stones")]
    [InlineData("Javelin volley", "javelins")]
    [InlineData("Slingers", "slingstones")]
    [InlineData("Falling rocks", "rockfall")]
    [InlineData("Cavalry melee", "melee")]
    public void An_effect_with_no_settings_is_the_kind_its_name_says(string name, string kind) =>
        Assert.Equal(kind, EffectKinds.FromName(name).Kind);

    [Theory]
    [InlineData("Burning arrows", "fire")]
    [InlineData("Archer volley", "powder")]
    [InlineData("Catapult smoke", "smoke")]
    [InlineData("Arrow", null)]
    [InlineData("Javelin", null)]
    [InlineData("Sling volley", "powder")]
    public void Imported_effects_keep_the_kinds_they_had_before_the_old_wars_came_in(string name, string? kind) =>
        Assert.Equal(kind, EffectKinds.KindOf(name));

    [Fact]
    public void Javelins_stick_where_they_land_sling_stones_do_not_and_only_arrows_darken_the_ground_as_they_come()
    {
        EffectPlan P(string kind, string? volley = null) => Plan(new EffectDef { Kind = kind, Volley = volley }, 4, 3);
        int Streaks(EffectPlan p) => p.Layers.Count(l => l.Sprite == "streak");
        int Thrown(EffectPlan p) => p.Layers.First(l => l.Sprite == "streak").Amount;
        Assert.Contains(P("arrows").Layers, l => l.Lying);
        Assert.All(new[] { "javelins", "slingstones" }, k => Assert.DoesNotContain(P(k).Layers, l => l.Lying));
        Assert.Equal((2, 2, 1, 1), (Streaks(P("javelins")), Streaks(P("javelins", "random")), Streaks(P("slingstones")), Streaks(P("slingstones", "random"))));
        Assert.True(Thrown(P("javelins")) < Thrown(P("arrows")) && Thrown(P("arrows")) < Thrown(P("slingstones")));
    }

    [Fact]
    public void Falling_rocks_one_after_another_come_down_through_the_whole_cycle()
    {
        float[] Starts(string volley) => Plan(new EffectDef { Kind = "rockfall", Volley = volley }, 2, 2).Layers.Where(l => l.Sprite == "drop" && l.Amount == 1).Select(l => l.Offset).ToArray();
        Assert.Equal(4, Starts("rolling").Length);
        Assert.True(Starts("rolling").Max() - Starts("rolling").Min() > 0.5f);
        Assert.True(Starts("together").Max() - Starts("together").Min() < 0.1f);
    }

    [Fact]
    public void A_fire_named_without_smoke_has_none()
    {
        Assert.False(EffectKinds.FromName("Burning farm").Smoke);
        Assert.True(EffectKinds.FromName("Smoking ruin").Smoke);
    }

    [Theory]
    [InlineData("powder", null)]
    [InlineData("powder", "together")]
    [InlineData("powder", "rolling")]
    [InlineData("cannon", "rolling")]
    public void Lingering_smoke_rides_the_wind_at_the_map_s_scale_no_further_than_real_smoke_carries(string kind, string? volley)
    {
        var air = new SmokeAir(1, 1 / SmokeDrift.UncalibratedMetresPerUnit);
        var plume = SmokeDrift.Of(kind, 6, 1, air);
        Assert.True(plume.Reach > 0);
        var usual = Plan(new EffectDef { Kind = kind, Volley = volley }, 6, 0.6f).Layers.Where(l => l.Sprite == "puff").ToList();
        var unticked = EffectRecipe.Plan(new EffectDef { Kind = kind, Volley = volley }, 6, 0.6f, 0.3f, Grey, false, air).Layers.Where(l => l.Sprite == "puff").ToList();
        var lingering = EffectRecipe.Plan(new EffectDef { Kind = kind, Volley = volley, Lingers = true }, 6, 0.6f, 0.3f, Grey, false, air).Layers.Where(l => l.Sprite == "puff").ToList();
        Assert.Equal(usual.Select(l => (l.Life, l.Amount, l.Cycles)), unticked.Select(l => (l.Life, l.Amount, l.Cycles)));
        Assert.All(usual, l => Assert.Equal(0, l.Reach));
        Assert.All(lingering, l => Assert.InRange(l.Reach, float.Epsilon, plume.Reach));
        Assert.True(lingering.Max(l => l.Life) > usual.Max(l => l.Life));
        Assert.All(lingering, l => Assert.InRange(l.Cycles, 2, SmokeDrift.MostEmitters));
        Assert.True(lingering.Count(l => l.Fixed) <= SmokeDrift.MostEmitters + 8);
        float ride = Math.Max(SmokeDrift.ScreenWind(air), SmokeDrift.StillDrift);
        Assert.All(lingering, l => Assert.True(l.Reach / (l.Life * (1 - l.Brake)) <= ride * 1.01f, $"{l.Life} s, {l.Cycles} turns"));
    }

    [Theory]
    [InlineData("powder", null)]
    [InlineData("powder", "together")]
    [InlineData("powder", "rolling")]
    [InlineData("cannon", "rolling")]
    public void Dense_smoke_holds_to_half_its_length_then_fades_out_gradually(string kind, string? volley)
    {
        var air = new SmokeAir(1, 1 / SmokeDrift.UncalibratedMetresPerUnit);
        var lingering = EffectRecipe.Plan(new EffectDef { Kind = kind, Volley = volley, Lingers = true }, 6, 0.6f, 0.3f, Grey, false, air).Layers.Where(l => l.Heat == SmokeHeat.Cold).ToList();
        Assert.NotEmpty(lingering);
        foreach (var l in lingering)
        {
            float peak = l.Ramp.Max(k => k.c.A);
            var after = l.Ramp.SkipWhile(k => k.c.A < peak).ToArray();
            Assert.All(after.Where(k => k.at <= 0.5f), k => Assert.Equal(peak, k.c.A, 3));
            var fade = after.Where(k => k.at >= 0.5f).ToArray();
            Assert.Equal(0, fade[^1].c.A, 3);
            Assert.Equal(1, fade[^1].at, 3);
            for (int i = 1; i < fade.Length; i++)
            {
                Assert.True(fade[i].c.A <= fade[i - 1].c.A);
                Assert.True(fade[i - 1].c.A - fade[i].c.A <= peak * 0.4f, "fades gradually");
            }
        }
    }

    [Fact]
    public void Smoke_is_seen_furthest_in_a_light_breeze_and_cleared_by_a_strong_wind()
    {
        double At(double wind) => SmokeDrift.ReachMetres("powder", 170, 1, wind);
        Assert.True(At(1) > At(2) && At(2) > At(4), $"{At(1):0} m, {At(2):0} m, {At(4):0} m");
        Assert.Equal(SmokeDrift.WindMetresPerSecond(0) * SmokeDrift.FiringSeconds, At(0), 3);
        Assert.InRange(SmokeDrift.WindMetresPerSecond(1), 1.5, 3.3);
        Assert.InRange(SmokeDrift.WindMetresPerSecond(2), 5.5, 8.0);
        Assert.InRange(SmokeDrift.WindMetresPerSecond(5), 13.9, 20.7);
    }

    [Fact]
    public void More_fire_and_a_wider_front_carry_further()
    {
        Assert.True(SmokeDrift.ReachMetres("powder", 170, 2, 1) > SmokeDrift.ReachMetres("powder", 170, 1, 1));
        Assert.True(SmokeDrift.ReachMetres("powder", 340, 1, 1) > SmokeDrift.ReachMetres("powder", 170, 1, 1));
        Assert.Equal(0, SmokeDrift.ReachMetres("powder", 170, 0, 1));
    }

    [Fact]
    public void The_same_ground_reaches_the_same_distance_on_any_map_scale()
    {
        var coarse = SmokeDrift.Of("powder", 6, 1, new SmokeAir(1, 1.0 / 30));
        var fine = SmokeDrift.Of("powder", 12, 1, new SmokeAir(1, 1.0 / 15));
        Assert.Equal(coarse.Reach * 2, fine.Reach, 3);
        Assert.Equal(SmokeAir.Of(new Wind(), new Calibration()), SmokeAir.Of(new Wind(), null));
        Assert.Equal(0.05, SmokeAir.Of(new Wind(), new Calibration { UnitsPerMeter = 0.05 }).UnitsPerMetre);
    }
    [Theory]
    [InlineData("powder")]
    [InlineData("cannon")]
    [InlineData("burst")]
    [InlineData("fire")]
    [InlineData("oil")]
    [InlineData("smoke")]
    [InlineData("broadside")]
    [InlineData("screen")]
    public void Every_smoke_is_either_cold_or_hot(string kind)
    {
        var smoke = Plan(new EffectDef { Kind = kind }).Layers.Where(l => l.Sprite == "puff" && !l.Glow).ToList();
        Assert.NotEmpty(smoke);
        Assert.All(smoke, l => Assert.NotEqual(SmokeHeat.None, l.Heat));
        Assert.All(smoke, l => Assert.True(l.Brake > 0, "rides the wind once thrown"));
    }

    [Fact]
    public void Fire_smoke_rises_in_still_air_and_is_laid_flat_by_a_gale()
    {
        float Rise(double wind) => EffectRecipe.Plan(new EffectDef { Kind = "fire" }, 2, 2, 0.3f, Grey, false, new SmokeAir(wind, 1 / 30.0))
            .Layers.Single(l => l.Heat == SmokeHeat.Hot).Rise;
        Assert.True(Rise(0) > Rise(1) && Rise(1) > Rise(3) && Rise(3) > Rise(5));
        Assert.True(Rise(5) < Rise(0) * 0.3f);
    }

    [Fact]
    public void A_gale_tears_powder_smoke_away_sooner_than_a_light_breeze()
    {
        float Gone(double wind) => EffectRecipe.Plan(new EffectDef { Kind = "powder", Volley = "together" }, 6, 0.6f, 0.3f, Grey, false, new SmokeAir(wind, 1 / 30.0))
            .Layers.Where(l => l.Heat == SmokeHeat.Cold).Max(l => l.Ramp.Where(k => k.c.A > 0.01f).Max(k => k.at));
        Assert.True(Gone(5) < Gone(2) && Gone(2) < Gone(1));
        Assert.Equal(Gone(0), Gone(1));
    }

    [Fact]
    public void Smoke_rides_the_wind_faster_on_a_closer_map_scale()
    {
        Assert.Equal(2 * SmokeDrift.ScreenWind(new SmokeAir(1, 1 / 30.0)), SmokeDrift.ScreenWind(new SmokeAir(1, 1 / 15.0)), 4);
        Assert.True(SmokeDrift.ScreenWind(new SmokeAir(5, 1 / 30.0)) > 5 * SmokeDrift.ScreenWind(new SmokeAir(1, 1 / 30.0)));
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("oil")]
    public void Fire_smoke_starts_in_the_flames(string kind)
    {
        var plan = Plan(new EffectDef { Kind = kind }, 3, 3);
        var flame = plan.Layers.First(l => l.Sprite == "flame");
        var smoke = plan.Layers.Single(l => l.Heat == SmokeHeat.Hot);
        float flameTop = flame.At.Y + flame.VMax * flame.Life + flame.Lift * flame.Life * flame.Life / 2;
        Assert.True(smoke.At.Y - smoke.Box.Y <= flameTop, $"smoke from {smoke.At.Y - smoke.Box.Y}, flames reach {flameTop}");
        Assert.True(smoke.Ramp.First(k => k.c.A > 0.3f).at <= 0.05f, "seen as it leaves the flames");
    }
}
