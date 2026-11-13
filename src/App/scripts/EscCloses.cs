using System;
using Godot;

namespace KTable.App;

sealed partial class EscCloses : Node
{
    Action _close = () => { };

    public static void On(Window window, Action? close = null) =>
        window.AddChild(new EscCloses { _close = close ?? (() => window.EmitSignal(Window.SignalName.CloseRequested)) });

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Keycode: Key.Escape, Pressed: true, Echo: false }) return;
        GetViewport().SetInputAsHandled();
        _close();
    }
}
