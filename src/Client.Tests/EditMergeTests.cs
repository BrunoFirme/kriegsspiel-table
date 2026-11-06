using System.Text.Json.Nodes;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class EditMergeTests
{
    static JsonNode N(string json) => JsonNode.Parse(json)!;

    static string Back(string from, string to, string now) => EditMerge.Back(N(from), N(to), N(now))!.ToJsonString();

    [Fact]
    public void Undoing_a_change_takes_back_only_what_it_changed()
    {
        var merged = Back("""{"Thickness":0.2,"Image":"a"}""", """{"Thickness":0.5,"Image":"a"}""", """{"Thickness":0.5,"Image":"painted"}""");
        Assert.Equal("""{"Thickness":0.2,"Image":"painted"}""", merged);
    }

    [Fact]
    public void A_look_renamed_comes_back_with_its_later_painting_kept()
    {
        var from = """{"States":[{"Name":"Fresh","Image":"a"},{"Name":"Look 2","Image":"a"}]}""";
        var to = """{"States":[{"Name":"Fresh","Image":"a"},{"Name":"Shattered","Image":"a"}]}""";
        var now = """{"States":[{"Name":"Fresh","Image":"a"},{"Name":"Shattered","Image":"b"}]}""";
        Assert.Equal("""{"States":[{"Name":"Fresh","Image":"a"},{"Name":"Look 2","Image":"b"}]}""", Back(from, to, now));
    }

    [Fact]
    public void A_list_that_changed_length_comes_back_whole()
    {
        var merged = Back("""{"Counters":[{"Name":"Ammo"}]}""", """{"Counters":[]}""", """{"Counters":[]}""");
        Assert.Equal("""{"Counters":[{"Name":"Ammo"}]}""", merged);
    }

    [Fact]
    public void A_field_the_change_added_goes_away_and_one_it_cleared_returns()
    {
        Assert.Equal("""{"W":1}""", Back("""{"W":1}""", """{"W":1,"Size":{"W":2}}""", """{"W":1,"Size":{"W":2}}"""));
        Assert.Equal("""{"W":1,"Size":{"W":2}}""", Back("""{"W":1,"Size":{"W":2}}""", """{"W":1}""", """{"W":1}"""));
    }

    [Fact]
    public void Redo_is_the_same_merge_the_other_way()
    {
        var from = """{"Thickness":0.2,"Image":"a"}""";
        var to = """{"Thickness":0.5,"Image":"a"}""";
        var undone = Back(from, to, """{"Thickness":0.5,"Image":"painted"}""");
        Assert.Equal("""{"Thickness":0.5,"Image":"painted"}""", Back(to, from, undone));
    }

    [Fact]
    public void Each_undo_step_carries_when_it_was_done_and_moves_with_its_stamp()
    {
        var h = new UndoHistory();
        h.Start("0");
        Assert.Equal(0, h.UndoStamp);
        h.Record("1", 5);
        h.Record("2", 9);
        Assert.Equal(9, h.UndoStamp);
        Assert.Equal("1", h.Undo(12));
        Assert.Equal((5L, 12L), (h.UndoStamp, h.RedoStamp));
        Assert.Equal("2", h.Redo(14));
        Assert.Equal((14L, 0L), (h.UndoStamp, h.RedoStamp));
        h.Record("3", 20);
        Assert.False(h.CanRedo);
    }
}
