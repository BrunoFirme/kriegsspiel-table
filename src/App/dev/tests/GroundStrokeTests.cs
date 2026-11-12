using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class GroundStrokeTests
{
    const int SettleFrames = 40, CommitFrames = 60, MostFramesToShow = 4;
    const float ViewAcross = 4, BrushRadius = 2, WaterIsBluerBy = 0.04f;
    const double Side = 40, FirstStrokeAt = -15;

    static Color Middle(TestRunner r)
    {
        var shot = r.GetViewport().GetTexture().GetImage();
        return shot.GetPixel(shot.GetWidth() / 2, shot.GetHeight() / 2);
    }

    static bool Water(Color c) => c.B > c.R + WaterIsBluerBy;

    [ScreenTest]
    static async Task A_dab_of_water_shows_under_the_brush_within_a_few_frames(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        var modern = Json.Clone(t.S.View.Lighting);
        modern.MapStyle = MapStyles.Modern;
        t.S.Do(new SetLighting { Lighting = modern });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), ViewAcross);
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "brush";
        cfg.ReliefBrush = BrushRadius;
        await r.Frames(SettleFrames);
        t.Table.Relief.Press(new Vec2(FirstStrokeAt, 0), false, false);
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        Check.That(!Water(Middle(r)), $"the middle is paper before the dab, seen {Middle(r).ToHtml(false)}");
        t.Table.Relief.Press(new Vec2(0, 0), false, false);
        int frames = 0;
        while (frames < SettleFrames && !Water(Middle(r)))
        {
            await r.Frame();
            frames++;
        }
        t.Table.Relief.Release();
        Check.That(frames <= MostFramesToShow, $"the water shows {frames} frames after the dab, seen {Middle(r).ToHtml(false)}");
    }
}
