using KTable.Core.Protocol;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

static class InputReplies
{
    internal static void Retyped(ScriptHost host, EditWidget ew, DynValue? back)
    {
        if (back is not { Type: DataType.String } text || text.String == ew.Value) return;
        var staged = host.Staging(ew.Id);
        if (ew.Index > staged.Count || staged[ew.Index - 1].Kind != "input") return;
        staged[ew.Index - 1].Value = text.String;
        host.Drawn(ew.Id, menu: false);
    }
}
