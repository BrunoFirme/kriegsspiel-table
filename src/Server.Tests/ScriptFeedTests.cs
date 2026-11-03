using KTable.Server.Scripting;

namespace KTable.Server.Tests;

public class ScriptFeedTests
{
    [Fact]
    public void Lines_are_kept_up_to_their_bounds_newest_last_and_numbered_in_order()
    {
        var f = new ScriptFeed();
        for (int i = 0; i < ScriptFeed.Keep + 10; i++) f.Add("line " + i, "print", "p-1", "Hussars");
        Assert.Equal(ScriptFeed.TextKeep, f.Text.Count);
        Assert.Equal("line " + (ScriptFeed.Keep + 9), f.Text[^1]);
        Assert.Equal(ScriptFeed.Keep, f.Lines.Count);
        Assert.Equal("line 10", f.Lines[0].Text);
        Assert.True(f.Lines.Zip(f.Lines.Skip(1)).All(p => p.Second.Seq == p.First.Seq + 1));
        Assert.Equal(("p-1", "Hussars"), (f.Lines[0].Source, f.Lines[0].Name));
    }

    [Fact]
    public void A_line_is_sent_once_and_unsent_ones_nobody_takes_keep_only_the_newest()
    {
        var f = new ScriptFeed();
        f.Add("a", "print", "", null);
        Assert.Equal("a", Assert.Single(f.TakeUnsent()).Text);
        Assert.Empty(f.TakeUnsent());
        for (int i = 0; i < ScriptFeed.Keep + 1; i++) f.Add("b" + i, "print", "", null);
        var sent = f.TakeUnsent();
        Assert.Equal(ScriptFeed.Keep, sent.Count);
        Assert.Equal("b1", sent[0].Text);
    }

    [Theory]
    [InlineData("chunk_1:(12,4-20): attempt to index a nil value", 12)]
    [InlineData("chunk_1:(3,0): unexpected symbol", 3)]
    [InlineData("out of time", 0)]
    public void An_error_points_at_its_script_line_when_the_message_says_one(string message, int line) =>
        Assert.Equal(line, ScriptFeed.LineOf(message));

    [Fact]
    public void Each_script_keeps_its_last_error_until_it_is_loaded_afresh()
    {
        var f = new ScriptFeed();
        Assert.Equal(7, f.ErrorRaised("p-1", "x:(7,1): first"));
        f.ErrorRaised("p-1", "x:(9,1): second");
        f.ErrorRaised("", "global trouble");
        Assert.True(f.LastError("p-1", out var m, out var l));
        Assert.Equal(("x:(9,1): second", 9), (m, l));
        f.Forget("p-1");
        Assert.False(f.LastError("p-1", out _, out _));
        Assert.True(f.LastError("", out _, out _));
        f.ForgetAll();
        Assert.False(f.LastError("", out _, out _));
    }
}
