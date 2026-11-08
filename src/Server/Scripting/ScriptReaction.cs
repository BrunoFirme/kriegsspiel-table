using KTable.Core.Protocol;

namespace KTable.Server.Scripting;

sealed class ScriptReaction
{
    public long Actor;
    public bool Action;
    public readonly List<Command> Items = new();
    public readonly List<Command?> Inverses = new();
    public readonly HashSet<string> Spawned = new();
    public readonly List<string> Arriving = new();
    public readonly HashSet<string> WidgetsDirty = new();
    public readonly HashSet<string> MenusDirty = new();
    public bool HotkeysDirty, GlobalMenuDirty;
    public readonly HashSet<PieceScript> Ran = new();
    public bool GlobalRan;
}
