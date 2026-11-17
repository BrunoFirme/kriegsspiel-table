namespace KTable.Client.Tests;

public class OpeningNotesTests
{
    static string Game(string name) => Path.Combine(Path.GetTempPath(), "ktable-client-tests", "games", name + ".kstable");

    [Fact]
    public void An_opening_that_began_is_left_over_until_it_is_ended()
    {
        var dir = TestFolders.New();
        var notes = new OpeningNotes(dir);
        Assert.False(notes.LeftOver(Game("feud")));

        notes.Begin(Game("feud"));

        Assert.True(notes.LeftOver(Game("feud")));
        Assert.True(new OpeningNotes(dir).LeftOver(Game("feud")));
        notes.End(Game("feud"));
        Assert.False(notes.LeftOver(Game("feud")));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public void A_note_belongs_to_one_game_however_its_path_is_spelled()
    {
        var notes = new OpeningNotes(TestFolders.New());
        notes.Begin(Game("feud"));

        Assert.False(notes.LeftOver(Game("siege")));
        Assert.True(notes.LeftOver(Game("FEUD")));
        Assert.True(notes.LeftOver(Path.Combine(Path.GetDirectoryName(Game("feud"))!, "..", "games", "feud.kstable")));
        notes.End(Game("siege"));
        Assert.True(notes.LeftOver(Game("feud")));
    }

    [Fact]
    public void A_game_that_ran_a_while_is_no_longer_left_over()
    {
        var notes = new OpeningNotes(TestFolders.New());
        notes.Begin(Game("feud"));

        notes.Ran(OpeningNotes.SettledAfterSeconds / 2);
        Assert.True(notes.LeftOver(Game("feud")));
        notes.Ran(OpeningNotes.SettledAfterSeconds / 2);
        Assert.False(notes.LeftOver(Game("feud")));
    }

    [Fact]
    public void Running_counts_from_the_newest_opening_and_touches_no_other_note()
    {
        var notes = new OpeningNotes(TestFolders.New());
        notes.Begin(Game("feud"));
        notes.Ran(OpeningNotes.SettledAfterSeconds - 1);
        notes.Begin(Game("siege"));
        notes.Ran(1);

        Assert.True(notes.LeftOver(Game("feud")));
        Assert.True(notes.LeftOver(Game("siege")));
        notes.Ran(OpeningNotes.SettledAfterSeconds);
        Assert.True(notes.LeftOver(Game("feud")));
        Assert.False(notes.LeftOver(Game("siege")));
    }

    [Fact]
    public void Leaving_the_game_clears_its_note_and_leaving_again_does_nothing()
    {
        var notes = new OpeningNotes(TestFolders.New());
        notes.Begin(Game("feud"));

        notes.Left();
        notes.Left();

        Assert.False(notes.LeftOver(Game("feud")));
    }

    [Fact]
    public void A_missing_folder_is_made_when_an_opening_begins_and_is_no_trouble_before()
    {
        var dir = Path.Combine(TestFolders.New(), "not", "there");
        var notes = new OpeningNotes(dir);

        Assert.False(notes.LeftOver(Game("feud")));
        notes.End(Game("feud"));
        notes.Ran(OpeningNotes.SettledAfterSeconds);
        notes.Left();
        Assert.False(Directory.Exists(dir));

        notes.Begin(Game("feud"));
        Assert.True(notes.LeftOver(Game("feud")));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_does_not_stop_the_opening()
    {
        var blocked = Path.Combine(TestFolders.New(), "a-file");
        File.WriteAllText(blocked, "");
        var notes = new OpeningNotes(blocked);

        notes.Begin(Game("feud"));

        Assert.False(notes.LeftOver(Game("feud")));
    }
}
