using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleVectorColorTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";

    static TranslateContext Ctx() => new() { ResolveImage = (url, type, scale) => "marker" };

    static string Run(string body)
    {
        using var t = new Table();
        var r = ModuleTranslator.Translate("function onLoad()\n" + body + "\nend\n", Ctx());
        var res = t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = r.Script });
        Assert.DoesNotContain(res, o => o.Message is ErrorMessage);
        return t.Server.State.Find(U, P)!.Label ?? "";
    }

    [Fact]
    public void Vectors_index_by_number_and_name_and_rotate_as_modules_expect()
    {
        const string lua = "  local v = Vector(1, 2, 3)\n  v[2] = 5\n  local x, y, z = v:get()\n  local f = Vector.new(0, 0, 1):rotateOver('y', 90)\n"
                           + "  local d = Vector.between(Vector(0, 0, 0), Vector(3, 0, 4))\n  self.setName(string.format('%g,%g,%g|%.0f,%.0f|%g|%g', x, y, z, f.x, f.z, d:magnitude(), v[1]))";
        Assert.Equal("1,5,3|1,0|5|1", Run(lua));
    }

    [Fact]
    public void Vector_methods_change_the_vector_in_place_as_modules_expect()
    {
        const string lua = "  local v = Vector(1, 0, 0)\n  v:add(Vector(0, 2, 0)):scale(2):setAt('z', 4)\n  local n = v:copy():normalize()\n"
                           + "  local c = Vector(1, 0, 0):cross(Vector(0, 1, 0))\n  local same = Vector(1, 0, 0):equals(Vector(1.001, 0, 0))\n"
                           + "  self.setName(string.format('%s|%.2f|%s|%s|%s', tostring(v), n:magnitude(), tostring(c), tostring(same), tostring(Vector(1, 0, 0):heading('y'))))";
        Assert.Equal("Vector: { 2, 4, 4 }|1.00|Vector: { 0, 0, 1 }|true|90", Run(lua));
    }

    [Fact]
    public void Colours_come_from_names_and_hex_and_go_back_to_them()
    {
        const string lua = "  local c = Color.fromString('Red')\n  local r, g, b, a = c:get()\n  local h = Color(1, 0, 0):toHex()\n  local k = Color.fromHex('#00FF00'):toString()\n"
                           + "  local t = Color.Blue:copy():setAt('a', 0.5)\n  local m = Color({1, 1, 1}):lerp(Color(0, 0, 0), 0.5)\n"
                           + "  self.setName(string.format('%g,%g,%g,%g|%s|%s|%g|%g|%s', r, g, b, a, h, tostring(k), t.a, m.r, tostring(Color.Red:equals(c))))";
        Assert.Equal("0.856,0.1,0.094,1|ff0000|nil|0.5|0|true", Run(lua));
    }

    [Fact]
    public void A_colour_from_a_tint_keeps_its_methods_through_set_colour_tint()
    {
        const string lua = "  self.setColorTint(Color.fromString('Green'))\n  local c = self.getColorTint():setAt('a', 1)\n  self.setName((c:toString() or '?') .. '/' .. c.a)";
        Assert.Equal("Green/1", Run(lua));
    }
}
