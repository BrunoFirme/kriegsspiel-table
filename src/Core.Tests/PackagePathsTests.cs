using KTable.Core;

namespace KTable.Core.Tests;

public sealed class PackagePathsTests
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "ktable-paths", "packages");

    [Theory]
    [InlineData("user.mine", "pack.json")]
    [InlineData("tts.Some_Module", "assets/0a1b.png")]
    [InlineData("sheets", "tiles\\map\\0\\0_0.jpg")]
    [InlineData("sheets", "relief/abc.bin")]
    [InlineData("scenarios/kessel-valley", "tiles/map/index.json")]
    [InlineData("scenarios/kessel-valley", "relief/abc.json")]
    public void A_file_inside_its_pack_s_folder_is_found_there(string package, string rel)
    {
        var abs = PackagePaths.Inside(Root, package, rel);
        Assert.NotNull(abs);
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Root, package)) + Path.DirectorySeparatorChar, abs);
    }

    [Theory]
    [InlineData("..", "pack.json")]
    [InlineData(".", "pack.json")]
    [InlineData("...", "pack.json")]
    [InlineData("a/../..", "pack.json")]
    [InlineData("a\\b", "pack.json")]
    [InlineData("C:", "pack.json")]
    [InlineData("", "pack.json")]
    [InlineData(" ", "pack.json")]
    [InlineData("user.x", "../pack.json")]
    [InlineData("user.x", "assets\\..\\..\\pack.json")]
    [InlineData("user.x", "/etc/passwd")]
    [InlineData("user.x", "\\Windows\\win.ini")]
    [InlineData("user.x", "C:\\Windows\\win.ini")]
    [InlineData("user.x", "C:win.ini")]
    [InlineData("user.x", "a.png:stream")]
    [InlineData("user.x", "assets//a.png")]
    [InlineData("user.x", "assets/ ./a.png")]
    [InlineData("user.x", "")]
    [InlineData("scenarios/", "pack.json")]
    [InlineData("scenarios/..", "pack.json")]
    [InlineData("scenarios/a/b", "pack.json")]
    [InlineData("scenarios/a\\b", "pack.json")]
    [InlineData("scenarios\\a", "pack.json")]
    [InlineData("other/a", "pack.json")]
    [InlineData("Scenarios/a", "pack.json")]
    public void A_name_that_leaves_its_pack_s_folder_is_refused(string package, string rel) => Assert.Null(PackagePaths.Inside(Root, package, rel));

    [Fact]
    public void Missing_names_are_refused()
    {
        Assert.Null(PackagePaths.Inside(Root, null, "pack.json"));
        Assert.Null(PackagePaths.Inside(Root, "user.x", null));
        Assert.Null(PackagePaths.Inside(Root, "user.x", "a\u0001.png"));
    }

    [Theory]
    [InlineData("user.x", "assets/a.png", true)]
    [InlineData("", "tiles/map/index.json", true)]
    [InlineData("scenarios/ford", @"tiles\map\0\0_0.webp", true)]
    [InlineData(@"\\elsewhere\share", "tiles/map/index.json", false)]
    [InlineData("sheets", @"\\elsewhere\share\sky.png", false)]
    [InlineData("sheets", @"C:\Windows\sky.png", false)]
    [InlineData("..", "sky.png", false)]
    [InlineData("sheets", "../../sky.png", false)]
    [InlineData(null, "sky.png", false)]
    [InlineData("sheets", null, false)]
    public void A_file_is_looked_for_only_under_the_game_s_own_folder(string? package, string? rel, bool under)
    {
        Assert.Equal(under, PackagePaths.Under(Root, package, rel));
    }
}
