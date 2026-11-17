using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class TitlePictureTests
{
    static readonly Color Top = new(0.9f, 0.1f, 0.1f), Bottom = new(0.1f, 0.1f, 0.9f);
    const float Apart = 0.15f;

    static bool Reddish(Color c) => c.R > c.B + Apart;

    [ScreenTest]
    static async Task A_map_picture_on_the_title_has_the_map_s_north_at_the_top_however_its_sheet_is_turned(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        var file = Path.Combine(h.S.Store.Root, "halves.png");
        var img = Image.CreateEmpty(400, 200, false, Image.Format.Rgb8);
        img.FillRect(new Rect2I(0, 0, 400, 100), Top);
        img.FillRect(new Rect2I(0, 100, 400, 100), Bottom);
        img.SavePng(file);
        await AgentRecordTests.Ask(agent, "add_map_sheets", $$"""{"paths":["{{file.Replace("\\", "/")}}"]}""");
        for (int i = 0; i < 600 && h.S.View.Sheets.Count == 0; i++) await r.Frames(1);
        await r.Frames(30);
        var rig = h.Table.Rig;
        foreach (int turn in new[] { 0, 180 })
        {
            var laid = h.S.View.Sheets.Single();
            h.S.Do(new MoveSheet { Id = laid.Id, Pos = laid.Pos, RotDeg = turn });
            var sheet = h.S.View.Sheets.Single();
            rig.Pitch = -Mathf.Pi / 2 + 0.01f;
            rig.Frame(Coords.ToWorld(sheet.Pos), (float)sheet.Size.W * 1.3f);
            await r.Frames(30);
            var north = (Vector2I)rig.Cam.UnprojectPosition(Coords.ToWorld(sheet.Pos + new Vec2(0, sheet.Size.H * 0.3)));
            bool redNorth = Reddish(r.GetViewport().GetTexture().GetImage().GetPixelv(north));
            var title = ScenarioCards.Covering(h.S.Store, new Scenario { Id = "t", Sheets = { sheet } }, new Vector2I(256, 128))!;
            Check.That(Reddish(title.GetPixel(title.GetWidth() / 2, title.GetHeight() / 5)) == redNorth && Reddish(title.GetPixel(title.GetWidth() / 2, title.GetHeight() * 4 / 5)) != redNorth,
                $"turned {turn}°, the title's top is what the table shows at the map's north");
        }
    }
}
