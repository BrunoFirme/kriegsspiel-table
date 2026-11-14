using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GroundDepthTests
{
    const double Side = 1200, UnitsPerMetre = 0.02;
    const float DabRadius = 2, WideAcross = 8, CloseAcross = 0.3f, DryBy = 3, WaterIsBluerBy = 0.04f, Plateau = 0.008f, LookingDown = -1.5f;
    const int SettleFrames = 240, PatchPx = 6;
    static readonly Vec2 At = new(250, -180);

    static bool Water(Color c) => c.B > c.R + WaterIsBluerBy;

    static Color Middle(TestRunner r)
    {
        var shot = r.GetViewport().GetTexture().GetImage();
        int cx = shot.GetWidth() / 2, cy = shot.GetHeight() / 2, count = 0;
        float red = 0, green = 0, blue = 0;
        for (int y = cy - PatchPx; y <= cy + PatchPx; y++)
            for (int x = cx - PatchPx; x <= cx + PatchPx; x++)
            {
                var c = shot.GetPixel(x, y);
                (red, green, blue, count) = (red + c.R, green + c.G, blue + c.B, count + 1);
            }
        return new Color(red / count, green / count, blue / count);
    }

    static Vector2? WaterMiddle(TestRunner r)
    {
        var shot = r.GetViewport().GetTexture().GetImage();
        var sum = Vector2.Zero;
        int count = 0;
        for (int y = 0; y < shot.GetHeight(); y++)
            for (int x = 0; x < shot.GetWidth(); x++)
                if (Water(shot.GetPixel(x, y))) (sum, count) = (sum + new Vector2(x, y), count + 1);
        return count == 0 ? null : sum / count;
    }

    static async Task Look(OfflineTable t, TestRunner r, Vector3 at, float across)
    {
        t.Table.Rig.Frame(at, across);
        await r.Frames(SettleFrames);
    }

    [ScreenTest]
    static async Task A_close_look_at_a_big_map_shows_its_finest_ground_in_the_right_place(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        System.Array.Fill(flat.H, Plateau);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = UnitsPerMetre });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "brush";
        cfg.ReliefBrush = DabRadius;
        await Look(t, r, Coords.ToWorld(At), WideAcross);
        t.Table.Relief.Press(At, false, false);
        t.Table.Relief.Release();
        await r.Frames(SettleFrames);
        var dab = WaterMiddle(r);
        Check.That(dab != null, "the dab shows from afar");
        var cam = t.Table.Rig.Cam!;
        var hit = new Plane(Vector3.Up, 0).IntersectsRay(cam.ProjectRayOrigin(dab!.Value), cam.ProjectRayNormal(dab.Value));
        Check.That(hit != null, "the dab's middle lies on the ground");
        t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Pitch = LookingDown;
        await Look(t, r, hit!.Value, CloseAcross);
        Check.That(GroundTiles.DeepestBaked > GroundTiles.FullLevels, $"the close look drew tiles {GroundTiles.DeepestBaked} levels deep, past the {GroundTiles.FullLevels} kept for the whole map");
        Check.That(GroundTiles.Relaxed == 0, $"the closest look over high ground is drawn unsoftened, softened by {GroundTiles.Relaxed} levels");
        Check.That(Water(Middle(r)), $"close to, the dab's middle is water: {Middle(r).ToHtml(false)}");
        await Look(t, r, hit.Value + new Vector3(DryBy, 0, 0), CloseAcross);
        Check.That(!Water(Middle(r)), $"close to, the ground beside the dab is dry: {Middle(r).ToHtml(false)}");
    }
}
