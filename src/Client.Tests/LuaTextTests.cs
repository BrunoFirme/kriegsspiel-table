using KTable.Client.Scripting;
using KTable.Server.Scripting;

namespace KTable.Client.Tests;

public class LuaTextTests
{
    [Fact]
    public void Code_ends_where_a_string_or_a_comment_begins()
    {
        Assert.True(LuaText.IsCode("local x = 1"));
        Assert.False(LuaText.IsCode("print(\"abc"));
        Assert.True(LuaText.IsCode("print(\"a\\\"b\") x"));
        Assert.False(LuaText.IsCode("print('it\\'s"));
        Assert.False(LuaText.IsCode("x = 1 -- self."));
        Assert.True(LuaText.IsCode("s = \"--\" .. y"));
        Assert.True(LuaText.IsCode("x = 1 - 2"));
    }

    [Fact]
    public void What_is_typed_is_a_member_of_a_chain_or_the_start_of_a_word()
    {
        Assert.Equal(("self", "set_", true), LuaText.Before("  self.set_"));
        Assert.Equal(("self.ui", "", true), LuaText.Before("self.ui."));
        Assert.Equal(((string?)null, "boa", true), LuaText.Before("if boa"));
        Assert.Equal(((string?)null, "", true), LuaText.Before("x = ("));
        Assert.Equal(((string?)null, "", false), LuaText.Before("-- self.set"));
    }

    [Fact]
    public void Completions_are_the_receivers_members_or_the_top_level_names_that_start_so()
    {
        var self = LuaText.Completions("self", "set_").Select(e => e.Name).ToList();
        Assert.NotEmpty(self);
        Assert.All(self, n => Assert.StartsWith("set_", n));
        Assert.Equal(ApiDoc.MembersOf("piece").Count(e => e.Name.StartsWith("set_", StringComparison.Ordinal)), self.Count);
        var top = LuaText.Completions(null, "").Select(e => e.Name).ToList();
        Assert.Equal(ApiDoc.TopLevel.Select(e => e.Name), top);
    }

    [Fact]
    public void A_scripts_own_names_complete_once_without_the_apis_or_the_word_itself()
    {
        var script = "local count = 0\nfunction counter_up() end\nlocal count = 2\nlocal co\nfunction print() end\nlocalcake = 1";
        Assert.Equal(new[] { "count", "counter_up" }, LuaText.OwnNames(script, "cou"));
        Assert.Equal(new[] { "counter_up" }, LuaText.OwnNames(script, "count"));
        Assert.Equal(new[] { "count", "counter_up" }, LuaText.OwnNames(script, "co"));
        Assert.DoesNotContain("print", LuaText.OwnNames(script, ""));
    }

    [Fact]
    public void The_word_at_a_place_names_an_entry_of_what_it_follows()
    {
        var line = "self.set_state(2) board.find(x) print(y)";
        Assert.Equal(ApiDoc.Lookup("self", "set_state"), LuaText.EntryAt(line, 8));
        Assert.Equal(ApiDoc.Lookup("board", "find"), LuaText.EntryAt(line, 24));
        Assert.Equal(ApiDoc.Lookup(null, "print"), LuaText.EntryAt(line, 34));
        Assert.Null(LuaText.EntryAt(line, 17));
        Assert.Null(LuaText.EntryAt("", 5));
        Assert.Equal(ApiDoc.Lookup("piece", "set_state"), LuaText.EntryAt("(a).set_state", 99));
        Assert.NotNull(LuaText.EntryAt(line, 8));
        var e = new ApiEntry("piece", "n", "n()", "Does n.", "setN", "function", null, "a number");
        Assert.Equal("self.n()\nDoes n.\nReturns a number.\nModule: setN\nF1: the reference", LuaText.Describe(e).Replace(e.Display, "self.n()"));
    }
}
