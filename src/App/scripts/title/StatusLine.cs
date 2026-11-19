using Godot;

namespace KTable.App;

sealed partial class StatusLine : Label
{
    public StatusLine()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Modulate = Ui.AccentInk;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Visible = false;
    }

    public new string Text
    {
        get => base.Text;
        set => Say(value, failed: false);
    }

    public void Fail(string text) => Say(text, failed: true);

    public void Say(string text, bool failed)
    {
        base.Text = text;
        Modulate = failed ? Ui.Danger : Ui.AccentInk;
        Visible = text.Length > 0;
    }
}
