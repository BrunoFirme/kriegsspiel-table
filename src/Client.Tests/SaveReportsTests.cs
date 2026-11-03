using KTable.Core;
using KTable.Server;

namespace KTable.Client.Tests;

public class SaveReportsTests
{
    [Fact]
    public void Play_kept_aside_is_told_with_what_was_kept()
    {
        Assert.Equal("The log of the play after the last whole save could not be read (game.kstable.log.old: locked), so the game opened as it was last"
            + " written whole. What it does not hold is kept: game.kstable.log.unreadable-1.",
            SaveReports.Describe(new KeptAside(KeptAsideWhy.Unreadable, 0, "game.kstable.log.old: locked", new[] { Path.Combine("x", "game.kstable.log.unreadable-1") })));
        Assert.Equal("2 change(s) after the last whole save could not be replayed (AdvanceClock, change 7: no), so the game opened at the last change that could be.",
            SaveReports.Describe(new KeptAside(KeptAsideWhy.Unreplayed, 2, "AdvanceClock, change 7: no", Array.Empty<string>())));
        Assert.StartsWith("1 change(s) in the log after the last whole save do not carry on from it (a change before them is missing",
            SaveReports.Describe(new KeptAside(KeptAsideWhy.Gap, 1, null, Array.Empty<string>())));
    }

    [Fact]
    public void A_game_taken_from_a_checkpoint_says_from_which_and_what_was_kept()
    {
        var cp = new Checkpoint(Path.Combine("h", "turn-0003_20260101-000000.kstable"), CheckpointKind.Turn, 3, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var said = SaveReports.Describe(new Recovery("bad JSON", Path.Combine("l", "scn.unreadable-1.kstable"), cp, new[] { "turn-0004: torn" }, new[] { Path.Combine("l", "scn.log.unreadable-1") }, null));
        Assert.Equal($"The save could not be read (bad JSON); it was kept as scn.unreadable-1.kstable. Continuing from the checkpoint taken at the {cp.Describe()}:"
            + " turn-0003_20260101-000000.kstable. Newer checkpoints that could not be read: turn-0004: torn. The log of the play after the unreadable save"
            + " is kept with it: scn.log.unreadable-1.", said);
        Assert.StartsWith("The save file was missing. Continuing from", SaveReports.Describe(new Recovery(null, null, cp, Array.Empty<string>(), Array.Empty<string>(), null)));
    }
}
