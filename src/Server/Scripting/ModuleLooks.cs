using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Scripting;

static class ModuleLooks
{
    public static void Swap(SetState st, PieceInstance p, PieceDef? def, string? code, Func<PieceInstance, string> current)
    {
        if (def == null || string.IsNullOrWhiteSpace(code)) return;
        int looks = def.States.Count;
        if (looks < 2 || st.State == p.State || st.State < 0 || st.State >= looks || p.State >= looks) return;
        if (!def.States.Any(s => s.Module != null)) return;
        var kept = p.StateScripts is { } known && known.Count == looks ? known.ToList() : def.States.Select(s => s.Module?.ScriptState ?? "").ToList();
        kept[p.State] = current(p);
        st.Script = kept[st.State];
        st.StateScripts = kept;
    }
}
