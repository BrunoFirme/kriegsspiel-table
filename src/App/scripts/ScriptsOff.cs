using System;
using Godot;

namespace KTable.App;

static class ScriptsOff
{
    public const string Said = "Scripts are off until this game is opened again";
    const string Title = "Last opening stopped. Open with scripts off?", Off = "Scripts off", AsUsual = "Open as usual";

    public static void Ask(Node parent, Action<bool> then)
    {
        var q = Ui.Confirm(parent, Title, Off, () => then(true));
        q.CancelButtonText = AsUsual;
        q.Canceled += () => then(false);
    }
}
