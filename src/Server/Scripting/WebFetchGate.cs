using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Scripting;

sealed class WebFetchGate
{
    const int MostTold = 64;
    readonly HashSet<string> _typed = new(StringComparer.Ordinal);
    readonly HashSet<string> _hostsTold = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _refused = new(StringComparer.Ordinal);

    public void NoteTyped(string? text)
    {
        if (text != null && ScriptHost.IsWebLink(text)) _typed.Add(text.Trim());
    }

    static IEnumerable<string> Written(GameState state, PackageStore? store)
    {
        if (state.Script is { } global) yield return global;
        yield return state.ScriptState;
        foreach (var p in state.Layers.Values.SelectMany(l => l).SelectMany(p => p.AllStowed().Prepend(p)))
        {
            if (p.Script is { } own) yield return own;
            yield return p.ScriptState;
            foreach (var look in p.Looks ?? [])
            {
                if (look.Script is { } worn) yield return worn;
                yield return look.ScriptState;
            }
        }
        foreach (var d in store?.AllDefs ?? [])
            if (d.Script is { } kind) yield return kind;
    }

    public bool Allows(ScriptHost host, GameServer server, string link, string by, bool canTell)
    {
        if (!ScriptHost.IsWebLink(link)) return false;
        var address = link.Trim();
        var uri = new Uri(address);
        bool carriesMore = uri.Query.Length > 0 || uri.Fragment.Length > 0;
        if (carriesMore && !_typed.Contains(address) && !Written(server.State, server.Store).Any(t => t.Contains(address, StringComparison.Ordinal)))
        {
            if (_refused.Count < MostTold && _refused.Add(address))
                host.Report(by, $"not fetched: an address a script builds while it runs may not carry a question mark or a hash ({uri.Host}); write the whole address in the script, or have a person type it");
            return false;
        }
        if (canTell && _hostsTold.Count < MostTold && _hostsTold.Add(uri.Host))
            BestEffort.Try(() => host.Emit(new Say { Text = $"Scripts here fetch from the web address {uri.Host}" + (by.Length > 0 ? $" (first asked by {host.NameOf(by)})" : "") + ".", To = "u:" }));
        return true;
    }
}
