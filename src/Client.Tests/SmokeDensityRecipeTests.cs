using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SmokeDensityRecipeTests
{
    static readonly Rgba Grey = new(0.5f, 0.5f, 0.5f);
    static readonly SmokeAir Air = new(1, 1 / SmokeDrift.UncalibratedMetresPerUnit);

    static EffectPlan Plan(string kind, double density, string? volley = null, bool smoke = true) =>
        EffectRecipe.Plan(new EffectDef { Kind = kind, Volley = volley, Density = density, Smoke = smoke }, 6, 0.6f, 0.3f, Grey, false, Air);

    static List<EffectLayer> Smoke(EffectPlan p, string kind) =>
        p.Layers.Where(l => l.Sprite == "puff" && !l.Add && !l.Glow || kind == "fog" && l.Sprite == "mist").ToList();

    static string Look(EffectPlan p) =>
        string.Join(";", p.Layers.Select(l => $"{l.Sprite}:{l.Amount}:{l.SMax}:{l.Life}:{l.Reach}:{string.Join(",", l.Ramp.Select(r => r.c.A))}"));

    [Theory]
    [InlineData("powder", null)]
    [InlineData("powder", "together")]
    [InlineData("cannon", "rolling")]
    [InlineData("fire", null)]
    [InlineData("smoke", null)]
    [InlineData("fog", null)]
    [InlineData("burst", null)]
    public void Each_notch_finer_shows_less_smoke_fainter_and_smaller(string kind, string? volley)
    {
        var steps = new[] { SmokeDensity.Standard, SmokeDensity.Fine, SmokeDensity.VeryFine }.Select(d => Smoke(Plan(kind, d, volley), kind)).ToList();
        for (int i = 1; i < steps.Count; i++)
        {
            Assert.Equal(steps[i - 1].Count, steps[i].Count);
            Assert.True(steps[i].Sum(l => l.Amount) < steps[i - 1].Sum(l => l.Amount), $"{kind} step {i}: fewer puffs");
            Assert.True(steps[i].Max(l => l.SMax) < steps[i - 1].Max(l => l.SMax), $"{kind} step {i}: smaller puffs");
            Assert.True(steps[i].Max(l => l.Ramp.Max(r => r.c.A)) < steps[i - 1].Max(l => l.Ramp.Max(r => r.c.A)), $"{kind} step {i}: fainter");
        }
    }

    [Theory]
    [InlineData("powder", null)]
    [InlineData("cannon", "rolling")]
    [InlineData("smoke", null)]
    [InlineData("fog", null)]
    [InlineData("dust", null)]
    public void Very_fine_still_shows_near_half_of_fine(string kind, string? volley)
    {
        float Shown(double density) => Plan(kind, density, volley).Layers
            .Where(l => l.Sprite == "puff" && !l.Add && !l.Glow || kind == "fog" && l.Sprite == "mist" || kind == "dust" && l.Sprite == "dust")
            .Sum(l => l.Amount * l.SMax * l.SMax * l.Ramp.Max(r => r.c.A));
        Assert.True(Shown(SmokeDensity.VeryFine) >= 0.4f * Shown(SmokeDensity.Fine), $"{kind}: {Shown(SmokeDensity.VeryFine)} vs fine {Shown(SmokeDensity.Fine)}");
    }

    [Theory]
    [InlineData("powder", null)]
    [InlineData("cannon", "rolling")]
    [InlineData("fire", null)]
    [InlineData("broadside", null)]
    public void Fine_smoke_leaves_the_flashes_and_flames_as_they_were(string kind, string? volley)
    {
        static string Rest(EffectPlan p) => string.Join(";", p.Layers.Where(l => !(l.Sprite == "puff" && !l.Add && !l.Glow)).Select(l => $"{l.Sprite}:{l.Amount}:{l.SMax}:{string.Join(",", l.Ramp.Select(r => r.c.A))}"));
        Assert.Equal(Rest(Plan(kind, SmokeDensity.Standard, volley)), Rest(Plan(kind, SmokeDensity.VeryFine, volley)));
    }

    [Theory]
    [InlineData("powder", null)]
    [InlineData("powder", "rolling")]
    [InlineData("cannon", "rolling")]
    public void Dense_smoke_trails_downwind_no_further_than_real_powder_smoke_and_very_dense_is_thicker(string kind, string? volley)
    {
        var standard = Smoke(Plan(kind, SmokeDensity.Standard, volley), kind);
        var half = Smoke(Plan(kind, 0.5, volley), kind);
        var dense = Smoke(Plan(kind, SmokeDensity.Dense, volley), kind);
        var very = Smoke(Plan(kind, SmokeDensity.VeryDense, volley), kind);
        float reach = SmokeDrift.Of(kind, 6, SmokeDensity.DenseAmount(kind), Air).Reach;
        Assert.All(standard, l => Assert.Equal(0, l.Reach));
        Assert.All(dense, l => Assert.InRange(l.Reach, 0.01f, reach + 0.001f));
        Assert.All(half, l => Assert.InRange(l.Reach, 0.01f, reach / 2 + 0.001f));
        Assert.All(very, l => Assert.True(l.Reach > 0));
        Assert.All(dense.Concat(half).Concat(very), l => Assert.True(l.Reach <= SmokeDrift.Ride(Air, l.Drift) * l.Life * (1 - l.Brake) * 1.001f, $"{l.Reach} in {l.Life} s"));
        Assert.True(very.Max(l => l.SMax) > dense.Max(l => l.SMax));
        Assert.True(very.Max(l => l.Amount / l.Life) > dense.Max(l => l.Amount / l.Life));
    }

    [Theory]
    [InlineData("rain")]
    [InlineData("snow")]
    [InlineData("sparks")]
    [InlineData("lamps")]
    [InlineData("tracer")]
    [InlineData("wake")]
    [InlineData("arrows")]
    [InlineData("melee")]
    [InlineData("stones")]
    [InlineData("column")]
    [InlineData("spray")]
    public void Effects_that_make_no_smoke_ignore_the_smoke_setting(string kind)
    {
        Assert.False(EffectKinds.HasSmokeSetting(new EffectDef { Kind = kind }));
        Assert.Equal(Look(Plan(kind, SmokeDensity.Standard)), Look(Plan(kind, SmokeDensity.VeryFine)));
        Assert.Equal(Look(Plan(kind, SmokeDensity.Standard)), Look(Plan(kind, SmokeDensity.VeryDense)));
    }

    [Fact]
    public void A_fire_left_without_smoke_takes_no_smoke_setting_and_camp_fires_and_rockets_do()
    {
        Assert.False(EffectKinds.HasSmokeSetting(new EffectDef { Kind = "fire", Smoke = false }));
        Assert.Equal(Look(Plan("fire", SmokeDensity.Standard, smoke: false)), Look(Plan("fire", SmokeDensity.VeryFine, smoke: false)));
        foreach (var kind in new[] { "camp", "rocket", "oil", "screen", "barrage", "firearrows", "broadside" })
            Assert.True(EffectKinds.HasSmokeSetting(new EffectDef { Kind = kind }), kind);
        var rockets = Plan("rocket", SmokeDensity.VeryFine).Projectiles!.Shots.SelectMany(s => s.Trail).Where(l => l.Sprite == "puff").ToList();
        var usual = Plan("rocket", SmokeDensity.Standard).Projectiles!.Shots.SelectMany(s => s.Trail).Where(l => l.Sprite == "puff").ToList();
        Assert.True(rockets.Sum(l => l.Amount) < usual.Sum(l => l.Amount));
    }

    [Fact]
    public void A_placed_piece_s_smoke_overrides_its_kind_and_nothing_set_keeps_it()
    {
        var made = new EffectDef { Kind = "powder", Density = SmokeDensity.Fine };
        Assert.Same(made, EffectKinds.WithDensity(made, null));
        Assert.Same(made, EffectKinds.WithDensity(made, SmokeDensity.Fine));
        Assert.Equal(SmokeDensity.VeryDense, EffectKinds.WithDensity(made, SmokeDensity.VeryDense).Density);
        Assert.Equal(SmokeDensity.Fine, made.Density);
        var rain = new EffectDef { Kind = "rain" };
        Assert.Same(rain, EffectKinds.WithDensity(rain, SmokeDensity.Dense));
    }

    [Theory]
    [InlineData(-2, "Very fine")]
    [InlineData(-1, "Fine")]
    [InlineData(0, "Standard")]
    [InlineData(1, "Dense")]
    [InlineData(2, "Very dense")]
    [InlineData(-0.35, "-0.35")]
    [InlineData(1.5, "+1.50")]
    public void The_slider_names_its_notches_and_numbers_the_ground_between(double density, string text) =>
        Assert.Equal(text, EffectKinds.DensityText(density));
}
