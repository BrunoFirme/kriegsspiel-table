using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Updates;

namespace KTable.App;

static class TitleFitTests
{
    const int Roomy = 36, Tight = 30, Rows = 7, Rest = 300, Width = 300;
    const int RoomyHeight = Rows * Roomy + Rest, TightHeight = Rows * Tight + Rest;

    static (ScrollContainer room, VBoxContainer column, MenuFit fit, Button[] rows) Menu(TestRunner r, float height)
    {
        var room = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Size = new Vector2(Width, height) };
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Ui.Gap.None);
        room.AddChild(column);
        var rows = Enumerable.Range(0, Rows).Select(_ => new Button { CustomMinimumSize = new Vector2(0, Roomy) }).ToArray();
        foreach (var b in rows) column.AddChild(b);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, Rest) });
        r.AddChild(room);
        var fit = MenuFit.Keep(room, column, rows.Select(b => new MenuFit.Give(b, Roomy, Tight)).ToArray());
        return (room, column, fit, rows);
    }

    static async Task Sized(TestRunner r, ScrollContainer room, float height)
    {
        room.Size = new Vector2(Width, height);
        await r.Frames(3);
    }

    static bool Scrolls(ScrollContainer room) => room.GetVScrollBar().Visible;

    [NodeTest]
    static async Task The_title_menu_tightens_only_when_it_must_and_scrolls_only_when_tight_is_not_enough(TestRunner r)
    {
        var (room, column, fit, rows) = Menu(r, RoomyHeight + 40);
        try
        {
            await r.Frames(3);
            Check.That(!fit.Tight && rows.All(b => b.Size.Y == Roomy) && !Scrolls(room), "with room to spare the rows keep their full height and nothing scrolls");

            await Sized(r, room, (RoomyHeight + TightHeight) / 2);
            Check.That(fit.Tight && rows.All(b => b.Size.Y == Tight), "short of room the rows tighten");
            Check.That(!Scrolls(room), "and once tight it fits, so nothing scrolls");

            await Sized(r, room, TightHeight - 40);
            Check.That(fit.Tight && Scrolls(room), "shorter than even the tight menu, it scrolls");

            await Sized(r, room, RoomyHeight + 40);
            Check.That(!fit.Tight && rows.All(b => b.Size.Y == Roomy) && !Scrolls(room), "given room again the rows open back up");

            var card = new Control { CustomMinimumSize = new Vector2(0, 60) };
            column.AddChild(card);
            await r.Frames(3);
            Check.That(fit.Tight && !Scrolls(room), "a card that appears tightens the menu so it still fits");
            card.QueueFree();
            await r.Frames(3);
            Check.That(!fit.Tight, "and when it goes the menu opens up again");
        }
        finally
        {
            room.QueueFree();
        }
    }

    [NodeTest]
    static async Task An_empty_status_line_takes_no_room_and_shows_once_it_has_words(TestRunner r)
    {
        var column = new VBoxContainer();
        r.AddChild(column);
        try
        {
            var status = new StatusLine();
            column.AddChild(status);
            await r.Frames(1);
            Check.Equal(0f, column.GetCombinedMinimumSize().Y, "an empty status line takes no room");
            status.Text = "Installed the pack";
            status.Text += "\nand one more line";
            await r.Frames(1);
            Check.That(status.Visible && column.GetCombinedMinimumSize().Y > 0, "words show it");
            status.Text = "";
            await r.Frames(1);
            Check.That(!status.Visible && column.GetCombinedMinimumSize().Y == 0, "emptied, it takes no room again");
        }
        finally
        {
            column.QueueFree();
        }
    }

    [NodeTest]
    static async Task The_title_picture_drifts_through_its_shader_and_never_moves_its_box_so_it_glides_instead_of_stepping_whole_pixels(TestRunner r)
    {
        var size = new Vector2(1920, 1080);
        var root = new Control { Size = size };
        var world = new TextureRect { AnchorRight = 1, AnchorBottom = 1 };
        root.AddChild(world);
        r.AddChild(root);
        try
        {
            var box = world.GetRect();
            TitleScreen.Glide(root, world);
            await r.Frames(3);
            Check.Equal(box, world.GetRect(), "the picture's box stays where it was laid");
            var drift = (Vector2)((ShaderMaterial)world.Material).GetShaderParameter(TitleScreen.GlideBy);
            Check.That(drift.DistanceTo(TitleScreen.Drift(size, 0)) < 1, $"the shader carries the drift from its start: {drift}");
            Check.That(TitleScreen.Drift(size, 0.5f) == Vector2.Zero && TitleScreen.Drift(size, 1).IsEqualApprox(-TitleScreen.Drift(size, 0)), "it swings evenly about the middle");
        }
        finally
        {
            root.QueueFree();
        }
    }

    [NodeTest]
    static async Task The_update_line_keeps_to_the_title_column_whatever_the_release_is_called(TestRunner r)
    {
        var column = new VBoxContainer { Size = new Vector2(Width, 0) };
        r.AddChild(column);
        try
        {
            var cases = new[] { "0.9.87-beta", "0.9.87-beta with a subtitle far longer than the column could ever hold" }
                .SelectMany(name => new[] { (name, true, false), (name, true, true), (name, false, false) });
            foreach (var (name, installs, ready) in cases)
            {
                var line = UpdateOffer.Line(new Release(new System.Version(0, 9, 87), name, Releases.PagesAt + "tag/v0.9.87-beta", null), installs, ready, () => { });
                column.AddChild(line);
                await r.Frames(1);
                var shown = line.GetChildren().OfType<Label>().Single();
                Check.Equal((float)TitleStyle.ColumnWidth, line.Size.X, $"'{name}' (installs {installs}, downloaded {ready}) is as wide as the column");
                Check.That(line.GetCombinedMinimumSize().X <= TitleStyle.ColumnWidth, $"'{name}' never asks for more than the column");
                Check.Equal(ready ? $"{name} downloaded" : $"{name} is out", shown.TooltipText, "the tip says what the name stands for");
                line.QueueFree();
                await r.Frames(1);
            }
        }
        finally
        {
            column.QueueFree();
        }
    }
}
