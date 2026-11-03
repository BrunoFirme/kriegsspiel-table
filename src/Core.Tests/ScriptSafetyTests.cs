namespace KTable.Core.Tests;

public class ScriptSafetyTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "scripts", name));

    [Fact]
    public void The_worm_is_dropped_whole_and_named()
    {
        var v = ScriptSafety.Inspect(Fixture("worm.lua"));
        Assert.True(v.Hostile);
        Assert.Equal("", v.Script);
        Assert.Equal(new[] { ScriptSafety.KnownBlock }, v.Reasons);
        var bare = ScriptSafety.Inspect(Fixture("worm.lua").Replace("--[[Object base code]]", "").Replace("--[[Spawning object]]", "").Replace("tcejbo gninwapS", "x").Replace("edoc esab tcejbO", "y"));
        Assert.Equal("", bare.Script);
        Assert.Contains(ScriptSafety.Web, bare.Reasons);
        Assert.Contains(ScriptSafety.CopiesCode, bare.Reasons);
        Assert.Contains(ScriptSafety.EveryObject, bare.Reasons);
        Assert.Contains(ScriptSafety.Hidden, bare.Reasons);
    }

    [Fact]
    public void A_piece_script_passes_untouched()
    {
        var lua = Fixture("name_plate.lua");
        var v = ScriptSafety.Inspect(lua);
        Assert.True(v.Clean);
        Assert.Equal(lua, v.Script);
    }

    [Fact]
    public void The_installer_tool_passes_untouched()
    {
        var lua = Fixture("unit_installer.lua");
        Assert.Contains("setLuaScript", lua);
        var v = ScriptSafety.Inspect(lua);
        Assert.True(v.Clean);
        Assert.Equal(lua, v.Script);
    }

    [Fact]
    public void A_worm_appended_to_a_piece_script_is_cut_and_the_piece_script_kept()
    {
        var piece = Fixture("name_plate.lua").TrimEnd();
        var infected = piece + new string(' ', 400) + Fixture("worm.lua").Trim() + "\n\n";
        var v = ScriptSafety.Inspect(infected);
        Assert.Equal(new[] { ScriptSafety.KnownBlock }, v.Reasons);
        Assert.Equal(piece, v.Script);
    }

    [Theory]
    [InlineData("WebRequest.get('https://example.invalid/', function(r) end)", ScriptSafety.Web)]
    [InlineData("function onLoad() for _, o in ipairs(getAllObjects()) do o.setLuaScript('x') end end", ScriptSafety.EveryObject)]
    [InlineData("function onObjectSpawn(o) o.setLuaScript(self.getLuaScript()) end", ScriptSafety.CopiesCode)]
    [InlineData("local f = load(JSON.decode(self.getDescription()).code) f()", ScriptSafety.CodeFromData)]
    [InlineData("local f = loadstring(s)", ScriptSafety.CodeFromData)]
    [InlineData("io.open('x')", ScriptSafety.SystemLibs)]
    [InlineData("function onLoad() end" + "                                                                                                        " + "x = 1", ScriptSafety.Hidden)]
    public void Each_rule_drops_the_script_and_names_the_reason(string lua, string reason)
    {
        var v = ScriptSafety.Inspect(lua);
        Assert.Contains(reason, v.Reasons);
        Assert.Equal("", v.Script);
    }

    [Theory]
    [InlineData("function onload(saved) if saved ~= '' then local d = JSON.decode(saved) end end")]
    [InlineData("        self.createButton({label = 'x', position = {0, 0, 0}})")]
    [InlineData("local pos = self.getPosition() pos.x = pos.x + 1 local ratio = 2")]
    [InlineData("for _, o in ipairs(getAllObjects()) do o.highlightOn({1,0,0}) end")]
    [InlineData("")]
    public void Ordinary_scripts_are_clean(string lua)
    {
        var v = ScriptSafety.Inspect(lua);
        Assert.True(v.Clean);
        Assert.Equal(lua, v.Script);
    }

    [Fact]
    public void A_variant_that_carries_the_reversed_markers_is_dropped()
    {
        var v = ScriptSafety.Inspect("--[[Base]] if s:find('tcejbo gninwapS') == nil then end --[[End]]");
        Assert.Equal(new[] { ScriptSafety.KnownBlock }, v.Reasons);
        Assert.Equal("", v.Script);
    }
}
