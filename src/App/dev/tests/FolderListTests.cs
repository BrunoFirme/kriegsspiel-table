using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class FolderListTests
{
    [NodeTest]
    static async Task Folder_favourites_made_in_a_file_explorer_are_written_to_settings_and_come_back_on_the_next_start(TestRunner r)
    {
        var file = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N") + ".json");
        var place = Settings.FileAt;
        var keptIn = Ui.FoldersIn;
        var favouritesBefore = FileDialog.GetFavoriteList();
        var recentBefore = FileDialog.GetRecentList();
        var folder = Path.GetTempPath().Replace('\\', '/').TrimEnd('/') + "/";
        Settings.FileAt = () => file;
        try
        {
            Ui.KeepFolderLists(new Settings());
            var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem };
            FileBrowser.Fit(fd);
            r.AddChild(fd);
            fd.PopupCentered(new Vector2I(600, 400));
            await r.Frames(2);
            FileDialog.SetFavoriteList([folder]);
            fd.Hide();
            await r.Frames(1);
            Check.Equal(folder, Settings.Load().FolderFavourites.FirstOrDefault(), "a favourite made in the explorer is written to settings when it closes");
            FileDialog.SetFavoriteList([]);
            Ui.KeepFolderLists(Settings.Load());
            Check.Equal(folder, FileDialog.GetFavoriteList().FirstOrDefault(), "the next start hands the kept favourites to every explorer");
            fd.QueueFree();
        }
        finally
        {
            Settings.FileAt = place;
            Ui.FoldersIn = keptIn;
            FileDialog.SetFavoriteList(favouritesBefore);
            FileDialog.SetRecentList(recentBefore);
            File.Delete(file);
        }
    }
}
