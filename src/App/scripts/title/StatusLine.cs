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
        set
        {
            base.Text = value;
            Visible = value.Length > 0;
        }
    }
}
