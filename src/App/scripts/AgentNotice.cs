using Godot;

namespace KTable.App;

static class AgentNotice
{
    public static Control Line(Node owner)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var said = new Label { Text = "AI art and machine-made filler have no place in Kriegsspiel.", Modulate = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
        said.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(said);
        var link = new LinkButton { Text = "Author's notice", Underline = LinkButton.UnderlineMode.Always, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "In Help: For makers → For AI agents" };
        link.AddThemeFontSizeOverride("font_size", 12);
        link.AddThemeColorOverride("font_color", Ui.Ink);
        link.Pressed += () => GuideReader.Show(owner, "makers.md", "for-ai-agents");
        row.AddChild(link);
        return row;
    }
}
