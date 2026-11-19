using System.Security.AccessControl;
using System.Security.Principal;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class HostileSeatTests
{
    [Fact]
    public void A_seat_asking_for_the_whole_table_again_at_once_is_not_answered_until_two_seconds_pass()
    {
        using var t = new Table();
        Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(Table.Obs)).Message);
        Assert.Empty(t.Server.Resync(Table.Obs));
        Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(Table.Ump3)).Message);
        t.Pass(GameServer.ResyncEveryMs);
        Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(Table.Obs)).Message);
    }

    [Fact]
    public void Someone_already_seated_who_knocks_again_is_not_sent_the_table_again()
    {
        using var t = new Table();
        Assert.Empty(t.Knock(Table.Obs, "again"));
        Assert.Empty(t.Knock(Table.Ump3, "again", token: t.Tokens.GetValueOrDefault(Table.Ump3)));
    }

    [Fact]
    public void A_knock_with_a_name_pages_long_is_shown_to_the_umpires_cut_short()
    {
        using var t = new Table();
        var outs = t.Knock(77, new string('x', 5000));
        var knock = Assert.IsType<Knock>(outs.First(o => o.Message is Knock).Message);
        Assert.Equal(GameServer.MostNameChars, knock.Name.Length);
    }

    [Theory]
    [InlineData(@"\\elsewhere\share", "map")]
    [InlineData(@"C:\Windows", "map")]
    [InlineData("..", "map")]
    [InlineData("sheets", @"\\elsewhere\share\map")]
    [InlineData("sheets", @"C:\Windows\map")]
    [InlineData("sheets", "../../map")]
    public void A_sheet_naming_a_place_outside_the_game_s_files_is_refused(string package, string image)
    {
        using var t = new Table();
        int before = t.Server.State.Sheets.Count;
        t.Server.Handle(Table.Host, new AddSheet { Sheet = new SheetDef { Name = "x", Package = package, Image = image, Size = new Size2(10, 10) } });
        Assert.Equal(before, t.Server.State.Sheets.Count);
        t.Server.Handle(Table.Host, new AddSheet { Sheet = new SheetDef { Id = "s-here", Name = "x", Package = "sheets", Image = "map", Size = new Size2(10, 10) } });
        t.Server.Handle(Table.Host, new SetSheetPicture { Id = "s-here", Package = package, Image = image });
        var kept = t.Server.State.Sheets.Single(s => s.Id == "s-here");
        Assert.Equal(("sheets", "map"), (kept.Package, kept.Image));
    }

    [Theory]
    [InlineData(@"\\elsewhere\share\sky.png")]
    [InlineData(@"C:\Windows\sky.png")]
    [InlineData("../../sky.png")]
    public void A_backdrop_naming_a_place_outside_the_game_s_files_is_refused(string backdrop)
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetLighting { Lighting = new Lighting { Backdrop = backdrop } });
        Assert.NotEqual(backdrop, t.Server.State.Lighting?.Backdrop);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"\\elsewhere\share")]
    [InlineData("a/b")]
    [InlineData("")]
    public void A_pack_whose_id_is_not_a_plain_name_is_not_added(string id)
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new AddPack { Id = id });
        Assert.DoesNotContain(id, t.Server.State.Packs);
        Assert.Null(t.Store.Pack(id));
    }

    [Fact]
    public void A_save_from_an_older_format_keeps_a_copy_as_it_was_when_it_is_opened()
    {
        using var t = new Table();
        var saves = SaveStore.ForRoot(t.Root);
        var path = Path.Combine(t.Root, "saves", "older", "Game 1" + SaveStore.Ext);
        t.Server.SaveTo(path);
        var older = Json.Load<SaveFile>(path);
        older.Format = SaveFile.CurrentFormat - 1;
        Json.Save(path, older);
        var asWritten = File.ReadAllBytes(path);

        SaveLoader.Open(saves, path, t.Store, out _);

        var copy = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, $"Game 1.format-{SaveFile.CurrentFormat - 1}-*"));
        Assert.Equal(asWritten, File.ReadAllBytes(copy));
        SaveLoader.Open(saves, path, t.Store, out _);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "Game 1.format-*"));
    }

    [Fact]
    public void A_save_from_an_older_format_whose_copy_cannot_be_kept_is_not_opened_and_is_left_as_it_was()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = new Table();
        var saves = SaveStore.ForRoot(t.Root);
        var path = Path.Combine(t.Root, "saves", "unkept", "Game 1" + SaveStore.Ext);
        t.Server.SaveTo(path);
        var older = Json.Load<SaveFile>(path);
        older.Format = SaveFile.CurrentFormat - 1;
        Json.Save(path, older);
        var asWritten = File.ReadAllBytes(path);
        var folder = new DirectoryInfo(Path.GetDirectoryName(path)!);
        var before = Directory.GetFiles(folder.FullName);
        var acl = folder.GetAccessControl();
        var noNewFiles = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
        acl.AddAccessRule(noNewFiles);
        folder.SetAccessControl(acl);
        try
        {
            var e = Assert.Throws<SaveHeldException>(() => SaveLoader.Open(saves, path, t.Store, out _));
            Assert.Contains("the game was not opened", e.Message);
        }
        finally
        {
            acl.RemoveAccessRule(noNewFiles);
            folder.SetAccessControl(acl);
        }
        Assert.Equal(before, Directory.GetFiles(folder.FullName));
        Assert.Equal(asWritten, File.ReadAllBytes(path));
        SaveLoader.Open(saves, path, t.Store, out _);
        Assert.Single(Directory.GetFiles(folder.FullName, "Game 1.format-*"));
    }

    [Fact]
    public void A_save_in_this_version_s_format_is_opened_without_a_copy()
    {
        using var t = new Table();
        var saves = SaveStore.ForRoot(t.Root);
        var path = Path.Combine(t.Root, "saves", "current", "Game 1" + SaveStore.Ext);
        t.Server.SaveTo(path);
        SaveLoader.Open(saves, path, t.Store, out _);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "Game 1.format-*"));
    }
}
