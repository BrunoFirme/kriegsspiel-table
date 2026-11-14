using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ImportDoorTests
{
    [NodeTest]
    static Task A_shared_file_is_told_from_a_module_by_its_name_and_a_drop_takes_the_shared_file_first(TestRunner r)
    {
        Check.That(ImportDoor.IsShared(" C:/shared/Feud.KTPACK "), "a .ktpack is a shared file whatever its case and spacing");
        Check.That(!ImportDoor.IsShared("https://steamcommunity.com/sharedfiles/filedetails/?id=123"), "a Workshop link is not a shared file");
        Check.That(!ImportDoor.IsShared("C:/mods/2000.json"), "a module is not a shared file");
        Check.Equal("b.ktpack", ImportDoor.Taken(new[] { "a.json", "b.ktpack" }), "the shared file is taken before the module");
        Check.Equal("a.json", ImportDoor.Taken(new[] { "notes.txt", "a.json" }), "a module is taken past a file of no use");
        Check.Equal(null, ImportDoor.Taken(new[] { "notes.txt", "map.png" }), "files of no use are left");
        return Task.CompletedTask;
    }

    [NodeTest]
    static async Task The_import_window_fills_its_source_from_a_drop_and_hands_a_shared_file_to_the_installer(TestRunner r)
    {
        var installed = new List<string>();
        bool converted = false;
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        var dlg = new ImportDialog(root, (_, _) => converted = true) { Installs = installed.Add };
        Ui.Open(r, dlg);
        await r.Frames(2);
        var source = dlg.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(e => e.PlaceholderText == "file or Workshop link");
        var go = dlg.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == ImportDoor.Name);

        Check.That(FileDrops.Route(new[] { "C:/mods/2000.json" }), "a module dropped while the window is open is taken");
        Check.Equal("C:/mods/2000.json", source.Text, "the dropped module is the source");
        Check.That(FileDrops.Route(new[] { "C:/mods/2000.json", "C:/shared/Feud.ktpack" }), "a shared file dropped is taken");
        Check.Equal("C:/shared/Feud.ktpack", source.Text, "the shared file is the source");

        go.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(1);
        Check.Equal("C:/shared/Feud.ktpack", string.Join("|", installed), "the shared file went to the installer, once");
        Check.That(!dlg.Visible, "the window closed for the install");
        Check.That(!go.Disabled && !converted, "no conversion was started for a shared file");
        dlg.QueueFree();
        await r.Frames(2);
    }
}
