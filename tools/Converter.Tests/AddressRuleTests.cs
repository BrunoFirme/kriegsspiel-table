namespace KTable.Converter.Tests;

public sealed class AddressRuleTests
{
    [Theory]
    [InlineData("http://pastebin.com/raw.php?i=abc123", "http://pastebin.com/raw/abc123")]
    [InlineData("{verifycache}https://i.imgur.com/a.png", "https://i.imgur.com/a.png")]
    [InlineData("{de}https://a.test/de.png{en}https://a.test/en.png", "https://a.test/en.png")]
    [InlineData("{fr}https://a.test/fr.png{de}https://a.test/de.png", "https://a.test/fr.png")]
    [InlineData("https://a.test/back.png{Unique}", "https://a.test/back.png")]
    [InlineData("  https://a.test/plain.png ", "https://a.test/plain.png")]
    public void An_address_is_read_as_the_game_rewrites_it_before_fetching(string saved, string read)
    {
        Assert.Equal(read, AssetStore.Canonical(saved));
        Assert.Equal(AssetStore.Key(read), AssetStore.Key(saved));
    }

    [Theory]
    [InlineData("<!doctype html><html></html>")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<HTML><body>")]
    public void A_web_page_returned_for_a_picture_is_named_as_a_web_page_whatever_its_case(string page)
    {
        Assert.Equal("html", AssetStore.Sniff(System.Text.Encoding.ASCII.GetBytes(page)));
    }
}
