using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Scripting;

static class ScriptReport
{
    public static List<ScriptInfo> Of(ScriptHost h)
    {
        var timers = h._timers.CountByOwner();
        ScriptInfo Info(string id, PieceScript? ps)
        {
            var info = new ScriptInfo { Id = id, Status = ps == null ? "not loaded" : !ps.Enabled ? "stopped" : h._feed.LastError(id, out _, out _) ? "error" : "running" };
            if (h._feed.LastError(id, out var message, out var line)) { info.LastError = message; info.ErrorLine = line; }
            if (ps != null) info.Timers = timers.GetValueOrDefault(ps);
            return info;
        }
        var list = new List<ScriptInfo>();
        if (h._s.State.Script != null || h._global != null)
        {
            var g = Info("", h._global);
            g.Name = "Global script";
            list.Add(g);
        }
        foreach (var p in h._s.State.Layer(Layers.Umpire))
        {
            if (string.IsNullOrWhiteSpace(h.CodeFor(p))) continue;
            var i = Info(p.Id, h._pieces.GetValueOrDefault(p.Id));
            i.Name = h.NameOf(p.Id);
            i.Guid = p.Guid;
            i.Def = p.Def;
            i.Own = p.Script != null;
            list.Add(i);
        }
        return list;
    }
}
