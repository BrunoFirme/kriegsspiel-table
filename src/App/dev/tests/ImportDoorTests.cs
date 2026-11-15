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
    static async Task The_import_page_fills_its_source_from_a_drop_and_hands_a_shared_file_to_the_installer(TestRunner r)
    {
        var installed = new List<string>();
        bool converted = false;
        var page = new ImportPage(Scratch(), (_, _) => converted = true) { Installs = installed.Add };
        r.AddChild(page);
        await r.Frames(2);
        var source = page.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(e => e.PlaceholderText == "file or Workshop link");
        var go = page.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == ImportDoor.Name);

        Check.That(FileDrops.Route(new[] { "C:/mods/2000.json" }), "a module dropped while the page is open is taken");
        Check.Equal("C:/mods/2000.json", source.Text, "the dropped module is the source");
        Check.That(FileDrops.Route(new[] { "C:/mods/2000.json", "C:/shared/Feud.ktpack" }), "a shared file dropped is taken");
        Check.Equal("C:/shared/Feud.ktpack", source.Text, "the shared file is the source");

        go.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(1);
        Check.Equal("C:/shared/Feud.ktpack", string.Join("|", installed), "the shared file went to the installer, once");
        Check.That(!go.Disabled && !converted, "no conversion was started for a shared file");
        page.QueueFree();
        await r.Frames(2);
    }

    [NodeTest]
    static async Task The_import_log_comes_back_with_the_page_after_the_title_is_rebuilt_and_a_fresh_visit_starts_it_empty(TestRunner r)
    {
        const string Converted = "Converted the test module";
        var first = new ImportPage(Scratch(), (_, _) => { });
        r.AddChild(first);
        await r.Frames(2);
        ImportPage.Log(Converted);
        Check.That(LogOf(first).Contains(Converted), "the open page shows the conversion's log");
        first.QueueFree();
        await r.Frames(1);

        var back = new ImportPage(Scratch(), (_, _) => { }, keepLog: true);
        r.AddChild(back);
        await r.Frames(2);
        Check.That(LogOf(back).Contains(Converted), "the page brought back by a rebuild shows the log it had");
        ImportPage.Log("Done.");
        Check.That(LogOf(back).Contains("Done."), "lines written after the rebuild reach the page that came back");
        back.QueueFree();
        await r.Frames(1);

        var fresh = new ImportPage(Scratch(), (_, _) => { });
        r.AddChild(fresh);
        await r.Frames(2);
        Check.That(!LogOf(fresh).Contains(Converted), "a fresh visit starts with an empty log");
        fresh.QueueFree();
        await r.Frames(2);
    }

    static string Scratch() => Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));

    static string LogOf(Node page) => page.FindChildren("*", "RichTextLabel", true, false).OfType<RichTextLabel>().First(l => l.ScrollFollowing).GetParsedText();
}
