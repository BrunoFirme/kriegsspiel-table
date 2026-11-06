using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;

namespace KTable.App;

static class CacheShelfTests
{
    static Cache Folder(string dir) => new(CacheKind.ModuleDownloads, "Module files", "What importing downloaded", "Importing again downloads it again.", dir);

    static string Filled(params int[] sizes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-node-caches", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "text"));
        for (int i = 0; i < sizes.Length; i++) File.WriteAllBytes(Path.Combine(dir, i % 2 == 0 ? "" : "text", $"{i}.bin"), new byte[sizes[i]]);
        return dir;
    }

    static async Task<(VBoxContainer box, HBoxContainer row)> Shelf(TestRunner r, Cache c, List<string> said, Func<string?> busy)
    {
        var box = new VBoxContainer();
        r.AddChild(box);
        var row = new CacheShelf(box, said.Add, _ => busy()).Row(c);
        box.AddChild(row);
        for (int i = 0; i < 120 && Size(row).Length == 0; i++) await r.Frame();
        return (box, row);
    }

    static string Size(HBoxContainer row) => ((Label)row.GetChild(1)).Text;

    static Button Clear(HBoxContainer row) => (Button)row.GetChild(2);

    static Button? Asked(TestRunner r, HBoxContainer row) =>
        r.GetTree().Root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Clear" && b != Clear(row) && b.IsVisibleInTree());

    [NodeTest]
    static async Task A_cache_shows_its_size_and_clears_after_the_question_leaving_the_folder(TestRunner r)
    {
        var dir = Filled(3 << 20, 1 << 20);
        var said = new List<string>();
        var (box, row) = await Shelf(r, Folder(dir), said, () => null);
        Check.Equal("4 MB", Size(row), "the size measured");
        Check.That(!Clear(row).Disabled, "a full cache can be cleared");
        Clear(row).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(1);
        var yes = Asked(r, row);
        Check.That(yes != null, "clearing asks first");
        yes!.EmitSignal(BaseButton.SignalName.Pressed);
        for (int i = 0; i < 120 && Size(row) != "empty"; i++) await r.Frame();
        Check.Equal("Cleared 4 MB of module files.", said.LastOrDefault() ?? "", "what the status line says");
        Check.Equal("empty", Size(row), "the size measured again");
        Check.That(Clear(row).Disabled && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any(), "the folder stays, empty");
        box.QueueFree();
        Directory.Delete(dir, true);
    }

    [NodeTest]
    static async Task A_busy_cache_is_not_cleared_and_says_why(TestRunner r)
    {
        var dir = Filled(2048);
        var said = new List<string>();
        var (box, row) = await Shelf(r, Folder(dir), said, () => "An import is running.");
        Check.Equal("2 KB", Size(row), "the size measured");
        Clear(row).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(Asked(r, row) == null, "no question while busy");
        Check.Equal("An import is running.", said.LastOrDefault() ?? "", "it says why");
        Check.That(File.Exists(Path.Combine(dir, "0.bin")), "nothing deleted");
        box.QueueFree();
        Directory.Delete(dir, true);
    }
}
