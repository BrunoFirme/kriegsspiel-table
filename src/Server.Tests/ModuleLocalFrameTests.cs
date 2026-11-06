using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleLocalFrameTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static string Run(Table t, string lua)
    {
        var r = ModuleTranslator.Translate(lua, Ctx());
        var p = t.Server.State.Find(U, P)!;
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        return p.Label ?? "";
    }

    const string Lua = "function onLoad()\n  local w = self.positionToWorld({x=2, y=0, z=4})\n  local l = self.positionToLocal({x=w.x, y=0, z=w.z})\n"
                       + "  self.setName(string.format('%.3f,%.3f|%.3f,%.3f', w.x, w.z, l.x, l.z))\nend\n";

    [Fact]
    public void A_model_s_local_frame_is_its_mesh_unit_times_its_module_scale()
    {
        using var t = new Table();
        var def = t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf");
        def.LocalUnit = new Size2(0.5, 0.25);
        Assert.Equal("1.000,1.000|2.000,4.000", Run(t, Lua));
    }

    [Fact]
    public void A_flat_piece_keeps_the_size_rule_for_its_local_frame()
    {
        using var t = new Table();
        var def = t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf");
        Assert.Null(def.LocalUnit);
        var u = def.Size.H / 2;
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000},{1:0.000}|2.000,4.000", 2 * u, 4 * u), Run(t, Lua));
    }
}
