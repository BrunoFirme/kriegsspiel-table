using Godot;

namespace KTable.App;

sealed partial class FramePace : Node
{
    const double LeftAloneSeconds = 120;

    Settings _cfg = null!;
    bool _inFront = true;
    double _untouched;

    public static void Install(Node host, Settings cfg) => host.AddChild(new FramePace { Name = "FramePace", _cfg = cfg, ProcessMode = ProcessModeEnum.Always });

    bool ShouldRest => !_inFront || _untouched >= LeftAloneSeconds;

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn) (_inFront, _untouched) = (true, 0);
        else if (what == NotificationApplicationFocusOut) _inFront = false;
        else return;
        Settle();
    }

    public override void _Input(InputEvent e)
    {
        _untouched = 0;
        if (Graphics.Resting) Settle();
    }

    public override void _Process(double delta)
    {
        _untouched += delta;
        if (Graphics.Resting != ShouldRest) Settle();
    }

    void Settle()
    {
        if (Graphics.Resting == ShouldRest) return;
        Graphics.Resting = ShouldRest;
        Graphics.ApplyFrames(_cfg);
    }
}
