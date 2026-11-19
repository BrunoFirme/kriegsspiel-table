using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class ModuleScaleTests
{
    const string U = Layers.Umpire;
    const string P = "p-000002";
    const double Near = 1e-9;

    static void AsModel(Table t)
    {
        var def = t.Store.Pack("testpack")!.Pieces.Single(d => d.Id == "inf");
        (def.Kind, def.HeightScale) = ("model", 1);
    }

    static PieceInstance Run(Table t, string lua, bool translated = true)
    {
        var text = translated ? ModuleTranslator.Translate(lua, new TranslateContext()).Script : lua;
        Assert.DoesNotContain(t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = P, Text = text }), o => o.Message is ErrorMessage);
        return t.Server.State.Find(U, P)!;
    }

    const string Frontage = """
        steps = 6
        max_steps = 6
        min_steps = 0
        function scaleUp()
          self.Scale({x=1.11,y=1,z=1})
        end
        function scaleDown()
          self.Scale({x=0.9,y=1,z=1})
        end
        function addSteps()  if steps == nil then steps = 6 end
          steps=steps+1
          if steps > max_steps then steps = max_steps
          elseif steps <= max_steps then scaleUp()
          end
        end
        function reduceSteps()  if steps == nil then steps = 6 end
          steps=steps-1
          if steps < min_steps then steps = min_steps
          elseif steps >= min_steps then scaleDown()
          end
        end
        function onLoad()
          self.addContextMenuItem("   Increase +", addSteps)
          self.addContextMenuItem("   Reduce -", reduceSteps)
        end
        """;

    [Fact]
    public void A_module_scaling_a_model_across_only_widens_it_and_keeps_its_depth_and_height()
    {
        using var t = new Table();
        AsModel(t);
        var p = Run(t, Frontage);
        int reduce = p.MenuItems.FindIndex(m => m.Label == "   Reduce -") + 1, increase = p.MenuItems.FindIndex(m => m.Label == "   Increase +") + 1;
        t.Server.Handle(Table.Host, new PickMenuItem { Layer = U, Id = P, Index = reduce });
        t.Server.Handle(Table.Host, new PickMenuItem { Layer = U, Id = P, Index = reduce });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(1, p.Scale);
        Assert.Equal(0.81, p.WideBy, 9);
        Assert.Equal(1, p.DeepBy);
        Assert.Equal(1, p.WideBy * p.TallBy, 9);
        t.Server.Handle(Table.Host, new PickMenuItem { Layer = U, Id = P, Index = increase });
        p = t.Server.State.Find(U, P)!;
        Assert.Equal(0.81 * 1.11, p.WideBy, 9);
        Assert.Equal(1, p.WideBy * p.TallBy, 9);
    }

    [Fact]
    public void A_flat_piece_scaled_across_only_keeps_its_thickness_and_a_missing_side_stays_as_it_was()
    {
        using var t = new Table();
        var p = Run(t, "function onLoad()\n  self.scale({x=2,y=1,z=1})\n  self.scale({x=1,y=1})\nend\n");
        Assert.Equal((1d, 2d, 1d, 1d), (p.Scale, p.WideBy, p.DeepBy, p.TallBy));
    }

    [Fact]
    public void An_even_scale_still_resizes_the_whole_piece_and_keeps_a_stretch_it_had()
    {
        using var t = new Table();
        var p = Run(t, "function onLoad()\n  self.scale({x=1.5,y=1,z=1})\n  self.scale(2)\n  self.scale({x=2,y=2,z=2})\nend\n");
        Assert.Equal((4d, 1.5), (p.Scale, p.WideBy));
        Assert.Equal(1, p.DeepBy);
    }

    [Fact]
    public void A_stretch_stays_within_the_table_s_limits_and_a_factor_of_nothing_is_refused()
    {
        using var t = new Table();
        AsModel(t);
        var p = Run(t, "function on_load()\n  self.stretch_by(100, 1, 0.001)\nend\n", translated: false);
        Assert.Equal((PieceInstance.MostStretch, PieceInstance.LeastStretch), (p.WideBy, p.DeepBy));
        Assert.True(p.TallBy >= PieceInstance.LeastStretch - Near && p.TallBy <= PieceInstance.MostStretch + Near);
        Run(t, "function on_load()\n  self.stretch_by(0, 1, 1)\nend\n", translated: false);
        Assert.Contains(t.Server.Scripts.Console, l => l.Contains("stretch_by: a factor must be above 0"));
        Assert.Equal(PieceInstance.MostStretch, t.Server.State.Find(U, P)!.WideBy);
    }
}
