using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class PieceScript
{
    public string Id = "";
    public Table Env = null!;
    public string Source = "";
    public bool Enabled = true;
    public bool HasUpdate;
    public bool Ran;
    public bool Faulted;
    public string Name => Id.Length == 0 ? "the global script" : Id;
}
