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
    public void A_firing_layer_starts_part_way_into_its_cycle_and_a_picture_catches_it_at_the_shot()
    {
        var plan = Plan(new EffectDef { Kind = "cannon" }, phase: 0.25f);
        var fired = plan.Layers.Where(l => l.Explosive > 0).ToList();
        Assert.NotEmpty(fired);
        Assert.All(fired, l =>
        {
            Assert.Equal((0.25f + l.Offset) % 1 * l.Life, l.Start);
            Assert.Equal(0.025f * l.Life, l.Peak);
        });
        Assert.All(Plan(new EffectDef { Kind = "smoke" }).Layers, l => Assert.Equal((-1f, -1f), (l.Start, l.Peak)));
    }

    [Fact]
    public void A_rolling_volley_fires_section_after_section_down_the_line()
    {
        var together = Plan(new EffectDef { Kind = "powder", Volley = "together" }, w: 6);
        Assert.Equal(new[] { 0f, 0f, 0f }, together.Layers.Select(l => l.Offset));
        var rolling = Plan(new EffectDef { Kind = "powder", Volley = "rolling" }, w: 6);
        var offsets = rolling.Layers.Where(l => l.Sprite == "puff").Select(l => l.Offset).ToList();
        Assert.Equal(7, offsets.Count);
        Assert.Equal(offsets.OrderBy(o => o), offsets);
        Assert.Equal(0f, offsets[0]);
        Assert.True(offsets[^1] < 0.5f);
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
        Assert.Equal(90, l.Amount);
        Assert.Equal(new Vector3(1.425f, 0.0866025403f, 0.25f), l.Box);
        Assert.Equal(1.21243548f, l.VMax);
        Assert.Equal(new Rgba(0.55f, 0.55f, 0.55f, 0.68f), l.Ramp[2].c);
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
        Assert.All(smoke, l => Assert.True(l.Drift >= 0.6f, $"drift {l.Drift}"));
    }

    [Theory]
    [InlineData("Congreve rocket", "rocket")]
    [InlineData("Battery of guns", "cannon")]
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
    public void An_effect_with_no_settings_is_the_kind_its_name_says(string name, string kind) =>
        Assert.Equal(kind, EffectKinds.FromName(name).Kind);

    [Fact]
    public void A_fire_named_without_smoke_has_none()
    {
        Assert.False(EffectKinds.FromName("Burning farm").Smoke);
        Assert.True(EffectKinds.FromName("Smoking ruin").Smoke);
    }
}
