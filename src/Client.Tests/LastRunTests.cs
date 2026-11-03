namespace KTable.Client.Tests;

public class LastRunTests
{
    static readonly DateTime Started = new(2026, 11, 3, 5, 20, 1, 400);

    [Fact]
    public void A_run_that_never_ended_is_found_once_and_a_running_or_finished_one_is_not()
    {
        var runs = new LastRun(TestFolders.New());
        runs.Begin(11, Started);
        runs.Begin(22, Started.AddMinutes(5));
        runs.Begin(33, Started.AddMinutes(9));
        runs.End(33);

        var ended = runs.EndedUnexpectedly((pid, _) => pid == 22);

        Assert.Equal(new[] { new EndedRun(11, Started) }, ended);
        Assert.Empty(runs.EndedUnexpectedly((pid, _) => pid == 22));
        Assert.Equal(22, Assert.Single(runs.EndedUnexpectedly((_, _) => false)).Pid);
    }

    [Fact]
    public void A_damaged_mark_is_dropped_without_counting_as_a_run()
    {
        var dir = TestFolders.New();
        File.WriteAllText(Path.Combine(dir, "running-5.txt"), "not a time");
        File.WriteAllText(Path.Combine(dir, "running-x.txt"), Started.ToString("O"));

        Assert.Empty(new LastRun(dir).EndedUnexpectedly((_, _) => false));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public void The_log_of_a_run_starts_at_its_second_and_keeps_stack_lines_and_the_last_ones()
    {
        var lines = new[]
        {
            "2026-11-03 05:19:59 INFO  earlier run",
            "   at Earlier.Frame()",
            "2026-11-03 05:20:01 INFO  started",
            "2026-11-03 05:20:02 ERROR System.ArgumentException: bad",
            "   at Some.Frame()",
            "2026-11-03 05:20:03 INFO  last",
        };

        Assert.Equal(lines[2..], LastRun.LogSince(lines, Started, keep: 10));
        Assert.Equal(lines[4..], LastRun.LogSince(lines, Started, keep: 2));
        Assert.Empty(LastRun.LogSince(lines, Started.AddMinutes(1), keep: 10));
    }
}
