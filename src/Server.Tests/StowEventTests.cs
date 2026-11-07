using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class StowEventTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    const string Lua = "function tryObjectEnterContainer(container, object)\n  return not object.hasTag('Source')\nend\n"
                       + "function onObjectEnterContainer(container, object)\n  container.setName('took ' .. object.getName() .. ' ' .. tostring(object.hasTag('Spare')))\nend\n"
                       + "function onObjectLeaveContainer(container, object)\n  container.setName('gave ' .. object.getName())\nend\n"
                       + "function onLoad()\n  getObjectFromGUID('" + Marker + "').setName('Flag')\n  getObjectFromGUID('" + Marker + "').addTag('Spare')\n  log({ a = 1 }, 'table')\nend\n";

    static Table Prepared()
    {
        var t = new Table();
        var r = ModuleTranslator.Translate(Lua, new TranslateContext { IsGlobal = true, ResolveImage = (url, type, scale) => "marker" });
        Assert.Empty(r.Untranslated);
        var res = t.Server.Handle(Table.Host, new SetGlobalScript { Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Contains("table\\n  a: 1", string.Concat(t.Server.ConsoleOut().Select(o => Json.Serialize(o.Message))));
        return t;
    }

    [Fact]
    public void Putting_a_piece_in_and_taking_it_out_raise_the_bag_events_and_a_script_can_keep_one_out()
    {
        using var t = Prepared();
        var put = t.Server.Handle(Table.Host, new Stow { Layer = U, Holder = P, Id = Marker });
        Assert.DoesNotContain(put, o => o.Message is ErrorMessage);
        Assert.Equal("took Flag true", t.Server.State.Find(U, P)!.Label);
        var back = t.Server.Handle(Table.Host, new Unstow { Layer = U, Holder = P, Id = Marker, Pos = new(2, 2) });
        Assert.DoesNotContain(back, o => o.Message is ErrorMessage);
        Assert.Equal("gave Flag", t.Server.State.Find(U, P)!.Label);
        Assert.Equal("Flag", t.Server.State.Find(U, Marker)!.Label);
        var r = ModuleTranslator.Translate("function onLoad() self.addTag('Source') end", new TranslateContext { ResolveImage = (url, type, scale) => "marker" });
        t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = Marker, Text = r.Script });
        var kept = t.Server.Handle(Table.Host, new Stow { Layer = U, Holder = P, Id = Marker });
        Assert.Contains(kept.Select(o => o.Message).OfType<ErrorMessage>(), e => e.Text.Contains("keeps " + Marker + " out of " + P));
        Assert.NotNull(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void The_locked_property_reads_and_sets_the_lock()
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate("function onLoad()\n  local was = self.locked\n  self.locked = true\n  self.setName(tostring(was) .. '/' .. tostring(self.locked))\nend",
            new TranslateContext { ResolveImage = (url, type, scale) => "marker" });
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal(("false/true", true), (p.Label, p.Locked));
    }
}
