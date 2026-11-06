using System.Text.RegularExpressions;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.Server.Tests;

public class ScriptToolsTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static string Repo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "KriegsspielTable.sln"))) return d.FullName;
        throw new InvalidOperationException("the repository is not above the test's folder");
    }

    static List<ConsoleLine> LinesFor(IEnumerable<Outgoing> outs, long peer) =>
        outs.Where(o => o.PeerId == peer && o.Message is ScriptConsole).SelectMany(o => ((ScriptConsole)o.Message).Lines).ToList();

    static List<ConsoleLine> Run(Table t, string target, string code, long by = Table.Host)
    {
        var outs = t.Server.Request(by, new RunLua { Target = target, Code = code });
        return LinesFor(outs, by);
    }

    static string Result(Table t, string target, string code) =>
        Run(t, target, code).LastOrDefault(l => l.Kind is "result" or "error")?.Text ?? "";

    [Fact]
    public void Every_function_the_runtime_installs_is_in_the_reference()
    {
        using var t = new Table();
        var listed = ApiDoc.All.Select(e => e.Qualified).ToHashSet();
        var installed = Result(t, "", """
            local out = {}
            for _, g in ipairs({ "board", "game", "wait", "json", "ui" }) do
              for k in pairs(_G[g] or {}) do out[#out + 1] = g .. "." .. k end
            end
            table.sort(out)
            return table.concat(out, " ")
            """).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(installed.Length > 30, "the console gave back: " + string.Join(" ", installed));
        var unlisted = installed.Where(n => !listed.Contains(n)).ToList();
        Assert.True(unlisted.Count == 0, "not in ApiDoc: " + string.Join(", ", unlisted));
    }

    [Fact]
    public void Every_field_and_call_of_a_piece_in_the_code_is_in_the_reference()
    {
        var src = File.ReadAllText(Path.Combine(Repo(), "src", "Server", "Scripting", "ScriptApi.cs"));
        int from = src.IndexOf("DynValue Index(string id, string key)", StringComparison.Ordinal), to = src.IndexOf("static Vec2 ToWorld", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from);
        var keys = Regex.Matches(src[from..to], "case \"(\\w+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("set_state", keys);
        var listed = ApiDoc.All.Where(e => e.Group == "piece").Select(e => e.Name).ToHashSet();
        var unlisted = keys.Where(k => !listed.Contains(k)).ToList();
        Assert.True(unlisted.Count == 0, "not in ApiDoc: " + string.Join(", ", unlisted));
    }

    [Fact]
    public void Every_name_in_the_reference_answers_on_a_live_table()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function on_load() end" });
        var mayBeNil = new HashSet<string> { "self.name", "self.note", "self.tint", "self.picture_size", "self.local_unit", "game.calibration" };
        var names = ApiDoc.All.Where(e => e.Group is "piece" or "board" or "game" or "ui" or "wait" or "json" && !mayBeNil.Contains(e.Qualified)).Select(e => e.Qualified).ToList();
        var missing = Result(t, P, "local out = {} " + string.Join(" ", names.Select(n => $"if {n} == nil then out[#out + 1] = \"{n}\" end")) + " return table.concat(out, \" \") .. \"|\"");
        Assert.Equal("|", missing);
    }

    [Fact]
    public void The_reference_and_the_stubs_in_docs_are_the_ones_the_list_writes()
    {
        static string Norm(string s) => s.Replace("\r\n", "\n");
        var docs = Path.Combine(Repo(), "docs");
        Assert.True(Norm(File.ReadAllText(Path.Combine(docs, "scripting-api.md"))) == ApiDoc.Markdown(), "docs/scripting-api.md is out of date: run " + ApiDoc.Regenerate);
        Assert.True(Norm(File.ReadAllText(Path.Combine(docs, "ktable-api.lua"))) == ApiDoc.Stubs(), "docs/ktable-api.lua is out of date: run " + ApiDoc.Regenerate);
    }

    [Fact]
    public void Completion_knows_what_hangs_off_each_name()
    {
        Assert.Contains(ApiDoc.MembersOf("self"), e => e.Name == "set_state");
        Assert.Contains(ApiDoc.MembersOf("unit"), e => e.Name == "set_state");
        Assert.Contains(ApiDoc.MembersOf("board"), e => e.Name == "spawn");
        Assert.Contains(ApiDoc.MembersOf("self.ui"), e => e.Name == "set_attribute");
        Assert.DoesNotContain(ApiDoc.MembersOf("board"), e => e.Name == "set_state");
        Assert.Equal("setState", ApiDoc.Lookup("self", "set_state")!.ModuleCall);
        Assert.NotNull(ApiDoc.Lookup(null, "on_load"));
    }

    [Fact]
    public void What_a_script_prints_reaches_the_umpires_and_no_observer()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function on_load() print('ready', 3) log({ a = 1 }, 'state') end" });
        var outs = t.Server.ConsoleOut();
        var host = LinesFor(outs, Table.Host);
        Assert.Contains(host, l => l.Text == "ready\t3" && l.Kind == "print" && l.Source == P && l.Name == "Infantry");
        Assert.Contains(host, l => l.Text == "state: { a = 1 }");
        Assert.NotEmpty(LinesFor(outs, Table.Ump3));
        Assert.Empty(LinesFor(outs, Table.Obs));
        Assert.Empty(t.Server.ConsoleOut());
    }

    [Fact]
    public void An_error_names_its_line_and_the_script_shows_it_until_reloaded()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function on_load()\n  local x = nil\n  return x.y\nend" });
        var err = Assert.Single(LinesFor(t.Server.ConsoleOut(), Table.Host), l => l.Kind == "error");
        Assert.Equal(3, err.Line);
        var info = Assert.Single(t.Server.Scripts.Describe(), i => i.Id == P);
        Assert.Equal("error", info.Status);
        Assert.Equal(3, info.ErrorLine);
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function on_load() end" });
        Assert.Equal("running", Assert.Single(t.Server.Scripts.Describe(), i => i.Id == P).Status);
    }

    [Fact]
    public void An_umpire_who_arrives_gets_what_the_console_said_before()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "function on_load() print('before you came') end" });
        t.Server.ConsoleOut();
        t.Knock(40, "Late");
        var outs = t.Door(Table.Host, new Admit { PeerId = 40, Role = Role.Umpire });
        var backlog = Assert.Single(outs, o => o.PeerId == 40 && o.Message is ScriptConsole).Message as ScriptConsole;
        Assert.True(backlog!.Backlog);
        Assert.Contains(backlog.Lines, l => l.Text == "before you came");
    }

    [Fact]
    public void Code_runs_inside_the_script_and_an_expression_comes_back()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "count = 41 function bump() count = count + 1 return count end" });
        Assert.Equal("42", Result(t, P, "bump()"));
        Assert.Equal("42", Result(t, P, "count"));
        Assert.Equal("{ x = 0, z = 0 }", Result(t, P, "self.pos"));
        Assert.StartsWith("piece p-000002", Result(t, "", "board.find('p-000002')"));
    }

    [Fact]
    public void Code_changes_the_table_as_one_batch_the_umpire_takes_back()
    {
        using var t = new Table();
        Run(t, P, "self.set_counter('str', 3) self.set_name('Fusiliers')");
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(3, p.Counters["str"]);
        Assert.Equal("Fusiliers", p.Label);
        var batch = Assert.IsType<Batch>(t.Server.Log[^1]);
        Assert.Equal(Table.Host, batch.Actor);
        t.Server.Handle(Table.Host, new Undo { TargetSeq = batch.Seq });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(8, p.Counters["str"]);
        Assert.Null(p.Label);
    }

    [Fact]
    public void A_mistake_at_the_console_never_switches_the_script_off()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "n = 1" });
        Assert.Contains("attempt", Result(t, P, "nothing.here()"));
        Assert.Contains("stopped", Result(t, P, "while true do end"));
        var info = Assert.Single(t.Server.Scripts.Describe(), i => i.Id == P);
        Assert.Equal("running", info.Status);
        Assert.Equal("1", Result(t, P, "n"));
        Assert.DoesNotContain(t.Server.Log, c => c is Say);
    }

    [Fact]
    public void Only_the_host_and_the_leads_run_code()
    {
        using var t = new Table();
        var outs = t.Server.Request(Table.Ump3, new RunLua { Target = P, Code = "self.set_name('x')" });
        Assert.Contains(outs, o => o.PeerId == Table.Ump3 && o.Message is ErrorMessage);
        Assert.Null(t.Server.State.Find(U, P)!.Label);
        Assert.Contains(t.Server.Request(Table.Obs, new GetScripts()), o => o.Message is ErrorMessage);
        Run(t, P, "self.set_name('by the lead')", by: Table.Lead);
        Assert.Equal("by the lead", t.Server.State.Find(U, P)!.Label);
        Assert.Contains(t.Server.Request(Table.Ump3, new GetScripts()), o => o.Message is ScriptList);
    }

    [Fact]
    public void Hostile_code_is_refused_before_it_runs()
    {
        using var t = new Table();
        var lines = Run(t, "", "io.open('x', 'w')");
        Assert.Contains(lines, l => l.Kind == "error" && l.Text.StartsWith("refused"));
    }

    [Fact]
    public void A_timer_set_at_the_console_belongs_to_the_script()
    {
        using var t = new Table();
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = "fired = false" });
        Run(t, P, "wait.frames(1, function() fired = true end)");
        Assert.Equal(1, Assert.Single(t.Server.Scripts.Describe(), i => i.Id == P).Timers);
        t.Server.Scripts.Tick(1);
        t.Server.Scripts.Tick(2);
        Assert.Equal("true", Result(t, P, "fired"));
    }
}
