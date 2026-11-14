using KTable.Client;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class TileWriteTests : ConverterHarness
{
    [Fact]
    public void Slicing_a_map_where_a_tile_shared_with_another_folder_lies_leaves_the_other_folder_s_tile_as_it_was()
    {
        Directory.CreateDirectory(_root);
        var picture = Path.Combine(_root, "map.png");
        using (var bitmap = new SKBitmap(8, 8))
        {
            bitmap.Erase(new SKColor(60, 120, 40));
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(picture, png.ToArray());
        }
        string kept = Path.Combine(_root, "library", "0_0.png"), sliced = Path.Combine(_root, "tiles", "0", "0_0.png");
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.WriteAllText(kept, "another map's tile");
        Assert.True(FileLinks.Link(kept, sliced));

        var index = Tiles.Build(picture, Path.Combine(_root, "tiles"), new TilesCfg());

        Assert.Equal("png", index.Ext);
        Assert.Equal("another map's tile", File.ReadAllText(kept));
        Assert.NotEqual("another map's tile", File.ReadAllText(sliced));
    }
}
