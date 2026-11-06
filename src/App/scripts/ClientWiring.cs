global using KTable.Client.Prefs;
using System.Runtime.CompilerServices;
using Godot;

namespace KTable.App;

static class ClientWiring
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Wire()
    {
        KTable.Client.Problems.Echo = text => GD.PushWarning(text);
        Settings.FileAt = () => ProjectSettings.GlobalizePath("user://settings.json");
    }
#pragma warning restore CA2255
}
