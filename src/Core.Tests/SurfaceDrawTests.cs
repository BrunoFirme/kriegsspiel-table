using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class SurfaceDrawTests
{
    static SurfaceDraw Read(string statements)
    {
        var draw = SurfaceDraw.Plain;
        foreach (var line in statements.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (SurfaceDraw.Says(p)) draw = draw.After(p);
        }
        return draw;
    }

    [Theory]
    [InlineData(false, SurfaceAlpha.Cut, 0.5, false)]
    [InlineData(true, SurfaceAlpha.Opaque, 0.5, true)]
    [InlineData(false, SurfaceAlpha.Blend, 0.5, true)]
    [InlineData(true, SurfaceAlpha.Cut, 0.25, true)]
    public void A_surface_reads_back_the_same_from_its_statements_and_from_its_name(bool oneSided, SurfaceAlpha alpha, double cutoff, bool metal)
    {
        var draw = new SurfaceDraw(oneSided, alpha, cutoff, metal);
        Assert.Equal(draw, Read(draw.Statements()));
        Assert.Equal(draw, SurfaceDraw.Named(draw.Name));
    }

    [Fact]
    public void A_plain_surface_writes_no_finish_so_files_made_before_stay_the_same()
    {
        Assert.DoesNotContain("finish", SurfaceDraw.Plain.Statements());
        Assert.DoesNotContain(Finishes.Metal, SurfaceDraw.Plain.Name);
    }

    [Fact]
    public void Metal_faces_after_the_finish_line_shine_and_the_faces_before_it_do_not()
    {
        Assert.False(Read("cull none\nalpha cut 0.5\n").Metal);
        Assert.True(Read("cull none\nalpha cut 0.5\n" + SurfaceDraw.MetalStatement).Metal);
    }
}
