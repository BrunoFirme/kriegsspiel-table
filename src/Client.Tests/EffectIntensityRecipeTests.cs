using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class EffectIntensityRecipeTests
{
    static readonly Rgba Grey = new(0.5f, 0.5f, 0.5f);
    static readonly SmokeAir Air = new(1, 1 / SmokeDrift.UncalibratedMetresPerUnit);

    static EffectPlan Plan(EffectDef e) => EffectRecipe.Plan(e, 6, 0.6f, 0.3f, Grey, false, Air);

    static EffectPlan Fired(string? volley, double? intensity) => Plan(EffectKinds.Placed(new EffectDef { Kind = "powder", Volley = volley }, null, intensity));

    static int Flashes(EffectPlan p) => p.Layers.Where(l => l.Sprite == "muzzle").Sum(l => l.Amount);
    static int Puffs(EffectPlan p) => p.Layers.Where(l => l.Sprite == "puff").Sum(l => l.Amount);
    static List<EffectLayer> Dust(double density) => Plan(new EffectDef { Kind = "dust", Density = density }).Layers.Where(l => l.Sprite == "dust").ToList();

    [Fact]
    public void Musketry_at_will_shows_fewer_shots_and_less_smoke_slack_and_more_hot()
    {
        var slack = Fired(null, 0.25);
        var made = Fired(null, null);
        var hot = Fired(null, 2);
        Assert.True(Flashes(slack) < Flashes(made) && Flashes(made) < Flashes(hot), $"flashes {Flashes(slack)} < {Flashes(made)} < {Flashes(hot)}");
        Assert.True(Puffs(slack) < Puffs(made) && Puffs(made) < Puffs(hot), $"puffs {Puffs(slack)} < {Puffs(made)} < {Puffs(hot)}");
    }

    [Theory]
    [InlineData("together")]
    [InlineData("rolling")]
    public void Volleys_come_closer_together_hot_and_further_apart_slack(string volley)
    {
        var def = new EffectDef { Kind = "powder", Volley = volley };
        double every = EffectKinds.Interval(def);
        Assert.Equal(every / 2, EffectKinds.Interval(EffectKinds.WithIntensity(def, 2)), 9);
        Assert.Equal(every * 2, EffectKinds.Interval(EffectKinds.WithIntensity(def, 0.5)), 9);
        Assert.Equal(def.Amount, EffectKinds.WithIntensity(def, 2).Amount);
        float Cycle(double? by) => Fired(volley, by).Layers.First(l => l.Sprite == "muzzle").Life;
        Assert.True(Cycle(2) < Cycle(null) && Cycle(null) < Cycle(0.5));
    }

    [Fact]
    public void Intensity_as_made_or_on_a_kind_without_it_leaves_the_effect_alone()
    {
        var musket = new EffectDef { Kind = "powder" };
        Assert.Same(musket, EffectKinds.WithIntensity(musket, null));
        Assert.Same(musket, EffectKinds.WithIntensity(musket, EffectIntensity.AsMade));
        Assert.Equal(1, musket.Amount);
        foreach (var kind in new[] { "smoke", "fog", "screen", "oil", "dust", "fire", "camp", "lamps", "sparks", "flare", "wake" })
        {
            var still = new EffectDef { Kind = kind };
            Assert.False(EffectKinds.HasIntensity(still), kind);
            Assert.Same(still, EffectKinds.WithIntensity(still, 3));
        }
        Assert.Equal(0.5, EffectKinds.Placed(musket, SmokeDensity.Fine, 0.5).Amount);
        Assert.Equal(SmokeDensity.Fine, EffectKinds.Placed(musket, SmokeDensity.Fine, 0.5).Density);
        Assert.Equal("50%", EffectKinds.IntensityText(0.5));
    }

    static string Look(EffectPlan p) =>
        string.Join(";", p.Layers.Concat(p.Projectiles?.Shots.SelectMany(s => s.Trail.Concat(s.Burst)) ?? []).Select(l => $"{l.Sprite}:{l.Amount}:{l.Life}:{l.Offset}")) + "|" + p.Projectiles?.Every;

    [Theory]
    [InlineData("cannon", null)]
    [InlineData("cannon", "rolling")]
    [InlineData("broadside", null)]
    [InlineData("arrows", null)]
    [InlineData("arrows", "rolling")]
    [InlineData("firearrows", null)]
    [InlineData("javelins", null)]
    [InlineData("slingstones", null)]
    [InlineData("stones", null)]
    [InlineData("rockfall", null)]
    [InlineData("column", null)]
    [InlineData("spray", null)]
    [InlineData("burst", null)]
    [InlineData("barrage", null)]
    [InlineData("rocket", null)]
    public void What_fires_in_shots_fires_them_closer_together_hot_and_further_apart_slack(string kind, string? volley)
    {
        var def = new EffectDef { Kind = kind, Volley = volley };
        Assert.True(EffectKinds.HasIntensity(def) && EffectKinds.Fires(def));
        double every = EffectKinds.Interval(def);
        var hot = EffectKinds.WithIntensity(def, 2);
        var slack = EffectKinds.WithIntensity(def, 0.5);
        Assert.Equal(every / 2, EffectKinds.Interval(hot), 9);
        Assert.Equal(every * 2, EffectKinds.Interval(slack), 9);
        Assert.Equal(def.Amount, hot.Amount);
        Assert.Equal(every, EffectKinds.Interval(def), 9);
        Assert.NotEqual(Look(Plan(def)), Look(Plan(hot)));
        Assert.NotEqual(Look(Plan(def)), Look(Plan(slack)));
    }

    [Theory]
    [InlineData("arrows", "random", "streak")]
    [InlineData("javelins", "random", "streak")]
    [InlineData("slingstones", "random", "streak")]
    [InlineData("tracer", null, "streak")]
    [InlineData("rain", null, "streak")]
    [InlineData("snow", null, "drop")]
    [InlineData("melee", null, "dust")]
    public void What_plays_all_the_time_shows_less_slack_and_more_hot(string kind, string? volley, string sprite)
    {
        var def = new EffectDef { Kind = kind, Volley = volley };
        Assert.True(EffectKinds.HasIntensity(def) && !EffectKinds.Fires(def));
        int Shown(double? by) => EffectRecipe.Plan(EffectKinds.WithIntensity(def, by), 16, 6, 0.3f, Grey, false, Air).Layers.Where(l => l.Sprite == sprite).Sum(l => l.Amount);
        Assert.True(Shown(0.25) < Shown(null) && Shown(null) < Shown(3), $"{kind}: {Shown(0.25)} < {Shown(null)} < {Shown(3)}");
        Assert.Equal(EffectKinds.Interval(def), EffectKinds.Interval(EffectKinds.WithIntensity(def, 3)));
    }

    [Theory]
    [InlineData("powder", "together")]
    [InlineData("rain", null)]
    public void An_intensity_below_the_least_plays_as_the_least(string kind, string? volley)
    {
        var def = new EffectDef { Kind = kind, Volley = volley };
        var least = EffectKinds.WithIntensity(def, EffectIntensity.Least);
        var below = EffectKinds.WithIntensity(def, 0.1);
        Assert.Equal(least.Every, below.Every);
        Assert.Equal(least.Amount, below.Amount);
        Assert.Equal(EffectIntensity.AsMade, EffectIntensity.Least * EffectIntensity.Most);
    }

    [Fact]
    public void Dust_takes_the_smoke_setting_finer_and_denser()
    {
        Assert.True(EffectKinds.HasSmokeSetting(new EffectDef { Kind = "dust" }));
        Assert.Equal("Dust", EffectKinds.SmokeName(new EffectDef { Kind = "dust" }));
        Assert.Equal("Smoke", EffectKinds.SmokeName(new EffectDef { Kind = "powder" }));
        var steps = new[] { SmokeDensity.VeryFine, SmokeDensity.Fine, SmokeDensity.Standard, SmokeDensity.Dense, SmokeDensity.VeryDense }.Select(Dust).ToList();
        for (int i = 1; i < steps.Count; i++)
        {
            Assert.True(steps[i].Sum(l => l.Amount) > steps[i - 1].Sum(l => l.Amount), $"step {i}: more dust");
            Assert.True(steps[i].Max(l => l.SMax) >= steps[i - 1].Max(l => l.SMax), $"step {i}: no smaller");
            Assert.True(steps[i].Max(l => l.Ramp.Max(r => r.c.A)) >= steps[i - 1].Max(l => l.Ramp.Max(r => r.c.A)), $"step {i}: no fainter");
        }
        Assert.True(steps[0].Max(l => l.Ramp.Max(r => r.c.A)) < steps[2].Max(l => l.Ramp.Max(r => r.c.A)), "very fine dust is fainter than standard");
    }

    [Fact]
    public void Melee_dust_keeps_its_look_whatever_its_density()
    {
        string Look(double density) => string.Join(";", Plan(new EffectDef { Kind = "melee", Density = density }).Layers.Select(l => $"{l.Sprite}:{l.Amount}:{l.SMax}"));
        Assert.Equal(Look(SmokeDensity.Standard), Look(SmokeDensity.VeryFine));
    }
}
