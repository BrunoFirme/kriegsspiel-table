using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting.Translate;

namespace KTable.Server.Tests;

public class OffCentreBoundsTests
{
    const string U = Layers.Umpire;
    const string Line = "p-000090";

    static Table WithLine(double rot)
    {
        var t = new Table();
        var dir = Path.Combine(t.Root, "ranks", "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "rank.obj"), new[]
        {
            "v -0.5 0 -1.06", "v 0.5 0 -1.06", "v 0.5 0 0.03", "v -0.5 0 0.03", "v 9 0 9",
            "v -0.5 0.15 -1.06", "v 0.5 0.15 -1.06", "v 0.5 0.15 0.03", "v -0.5 0.15 0.03",
            "f 1 2 3 4", "f 6 9 8 7",
        });
        var pack = new PackManifest { Id = "ranks", Name = "Ranks" };
        pack.Assets["rank"] = new AssetEntry { File = "assets/rank.obj", Kind = "mesh" };
        t.Store.RegisterDef(pack, new PieceDef { Id = "rank", Name = "Line", Kind = "model", Mesh = "rank", Size = new Size2(1.3, 1.417), Thickness = 0.2 });
        t.Server.Handle(Table.Host, new SpawnPiece { Layer = U, Def = "rank", Id = Line, Pos = new Vec2(10, 5), RotDeg = rot });
        return t;
    }

    static string Run(Table t, string lua)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            var r = ModuleTranslator.Translate(lua, new TranslateContext { ResolveImage = (_, _, _) => "marker" });
            Assert.True(r.Parsed);
            t.Server.Handle(Table.Host, new SetScript { Layer = U, Id = "p-000002", Text = r.Script });
            Assert.DoesNotContain(t.Server.Scripts.Console, l => l.StartsWith("Script error"));
            return t.Server.State.Find(U, "p-000002")!.Label ?? "";
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Bounds_of_a_model_drawn_behind_its_pivot_are_centred_on_the_drawn_model_with_the_offset_from_the_pivot()
    {
        using var t = WithLine(90);
        var label = Run(t, $$"""
            function onLoad()
              local m = getObjectFromGUID('{{Line}}')
              local b, n = m.getBounds(), m.getBoundsNormalized()
              self.setName(string.format('%.2f,%.2f|%.2f,%.2f|%.2f,%.2f', b.center.x, b.center.z, b.offset.x, b.offset.z, n.offset.x, n.offset.z))
            end
            """);
        Assert.Equal("10.67,5.00|0.67,0.00|0.00,0.67", label);
    }

    [Fact]
    public void A_cast_meets_a_model_where_it_is_drawn_and_passes_the_bare_ground_by_its_pivot()
    {
        using var t = WithLine(90);
        var label = Run(t, $$"""
            function onLoad()
              local function n(x) return #Physics.cast({ origin = { x, 5, 5 }, direction = { 0, -1, 0 } }) end
              self.setName(n(11.2) .. ',' .. n(9.5))
            end
            """);
        Assert.Equal("1,0", label);
    }

    [Fact]
    public void The_middle_of_a_model_grows_with_its_scale_and_of_a_plain_piece_is_its_place()
    {
        using var t = WithLine(0);
        t.Server.State.Find(U, Line)!.Scale = 2;
        var line = t.Store.MiddleOnMap(t.Server.State.Find(U, Line)!);
        Assert.Equal(10, line.X, 3);
        Assert.Equal(5 + 0.4725 * 1.417 * 2, line.Z, 3);
        var plain = t.Server.State.Find(U, "p-000003")!;
        Assert.Equal(plain.Pos, t.Store.MiddleOnMap(plain));
    }
}
