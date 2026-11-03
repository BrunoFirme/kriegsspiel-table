using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class FaceDocsTests
{
    [Theory]
    [InlineData(2.0, 1.0, 0, 0, 512, 256)]
    [InlineData(0.1, 10.0, 0, 0, 512, 2048)]
    [InlineData(10.0, 0.1, 0, 0, 512, 16)]
    [InlineData(2.0, 1.0, 300, 200, 300, 200)]
    [InlineData(2.0, 1.0, 4096, 1024, 2048, 512)]
    [InlineData(2.0, 1.0, 40000, 10, 2048, 8)]
    public void A_blank_face_takes_the_front_s_size_or_the_piece_s_proportions(double pw, double ph, int fw, int fh, int w, int h)
    {
        var doc = FaceDocs.Blank(pw, ph, fw, fh);
        Assert.Equal((w, h), (doc.W, doc.H));
        Assert.Equal("paint", Assert.Single(doc.Layers).Kind);
    }

    [Fact]
    public void An_unreadable_picture_is_kept_locked_and_stretched_over_the_face()
    {
        var doc = new ArtDoc { W = 400, H = 200 };
        var l = FaceDocs.Unreadable(doc, "src-1", 800, 100);
        Assert.Equal(("image", "src-1", true), (l.Kind, l.Src, l.Locked));
        Assert.Equal((800, 100, 200f, 100f, 0.5f, 2f), (l.SrcW, l.SrcH, l.X, l.Y, l.Sx, l.Sy));
        var unknown = FaceDocs.Unreadable(doc, "src-2", 0, 0);
        Assert.Equal((400, 200, 1f, 1f), (unknown.SrcW, unknown.SrcH, unknown.Sx, unknown.Sy));
    }

    [Fact]
    public void Each_tool_has_its_letter()
    {
        Assert.Equal("brush", FaceDocs.ToolFor('B'));
        Assert.Equal("place", FaceDocs.ToolFor('n'));
        Assert.Null(FaceDocs.ToolFor('Q'));
        Assert.Equal(12, "VBEUTGICOMNH".Select(FaceDocs.ToolFor).Distinct().Count(t => t != null));
    }
}
