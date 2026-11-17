using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class TileIndexTests
{
    static string Written(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-tileindex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "index.json");
        File.WriteAllText(path, json);
        return path;
    }

    static string Index(string level, int tileSize = 256, string ext = "webp") =>
        $$"""{ "tileSize": {{tileSize}}, "sourceW": 1024, "sourceH": 512, "ext": "{{ext}}", "levels": [ {{level}} ] }""";

    [Fact]
    public void A_map_s_tile_index_is_read_as_written()
    {
        var index = TileIndex.Load(Written(Index("""{ "level": 0, "w": 1024, "h": 512, "cols": 4, "rows": 2 }""")));
        var level = Assert.Single(index.Levels);
        Assert.Equal((4, 2, 1024, 512), (level.Cols, level.Rows, level.W, level.H));
        Assert.Empty(TileIndex.Load(Written("{}")).Levels);
    }

    [Theory]
    [InlineData("""{ "level": 0, "w": 1024, "h": 512, "cols": 2000000000, "rows": 2000000000 }""")]
    [InlineData("""{ "level": 0, "w": 1024, "h": 512, "cols": 2000, "rows": 2000 }""")]
    [InlineData("""{ "level": 0, "w": 1024, "h": 512, "cols": 0, "rows": 2 }""")]
    [InlineData("""{ "level": 0, "w": 1024, "h": 512, "cols": 4, "rows": -1 }""")]
    [InlineData("""{ "level": 0, "w": 1048576, "h": 1048576, "cols": 4, "rows": 2 }""")]
    [InlineData("""{ "level": -1, "w": 1024, "h": 512, "cols": 4, "rows": 2 }""")]
    [InlineData("null")]
    public void An_index_whose_numbers_describe_no_map_is_refused(string level)
    {
        Assert.Throws<InvalidDataException>(() => TileIndex.Load(Written(Index(level))));
    }

    [Theory]
    [InlineData(0, "webp")]
    [InlineData(1000000, "webp")]
    [InlineData(256, "")]
    [InlineData(256, "../x")]
    public void An_index_with_no_tile_size_or_an_odd_file_ending_is_refused(int tileSize, string ext)
    {
        Assert.Throws<InvalidDataException>(() => TileIndex.Load(Written(Index("""{ "level": 0, "w": 1024, "h": 512, "cols": 4, "rows": 2 }""", tileSize, ext))));
    }
}
