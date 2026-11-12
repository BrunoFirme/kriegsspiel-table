using KTable.Client.Prefs;

namespace KTable.Client.Tests;

public class InterfaceTintTests
{
    static readonly Rgb Ink = new(0.914f, 0.898f, 0.859f);
    static readonly Rgb[] Surfaces = { InterfaceTint.Bar, InterfaceTint.Surface, InterfaceTint.Raised, InterfaceTint.Overlay };
    static readonly string[] Picks = { "#ff0000", "#0000ff", "#00ff00", "#ffff00", "#ffffff", "#000000", "#010203", "#805f42" };
    const float LeastInkContrast = 7f;

    static float Contrast(Rgb a, Rgb b) => (MathF.Max(a.Luminance, b.Luminance) + 0.05f) / (MathF.Min(a.Luminance, b.Luminance) + 0.05f);

    [Fact]
    public void Charcoal_leaves_every_surface_exactly_as_the_app_draws_it()
    {
        foreach (var own in Surfaces) Assert.Equal(own, InterfaceTint.Tinted(own, InterfaceTint.Charcoal));
        Assert.Equal(InterfaceTint.Charcoal, InterfaceTint.Presets[0].Tint);
    }

    [Fact]
    public void Any_tint_keeps_each_surface_as_bright_as_before_so_text_reads_the_same()
    {
        foreach (var tint in InterfaceTint.Presets.Select(p => p.Tint).Concat(Picks))
            foreach (var own in Surfaces)
            {
                var tinted = InterfaceTint.Tinted(own, tint);
                Assert.InRange(tinted.Luminance, own.Luminance * 0.995f, own.Luminance * 1.005f);
                Assert.True(Contrast(Ink, tinted) >= LeastInkContrast, $"{tint} on {own}");
                Assert.All(new[] { tinted.R, tinted.G, tinted.B }, c => Assert.InRange(c, 0f, 1f));
            }
    }

    [Fact]
    public void A_loud_colour_tints_no_stronger_than_the_cap()
    {
        foreach (var tint in Picks)
            Assert.True(InterfaceTint.Tinted(InterfaceTint.Surface, tint).HueAndStrength().Strength <= InterfaceTint.MostStrength + 0.02f, tint);
    }

    [Fact]
    public void Brass_stays_brass_under_charcoal_or_a_grey_tint()
    {
        foreach (var tint in new[] { InterfaceTint.Charcoal, "#808080", "#7f7d7a" })
            foreach (var own in new[] { InterfaceTint.Accent, InterfaceTint.AccentInk, InterfaceTint.OnAccent })
                Assert.Equal(own, InterfaceTint.Hued(own, tint));
    }

    [Fact]
    public void Brass_takes_the_tint_hue_and_keeps_its_brightness_so_button_text_reads_the_same()
    {
        const float LeastButtonContrast = 4.5f;
        foreach (var tint in InterfaceTint.Presets.Select(p => p.Tint).Concat(Picks).Append("#225d4f"))
        {
            var accent = InterfaceTint.Hued(InterfaceTint.Accent, tint);
            foreach (var own in new[] { InterfaceTint.Accent, InterfaceTint.AccentInk, InterfaceTint.OnAccent })
                Assert.InRange(InterfaceTint.Hued(own, tint).Luminance, own.Luminance * 0.995f, own.Luminance * 1.005f);
            Assert.True(Contrast(accent, InterfaceTint.Hued(InterfaceTint.OnAccent, tint)) >= LeastButtonContrast, tint);
            Assert.True(Contrast(InterfaceTint.Hued(InterfaceTint.AccentInk, tint), InterfaceTint.Tinted(InterfaceTint.Surface, tint)) >= LeastButtonContrast, tint);
        }
        var (hue, _) = InterfaceTint.Hued(InterfaceTint.Accent, "#225d4f").HueAndStrength();
        var (tintHue, _) = Rgb.FromHex("#225d4f")!.Value.HueAndStrength();
        Assert.InRange(hue, tintHue - 1, tintHue + 1);
    }

    [Fact]
    public void Walnut_turns_the_panels_brown()
    {
        var (hue, strength) = InterfaceTint.Tinted(InterfaceTint.Surface, InterfaceTint.Presets.First(p => p.Name == "Walnut").Tint).HueAndStrength();
        Assert.InRange(hue, 20f, 40f);
        Assert.True(strength > 0.35f);
    }

    [Fact]
    public void Only_a_colour_or_charcoal_is_kept()
    {
        Assert.Equal(InterfaceTint.Charcoal, InterfaceTint.Kept("brown"));
        Assert.Equal(InterfaceTint.Charcoal, InterfaceTint.Kept(null));
        Assert.Equal("#805f42", InterfaceTint.Kept("#805F42"));
        Assert.Equal("Walnut", InterfaceTint.Named("#805F42"));
        Assert.Null(InterfaceTint.Named("#123456"));
        Assert.Equal("#805f42", Rgb.FromHex("#805f42")!.Value.Hex);
    }
}
