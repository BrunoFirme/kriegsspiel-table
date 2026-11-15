using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class UiScaleTests
{
    static readonly float[] Sizes = { 0.7f, 0.75f, 1f, 1.25f, 1.5f, 2.25f };

    static int At(float design, float size) => (int)MathF.Round(design * size, MidpointRounding.AwayFromZero);

    static async Task Sized(TestRunner r, float scale, Func<Task> check)
    {
        Ui.InstallTheme();
        Ui.Watch(r.GetTree());
        float was = Ui.Scale;
        try
        {
            Ui.SetScale(scale, r.GetTree().Root);
            await check();
        }
        finally { Ui.SetScale(was, r.GetTree().Root); }
    }

    [NodeTest]
    static async Task Every_interface_size_draws_fonts_boxes_and_icons_at_that_size_and_never_stretches_the_canvas(TestRunner r)
    {
        foreach (float size in Sizes)
            await Sized(r, size, () =>
            {
                var theme = ThemeDB.GetDefaultTheme();
                Check.Equal(1f, r.GetTree().Root.ContentScaleFactor, $"at {size} the window shows its pixels one to one");
                Check.Equal(At(Ui.BaseFontSize, size), theme.DefaultFontSize, $"at {size} text is laid out at the size it is drawn");
                Check.Equal(At(10, size), (int)theme.GetStylebox("normal", "Button").ContentMarginLeft, $"at {size} a button's padding");
                Check.Equal(At(6, size), theme.GetConstant("separation", "VBoxContainer"), $"at {size} the gap between rows");
                Check.Equal(At(34, size), theme.GetConstant("title_height", "Window"), $"at {size} a window's title bar");
                Check.Equal(At(16, size), theme.GetIcon("checked", "CheckBox").GetWidth(), $"at {size} the tick's picture");
                Check.That(theme.GetIcon("arrow", "OptionButton") is DpiTexture arrow && Mathf.IsEqualApprox(arrow.BaseScale, size), $"at {size} the engine's own icons are drawn at that size");
                Check.That(new[] { Ui.Sans(), Ui.SansStrong(), Ui.SansItalic(), Ui.Serif() }.All(f => f is not FontFile { MultichannelSignedDistanceField: true }), $"at {size} letters keep their hinting");
                return Task.CompletedTask;
            });
    }

    static (HBoxContainer Row, Label Name, StyleBoxFlat Box) Row()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        var name = new Label { Text = "Name", CustomMinimumSize = Ui.Px(100, 20) };
        name.AddThemeFontSizeOverride("font_size", Ui.Px(12));
        var box = Ui.Box(Colors.Black, 3, 10, 4);
        name.AddThemeStyleboxOverride("normal", box);
        row.AddChild(name);
        return (row, name, box);
    }

    static void Shows(HBoxContainer row, Label name, StyleBoxFlat box, float size)
    {
        Check.Equal(At(Ui.Gap.Group, size), row.GetThemeConstant("separation"), $"at {size} the row's gap");
        Check.Equal(new Vector2(At(100, size), At(20, size)), name.CustomMinimumSize, $"at {size} the label's least size");
        Check.Equal(At(12, size), name.GetThemeFontSize("font_size"), $"at {size} the label's letters");
        Check.Equal(At(10, size), (int)box.ContentMarginLeft, $"at {size} the box's padding");
        Check.Equal(At(3, size), box.CornerRadiusTopLeft, $"at {size} the box's corners");
    }

    [NodeTest]
    static async Task Changing_the_size_redraws_what_is_open_at_the_new_size_and_back_without_drift(TestRunner r)
    {
        await Sized(r, 1f, async () =>
        {
            var (row, name, box) = Row();
            r.AddChild(row);
            await r.Frame();
            foreach (float size in new[] { 1.5f, 0.75f, 1.25f, 0.7f, 1.45f, 1f })
            {
                Ui.SetScale(size, r.GetTree().Root);
                Shows(row, name, box, size);
            }
        });
    }

    [NodeTest]
    static async Task A_panel_kept_aside_while_the_size_changed_takes_the_new_size_when_shown_again(TestRunner r)
    {
        await Sized(r, 1f, async () =>
        {
            var (row, name, box) = Row();
            r.AddChild(row);
            await r.Frame();
            r.RemoveChild(row);
            Ui.SetScale(1.25f, r.GetTree().Root);
            r.AddChild(row);
            Shows(row, name, box, 1.25f);
        });
    }

    [NodeTest]
    static Task A_small_window_takes_the_largest_step_that_fits(TestRunner r)
    {
        Check.That(Mathf.IsEqualApprox(0.9f, Ui.ScaleThatFits(new Vector2(1000, 560), 1.25f)), "a 1000x560 window holds 90%");
        Check.That(Mathf.IsEqualApprox(1.25f, Ui.ScaleThatFits(new Vector2(1920, 1080), 1.25f)), "a 1080p window holds 125%");
        Check.That(Mathf.IsEqualApprox(0.7f, Ui.ScaleThatFits(new Vector2(400, 300), 1f)), "no window shrinks the interface below 70%");
        return Task.CompletedTask;
    }
}
