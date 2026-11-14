namespace KTable.Client.Tests;

static class OneCopy
{
    public static bool Holds(string a, string b)
    {
        int before = File.ReadAllBytes(b).Length;
        long own = new FileInfo(a).Length;
        using (var grown = new FileStream(a, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) grown.WriteByte(7);
        bool one = File.ReadAllBytes(b).Length == before + 1;
        using (var back = new FileStream(a, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) back.SetLength(own);
        return one;
    }
}

public class FileLinksTests
{
    readonly string _dir = TestFolders.New();

    string Put(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_linked_file_is_the_same_data_under_a_second_name_and_outlives_the_first()
    {
        string first = Put("library/0_0.webp", "tile"), second = Path.Combine(_dir, "scenario", "deep", "0_0.webp");

        Assert.True(FileLinks.Link(first, second));

        Assert.Equal("tile", File.ReadAllText(second));
        Assert.True(OneCopy.Holds(first, second));
        File.Delete(first);
        Assert.Equal("tile", File.ReadAllText(second));
    }

    [Fact]
    public void Nothing_is_linked_over_a_file_that_is_there_or_from_one_that_is_not()
    {
        string first = Put("a.webp", "tile"), taken = Put("b.webp", "mine");

        Assert.False(FileLinks.Link(first, taken));
        Assert.False(FileLinks.Link(Path.Combine(_dir, "gone.webp"), Path.Combine(_dir, "c.webp")));

        Assert.Equal("mine", File.ReadAllText(taken));
        Assert.False(File.Exists(Path.Combine(_dir, "c.webp")));
    }

    [Fact]
    public void A_doubled_file_replaced_by_a_link_reads_the_same_and_takes_no_room_of_its_own()
    {
        string kept = Put("library/0_0.webp", "tile"), doubled = Put("scenario/0_0.webp", "tile");

        Assert.True(FileLinks.Replace(kept, doubled));

        Assert.Equal("tile", File.ReadAllText(doubled));
        Assert.True(OneCopy.Holds(kept, doubled));
        Assert.Equal(["0_0.webp"], Directory.GetFiles(Path.Combine(_dir, "scenario")).Select(Path.GetFileName));
    }

    [Fact]
    public void Where_the_drive_cannot_link_the_doubled_file_stays_as_it_was()
    {
        string kept = Put("library/0_0.webp", "tile"), doubled = Put("scenario/0_0.webp", "tile");
        FileLinks.Maker.Value = (_, _) => false;

        Assert.False(FileLinks.Replace(kept, doubled));
        Assert.False(FileLinks.Link(kept, Path.Combine(_dir, "other.webp")));

        Assert.False(OneCopy.Holds(kept, doubled));
        Assert.Equal(["0_0.webp"], Directory.GetFiles(Path.Combine(_dir, "scenario")).Select(Path.GetFileName));
    }

    [Fact]
    public void A_doubled_file_something_is_reading_is_left_alone_with_nothing_left_beside_it()
    {
        if (!OperatingSystem.IsWindows()) return;
        string kept = Put("library/0_0.webp", "tile"), doubled = Put("scenario/0_0.webp", "tile");

        using (new FileStream(doubled, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(FileLinks.Replace(kept, doubled));

        Assert.False(OneCopy.Holds(kept, doubled));
        Assert.Equal(["0_0.webp"], Directory.GetFiles(Path.Combine(_dir, "scenario")).Select(Path.GetFileName));
    }
}
