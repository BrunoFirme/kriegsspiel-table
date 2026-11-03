using System.Text;
using KTable.Client.Scripting;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class ScriptFilesTests
{
    static readonly DateTime T0 = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    sealed class Target
    {
        public string? Script;
        public bool Here = true;
        public bool Takes = true;
        public readonly List<string> Applied = new();
        public void Apply(string text) { Applied.Add(text); if (Takes) Script = text.Trim().Length == 0 ? null : text; }
    }

    static (ScriptFiles files, Target target, ScriptLink link) Linked(string? script = "function on_load() end\n", string file = "hussar.lua")
    {
        var files = new ScriptFiles(Path.Combine(TestFolders.New(), "packs", "user-mine"), "Hussars", "-- stubs\n");
        var target = new Target { Script = script };
        var started = files.Start("def:user.mine.hussar", file, () => target.Script, () => target.Here, Keep.Ask, T0);
        Assert.NotNull(started.Link);
        return (files, target, started.Link!);
    }

    static List<ScriptEvent> Run(ScriptFiles files, Target target, DateTime at)
    {
        var said = files.Poll(at);
        foreach (var e in said.Where(e => e.Kind == ScriptEventKind.Changed)) target.Apply(e.Text);
        return said;
    }

    static List<ScriptEvent> Settle(ScriptFiles files, Target target, int polls = 3)
    {
        var all = new List<ScriptEvent>();
        for (int i = 0; i < polls; i++) all.AddRange(Run(files, target, T0.AddMilliseconds(250 * (i + 1))));
        return all;
    }

    [Fact]
    public void Linking_writes_the_script_and_the_api_stubs_into_the_folder()
    {
        var (files, _, link) = Linked();
        Assert.Equal("function on_load() end\n", File.ReadAllText(link.Path));
        Assert.Equal("-- stubs\n", File.ReadAllText(Path.Combine(files.Folder, ScriptFiles.StubsFile)));
        Assert.Contains("Hussars", File.ReadAllText(files.LogPath));
    }

    [Fact]
    public void File_names_are_slugs_of_the_kind_or_the_piece_and_never_a_reserved_name()
    {
        Assert.Equal("hussar-block-1a2b3c4d.lua", ScriptFiles.KindFile("user.mine", "user.mine.hussar-block-1a2b3c4d"));
        Assert.Equal("other-pack-x.lua", ScriptFiles.KindFile("user.mine", "other.pack.x"));
        Assert.Equal("2-ir-14-p-000053.lua", ScriptFiles.PieceFile("p-000053", "2/IR 14"));
        Assert.Equal("p-000053.lua", ScriptFiles.PieceFile("p-000053", null));
        Assert.EndsWith(Path.Combine("scenarios", "con-1"), ScriptFiles.ScenarioFolder("root", "CON"));
        Assert.EndsWith(Path.Combine("packs", "user-mine"), ScriptFiles.PackFolder("root", "user.mine"));
    }

    [Fact]
    public void An_existing_file_with_other_text_is_asked_about_and_left_alone()
    {
        var folder = TestFolders.New();
        File.WriteAllText(Path.Combine(folder, "a.lua"), "-- from yesterday\n");
        var files = new ScriptFiles(folder, "t", null);
        var target = new Target { Script = "-- today\n" };
        var asked = files.Start("p1", "a.lua", () => target.Script, () => true, Keep.Ask, T0);
        Assert.True(asked.Conflict);
        Assert.Equal("-- from yesterday\n", asked.FileText);
        Assert.Empty(files.Links);
        Assert.Equal("-- from yesterday\n", File.ReadAllText(Path.Combine(folder, "a.lua")));

        var kept = files.Start("p1", "a.lua", () => target.Script, () => true, Keep.Script, T0);
        Assert.NotNull(kept.Link);
        Assert.Equal("-- today\n", File.ReadAllText(kept.Link!.Path));
    }

    [Fact]
    public void Using_the_file_runs_it_on_the_next_poll()
    {
        var folder = TestFolders.New();
        File.WriteAllText(Path.Combine(folder, "a.lua"), "-- from yesterday\n");
        var files = new ScriptFiles(folder, "t", null);
        var target = new Target { Script = "-- today\n" };
        files.Start("p1", "a.lua", () => target.Script, () => true, Keep.File, T0);
        Settle(files, target);
        Assert.Equal(new[] { "-- from yesterday\n" }, target.Applied);
        Assert.Equal("-- from yesterday\n", target.Script);
    }

    [Fact]
    public void A_change_runs_once_and_only_after_two_polls_see_the_same_text()
    {
        var (files, target, link) = Linked();
        File.WriteAllText(link.Path, "function on_load() game.say('hi') end\n");
        Assert.Empty(Run(files, target, T0.AddMilliseconds(250)));
        Assert.Empty(target.Applied);
        var said = Run(files, target, T0.AddMilliseconds(500));
        Assert.Equal(ScriptEventKind.Changed, Assert.Single(said).Kind);
        Settle(files, target, 6);
        Assert.Single(target.Applied);
        Assert.Contains("hussar.lua: saved", File.ReadAllText(files.LogPath));
    }

    [Fact]
    public void A_file_still_being_written_waits_until_it_stops_changing()
    {
        var (files, target, link) = Linked();
        File.WriteAllText(link.Path, "function on_");
        Run(files, target, T0.AddMilliseconds(250));
        File.WriteAllText(link.Path, "function on_load() end -- done\n");
        Run(files, target, T0.AddMilliseconds(500));
        Run(files, target, T0.AddMilliseconds(750));
        Assert.Equal(new[] { "function on_load() end -- done\n" }, target.Applied);
    }

    [Fact]
    public void Windows_line_ends_and_a_byte_order_mark_read_as_plain_text()
    {
        var (files, target, link) = Linked("a = 1\nb = 2\n");
        File.WriteAllBytes(link.Path, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("a = 1\r\nb = 2\r\n")).ToArray());
        Settle(files, target);
        Assert.Empty(target.Applied);
        File.WriteAllText(link.Path, "a = 1\r\nb = 3\r\n");
        Settle(files, target);
        Assert.Equal(new[] { "a = 1\nb = 3\n" }, target.Applied);
    }

    [Fact]
    public void A_hostile_script_is_refused_and_logged_and_the_old_one_keeps_running()
    {
        var (files, target, link) = Linked();
        File.WriteAllText(link.Path, "WebRequest.get('http://x', function() end)\n");
        var said = Settle(files, target, 6);
        Assert.Equal(ScriptEventKind.Refused, Assert.Single(said).Kind);
        Assert.Empty(target.Applied);
        Assert.Equal("function on_load() end\n", target.Script);
        Assert.Contains("refused, not run: calls the web", File.ReadAllText(files.LogPath));
    }

    [Fact]
    public void A_binary_or_oversized_file_is_refused_by_name()
    {
        var (files, target, link) = Linked();
        File.WriteAllBytes(link.Path, new byte[] { 0x61, 0, 0x62 });
        var said = Settle(files, target);
        Assert.Contains("NUL", Assert.Single(said).Text);
        File.WriteAllBytes(link.Path, Enumerable.Repeat((byte)'-', ScriptFiles.MaxBytes + 1).ToArray());
        said = Settle(files, target);
        Assert.Contains("over 4 MB", Assert.Single(said).Text);
        Assert.Empty(target.Applied);
    }

    [Fact]
    public void An_emptied_file_removes_the_script()
    {
        var (files, target, link) = Linked();
        File.WriteAllText(link.Path, "");
        Settle(files, target);
        Assert.Null(target.Script);
    }

    [Fact]
    public void A_locked_file_is_tried_again_and_picked_up_once_free()
    {
        var (files, target, link) = Linked();
        File.WriteAllText(link.Path, "x = 2\n");
        List<ScriptEvent> said;
        using (new FileStream(link.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            said = Settle(files, target);
        Assert.Equal(ScriptEventKind.Unreadable, Assert.Single(said).Kind);
        Assert.Empty(target.Applied);
        Settle(files, target);
        Assert.Equal(new[] { "x = 2\n" }, target.Applied);
    }

    [Fact]
    public void A_deleted_file_stays_linked_and_is_picked_up_when_it_comes_back()
    {
        var (files, target, link) = Linked();
        File.Delete(link.Path);
        var said = Settle(files, target);
        Assert.Equal(ScriptEventKind.Missing, Assert.Single(said).Kind);
        Assert.Equal("function on_load() end\n", target.Script);
        File.WriteAllText(link.Path, "y = 1\n");
        Settle(files, target);
        Assert.Equal(new[] { "y = 1\n" }, target.Applied);
    }

    [Fact]
    public void A_change_made_in_the_app_rewrites_the_file_without_echoing_back()
    {
        var (files, target, link) = Linked();
        target.Script = "z = 9\n";
        var said = Settle(files, target, 6);
        Assert.Equal(ScriptEventKind.Rewritten, Assert.Single(said).Kind);
        Assert.Equal("z = 9\n", File.ReadAllText(link.Path));
        Assert.Empty(target.Applied);
    }

    [Fact]
    public void A_rewrite_keeps_edits_that_were_never_applied_in_a_bak_file()
    {
        var (files, target, link) = Linked();
        File.WriteAllText(link.Path, "WebRequest.get('x')\n");
        Settle(files, target);
        target.Script = "z = 9\n";
        Settle(files, target);
        Assert.Equal("z = 9\n", File.ReadAllText(link.Path));
        Assert.Equal("WebRequest.get('x')\n", File.ReadAllText(link.Path + ".bak"));
    }

    [Fact]
    public void A_change_the_table_does_not_take_is_said_and_the_file_is_kept()
    {
        var (files, target, link) = Linked();
        target.Takes = false;
        File.WriteAllText(link.Path, "w = 1\n");
        Settle(files, target);
        var said = files.Poll(T0 + ScriptFiles.TakeWait + TimeSpan.FromSeconds(1));
        Assert.Equal(ScriptEventKind.NotTaken, Assert.Single(said).Kind);
        Settle(files, target);
        Assert.Equal("w = 1\n", File.ReadAllText(link.Path));
        Assert.Single(target.Applied);
    }

    [Fact]
    public void A_change_waits_for_a_round_trip_before_it_is_compared_again()
    {
        var (files, target, link) = Linked();
        target.Takes = false;
        File.WriteAllText(link.Path, "late = true\n");
        Settle(files, target);
        target.Script = "late = true\n";
        Assert.Empty(files.Poll(T0.AddSeconds(2)));
        Assert.Empty(files.Poll(T0.AddSeconds(3)));
        Assert.Equal("late = true\n", File.ReadAllText(link.Path));
    }

    [Fact]
    public void A_link_whose_piece_is_gone_ends_and_the_file_stays()
    {
        var (files, target, link) = Linked();
        target.Here = false;
        var said = files.Poll(T0.AddSeconds(1));
        Assert.Equal(ScriptEventKind.Ended, Assert.Single(said).Kind);
        Assert.Empty(files.Links);
        Assert.True(File.Exists(link.Path));
    }

    [Fact]
    public void The_console_mirror_names_linked_files_with_their_line_and_others_by_name()
    {
        var (files, _, _) = Linked();
        files.Console(new List<ConsoleLine>
        {
            new() { Seq = 1, At = "2026-11-02T12:00:01Z", Kind = "error", Source = "p-1", Line = 12, Text = "attempt to index a nil value" },
            new() { Seq = 2, At = "2026-11-02T12:00:02Z", Kind = "print", Source = "p-2", Text = "hello\nthere" },
            new() { Seq = 3, At = "2026-11-02T12:00:03Z", Kind = "result", Source = "", Text = "42" },
        }, id => id == "p-1" ? "user.mine.hussar" : "other", l => l.Source.Length == 0 ? "global" : "Gun " + l.Source);
        var log = File.ReadAllText(files.LogPath);
        Assert.Contains("hussar.lua:12: error: attempt to index a nil value", log);
        Assert.Contains("Gun p-2: hello\n    there", log);
        Assert.Contains("global: = 42", log);
    }

    [Fact]
    public void The_console_mirror_writes_each_line_once_and_nothing_before_a_link()
    {
        var folder = TestFolders.New();
        var files = new ScriptFiles(folder, "t", null);
        var lines = new List<ConsoleLine> { new() { Seq = 1, Source = "", Text = "before" } };
        files.Console(lines, _ => null, _ => "global");
        files.Start("", ScriptFiles.GlobalFile, () => "", () => true, Keep.Ask, T0);
        lines.Add(new ConsoleLine { Seq = 2, Source = "", Text = "after" });
        files.Console(lines, _ => null, _ => "global");
        files.Console(lines, _ => null, _ => "global");
        var log = File.ReadAllText(files.LogPath);
        Assert.DoesNotContain("before", log);
        Assert.Single(log.Split('\n'), l => l.Contains("after"));
        Assert.Contains("global.lua: after", log);
    }

    [Fact]
    public void The_console_log_moves_aside_when_it_grows_past_its_limit()
    {
        var (files, _, _) = Linked();
        File.WriteAllText(files.LogPath, new string('x', (int)ScriptFiles.LogBytes + 10));
        files.Note("fresh", T0);
        Assert.True(File.Exists(Path.Combine(files.Folder, ScriptFiles.OldLogFile)));
        Assert.True(new FileInfo(files.LogPath).Length < 200);
    }
}
