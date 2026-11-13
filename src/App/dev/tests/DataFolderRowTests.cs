using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

static class DataFolderRowTests
{
    const int LongestWait = 600;

    static string Scratch() => Path.Combine(Path.GetTempPath(), "ktable-node-datafolder", Guid.NewGuid().ToString("N"));

    static string Filled(string folder)
    {
        foreach (var (rel, text) in new[] { (Path.Combine("packages", "user.blocks", "pack.json"), "{}"), (Path.Combine("saves", "ford", "game.kstable"), new string('k', 5000)) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, rel))!);
            File.WriteAllText(Path.Combine(folder, rel), text);
        }
        return folder;
    }

    static Button? Offered(TestRunner r, string text) =>
        r.GetTree().Root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());

    [NodeTest]
    static async Task The_storage_row_names_the_folder_says_why_a_folder_will_not_do_and_moves_the_data_only_after_the_question(TestRunner r)
    {
        var root = Scratch();
        string usual = Path.Combine(root, "default", DataPaths.Name), from = Filled(usual);
        var box = new VBoxContainer();
        r.AddChild(box);
        var said = new List<string>();
        int restarts = 0;
        var cfg = new Settings { CaptureFolder = Path.Combine(from, "captures") };
        var maker = new DataFolderRow(box, said.Add, () => null, cfg, () => restarts++, usual, from);
        var row = maker.Row();
        box.AddChild(row);
        await r.Frame();
        Check.Equal(from, ((Label)row.GetChild(3)).Text, "the row names the folder the data is in");
        var size = (Label)row.GetChild(1);
        for (int i = 0; i < LongestWait && size.Text.Length == 0; i++) await r.Frame();
        Check.That(size.Text.EndsWith("KB", StringComparison.Ordinal), "and says how much it holds, as the caches above it do: " + size.Text);
        Check.That(row.GetChild(2) is Button { Text: "Move…" }, "Move… stands where the caches' Clear buttons stand");

        maker.Offer(DataFolderMove.Look(Path.GetDirectoryName(from)!, from));
        Check.Equal("The data is already in that folder.", said.LastOrDefault() ?? "", "the same folder is refused with the reason");
        Check.That(Offered(r, "Move and restart") == null, "and nothing is asked");

        var plan = DataFolderMove.Look(Path.Combine(root, "hdd"), from);
        maker.Offer(plan);
        await r.Frames(2);
        var yes = Offered(r, "Move and restart");
        Check.That(yes != null && !Directory.Exists(plan.To), "an empty folder is offered, and nothing moves before the answer");
        yes!.EmitSignal(BaseButton.SignalName.Pressed);
        for (int i = 0; i < LongestWait && restarts == 0 && said.Count == 1; i++) await r.Frame();
        Check.Equal(1, restarts, "after the move the app restarts: " + string.Join(" | ", said));
        Check.That(File.Exists(Path.Combine(plan.To, "saves", "ford", "game.kstable")) && !File.Exists(Path.Combine(from, "saves", "ford", "game.kstable")), "the data is in the new folder and gone from the old");
        Check.Equal(plan.To, DataFolder.Chosen(usual), "the app is pointed at it");
        Check.Equal(Path.Combine(plan.To, "captures"), cfg.CaptureFolder, "and a capture folder kept inside the data follows it");

        var kept = Filled(Path.Combine(root, "other-pc", DataPaths.Name));
        maker.Offer(DataFolderMove.Look(Path.GetDirectoryName(kept)!, plan.To));
        await r.Frames(2);
        Check.That(Offered(r, "Use it and restart") != null, "a folder that already holds the app's data is offered as it is");
        Offered(r, "Use it and restart")!.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(restarts == 2 && DataFolder.Chosen(usual) == kept, "taken, the app points there and restarts");
        Check.That(File.Exists(Path.Combine(plan.To, "saves", "ford", "game.kstable")), "and the data left behind is untouched");
        box.QueueFree();
        Directory.Delete(root, true);
    }
}
