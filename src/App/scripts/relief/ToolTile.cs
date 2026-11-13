using System;
using Godot;

namespace KTable.App;

static class ToolTile
{
    const int High = 74, PictureHigh = 46, Inset = 4;

    public static Button Of(string tool, string name, string tip, bool chosen)
    {
        var b = new Button
        {
            ToggleMode = true, ButtonPressed = chosen, FocusMode = Control.FocusModeEnum.None, TooltipText = tip, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, High),
        };
        Ui.MarkTool(b);
        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, Inset);
        stack.AddThemeConstantOverride("separation", Ui.Gap.None);
        var picture = new Control { CustomMinimumSize = new Vector2(0, PictureHigh), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        picture.AddChild(new ToolIcon { Kind = ToolIcon.TerrainKinds + tool });
        stack.AddChild(picture);
        stack.AddChild(new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore });
        b.AddChild(stack);
        return b;
    }

    public static VBoxContainer Rows((string Key, string Text, string Tip)[] tools, string now, int toARow, Action<string> pick, Func<string, string>? picture = null)
    {
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        for (int i = 0; i < tools.Length; i += toARow)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
            for (int k = i; k < Math.Min(tools.Length, i + toARow); k++)
            {
                var (key, text, tip) = tools[k];
                var b = Of(picture?.Invoke(key) ?? key, text, tip, key == now);
                b.Pressed += () => pick(key);
                row.AddChild(b);
            }
            rows.AddChild(row);
        }
        return rows;
    }
}
