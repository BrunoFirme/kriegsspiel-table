using KTable.Core.Model;

namespace KTable.Core.Tests;

public class ReliefContoursTests
{
    const int Side = 129;

    static double Uv(double cell) => cell / (Side - 1);

    static ContourLine Ring(double radiusCells, double height)
    {
        var points = Enumerable.Range(0, 49).Select(i => (Uv(64 + Math.Cos(i * Math.Tau / 48) * radiusCells), Uv(64 + Math.Sin(i * Math.Tau / 48) * radiusCells))).ToList();
        return new ContourLine(points, height);
    }

    static float At(ReliefGrid g, int x, int y) => g.H[y * Side + x];

    [Fact]
    public void The_ground_passes_through_each_contour_and_grades_between_them()
    {
        var g = new ReliefGrid(Side, Side);
        var box = ReliefContours.Raise(g, [Ring(36, 0.01), Ring(24, 0.02), Ring(12, 0.03)]);
        Assert.False(box.Empty);
        Assert.Equal(0.01f, At(g, 64 + 36, 64), 5);
        Assert.Equal(0.02f, At(g, 64, 64 - 24), 5);
        Assert.Equal(0.03f, At(g, 64 - 12, 64), 5);
        Assert.InRange(At(g, 64 + 30, 64), 0.012f, 0.018f);
        Assert.InRange(At(g, 64 + 18, 64), 0.022f, 0.028f);
        Assert.InRange(At(g, 64, 64), 0.0295f, 0.0305f);
    }

    [Fact]
    public void Outside_the_lines_the_ground_runs_out_to_what_was_there_and_far_ground_is_left_alone()
    {
        var g = new ReliefGrid(Side, Side);
        Array.Fill(g.H, 0.004f);
        var box = ReliefContours.Raise(g, [Ring(20, 0.02), Ring(10, 0.03)]);
        Assert.Equal(0.004f, At(g, box.X0, 64), 6);
        Assert.InRange(At(g, 64 + 25, 64), 0.005f, 0.02f);
        Assert.Equal(0.004f, At(g, 2, 2), 6);
        Assert.True(box.X0 > 2 && box.X1 < Side - 3, "the change keeps to the lines and a margin round them");
    }

    [Fact]
    public void An_open_line_holds_its_height_along_itself()
    {
        var g = new ReliefGrid(Side, Side);
        ReliefContours.Raise(g, [new ContourLine([(Uv(30), Uv(60)), (Uv(90), Uv(60))], 0.02), new ContourLine([(Uv(30), Uv(80)), (Uv(90), Uv(80))], 0.01)]);
        Assert.Equal(0.02f, At(g, 60, 60), 5);
        Assert.Equal(0.01f, At(g, 60, 80), 5);
        Assert.InRange(At(g, 60, 70), 0.012f, 0.018f);
    }

    [Fact]
    public void No_lines_change_nothing() => Assert.True(ReliefContours.Raise(new ReliefGrid(Side, Side), []).Empty);
}
