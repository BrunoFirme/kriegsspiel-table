using KTable.Client.Prefs;

namespace KTable.App;

public sealed class PieceLook
{
    public float LetteringReach { get; set; } = Settings.ReachOff;
    public bool ShowFaceText { get; set; } = true;
    public float FaceTextScale { get; set; } = 1f;
    public bool ShowFaceBoxes { get; set; }
    public bool FlatSelection { get; set; }
}
