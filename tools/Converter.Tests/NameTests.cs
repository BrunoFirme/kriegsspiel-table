namespace KTable.Converter.Tests;

public sealed class NameTests
{
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
