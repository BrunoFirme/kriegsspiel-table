using System.Linq;
using KTable.Client;

namespace KTable.Client.Tests;

public class FolderPlacesTests
{
    [Theory]
    [InlineData("\"C:\\Users\\me\\Desktop\\RandomMaps\\map one.png\"", "C:\\Users\\me\\Desktop\\RandomMaps\\map one.png")]
    [InlineData("  'C:\\maps\\'  ", "C:\\maps")]
    [InlineData("\u201CD:\\scans\\sheet.jpg\u201D", "D:\\scans\\sheet.jpg")]
    [InlineData("file:///C:/Users/me/My%20Maps/a.png", "C:\\Users\\me\\My Maps\\a.png")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("C:/maps/sheet.png", "C:/maps/sheet.png")]
    public void A_pasted_path_is_read_whole_without_its_quotes(string pasted, string expected)
    {
        if (!System.OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, FolderPlaces.TypedPath(pasted));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("map.png")]
    [InlineData("maps\\map.png")]
    [InlineData("\"\"")]
    [InlineData("https://example.com/map.png")]
    public void Text_that_is_not_a_whole_path_is_left_to_the_explorer(string typed) => Assert.Null(FolderPlaces.TypedPath(typed));

    [Fact]
    public void Places_keep_only_folders_that_exist_once_each()
    {
        var places = FolderPlaces.Gather(
            [new("Desktop", "C:\\Users\\me\\OneDrive\\Desktop"), new("Documents", ""), new("Downloads", "C:\\Users\\me\\Downloads"), new("Pictures", "C:\\gone"), new("Again", "c:\\users\\me\\downloads\\")],
            p => p != "C:\\gone");
        Assert.Equal(["Desktop", "Downloads"], places.ConvertAll(p => p.Name));
    }

    [Fact]
    public void This_pc_offers_only_folders_it_has()
    {
        var places = FolderPlaces.OnThisPc();
        Assert.NotEmpty(places);
        Assert.All(places, p => Assert.True(System.IO.Directory.Exists(p.Path)));
        Assert.Equal(places.Count, places.Select(p => p.Path.ToLowerInvariant()).Distinct().Count());
    }
}
