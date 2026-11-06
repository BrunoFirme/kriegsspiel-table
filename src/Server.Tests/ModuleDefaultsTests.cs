using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleDefaultsTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    [Fact]
    public void A_button_or_input_the_script_leaves_unsized_takes_the_size_a_module_gives_it()
    {
        const string lua = "function onLoad()\n  self.createButton({label='A', click_function='f'})\n"
                           + "  self.createButton({label='B', click_function='f', width=100, height=100, font_size=100, color={1,1,1}, font_color={0,0,0}})\n"
                           + "  self.createInput({label='C', input_function='g'})\n  self.createInput({label='D', input_function='g', width=100, height=100, font_size=100})\nend\n"
                           + "function f() end\nfunction g() end\n";
        using var t = new Table();
        var r = ModuleTranslator.Translate(lua, Ctx());
        var p = t.Server.State.Find(U, P)!;
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Equal(4, p.Widgets.Count);
        var (a, b, c, d) = (p.Widgets[0], p.Widgets[1], p.Widgets[2], p.Widgets[3]);
        Assert.Equal((b.W, b.H, b.Font, b.Color, b.TextColor), (a.W, a.H, a.Font, a.Color, a.TextColor));
        Assert.Equal((d.W, d.H, d.Font), (c.W, c.H, c.Font));
        Assert.True(a.W > 0 && a.H > 0 && c.W > 0);
    }

    [Fact]
    public void A_button_with_no_click_function_is_refused_as_modules_expect()
    {
        const string lua = "made = nil\nfunction onLoad()\n  made = self.createButton({label='A'})\n  self.createInput({label='B'})\n  self.createButton({label='C', click_function='f'})\nend\nfunction f() end\n";
        using var t = new Table();
        var r = ModuleTranslator.Translate(lua, Ctx());
        var p = t.Server.State.Find(U, P)!;
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        Assert.Single(p.Widgets);
        Assert.Equal("C", p.Widgets[0].Label);
    }
}
