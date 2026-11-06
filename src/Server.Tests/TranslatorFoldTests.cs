using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class TranslatorFoldTests
{
    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    [Fact]
    public void A_member_written_in_any_casing_TTS_accepts_is_the_same_member_here()
    {
        const string lua = "function onLoad()\n  self.Scale({x=1.1,y=1,z=1})\n  self.set_position({1,2,3})\n  local p = self.Get_Position()\n  self.Interactable = false\n  local t = self.tooltip\nend\n";
        var r = TtsTranslator.Translate(lua, Ctx());
        Assert.True(r.Parsed);
        Assert.Contains("compat.scale_by(self, {x=1.1,y=1,z=1})", r.Script);
        Assert.Contains("compat.set_position(self, {1,2,3})", r.Script);
        Assert.Contains("compat.position(self)", r.Script);
        Assert.Contains("compat.set_prop(self, \"interactable\", false)", r.Script);
        Assert.Contains("compat.get_prop(self, \"tooltip\"", r.Script);
    }

    [Fact]
    public void A_name_TTS_would_not_resolve_is_left_alone()
    {
        var r = TtsTranslator.Translate("function onLoad() self.getposition() self.scalE() end", Ctx());
        Assert.Contains("self.getposition()", r.Script);
        Assert.Contains("self.scalE()", r.Script);
        Assert.Equal("scale", TtsTranslator.FoldMember("Scale"));
        Assert.Equal("getPosition", TtsTranslator.FoldMember("get_position"));
        Assert.Equal("getPosition", TtsTranslator.FoldMember("Get_Position"));
        Assert.Equal("getposition", TtsTranslator.FoldMember("getposition"));
    }
}
