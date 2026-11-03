using System.Diagnostics;
using System.Globalization;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Server.Tests;

[Collection(HeavyTests.Name)]
public class ScriptMemoryTests
{
    const string P = "p-000002";
    const string Overrun = "stopped: more than 256 MB of memory in one call";

    static void Load(Table t, string script) =>
        t.Server.Handle(Table.Host, new SetScript { Layer = Layers.Umpire, Id = P, Text = script });

    static string? Stopped(Table t) => t.Server.Scripts.Console.FirstOrDefault(l => l.StartsWith("Script error") && l.Contains(P) && l.Contains("stopped"));

    [Fact]
    public void A_table_grown_without_end_is_stopped_for_its_memory_and_the_host_goes_on()
    {
        using var t = new Table();
        var sw = Stopwatch.StartNew();
        Load(t, "function on_load() local t, i = {}, 0 while true do i = i + 1 t[i] = { i, tostring(i), {}, {} } end end");
        Assert.True(sw.ElapsedMilliseconds < KTable.Server.Scripting.ScriptHost.LoadBudgetMs, $"took {sw.ElapsedMilliseconds} ms");
        var line = Stopped(t);
        Assert.NotNull(line);
        Assert.Contains(Overrun, line);
        Assert.Contains("off until it is reloaded", line);

        Load(t, "function on_load() self.set_text('name', 'still here') end");
        Assert.Equal("still here", t.Server.State.Find(Layers.Umpire, P)!.Text["name"]);
    }

    [Fact]
    public void A_pcall_does_not_keep_a_call_past_its_memory()
    {
        using var t = new Table();
        Load(t, "function on_load() local t = {} pcall(function() local i = 0 while true do i = i + 1 t[i] = { i, {}, {} } end end) self.set_text('name', 'escaped') end");
        Assert.Contains(Overrun, Stopped(t));
        Assert.NotEqual("escaped", t.Server.State.Find(Layers.Umpire, P)!.Text.GetValueOrDefault("name"));
    }

    [Fact]
    public void A_coroutine_the_script_made_is_stopped_for_its_memory_too()
    {
        using var t = new Table();
        Load(t, "function on_load() local co = coroutine.create(function() local t, i = {}, 0 while true do i = i + 1 t[i] = { i, {}, {} } end end) coroutine.resume(co) end");
        Assert.Contains(Overrun, Stopped(t));
    }

    [Fact]
    public void Big_strings_copied_in_a_loop_are_stopped_near_the_cap_not_a_hundred_thousand_instructions_on()
    {
        using var t = new Table();
        GC.Collect();
        Load(t, "function on_load() local s, t = string.rep('x', 50000), {} while true do for i = 1, 250 do t[#t + 1] = s .. i end print(#t) end end");
        Assert.Contains(Overrun, Stopped(t));
        var reached = t.Server.Scripts.Console.Select(l => int.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Max();
        Assert.InRange(reached, 2000, 4500);
    }

    [Fact]
    public void Garbage_is_not_growth_a_call_making_much_more_than_the_cap_of_it_runs_to_the_end()
    {
        using var t = new Table();
        Load(t, """
            function on_load()
              local s, n = string.rep('x', 100000), 0
              for i = 1, 3000 do local c = s .. i n = n + #c end
              self.set_text('name', tostring(n))
            end
            """);
        Assert.Null(Stopped(t));
        Assert.Equal("300010893", t.Server.State.Find(Layers.Umpire, P)!.Text["name"]);
    }
}
