using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class StringSplitTests
{
    [Fact]
    public void A_module_s_own_string_split_is_kept_and_not_taken_for_card_dealing()
    {
        using var t = new Table();
        const string lua = "function string.split(s, sep) local out = {} for p in s:gmatch('[^' .. sep .. ']+') do out[#out + 1] = p end return out end\n"
                           + "function onLoad()\n  local a = string.split('a,b', ',')\n  local text = 'x;y;z'\n  local c = text:split(';')\n"
                           + "  self.setName(#a .. '/' .. #c .. '/' .. c[3])\nend\n";
        var tr = ModuleTranslator.Translate(lua, new TranslateContext());
        Assert.DoesNotContain("compat.todo", tr.Script);
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = Layers.Umpire, Id = "p-000002", Text = tr.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("2/3/z", t.Server.State.Find(Layers.Umpire, "p-000002")!.Label);
    }
}
