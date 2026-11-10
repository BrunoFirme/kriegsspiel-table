using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class GroundCardTests
{
    static readonly Color West = new(0.9f, 0.1f, 0.1f), East = new(0.1f, 0.1f, 0.9f);

    static Scenario GroundOnly(double rotDeg)
    {
        var tag = Guid.NewGuid().ToString("N");
        var sheet = new SheetDef { Id = "s-000002", Name = "Ground", Package = "scenarios/ground-" + tag, Size = new Size2(120, 60), RotDeg = rotDeg, Relief = tag };
        return new Scenario { Id = "local.ground-" + tag, Sheets = { sheet } };
    }

    static string KeepGround(SheetDef sheet)
    {
        var img = Image.CreateEmpty(2000, 1000, false, Image.Format.Rgb8);
        img.FillRect(new Rect2I(0, 0, 1000, 1000), West);
        img.FillRect(new Rect2I(1000, 0, 1000, 1000), East);
        var file = GroundCard.FileFor(sheet);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        img.SaveJpg(file);
        return file;
    }

    static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 0.15f && Math.Abs(a.B - b.B) < 0.15f;

    [NodeTest]
    static async Task A_scenario_of_painted_ground_shows_the_ground_drawn_at_its_table_on_its_card(TestRunner r)
    {
        var store = new PackageStore(Path.Combine(Path.GetTempPath(), "ktable-ground-cards", Guid.NewGuid().ToString("N")));
        var s = GroundOnly(0);
        Check.That(ScenarioCards.ThumbImage(store, s, 512) == null, "no card before the ground was drawn");
        var file = KeepGround(s.Sheets[0]);
        try
        {
            var card = ScenarioCards.ThumbImage(store, s, 512);
            Check.That(card != null, "the card shows the ground drawn at the table");
            Check.Equal(512, card!.GetWidth(), "the card is no wider than asked");
            Check.Equal(256, card.GetHeight(), "the card keeps the ground's shape");
            Check.That(Near(card.GetPixel(10, 128), West) && Near(card.GetPixel(500, 128), East), "the card is the ground as kept");
        }
        finally { File.Delete(file); }
        await r.Frames(1);
    }

    [NodeTest]
    static async Task Ground_turned_to_face_the_top_is_turned_on_its_card_as_a_map_picture_is(TestRunner r)
    {
        var store = new PackageStore(Path.Combine(Path.GetTempPath(), "ktable-ground-cards", Guid.NewGuid().ToString("N")));
        var s = GroundOnly(180);
        var file = KeepGround(s.Sheets[0]);
        try
        {
            var card = ScenarioCards.ThumbImage(store, s, 512);
            Check.That(card != null && Near(card.GetPixel(10, 128), East) && Near(card.GetPixel(500, 128), West), "the card is turned half round");
        }
        finally { File.Delete(file); }
        await r.Frames(1);
    }

    [NodeTest]
    static async Task The_ground_card_covers_the_cropped_part_at_the_sheet_shape(TestRunner r)
    {
        var sheet = new SheetDef { Id = "s-1", Package = "scenarios/a", Size = new Size2(200, 100), Crop = new FaceBox { X = 0.25, Y = 0.1, W = 0.5, H = 0.8 } };
        var spec = GroundCard.For(sheet);
        Check.Equal(new Vector2I(GroundCard.Width, GroundCard.Width / 2), spec.Size, "the picture has the sheet's shape");
        Check.That(spec.Uv.IsEqualApprox(new Rect2(0.25f, 0.1f, 0.5f, 0.8f)), $"the picture covers the crop: {spec.Uv}");
        Check.That(GroundCard.FileFor(sheet) != GroundCard.FileFor(new SheetDef { Id = "s-1", Package = "scenarios/b" }), "each scenario's ground has its own card");
        await r.Frames(1);
    }
}
