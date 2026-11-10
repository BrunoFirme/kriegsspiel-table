using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class StowScriptTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const string Marker = "p-000003";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static void Load(Table t, string lua, bool translated)
    {
        var text = translated ? ModuleTranslator.Translate(lua, Ctx()).Script : lua;
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = text });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
    }

    [Fact]
    public void A_script_stows_a_piece_and_takes_it_out_where_it_says()
    {
        using var t = new Table();
        Load(t, "function on_load()\n  self.stow(board.find('" + Marker + "'))\n  self.set_name(tostring(#self.stowed) .. ':' .. self.stowed[1].id)\nend\n", translated: false);
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal("1:" + Marker, p.Label);
        Assert.Null(t.Server.State.Find(U, Marker));
        Load(t, "function on_load()\n  local m = self.unstow('" + Marker + "', { x = 7, z = 8 }, 45)\n  m.set_name('out')\n  self.set_name(tostring(#self.stowed))\nend\n", translated: false);
        var m = t.Server.State.Find(U, Marker)!;
        Assert.Equal((new Vec2(7, 8), 45.0, "out"), (m.Pos, m.RotDeg, m.Label));
        Assert.Equal("0", p.Label);
    }

    static string Lua(Table t, string code) =>
        t.Server.Request(Table.Host, new RunLua { Target = P, Code = code })
            .Where(o => o.Message is ScriptConsole).SelectMany(o => ((ScriptConsole)o.Message).Lines).LastOrDefault(l => l.Kind is "result" or "error")?.Text ?? "";

    [Fact]
    public void A_script_marks_a_ready_piece_and_releases_and_recalls_it()
    {
        using var t = new Table();
        var m = "board.find('" + Marker + "')";
        Assert.Equal("true", Lua(t, $"local m = {m} self.stow(m) self.set_ready(m) return tostring(self.ready ~= nil)"));
        Assert.Equal(Marker + " false true", Lua(t, "return self.ready.id .. ' ' .. tostring(self.recall()) .. ' ' .. tostring(self.release())"));
        Assert.NotNull(t.Server.State.Find(U, Marker));
        Assert.Equal("false true", Lua(t, "return tostring(self.release()) .. ' ' .. tostring(self.recall())"));
        Assert.Null(t.Server.State.Find(U, Marker));
        Assert.Equal("nil false", Lua(t, "self.set_ready(nil) return tostring(self.ready) .. ' ' .. tostring(self.release())"));
        Assert.Equal("true", Lua(t, $"self.unstow('{Marker}') self.set_ready({m}) return tostring(self.recall())"));
        Assert.Null(t.Server.State.Find(U, Marker));
    }

    [Fact]
    public void A_module_bag_script_puts_and_takes_through_the_same_calls()
    {
        using var t = new Table();
        const string lua = "function onLoad()\n  self.putObject(getObjectFromGUID('" + Marker + "'))\n  local n = self.getQuantity()\n  local list = self.getObjects()\n"
                           + "  local o = self.takeObject({ guid = list[1].guid, position = {x = 5, y = 1, z = 6}, rotation = {x = 0, y = 90, z = 0}, callback_function = function(obj) obj.setName('taken') end })\n"
                           + "  self.setName(n .. '/' .. self.getQuantity() .. '/' .. list[1].index)\nend\n";
        t.Server.State.Find(U, P)!.Holds = true;
        Load(t, lua, translated: true);
        t.Server.Scripts.Tick(1.0);
        var p = t.Server.State.Find(U, P)!;
        var m = t.Server.State.Find(U, Marker)!;
        Assert.Equal("1/0/0", p.Label);
        Assert.Equal((new Vec2(5, 6), 90.0, "taken"), (m.Pos, m.RotDeg, m.Label));
    }

    [Fact]
    public void Undo_brings_a_stowed_piece_back_and_a_taken_one_in()
    {
        using var t = new Table();
        var res = t.Server.Handle(Table.Host, new Stow { Layer = U, Holder = P, Id = Marker });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Null(t.Server.State.Find(U, Marker));
        var undone = t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.State.LastSeq });
        Assert.Empty(undone.Select(o => o.Message).OfType<ErrorMessage>().Select(e => e.Text));
        Assert.NotNull(t.Server.State.Find(U, Marker));
        Assert.Null(t.Server.State.Find(U, P)!.Stowed);
        t.Server.Handle(Table.Host, new Unstow { Layer = U, Holder = P, Id = Marker, Pos = new(3, 3) });
        Assert.Equal("nothing stowed there", Assert.IsType<ErrorMessage>(t.Server.Handle(Table.Host, new Unstow { Layer = U, Holder = P }).Single(o => o.Message is ErrorMessage).Message).Text);
    }
}
