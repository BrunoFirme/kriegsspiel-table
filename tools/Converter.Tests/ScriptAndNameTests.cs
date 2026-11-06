namespace KTable.Converter.Tests;

public sealed class ScriptAndNameTests
{
    [Fact]
    public void A_counter_header_becomes_counters_with_their_bounds_and_defaults()
    {
        var lua = "counter1 = \"Ammo\"\nMIN_CTR1 = 0\nMAX_CTR1 = 12\nctr1 = 5\ncounter2 = 'Morale'\nctr2 = 3\nfunction onLoad() end\n";
        var si = ScriptInfo.Parse(lua, "");
        Assert.Equal("data", si.Kind);
        var ammo = si.Counters.Single(c => c.Name == "Ammo");
        Assert.Equal((0, 12, 5), (ammo.Min, ammo.Max, ammo.Default));
        var morale = si.Counters.Single(c => c.Name == "Morale");
        Assert.Equal((0, 99, 3), (morale.Min, morale.Max, morale.Default));
        Assert.Equal("Ammo", si.CounterKeys["ctr1"]);
    }

    [Fact]
    public void The_older_named_counters_are_read_too()
    {
        var si = ScriptInfo.Parse("MAX_AMMO = 6\nammo = 4\nMAX_MEN = 9\nmen = 9\n", "");
        Assert.Equal(new[] { "Ammo", "Manpower" }, si.Counters.Select(c => c.Name).Order());
        Assert.Equal(6, si.Counters.Single(c => c.Name == "Ammo").Max);
    }

    [Fact]
    public void An_editable_sheet_s_fields_become_a_name_counters_and_lines()
    {
        var state = """{"fields":[{"role":"name","value":["Hussars"]},{"role":"x","value":["7"]},{"role":"y","value":["light cavalry"]}]}""";
        var si = ScriptInfo.Parse("-- MarumEditable sheet\n", state);
        Assert.Equal(("data", "editable sheet"), (si.Kind, si.Variant));
        Assert.Equal("Hussars", si.TextLines.Single(t => t.Name == "Name").Default);
        Assert.Equal(7, si.Counters.Single().Default);
        Assert.Equal("light cavalry", si.TextLines.Single(t => t.Name == "Line 1").Default);
        Assert.Equal(("counter", "Value 1"), si.FieldMap[1]);
    }

    [Theory]
    [InlineData("local radius_1 = 10", "range rings (vector-line circles)")]
    [InlineData("tableHeightOffset = 1", "Flex Table Control")]
    [InlineData("bagStatus = {}", "fog-of-war memory bag")]
    public void What_cannot_be_data_is_dropped_and_named_for_the_report(string lua, string variant)
    {
        var si = ScriptInfo.Parse(lua, "");
        Assert.Equal(("dropped", variant), (si.Kind, si.Variant));
    }

    [Fact]
    public void No_script_is_nothing_and_one_that_holds_no_data_is_reported_as_dropped()
    {
        Assert.Equal("none", ScriptInfo.Parse("", "").Kind);
        Assert.Equal("dropped", ScriptInfo.Parse("function onLoad() end", "").Kind);
    }

    [Theory]
    [InlineData("1er Régiment de Hussards", "1er-regiment-de-hussards")]
    [InlineData("  ***  ", "piece")]
    [InlineData("Ünïcödé ✓ name", "unicode-name")]
    [InlineData("Søren's guns", "s-ren-s-guns")]
    public void Names_become_ids_of_plain_letters_and_dashes(string name, string id) => Assert.Equal(id, DefFactory.Slug(name));

    [Fact]
    public void A_long_name_is_cut_without_a_dash_left_at_its_end()
    {
        var id = DefFactory.Slug(new string('a', 39) + " bbbbbbbb");
        Assert.True(id.Length <= 40);
        Assert.False(id.EndsWith('-'));
        Assert.Equal(DefFactory.Hash8("x"), DefFactory.Hash8("x"));
        Assert.Equal(8, DefFactory.Hash8("x").Length);
    }
}
