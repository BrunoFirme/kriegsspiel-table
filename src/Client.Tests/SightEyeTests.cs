using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SightEyeTests
{
    const double Reach = 10;

    static SightingDraft Watching(bool square, params (string Id, Vec2 At)[] pieces)
    {
        var d = new SightingDraft { Radius = Reach, Format = square ? SightingFormat.Square : SightingFormat.Round, Watchers = pieces.Select(p => p.Id).ToList() };
        d.Place(pieces);
        foreach (var eye in d.Eyes) eye.Lay(d.SeenReach, ang => d.Farthest(eye.Center, ang));
        return d;
    }

    [Fact]
    public void Pieces_standing_close_look_out_as_one_eye_from_between_them()
    {
        var eyes = SightEye.Gather(new[] { ("a", new Vec2(0, 0)), ("b", new Vec2(0.1, 0)), ("c", new Vec2(30, 0)) }, Reach);
        Assert.Equal(2, eyes.Count);
        Assert.Equal(new[] { "a", "b" }, eyes[0].Ids);
        Assert.Equal(0.05, eyes[0].Center.X, 9);
        Assert.Equal(new[] { "c" }, eyes[1].Ids);
    }

    [Fact]
    public void However_many_pieces_look_out_the_eyes_stay_within_what_one_picture_holds()
    {
        var crowd = Enumerable.Range(0, 500).Select(i => ($"p{i}", new Vec2(i % 25 * 3, i / 25 * 3))).ToArray();
        var eyes = SightEye.Gather(crowd, Reach);
        Assert.InRange(eyes.Count, 1, SightEye.MostEyes);
        Assert.Equal(500, eyes.Sum(e => e.Ids.Count));
    }

    [Fact]
    public void Several_pieces_see_what_any_of_them_sees_and_the_picture_spans_them_all()
    {
        var d = Watching(false, ("a", new Vec2(0, 0)), ("b", new Vec2(30, 0)));
        Assert.True(d.Many);
        Assert.Equal(new Vec2(15, 0), d.Center);
        Assert.Equal(15 + Reach, d.Frame, 9);
        Assert.True(d.EyesSee(new Vec2(4, 0)) && d.EyesSee(new Vec2(33, 0)));
        Assert.False(d.EyesSee(new Vec2(15, 0)));
        Assert.Equal(new Vec2(40, 0), d.Clip(new Vec2(60, 0)));
    }

    [Fact]
    public void A_traced_wall_hides_what_is_behind_it_from_each_eye_on_its_own()
    {
        var d = new SightingDraft { Radius = Reach, Format = SightingFormat.Round, Watchers = new() { "a", "b" } };
        d.Walls.Add(new List<Vec2> { new(2, -3), new(2, 3) });
        d.Place(new[] { ("a", new Vec2(0, 0)), ("b", new Vec2(8, 0)) });
        foreach (var eye in d.Eyes) eye.Lay(d.SeenReach, ang => d.Farthest(eye.Center, ang));
        Assert.False(d.Eyes[0].Sees(new Vec2(4, 0)));
        Assert.True(d.Eyes[1].Sees(new Vec2(4, 0)));
        Assert.True(d.EyesSee(new Vec2(4, 0)));
        Assert.True(d.EyesSee(new Vec2(-0.5, 0)));
        Assert.Equal(2, d.Farthest(new Vec2(0, 0), 0), 9);
        Assert.Equal(Reach, d.Farthest(new Vec2(0, 0), Math.PI), 9);
    }

    [Fact]
    public void A_square_eye_sees_into_the_corners_of_its_own_square()
    {
        var d = Watching(true, ("a", new Vec2(0, 0)), ("b", new Vec2(40, 0)));
        Assert.True(d.Eyes[0].Sees(new Vec2(9, 9)));
        Assert.False(d.Eyes[0].Sees(new Vec2(11, 0)));
    }
}
