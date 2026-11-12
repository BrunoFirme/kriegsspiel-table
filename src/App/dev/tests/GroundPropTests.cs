using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GroundPropTests
{
    const string U = Layers.Umpire;
    const float Snug = 0.012f, Below = 0.001f;
    const int RidgeCells = 40, LoadFrames = 120;

    static async Task<OfflineTable> Ridge(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        var grid = ReliefGrid.Flat(40, 40);
        ReliefBrush.Stroke(grid, new[] { (0.2, 0.5), (0.8, 0.5) }, RidgeCells, new Brush(BrushMode.Raise, 0, 0.6));
        var (sha, _) = ReliefImport.Save(grid, t.S.Store.Root);
        t.S.Relief.Remember(sha, grid);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(40, 40), Relief = sha, Height = 1 } });
        await r.Seconds(0.5);
        return t;
    }

    static Vec2 Steepest(Ground g) => Enumerable.Range(-30, 61).Select(i => new Vec2(0, i * 0.25)).MaxBy(g.SlopeAt);

    static async Task<PieceNode> Lay(TestRunner r, OfflineTable t, string name, string id, Vec2 at)
    {
        t.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf(name), Id = id, Pos = at });
        for (int i = 0; i < LoadFrames && t.Table.Pieces.NodeOf(U, id) is not { Fit.Fitted: true }; i++) await r.Frame();
        var n = t.Node(id);
        Check.That(n.Fit.Fitted, $"the {name} lies on the ground");
        return n;
    }

    static void HugsTheGround(PieceNode n, Ground g, string what)
    {
        var points = n.WorldFaces() ?? throw new TestFailure("the model has no faces");
        var middle = n.Position;
        foreach (var (sx, sz) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
        {
            var quarter = points.Where(p => Mathf.Sign(p.X - middle.X) == sx && Mathf.Sign(p.Z - middle.Z) == sz).ToList();
            Check.That(quarter.Count > 0, $"{what}: the model reaches every quarter round its middle");
            var gaps = quarter.Select(p => p.Y - (float)g.DrawnAt(Coords.FromWorld(p))).ToList();
            Check.That(gaps.Min() > -Below, $"{what}: nothing sinks under the drawn ground in the {sx},{sz} quarter: {gaps.Min():0.0000}");
            Check.That(gaps.Min() < Snug, $"{what}: its foot touches the ground in the {sx},{sz} quarter, not {gaps.Min():0.0000} above it");
        }
    }

    [NodeTest]
    static async Task A_crater_on_a_hillside_bends_to_the_ground_under_every_part_of_it_and_again_where_it_is_moved(TestRunner r)
    {
        var t = await Ridge(r);
        var g = t.S.Ground;
        var at = Steepest(g);
        var crater = await Lay(r, t, "Large crater", "c1", at);
        float across = (float)(g.DrawnAt(at + new Vec2(0, crater.Footprint.Y / 2)) - g.DrawnAt(at - new Vec2(0, crater.Footprint.Y / 2)));
        Check.That(Mathf.Abs(across) > 0.1f, $"the slope under the crater is real: it rises {across:0.000} across it");
        HugsTheGround(crater, g, "laid on the slope");

        var flatter = new Vec2(-6, 9);
        t.S.Do(new MovePiece { Layer = U, Id = "c1", Pos = flatter, RotDeg = 40, Y = 0 });
        await r.Seconds(1.2);
        HugsTheGround(crater, g, "moved and turned");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Ground_props_throw_shadows_only_from_their_raised_parts(TestRunner r)
    {
        var t = await Ridge(r);
        var at = Steepest(t.S.Ground);
        var crater = await Lay(r, t, "Shell crater", "c1", at);
        var bank = await Lay(r, t, "Breastwork", "b1", at + new Vec2(5, 0));
        Check.That(!crater.Fit.CastsFromGround && !bank.Fit.CastsFromGround, "the earth on the ground throws no shadow of its own");
        Check.Equal(0, crater.Fit.RaisedShadowFaces, "a crater has nothing raised enough to throw one");
        Check.That(bank.Fit.RaisedShadowFaces > 0, "a breastwork's bank and gabions still throw theirs");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
