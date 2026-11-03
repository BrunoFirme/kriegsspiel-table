using System;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core;

public static class WireTap
{
    public static Action<string, long, object>? Listener { get; set; }

    public static Func<GameState?>? HostState { get; set; }

    public static void Note(string what, long peer, Message m)
    {
        var l = Listener;
        if (l == null) return;
        BestEffort.Try(l, what, peer, m);
    }

    public static void Note(string what, long peer, Command c)
    {
        var l = Listener;
        if (l == null) return;
        BestEffort.Try(l, what, peer, c);
    }
}
