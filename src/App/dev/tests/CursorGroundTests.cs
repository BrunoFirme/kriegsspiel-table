using System;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class CursorGroundTests
{
    const double Side = 40, Wave = 0.01, CellsAWave = 8;
    const int VertexEvery = 4, Across = 12, SettleFrames = 10;
    const float Distance = 8, Pitch = -0.6f, OnDrawn = 0.005f, DataApart = 0.05f;

    static async Task<OfflineTable> Waved(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        int cols = Ground.MostVertsAcross * VertexEvery + 1;
        var grid = new ReliefGrid(cols, cols);
        for (int y = 0; y < cols; y++)
            for (int x = 0; x < cols; x++)
                grid.H[y * cols + x] = (float)(Wave * (1 + Math.Sin(2 * Math.PI * x / CellsAWave)));
        var (sha, _) = ReliefImport.Save(grid, t.S.Store.Root);
        t.S.Relief.Remember(sha, grid);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        await r.Frames(SettleFrames);
        return t;
    }

    [NodeTest]
    static async Task The_cursor_finds_the_ground_as_it_is_drawn_not_the_finer_relief_under_it(TestRunner r)
    {
        var t = await Waved(r);
        var rig = t.Table.Rig;
        rig.Show(new CameraRig.View(Vector3.Zero, Distance, 0, Pitch, false));
        await r.Frames(SettleFrames);
        var size = rig.GetViewport().GetVisibleRect().Size;
        double apart = 0;
        for (int i = 1; i < Across; i++)
            for (int j = 1; j < Across; j++)
            {
                var screen = new Vector2(size.X * i / Across, size.Y * j / Across);
                if (rig.GroundAt(screen) is not { } hit) continue;
                var on = Coords.FromWorld(hit);
                double drawn = t.S.Ground.DrawnAt(on);
                apart = Math.Max(apart, Math.Abs(t.S.Ground.At(on) - drawn));
                Check.That(Math.Abs(hit.Y - drawn) < OnDrawn, $"the cursor at {screen} meets the ground at height {hit.Y:0.000} where it is drawn at {drawn:0.000}");
            }
        Check.That(apart > DataApart, $"the relief between the drawn corners stands {apart:0.000} off the drawn ground, so the test can tell them apart");
    }
}
