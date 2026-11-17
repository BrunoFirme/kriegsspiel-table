namespace KTable.Client.Tests;

public sealed class StrangersTests
{
    const double Grace = 20;

    [Fact]
    public void Someone_connected_who_never_knocks_is_shown_out_once_the_grace_has_passed()
    {
        var strangers = new Strangers(Grace);
        Assert.Empty(strangers.Overstayed([5], _ => false, now: 100));
        Assert.Empty(strangers.Overstayed([5], _ => false, now: 100 + Grace - 1));
        Assert.Equal([5L], strangers.Overstayed([5], _ => false, now: 100 + Grace));
    }

    [Fact]
    public void Someone_seated_or_waiting_at_the_door_stays_however_long()
    {
        var strangers = new Strangers(Grace);
        Assert.Empty(strangers.Overstayed([5, 6], id => id == 5, now: 0));
        Assert.Equal([6L], strangers.Overstayed([5, 6], id => id == 5, now: 1000));
        Assert.Empty(strangers.Overstayed([5], _ => true, now: 5000));
    }

    [Fact]
    public void Someone_put_out_gets_the_same_grace_from_that_moment_and_knocking_again_stops_the_clock()
    {
        var strangers = new Strangers(Grace);
        Assert.Empty(strangers.Overstayed([5], _ => true, now: 0));
        Assert.Empty(strangers.Overstayed([5], _ => false, now: 500));
        Assert.Empty(strangers.Overstayed([5], _ => true, now: 510));
        Assert.Empty(strangers.Overstayed([5], _ => false, now: 600));
        Assert.Equal([5L], strangers.Overstayed([5], _ => false, now: 600 + Grace));
    }

    [Fact]
    public void A_connection_that_left_and_one_that_reuses_its_number_start_afresh()
    {
        var strangers = new Strangers(Grace);
        strangers.Overstayed([5], _ => false, now: 0);
        strangers.Overstayed([], _ => false, now: 10);
        Assert.Empty(strangers.Overstayed([5], _ => false, now: 25));
    }

    [Fact]
    public void One_sender_is_let_through_only_so_often_and_others_are_not_held_by_it()
    {
        var poses = new PerSender(everyMs: 15);
        Assert.True(poses.Lets(2, 1000));
        Assert.False(poses.Lets(2, 1010));
        Assert.True(poses.Lets(3, 1010));
        Assert.True(poses.Lets(2, 1015));
        poses.Forget(2);
        Assert.True(poses.Lets(2, 1016));
    }
}
