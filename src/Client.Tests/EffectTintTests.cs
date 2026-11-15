using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class EffectTintTests
{
    static readonly Rgba Red = new(0.8f, 0.1f, 0.1f);
    static readonly SmokeAir Air = new(1, 1 / SmokeDrift.UncalibratedMetresPerUnit);

    [Theory]
    [InlineData("smoke")]
    [InlineData("fog")]
    [InlineData("screen")]
    [InlineData("dust")]
    [InlineData("melee")]
    [InlineData("flare")]
    [InlineData("tracer")]
    [InlineData("sparks")]
    [InlineData("lamps")]
    [InlineData("fire")]
    public void Smoke_mist_dust_lights_and_a_fire_s_smoke_take_a_tint(string kind) => Assert.True(EffectKinds.HasTint(new EffectDef { Kind = kind }), kind);

    [Theory]
    [InlineData("rain")]
    [InlineData("snow")]
    [InlineData("spray")]
    [InlineData("column")]
    [InlineData("wake")]
    [InlineData("arrows")]
    [InlineData("javelins")]
    [InlineData("stones")]
    [InlineData("slingstones")]
    [InlineData("rockfall")]
    [InlineData("powder")]
    [InlineData("cannon")]
    [InlineData("burst")]
    [InlineData("barrage")]
    [InlineData("oil")]
    [InlineData("broadside")]
    [InlineData("rocket")]
    [InlineData("firearrows")]
    [InlineData("camp")]
    public void Water_falling_things_and_gunfire_keep_their_own_colour(string kind)
    {
        var made = new EffectDef { Kind = kind };
        Assert.False(EffectKinds.HasTint(made), kind);
        Assert.Same(made, EffectKinds.WithTint(made, "#2e8b45"));
    }

    [Fact]
    public void A_fire_without_smoke_has_nothing_to_tint() => Assert.False(EffectKinds.HasTint(new EffectDef { Kind = "fire", Smoke = false }));

    [Fact]
    public void A_tint_replaces_the_colour_and_keeps_how_see_through_it_is()
    {
        Assert.Equal("#2e8b45", EffectKinds.WithTint(new EffectDef { Kind = "smoke", Color = "#D9623A" }, "#2e8b45").Color);
        Assert.Equal("#2e8b4566", EffectKinds.WithTint(new EffectDef { Kind = "fog", Color = "#E2E4E666" }, "#2e8b45").Color);
        Assert.Equal("#2e8b45", EffectKinds.WithTint(new EffectDef { Kind = "flare" }, "#2e8b45ff").Color);
        Assert.Equal("#aabbcc", EffectKinds.WithTint(new EffectDef { Kind = "smoke" }, "abc").Color);
    }

    [Fact]
    public void No_tint_or_a_broken_one_leaves_the_effect_as_made()
    {
        var made = new EffectDef { Kind = "smoke", Color = "#D9623A" };
        Assert.Same(made, EffectKinds.WithTint(made, null));
        Assert.Same(made, EffectKinds.WithTint(made, ""));
        Assert.Same(made, EffectKinds.WithTint(made, "#zzzzzz"));
        Assert.Equal("#D9623A", made.Color);
    }

    [Fact]
    public void A_placed_effect_takes_its_tint_with_its_smoke()
    {
        var placed = EffectKinds.Placed(new EffectDef { Kind = "smoke", Color = "#D9623A" }, SmokeDensity.Fine, null, "#2a6ebb");
        Assert.Equal(("#2a6ebb", SmokeDensity.Fine), (placed.Color, placed.Density));
    }

    static ShotSpec Shell(EffectDef e, Rgba c) => EffectRecipe.Plan(e, 6, 6, 0.3f, c, false, Air).Projectiles!.Shots[0];

    [Fact]
    public void A_star_shell_with_no_colour_burns_white_as_before()
    {
        var plain = new EffectDef { Kind = "flare" };
        var shot = Shell(plain, new Rgba(0.93f, 0.95f, 1f));
        Assert.Equal(new Rgba(1, 1, 0.95f), shot.HeadColor);
        Assert.Equal(new Rgba(1, 0.85f, 0.5f, 1), shot.Trail.First(l => l.Sprite == "spark").Ramp[0].c);
    }

    [Fact]
    public void A_star_shell_given_a_colour_burns_in_it_and_lights_the_ground_with_it()
    {
        var shot = Shell(new EffectDef { Kind = "flare", Color = "#cc1a1a" }, Red);
        Assert.True(shot.HeadColor.R > shot.HeadColor.G && shot.HeadColor.R > shot.HeadColor.B, $"head {shot.HeadColor}");
        var spark = shot.Trail.First(l => l.Sprite == "spark").Ramp;
        Assert.True(spark[1].c.R > spark[1].c.G, $"sparks {spark[1].c}");
        Assert.Equal(Red, shot.LightColor);
        var puff = shot.Trail.First(l => l.Sprite == "puff").Ramp[1].c;
        Assert.True(puff.G > Red.G + 0.3f, $"its smoke stays pale: {puff}");
    }
}
