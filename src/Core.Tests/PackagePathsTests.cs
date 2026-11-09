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
    public void A_name_that_leaves_its_pack_s_folder_is_refused(string package, string rel) => Assert.Null(PackagePaths.Inside(Root, package, rel));

    [Fact]
    public void Missing_names_are_refused()
    {
        Assert.Null(PackagePaths.Inside(Root, null, "pack.json"));
        Assert.Null(PackagePaths.Inside(Root, "user.x", null));
        Assert.Null(PackagePaths.Inside(Root, "user.x", "a\u0001.png"));
    }
}
