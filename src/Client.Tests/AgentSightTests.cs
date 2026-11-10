using System.Text.Json;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public partial class TableToolsTests
{
    [Fact]
    public async Task Pieces_kinds_and_lines_come_a_page_at_a_time_and_pieces_filter_by_area_and_tag()
    {
        var first = await Call("list_pieces", new { limit = 1 });
        Assert.StartsWith("pieces 1–1 of 2 (offset 1 for the next page)", first.Text);
        var second = await Call("list_pieces", new { limit = 1, offset = 1 });
        Assert.StartsWith("pieces 2–2 of 2", second.Text);
        Assert.DoesNotContain("next page", second.Text);
        var area = await Call("list_pieces", new { within = new[] { 0.5, 1.5, 2.0, 3.0 } });
        Assert.StartsWith("1 pieces", area.Text);
        Assert.Contains("\"p-1\"", area.Text);
        Assert.True((await Call("list_pieces", new { within = new[] { 1.0, 2.0 } })).IsError);
        await Call("set_piece_values", new { id = "p-2", tags = new[] { "cavalry" } });
        Assert.Contains("\"p-2\"", (await Call("list_pieces", new { tag = "Cavalry" })).Text);
        Assert.StartsWith("kinds 1–2 of 5", (await Call("list_kinds", new { limit = 2 })).Text);
        foreach (var x in new[] { 0.0, 10.0, 20.0 }) await Call("draw", new { points = new[] { new[] { x, 0.0 }, new[] { x + 1, 1.0 } } });
        Assert.StartsWith("lines 3–3 of 3", (await Call("list_drawings", new { offset = 2 })).Text);
    }

    [Fact]
    public async Task The_console_is_read_further_back_with_before()
    {
        await Call("write_script", new { text = "function on_load() print('first') print('second') end\n" });
        var second = _s.ConsoleLines.Last(l => l.Text.Contains("second")).Seq;
        var back = await Call("read_console", new { before = second });
        Assert.Contains("first", back.Text);
        Assert.DoesNotContain("second", back.Text);
    }

    [Fact]
    public async Task Panel_elements_come_a_page_at_a_time()
    {
        await Call("write_script", new { text = "function on_load() ui.set([[<Button id=\"a\">A</Button><Button id=\"b\">B</Button>]]) end\n" });
        Frame();
        var page = await Call("read_panels", new { offset = 1 });
        Assert.StartsWith("elements on the table 2–2 of 2", page.Text);
        Assert.Contains("\"b\"", page.Text);
    }

    [Fact]
    public async Task Waiting_says_what_a_script_or_another_seat_did_or_that_nothing_happened()
    {
        Assert.StartsWith("Nothing happened", (await Call("wait_for", new { seconds = 1 })).Text);
        Assert.True((await Call("wait_for", new { what = "weather" })).IsError);
        await Call("write_script", new { text = "function on_load() wait.seconds(0.05, function() game.say('The guard charges') end) end\n" });
        var ticking = new TableTools(() => _s, () => false, _ => { Frame(); return Task.CompletedTask; }, _log.Add).Tools().First(t => t.Name == "wait_for");
        var heard = await ticking.Run(JsonSerializer.SerializeToElement(new { what = "chat", seconds = 5 }));
        Assert.Contains("a script said: The guard charges", heard.Text);
    }
}
