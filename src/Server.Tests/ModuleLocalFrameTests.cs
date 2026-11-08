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

    const string Offsets = "function at(v)\n  local w = self.positionToWorld(v)\n  local p = self.getPosition()\n"
                           + "  return string.format('%.3f,%.3f,%.3f', w.x - p.x + 0.0001, w.y - p.y + 0.0001, w.z - p.z + 0.0001)\nend\n";

    [Fact]
    public void A_model_turned_over_about_its_front_back_axis_lies_upside_down_and_mirrors_its_local_frame_as_the_game_does()
    {
        using var t = new Table();
        var def = t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf");
        (def.Kind, def.LocalUnit) = ("model", new Size2(1, 1));
        var lua = Offsets + "function onLoad()\n  self.setRotation({0, 0, 180})\n  local r = self.getRotation()\n"
                  + "  self.setName(at({1, 0.5, 2}) .. '|' .. string.format('%.0f,%.0f,%.0f', r.x, r.y, r.z))\nend\n";
        Assert.Equal("-1.000,-0.500,2.000|0,0,180", Run(t, lua));
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal((false, 0.0, 180.0), (p.FaceDown, p.TiltX, p.TiltZ));
    }

    [Fact]
    public void A_flat_piece_set_leaning_reports_its_lean_and_its_local_frame_leans_with_it()
    {
        using var t = new Table();
        t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf").LocalUnit = new Size2(1, 1);
        var lua = Offsets + "function onLoad()\n  self.setRotation({30, 90, 0})\n  local r = self.getRotation()\n"
                  + "  local back = self.positionToLocal(self.positionToWorld({0.4, 0.3, -0.7}))\n"
                  + "  self.setName(at({0, 1, 0}) .. '|' .. string.format('%.0f,%.0f,%.0f|%.3f,%.3f,%.3f', r.x, r.y, r.z, back.x, back.y, back.z))\nend\n";
        Assert.Equal("0.500,0.866,0.000|30,90,0|0.400,0.300,-0.700", Run(t, lua));
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal((false, 30.0, 0.0, 90.0), (p.FaceDown, p.TiltX, p.TiltZ, p.RotDeg));
    }

    [Fact]
    public void A_flat_piece_turned_over_about_its_left_right_axis_lies_face_down_facing_the_other_way_with_no_lean()
    {
        using var t = new Table();
        Run(t, "function onLoad() self.setRotation({180, 0, 0}) end");
        var p = t.Server.State.Find(U, P)!;
        Assert.Equal((true, 0.0, 0.0, 180.0), (p.FaceDown, p.TiltX, p.TiltZ, p.RotDeg));
    }

    [Fact]
    public void A_model_spawned_turned_over_comes_upside_down_and_a_lean_is_one_undo_away()
    {
        using var t = new Table();
        t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf").Kind = "model";
        Run(t, "function onLoad()\n  local s = spawnObject({ type = 'Custom_Model', position = {3, 1, 3}, rotation = {0, 90, 180} })\n  s.setCustomObject({ image = 'inf' })\nend\n");
        var spawned = t.Server.State.Layer(U).Last(q => q.Def == "inf");
        Assert.Equal((false, 0.0, 180.0, 90.0), (spawned.FaceDown, spawned.TiltX, spawned.TiltZ, spawned.RotDeg));
        t.Server.Handle(Table.Host, new TiltPiece { Layer = U, Id = spawned.Id, X = 15, Z = 400 });
        Assert.Equal((15.0, 40.0), (spawned.TiltX, spawned.TiltZ));
        t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Equal((0.0, 180.0), (spawned.TiltX, spawned.TiltZ));
    }
}
