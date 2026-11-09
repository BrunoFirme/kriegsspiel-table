namespace KTable.Converter.Tests;

public sealed class LocalPathTests
{
    [Theory]
    [InlineData("file:///home/rider/maps/field.png", "/home/rider/maps/field.png")]
    [InlineData("file:///home/rider/My%20Maps/field.png", "/home/rider/My Maps/field.png")]
    [InlineData("/home/rider/maps/field.png", "/home/rider/maps/field.png")]
    public void A_linux_file_address_keeps_its_root(string url, string expected) => Assert.Equal(expected, AssetStore.LocalPath(url));

    [Theory]
    [InlineData("file:///C:/Users/rider/field.png")]
    [InlineData("file://C:/Users/rider/field.png")]
    [InlineData("C:\\Users\\rider\\field.png")]
    public void A_windows_file_address_is_read_as_a_drive_path(string url)
    {
        var path = AssetStore.LocalPath(url);
        Assert.NotNull(path);
        Assert.StartsWith("C:", path);
        Assert.EndsWith("field.png", path);
    }

    [Theory]
    [InlineData("http://i.imgur.com/abc.png")]
    [InlineData("i.imgur.com/abc.png")]
    [InlineData("//cdn.example.org/abc.png")]
    [InlineData("file://server/share/abc.png")]
    public void A_web_or_network_address_is_not_a_local_file(string url) => Assert.Null(AssetStore.LocalPath(url));

    [Fact]
    public void Modules_are_looked_for_where_flatpak_and_proton_keep_the_game()
    {
        var homes = ModuleFolders.HomeFolders().Select(h => h.Replace('\\', '/')).ToList();
        Assert.Contains(homes, h => h.Contains(".var/app/com.valvesoftware.Steam/.local/share/"));
        Assert.Contains(homes, h => h.Contains("steamapps/compatdata/286160/pfx/drive_c/users/steamuser/Documents/My Games/"));
        var mods = ModuleFolders.ModsFolders(Path.Combine(Path.GetTempPath(), "no-steam-here")).Select(m => m.Replace('\\', '/')).ToList();
        Assert.Contains(mods, m => m.Contains("no-steam-here/steamapps/compatdata/286160/pfx/drive_c/users/steamuser/Documents/My Games/") && m.EndsWith("/Mods"));
    }
}
