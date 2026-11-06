using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class DiceScriptTests
{
    const string U = Layers.Umpire;
    const string Die = "p-000009";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    [Fact]
    public void A_TTS_script_rolls_a_die_reads_its_value_and_sets_a_face()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "die6", Id = Die, Pos = new(3, 3) });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        const string lua = "function onLoad()\n  self.setRotationValue(5)\n  local before = self.getValue()\n  local faces = #self.getRotationValues()\n"
                           + "  local rolled = {}\n  for i = 1, 40 do self.roll() rolled[self.getValue()] = true end\n  local seen = 0\n  for _ in pairs(rolled) do seen = seen + 1 end\n"
                           + "  self.setName(before .. '/' .. faces .. '/' .. (seen > 1 and 'many' or 'one'))\nend\n";
        var r = TtsTranslator.Translate(lua, Ctx());
        res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Die, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("5/6/many", t.Server.State.Find(U, Die)!.Label);
    }
}
