using KTable.Core.Model;

namespace KTable.Core.Tests;

public class SheetLeashTests
{
    static SheetDef Sheet(double x, double z, double w, double h, double rot = 0) => new() { Pos = new Vec2(x, z), Size = new Size2(w, h), RotDeg = rot };

    [Fact]
    public void No_sheets_give_no_leash()
    {
        Assert.Null(SheetLeash.Of(new List<SheetDef>()));
    }

    [Fact]
    public void The_centre_is_weighted_by_sheet_area_and_the_reach_runs_to_the_farthest_corner()
    {
        var leash = SheetLeash.Of(new[] { Sheet(0, 0, 300, 100), Sheet(1000, 0, 100, 100) })!.Value;
        Assert.Equal(250, leash.Centre.X, 6);
        Assert.Equal(0, leash.Centre.Z, 6);
        Assert.Equal(System.Math.Sqrt(800 * 800 + 50 * 50), leash.Reach, 6);
    }

    [Fact]
    public void A_turned_sheet_reaches_to_its_turned_corners()
    {
        var leash = SheetLeash.Of(new[] { Sheet(0, 0, 200, 100, 45) })!.Value;
        Assert.Equal(System.Math.Sqrt(100 * 100 + 50 * 50), leash.Reach, 6);
    }

    [Fact]
    public void Moves_inside_the_leash_are_kept_whole()
    {
        var leash = new SheetLeash(new Vec2(0, 0), 100);
        var to = new Vec2(350, 0);
        Assert.Equal(to, leash.Hold(new Vec2(0, 0), to, 0));
    }

    [Fact]
    public void A_move_past_the_leash_stops_on_it_and_a_wider_view_lets_the_camera_go_farther()
    {
        var leash = new SheetLeash(new Vec2(0, 0), 100);
        var held = leash.Hold(new Vec2(0, 0), new Vec2(0, 10000), 0);
        Assert.Equal(leash.Allowed(0), held.Length, 6);
        Assert.Equal(0, held.X, 6);
        Assert.True(leash.Hold(new Vec2(0, 0), new Vec2(0, 10000), 1000).Length > held.Length);
    }

    [Fact]
    public void Beyond_the_leash_the_camera_can_come_back_or_slide_but_never_go_farther()
    {
        var leash = new SheetLeash(new Vec2(0, 0), 100);
        var far = new Vec2(5000, 0);
        Assert.Equal(new Vec2(4000, 0), leash.Hold(far, new Vec2(4000, 0), 0));
        Assert.Equal(5000, leash.Hold(far, new Vec2(6000, 0), 0).Length, 6);
        Assert.Equal(5000, leash.Hold(far, new Vec2(5000, 500), 0).Length, 6);
    }
}
