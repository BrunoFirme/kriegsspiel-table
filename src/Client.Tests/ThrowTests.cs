using System.Numerics;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class ThrowTests
{
    const double Last = 0.2;

    static ThrowTrail Trail(Func<double, Vec2> at, double fps = 60)
    {
        var trail = new ThrowTrail();
        int frames = (int)Math.Round(Last * fps);
        for (int i = 0; i <= frames; i++) trail.Add(i / fps, at(i / fps));
        return trail;
    }

    static ThrowTrail Steady(double speed, double fps = 60) => Trail(t => new Vec2(speed * t, 0), fps);

    static ThrowTrail Curving(double speed, double turnPerSecond) =>
        Trail(t => turnPerSecond == 0 ? new Vec2(speed * t, 0) : new Vec2(speed / turnPerSecond * Math.Sin(turnPerSecond * t), speed / turnPerSecond * (1 - Math.Cos(turnPerSecond * t))));

    static Fling Hand(double speedAcross) => new(new Vector3((float)speedAcross, 0, 0), 0);

    static float Flat(Fling f) => new Vector2(f.Velocity.X, f.Velocity.Z).Length();

    [Fact]
    public void A_flick_is_read_at_the_speed_the_hand_had_as_it_let_go()
    {
        const double Pull = 150, Final = Pull * Last;
        var speed = Trail(t => new Vec2(Pull * t * t / 2, 0)).Hand(Last + 0.005).Velocity.Length();
        Assert.InRange(speed, 0.85 * Final, Final);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(144)]
    public void A_moving_hand_throws_at_any_frame_rate_and_a_stopped_one_sets_down(double fps)
    {
        var trail = Steady(20, fps);
        var moving = trail.Hand(Last + 0.02);
        Assert.True(moving.Thrown);
        Assert.InRange(moving.Velocity.Length(), 19.4f, 20.6f);
        Assert.False(trail.Hand(Last + 0.12).Thrown);
    }

    [Fact]
    public void A_slow_frame_just_before_letting_go_still_throws()
    {
        var trail = new ThrowTrail();
        for (int i = 0; i <= 6; i++) trail.Add(i / 60.0, new Vec2(20 * i / 60.0, 0));
        const double Hitch = 0.25;
        trail.Add(Hitch, new Vec2(20 * Hitch, 0));
        var flung = trail.Hand(Hitch + 0.005);
        Assert.True(flung.Thrown);
        Assert.InRange(flung.Velocity.Length(), 19.4f, 20.6f);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(400)]
    public void A_harder_flick_throws_harder_with_no_plateau(float viewSpan)
    {
        foreach (var feel in new[] { ThrowFeel.Piece, ThrowFeel.Die })
        {
            Assert.False(feel.Release(Hand(feel.From * 0.9 * viewSpan), viewSpan).Thrown);
            var speeds = new[] { 1.2, 2, 3, 4.5, 6 }.Select(k => Flat(feel.Release(Hand(feel.From * k * viewSpan), viewSpan))).ToList();
            for (int i = 1; i < speeds.Count; i++) Assert.True(speeds[i] > speeds[i - 1], $"{string.Join(" < ", speeds)}");
        }
    }

    [Fact]
    public void Zoomed_in_a_flick_leaves_as_fast_as_it_was_seen_and_zoomed_out_as_hard_as_the_mouse_went()
    {
        const float Effort = 3;
        var feel = ThrowFeel.Piece;
        Assert.Equal(Effort * 6, Flat(feel.Release(Hand(Effort * 6), 6)), 3);
        float wide = Flat(feel.Release(Hand(Effort * 50), 50)), wider = Flat(feel.Release(Hand(Effort * 400), 400));
        Assert.Equal(wide, wider, 3);
        Assert.True(wide < Effort * 50);
    }

    [Fact]
    public void A_flick_just_past_letting_go_starts_slow()
    {
        const float Span = 8;
        var feel = ThrowFeel.Die;
        float seen = feel.From * 1.1f * Span, thrown = Flat(feel.Release(Hand(seen), Span));
        Assert.InRange(thrown, 0.01f, 0.15f * seen);
    }

    [Fact]
    public void A_curving_flick_turns_the_way_it_curves()
    {
        float left = Curving(20, 4).Hand(Last + 0.005).Turn, right = Curving(20, -4).Hand(Last + 0.005).Turn, straight = Curving(20, 0).Hand(Last + 0.005).Turn;
        Assert.InRange(left, 3f, 5f);
        Assert.Equal(-left, right, 3);
        Assert.Equal(0, straight, 4);
        var thrown = ThrowFeel.Die.Release(Curving(20, 4).Hand(Last + 0.005), 8);
        Assert.True(thrown.Turn > 0 && thrown.Turn < left);
    }

    [Fact]
    public void A_bigger_piece_is_heavier_within_one_to_eight()
    {
        float marker = Heft.Of(0.5f, 0.5f, 0.1f), block = Heft.Of(1.6f, 0.8f, 0.25f), model = Heft.Of(2, 2, 1), fort = Heft.Of(6, 6, 2);
        Assert.True(marker < block && block < model);
        Assert.Equal(model, fort);
        Assert.Equal(8, fort / marker, 3);
    }
}
