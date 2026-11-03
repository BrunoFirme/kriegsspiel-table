using System.Diagnostics;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client;

sealed class ScriptSaves
{
    readonly Func<GameServer?> _server;
    readonly Func<Action<List<Outgoing>>> _dispatch;
    readonly Func<ClientLog> _log;

    public ScriptSaves(Func<GameServer?> server, Func<Action<List<Outgoing>>> dispatch, Func<ClientLog> log)
    {
        _server = server; _dispatch = dispatch; _log = log;
    }

    public void Round()
    {
        if (_server() is not { } server || !server.Scripts.AnyScriptRan) return;
        var clock = Stopwatch.StartNew();
        Ask(server.Scripts.Save);
        if (clock.ElapsedMilliseconds > 20)
            _log().Print($"[scripts] on_save {clock.ElapsedMilliseconds} ms, asked {server.Scripts.AskedLast} of {server.State.Layer(Layers.Umpire).Count}, {server.Scripts.WithUpdate} want a tick");
    }

    public void Backlog()
    {
        if (_server() is { } server && server.Scripts.Backlog > 50) Ask(server.Scripts.Save);
    }

    public void All()
    {
        if (_server() is { } server) Ask(server.Scripts.SaveAll);
    }

    void Ask(Func<List<Outgoing>> save)
    {
        try { _dispatch()(save()); } catch (Exception e) { _log().Error("[scripts] on_save failed: " + e); }
    }
}
