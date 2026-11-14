using KTable.Core.Model;

namespace KTable.Client.Tests;

public class ViewshedTests
{
    const double Reach = 20, RidgeFrom = 4, RidgeTo = 5, RidgeTop = 2, HillFrom = 12, HillRise = 1.2;

    static double RidgeThenHill(Vec2 p) => p.X >= RidgeFrom && p.X <= RidgeTo ? RidgeTop : p.X > HillFrom ? (p.X - HillFrom) * HillRise : 0;

    static Viewshed From(double eye, Func<Vec2, double>? ground = null)
    {
        var seen = new Viewshed(new Vec2(0, 0), Reach, ground ?? RidgeThenHill);
        seen.LookFrom(eye);
        return seen;
    }

    [Fact]
    public void On_level_ground_the_eye_sees_all_of_its_reach_and_nothing_past_it()
    {
        var seen = From(1, _ => 0);
        Assert.All(seen.Seen, v => Assert.Equal(Viewshed.SeenValue, v));
        Assert.True(seen.Sees(new Vec2(-19.5, 0)));
        Assert.True(seen.Sees(new Vec2(0, 0)));
        Assert.False(seen.Sees(new Vec2(0, 20.5)));
    }

    [Fact]
    public void A_ridge_hides_the_ground_behind_it_and_the_hill_beyond_shows_again()
    {
        var seen = From(1);
        Assert.True(seen.Sees(new Vec2(2, 0)), "the near slope is in view");
        Assert.True(seen.Sees(new Vec2(4.03, 0)), "so is the face of the ridge");
        Assert.False(seen.Sees(new Vec2(4.8, 0)), "its flat top stands over the eye and is not");
        Assert.False(seen.Sees(new Vec2(7, 0)), "the ground behind the ridge is dead ground");
        Assert.False(seen.Sees(new Vec2(14, 0)), "and so is the foot of the far hill");
        Assert.True(seen.Sees(new Vec2(18.5, 0)), "the far hill rises back into view");
        Assert.True(seen.Sees(new Vec2(-7, 0)), "nothing stands in the way to the west");
        Assert.True(seen.Sees(new Vec2(0, 7)), "nor to the north");
    }

    [Fact]
    public void A_higher_eye_sees_over_the_ridge()
    {
        var seen = From(1);
        Assert.False(seen.Sees(new Vec2(7, 0)));
        seen.LookFrom(10);
        Assert.True(seen.Sees(new Vec2(7, 0)));
    }

    [Fact]
    public void Looking_again_from_the_same_height_changes_nothing()
    {
        var seen = From(1);
        int looks = seen.Looks;
        seen.LookFrom(1);
        Assert.Equal(looks, seen.Looks);
        seen.LookFrom(1.5);
        Assert.Equal(looks + 1, seen.Looks);
    }

    [Fact]
    public void A_survey_fits_only_the_place_and_reach_it_was_made_for()
    {
        var seen = From(1);
        Assert.True(seen.Fits(new Vec2(0, 0), Reach));
        Assert.False(seen.Fits(new Vec2(0.5, 0), Reach));
        Assert.False(seen.Fits(new Vec2(0, 0), Reach * 1.1));
    }

    [Fact]
    public void A_viewshed_goes_with_every_frame_and_reaches_its_corners()
    {
        var d = new SightingDraft { Surveyed = true, Radius = 10 };
        Assert.Equal(("circle", 10.0), (d.Shape().Kind, d.SeenReach));
        d.Format = SightingFormat.Square;
        Assert.True(d is { Surveyed: true, Square: true });
        Assert.Equal(10 * Math.Sqrt(2), d.SeenReach, 9);
        d.Format = SightingFormat.Drawn;
        Assert.Equal(10 * Math.Sqrt(2), d.SeenReach, 9);
        Assert.Equal(SightingFormat.Round, SightingDraft.FormatNamed("viewshed"));
        Assert.DoesNotContain("viewshed", KTable.Client.Prefs.Settings.SightingFormats);
        Assert.Equal(SightingDraft.ManHigh, d.WatchMetres);
        Assert.Equal((SightingDraft.LowestWatch, SightingDraft.HighestWatch, SightingDraft.ManHigh), (SightingDraft.FitWatch(0), SightingDraft.FitWatch(5000), SightingDraft.FitWatch(double.NaN)));
    }
}
