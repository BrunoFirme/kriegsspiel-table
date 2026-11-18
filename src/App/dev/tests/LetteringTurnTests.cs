using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

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
                new() { Id = "a", Def = "label", Pos = new Vec2(0, 0), Lettering = new KTable.Core.Model.Lettering { Text = "[ff0000][b]Upper[/b]\nFord" } },
            });
        await r.Frames(3);
        var label = t.Node("a").GetChildren().OfType<Label3D>().FirstOrDefault() ?? throw new TestFailure("the text is drawn live");
        Check.That(label.Font == Fonts.Widget && label.OutlineSize == 1, "the text wears the table face with a hairline outline");
        Check.Equal("Upper\nFord", label.Text, "formatting codes are not drawn");
        t.Table.QueueFree();
    }

    static List<Label3D> Inked(PieceNode n) => n.FindChildren("*", nameof(Label3D), recursive: true, owned: false).OfType<Label3D>().Where(l => l.Font == Fonts.Widget).ToList();

    static string Inks(List<Label3D> labels) => string.Join(" | ", labels.Select(l => $"{l.Text.Trim()} #{l.Modulate.ToHtml(false)}"));

    [NodeTest]
    static async Task Module_text_takes_its_colour_code_draws_mixed_colours_run_by_run_and_a_tint_replaces_them_until_reset(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
            store.RegisterDef(new PackManifest { Id = "test.text", Name = "Text" }, new PieceDef { Id = "label", Name = "Label", Size = new Size2(4, 1), Thickness = 0.02 }),
            arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
            {
                new() { Id = "a", Def = "label", Pos = new Vec2(0, 0), Lettering = new KTable.Core.Model.Lettering { Text = "[FF0000]Hold[-]", Color = "#000000" } },
                new() { Id = "b", Def = "label", Pos = new Vec2(0, 3), Lettering = new KTable.Core.Model.Lettering { Text = "Upper [00FF00]Ford", Color = "#000000" } },
            });
        await r.Frames(3);
        Check.Equal("Hold #ff0000", Inks(Inked(t.Node("a"))), "a leading colour code colours the whole text");
        var mixed = Inked(t.Node("b"));
        Check.Equal("Upper #000000 | Ford #00ff00", Inks(mixed), "mixed colours draw each run in its own colour, uncoded letters in the text's colour");
        Check.That(mixed[1].Position.X > mixed[0].Position.X && Mathf.IsEqualApprox(mixed[0].Position.Y, mixed[1].Position.Y), $"the runs stand side by side on one line: {mixed[0].Position} and {mixed[1].Position}");
        t.S.Do(new SetTint { Layer = Layers.Umpire, Id = "a", Value = "#FFFFFF" });
        t.S.Do(new SetTint { Layer = Layers.Umpire, Id = "b", Value = "#0000FF" });
        await r.Frames(3);
        Check.Equal("Hold #ffffff", Inks(Inked(t.Node("a"))), "a tint replaces the code's colour");
        Check.Equal("Upper Ford #0000ff", Inks(Inked(t.Node("b"))), "a tint turns mixed colours into one, dark text included");
        Check.That(t.Node("b").TintShown().IsEqualApprox(Colors.Blue), "the tint row shows the tint the text wears");
        t.S.Do(new SetTint { Layer = Layers.Umpire, Id = "a", Value = null });
        await r.Frames(3);
        Check.Equal("Hold #ff0000", Inks(Inked(t.Node("a"))), "reset brings back the module's colour");
        Check.That(t.Node("a").TintShown().IsEqualApprox(Colors.Red), "and the tint row shows it");
        t.Table.QueueFree();
    }

    const int FontPixels = 64, ScreenPixelsToOne = 2, ViewPixels = 220, SettleFrames = 4;
    const float InkFrom = 0.5f, PixelsOff = 1.5f;

    static async Task<(float Top, float Foot, float Left, float Right)> InkRows(TestRunner r, string text)
    {
        var port = new SubViewport
        {
            Size = new Vector2I(ViewPixels, ViewPixels) * ScreenPixelsToOne, OwnWorld3D = true, World3D = new World3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var night = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
        port.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = ViewPixels, Position = new Vector3(0, 0, 10), Environment = night, Current = true });
        var label = ModuleText.Drawn(new KTable.Core.Model.Lettering { Text = text, Size = FontPixels, UnitsPerPixel = 1 }, 1, Colors.White, null);
        label.RotationDegrees = Vector3.Zero;
        port.AddChild(label);
        r.AddChild(port);
        await r.Frames(SettleFrames);
        var shot = port.GetTexture().GetImage();
        port.QueueFree();
        int top = -1, foot = -1, left = shot.GetWidth(), right = -1;
        for (int y = 0; y < shot.GetHeight(); y++)
            for (int x = 0; x < shot.GetWidth(); x++)
            {
                if (shot.GetPixel(x, y).V < InkFrom) continue;
                if (top < 0) top = y;
                foot = y;
                (left, right) = (Math.Min(left, x), Math.Max(right, x));
            }
        if (top < 0) throw new TestFailure($"no ink was drawn for \"{text}\"");
        float middle = shot.GetHeight() / 2f, centre = shot.GetWidth() / 2f;
        return ((middle - top) / ScreenPixelsToOne, (middle - foot - 1) / ScreenPixelsToOne, (left - centre) / ScreenPixelsToOne, (right + 1 - centre) / ScreenPixelsToOne);
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

    [ScreenTest]
    static async Task Module_text_in_mixed_colours_covers_the_same_ground_as_the_same_text_in_one(TestRunner r)
    {
        foreach (var (one, mixed) in new[] { ("Up Fo", "Up [00FF00]Fo"), ("Ho\nthe", "Ho\n[FF0000]t[-]he") })
        {
            var plain = await InkRows(r, one);
            var coloured = await InkRows(r, mixed);
            Check.That(Mathf.Abs(plain.Top - coloured.Top) < PixelsOff && Mathf.Abs(plain.Foot - coloured.Foot) < PixelsOff
                && Mathf.Abs(plain.Left - coloured.Left) < PixelsOff && Mathf.Abs(plain.Right - coloured.Right) < PixelsOff,
                $"\"{mixed}\" inks where \"{one}\" does: {coloured} against {plain}");
        }
    }
}
