using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class LetteringTurnTests
{
    [NodeTest]
    static async Task Module_text_saved_at_turn_zero_reads_upright_from_the_south_as_the_game_shows_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
            store.RegisterDef(new PackManifest { Id = "test.text", Name = "Text" }, new PieceDef { Id = "label", Name = "Label", Size = new Size2(4, 1), Thickness = 0.02 }),
            arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
            {
                new() { Id = "a", Def = "label", Pos = new Vec2(0, 0), RotDeg = 0, Lettering = new KTable.Core.Model.Lettering { Text = "Bull Run campaign" } },
            });
        await r.Frames(3);
        var label = t.Node("a").GetChildren().OfType<Label3D>().FirstOrDefault() ?? throw new TestFailure("the text is drawn live");
        var up = label.GlobalBasis * Vector3.Up;
        var north = Coords.ToWorld(new Vec2(0, 1), 0) - Coords.ToWorld(new Vec2(0, 0), 0);
        Check.That(up.Normalized().Dot(north.Normalized()) > 0.99f, $"the top of the text points north (+Z on the map): {up}");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task Module_text_wears_the_table_face_with_a_hairline_outline_and_leaves_its_formatting_codes_undrawn(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
            store.RegisterDef(new PackManifest { Id = "test.text", Name = "Text" }, new PieceDef { Id = "label", Name = "Label", Size = new Size2(4, 1), Thickness = 0.02 }),
            arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
            {
                new() { Id = "a", Def = "label", Pos = new Vec2(0, 0), Lettering = new KTable.Core.Model.Lettering { Text = "[b]Upper[/b]\n[ff0000]Ford" } },
            });
        await r.Frames(3);
        var label = t.Node("a").GetChildren().OfType<Label3D>().FirstOrDefault() ?? throw new TestFailure("the text is drawn live");
        Check.That(label.Font == Fonts.Widget && label.OutlineSize == 1, "the text wears the table face with a hairline outline");
        Check.Equal("Upper\nFord", label.Text, "formatting codes are not drawn");
        t.Table.QueueFree();
    }

    const int FontPixels = 64, ScreenPixelsToOne = 2, ViewPixels = 220, SettleFrames = 4;
    const float InkFrom = 0.5f, PixelsOff = 1.5f;

    static async Task<(float Top, float Foot)> InkRows(TestRunner r, string text)
    {
        var port = new SubViewport
        {
            Size = new Vector2I(ViewPixels, ViewPixels) * ScreenPixelsToOne, OwnWorld3D = true, World3D = new World3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var night = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
        port.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = ViewPixels, Position = new Vector3(0, 0, 10), Environment = night, Current = true });
        var label = ModuleText.Label(new KTable.Core.Model.Lettering { Text = text, Size = FontPixels, UnitsPerPixel = 1 }, 1, Colors.White);
        label.RotationDegrees = Vector3.Zero;
        port.AddChild(label);
        r.AddChild(port);
        await r.Frames(SettleFrames);
        var shot = port.GetTexture().GetImage();
        port.QueueFree();
        int top = -1, foot = -1;
        for (int y = 0; y < shot.GetHeight(); y++)
            for (int x = 0; x < shot.GetWidth(); x++)
            {
                if (shot.GetPixel(x, y).G < InkFrom) continue;
                if (top < 0) top = y;
                foot = y;
                break;
            }
        if (top < 0) throw new TestFailure($"no ink was drawn for \"{text}\"");
        float middle = shot.GetHeight() / 2f;
        return ((middle - top) / ScreenPixelsToOne, (middle - foot - 1) / ScreenPixelsToOne);
    }

    [ScreenTest]
    static async Task Module_text_sets_a_bracket_in_the_middle_of_its_line_and_its_lines_a_font_size_apart_as_the_game_sets_them(TestRunner r)
    {
        var one = await InkRows(r, ")");
        Check.That(Mathf.Abs(one.Top + one.Foot) / 2 < PixelsOff, $"a bracket's ink is centred on its line: top {one.Top:0.#} px above the middle, foot {one.Foot:0.#}");

        var two = await InkRows(r, ")\n)");
        float tall = one.Top - one.Foot;
        Check.That(Mathf.Abs(two.Top - two.Foot - tall - FontPixels) < PixelsOff, $"two lines stand a font size apart: ink {two.Top - two.Foot:0.#} px tall, one bracket {tall:0.#}");
        Check.That(Mathf.Abs(two.Top + two.Foot) / 2 < PixelsOff, $"two lines are centred together: top {two.Top:0.#} px above the middle, foot {two.Foot:0.#}");
    }
}
