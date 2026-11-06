namespace KTable.Client.Tests;

public class ImportStagesTests
{
    [Fact]
    public void Stages_follow_the_converters_lines_and_each_ended_stage_is_kept_with_its_time()
    {
        var s = new ImportStages();
        Assert.True(s.Read("STAGE read 0", 0));
        Assert.Equal("read", s.Stage);
        Assert.Null(s.Share);
        Assert.True(s.Read("STAGE download 4", 2));
        Assert.True(s.Read("AT 1 4 1048576 0 0 Ala I Battalion", 3));
        Assert.Equal(("download", 1, 4, 1048576L, "Ala I Battalion"), (s.Stage, s.Done, s.Total, s.Bytes, s.Item));
        Assert.Equal(0.25, s.Share);
        Assert.True(s.Read("AT 4 4 4194304 0 1", 6));
        Assert.Equal(("", 1), (s.Item, s.Failed));
        s.End(7);
        Assert.Null(s.Stage);
        Assert.Equal(new[] { new ImportStages.Finished("read", 0, 0, 0, 2), new ImportStages.Finished("download", 4, 1, 4194304, 5) }, s.Past);
    }

    [Fact]
    public void Other_lines_are_left_for_the_log_and_damaged_progress_lines_change_nothing()
    {
        var s = new ImportStages();
        Assert.False(s.Read("Importing 'Feud' from C:\\feud.json", 0));
        Assert.False(s.Read("IMPORTED scenario=feud pack=tts.feud", 0));
        Assert.True(s.Read("AT 1 2 3 0 0", 0));
        Assert.Null(s.Stage);
        Assert.True(s.Read("STAGE", 0));
        Assert.Null(s.Stage);
        s.Read("STAGE pieces 10", 1);
        foreach (var bad in new[] { "AT", "AT x 10 0 0 0", "AT 1 10", "AT 1 10 0 0 0x" }) Assert.True(s.Read(bad, 2));
        Assert.Equal((0, 10), (s.Done, s.Total));
        s.Read("AT 12 10 0 0 0 Name with  two spaces", 3);
        Assert.Equal((1.0, "Name with  two spaces"), (s.Share, s.Item));
    }

    [Fact]
    public void Time_left_waits_for_a_few_seconds_and_steps_and_follows_bytes_when_their_total_is_known()
    {
        var s = new ImportStages();
        s.Read("STAGE pieces 100", 0);
        s.Read("AT 2 100 0 0 0", 1);
        Assert.Null(s.SecondsLeft(1));
        s.Read("AT 2 100 0 0 0", 4);
        Assert.Null(s.SecondsLeft(4));
        s.Read("AT 25 100 0 0 0", 10);
        Assert.Equal(30, s.SecondsLeft(10)!.Value, 6);
        s.Read("STAGE module 1", 20);
        s.Read("AT 0 1 1000 4000 0", 24);
        Assert.Equal(12, s.SecondsLeft(24)!.Value, 6);
        Assert.Equal(250, s.BytesPerSecond(24)!.Value, 6);
    }

    [Fact]
    public void The_rate_is_taken_over_the_last_seconds_only()
    {
        var s = new ImportStages();
        s.Read("STAGE download 10", 0);
        s.Read("AT 1 10 1000000 0 0", 1);
        for (int t = 2; t <= 20; t++) s.Read($"AT {Math.Min(10, t)} 10 {1000000 + (t - 1) * 100} 0 0", t);
        Assert.InRange(s.BytesPerSecond(20)!.Value, 99, 101);
        Assert.Null(new ImportStages().BytesPerSecond(0));
    }
}
