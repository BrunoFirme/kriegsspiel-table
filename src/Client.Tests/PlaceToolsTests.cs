using System.Text.Json;
using KTable.Client.Agent;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public partial class TableToolsTests
{
    [Fact]
    public async Task Places_are_listed_with_who_stands_there_and_a_player_is_sent_to_one()
    {
        _s.Do(new SetPlace { Place = new PlacePin { Name = "The Tavern", Pos = new Vec2(40, 40) } });
        var tools = new TableTools(() => _s, () => false, _ => Task.CompletedTask, _log.Add, new TableHooks { Bodies = () => [new PlaceBody("p-1", "Ann")] })
            .Tools().ToDictionary(t => t.Name);
        async Task<ToolResult> Run(string name, object args) => await tools[name].Run(JsonSerializer.SerializeToElement(args));
        var listed = await Run("list_places", new { });
        Assert.Contains("- The Tavern (pin) 40, 40", listed.Text);
        Assert.DoesNotContain("here: Ann", listed.Text);
        Assert.EndsWith(Hearsay.Mark, listed.Text);
        Assert.True((await Run("send_to_place", new { who = "Bob", place = "The Tavern" })).IsError);
        Assert.True((await Run("send_to_place", new { who = "Ann", place = "Nowhere" })).IsError);
        var sent = await Run("send_to_place", new { who = "everyone", place = "tavern" });
        Assert.False(sent.IsError, sent.Text);
        Assert.Equal(new Vec2(40, 40), _s.View.Find(Layers.Umpire, "p-1")!.Pos);
        Assert.Contains("- The Tavern (pin) 40, 40 · here: Ann", (await Run("list_places", new { })).Text);
        Assert.True((await Call("send_to_place", new { who = "Ann", place = "The Tavern" })).IsError);
    }
}
