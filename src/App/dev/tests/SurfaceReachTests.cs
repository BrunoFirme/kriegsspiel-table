using System.Collections.Generic;
using System.Threading.Tasks;
using KTable.Client.Hand;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class SurfaceReachTests
{
    const string U = Layers.Umpire;
    const double Step = 0.04, Beyond = 1.5;

    static void Kinds(PackageStore store)
    {
        TiltTests.Prism(store);
        var pack = new PackManifest { Id = "reach", Name = "Reach" };
        store.RegisterDef(pack, new PieceDef { Id = "flag", Name = "Flag", Size = new Size2(0.4, 0.4), Thickness = 0.6 });
        store.RegisterDef(pack, new PieceDef { Id = "unit", Name = "Battalion", Size = new Size2(2, 1), Thickness = 0.2, Parts = new() { new PartDef { Def = "flag", X = 1.5, Z = 0.4, Y = 0.2 } } });
        store.RegisterDef(pack, new PieceDef { Id = "slab", Name = "Slab", Size = new Size2(3, 0.8), Thickness = 0.3 });
    }

    static readonly List<PieceInstance> Shown = new()
    {
        new() { Id = "slab", Def = "slab", Pos = new Vec2(-10, 0), RotDeg = 23 },
        new() { Id = "leaning", Def = "slab", Pos = new Vec2(-5, 0), RotDeg = 140, TiltX = 35, TiltZ = 50 },
        new() { Id = "flagged", Def = "unit", Pos = new Vec2(0, 0), RotDeg = 61 },
        new() { Id = "model", Def = "tri", Pos = new Vec2(5, 0), RotDeg = 37 },
        new() { Id = "leaning model", Def = "tri", Pos = new Vec2(10, 0), RotDeg = 200, TiltX = 25 },
    };

    [NodeTest]
    static async Task No_piece_holds_up_anything_beyond_its_surface_reach(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: Kinds, arrange: g => g.Layers[U].AddRange(Shown));
        await r.Frames(20);
        foreach (var shown in Shown)
        {
            ITablePiece n = t.Node(shown.Id);
            float reach = n.SurfaceReach;
            Check.That(float.IsFinite(reach), $"{shown.Id} has a reach: {reach}");
            int held = 0;
            double half = reach + Beyond;
            for (double x = -half; x <= half; x += Step)
                for (double z = -half; z <= half; z += Step)
                {
                    var at = shown.Pos + new Vec2(x, z);
                    if (n.SurfaceAt(at) == null) continue;
                    held++;
                    Check.That(new Vec2(x, z).Length <= reach, $"{shown.Id} holds a piece up at {x:0.00},{z:0.00}, beyond its reach {reach:0.00}");
                }
            Check.That(held > 0, $"{shown.Id} holds something up somewhere");
        }
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
