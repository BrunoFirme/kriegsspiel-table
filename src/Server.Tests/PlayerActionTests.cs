using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class PlayerActionTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    const string Lua = "function onPlayerAction(player, action, targets)\n  if action == Player.Action.Delete or action == Player.Action.FlipOver then\n"
                       + "    for _, obj in ipairs(targets) do\n      if obj.hasTag('Source') then\n        broadcastToColor('Source pieces stay', player.color, 'Red')\n        return false\n      end\n    end\n  end\n"
                       + "  seen = (seen or 0) + 1\n  return true\nend\n";

    const string Probe = "\nfunction onLoad()\n  local p = getObjectFromGUID('" + P + "')\n  p.addTag('Source')\n  p.setName(tostring(p.hasTag('Source')) .. '/' .. type(on_player_action))\nend\n";

    static Table Prepared()
    {
        var t = new Table();
        var r = TtsTranslator.Translate(Lua + Probe, new TranslateContext { IsGlobal = true, ResolveImage = (url, type, scale) => "marker" });
        Assert.Empty(r.Untranslated);
        Assert.True(r.Script.Contains("function on_player_action("), r.Script);
        var res = t.Server.Handle(Table.Host, new SetGlobalScript { Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("true/function", t.Server.State.Find(U, P)!.Label);
        return t;
    }

    [Fact]
    public void A_module_s_onPlayerAction_can_refuse_a_delete_or_a_flip_and_lets_the_rest_through()
    {
        using var t = Prepared();
        var outs = new List<Outgoing>();
        var direct = t.Server.Scripts.ScriptVeto(new DeletePiece { Layer = U, Id = P }, Table.Host, outs);
        Assert.True(direct != null, "direct: " + string.Join(" | ", outs.Select(o => o.Message switch { CommandMessage cm when cm.Command is Say s => "S:" + s.Text, var m => m.GetType().Name })));
        var refused = t.Server.Handle(Table.Host, new DeletePiece { Layer = U, Id = P });
        var said = string.Join(" | ", refused.Select(o => o.Message switch { ErrorMessage e => "E:" + e.Text, CommandMessage cm when cm.Command is Say s => "S:" + s.Text, var m => m.GetType().Name }));
        var errors = refused.Select(o => o.Message).OfType<ErrorMessage>().ToList();
        Assert.True(errors.Count == 1, said);
        Assert.Contains("does not allow this delete", errors[0].Text);
        Assert.Contains(refused.Select(o => o.Message).OfType<CommandMessage>(), m => m.Command is Say s && s.Text.Contains("Source pieces stay"));
        Assert.NotNull(t.Server.State.Find(U, P));
        var flip = t.Server.Handle(Table.Host, new FlipPiece { Layer = U, Id = P, FaceDown = true });
        Assert.Contains(flip.Select(o => o.Message).OfType<ErrorMessage>(), e => e.Text.Contains("does not allow this flip"));
        var allowed = t.Server.Handle(Table.Host, new DeletePiece { Layer = U, Id = Marker });
        Assert.DoesNotContain(allowed, o => o.Message is ErrorMessage);
        Assert.Null(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void An_umpire_is_Black_to_a_TTS_script()
    {
        using var t = new Table();
        var r = TtsTranslator.Translate("function onLoad() self.setName(Player.getPlayers()[1].color .. '/' .. tostring(Player.Black.seated) .. '/' .. tostring(Player.Green.seated)) end",
            new TranslateContext { ResolveImage = (url, type, scale) => "marker" });
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal("Black/true/false", t.Server.State.Find(U, P)!.Label);
    }
}
