using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client.Panels;

public static class PanelSeats
{
    static readonly string[] Colors = { "White", "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Brown", "Teal", "Grey", "Black" };
    const string GameMaster = "Black", Spectator = "Grey";

    public static (string Color, bool Host, bool Admin) Of(PeerInfo me, IReadOnlyList<PeerInfo> peers)
    {
        if (me.Role == Role.HostUmpire) return (GameMaster, true, true);
        if (!Visibility.IsUmpire(me.Role)) return (Spectator, false, false);
        int i = 1;
        foreach (var p in peers.Where(p => Visibility.IsUmpire(p.Role) && p.Role != Role.HostUmpire))
        {
            if (p.PeerId == me.PeerId) return (Colors[Math.Min(i, Colors.Length - 1) - 1], false, me.Role != Role.Umpire);
            i++;
        }
        return (Spectator, false, me.Role != Role.Umpire);
    }

    public static List<UiAssetPicture> Assets(GameState g)
    {
        var list = new List<UiAssetPicture>();
        foreach (var (name, url) in g.UiPictures) list.Add(new(name, url));
        foreach (var a in g.UiAssets ?? new()) list.Add(new(a.Name, a.Url));
        foreach (var p in g.Layers.Values.SelectMany(l => l)) foreach (var a in p.UiAssets ?? new()) list.Add(new(a.Name, a.Url));
        return list;
    }

    public static string? Picture(GameState g, string name)
    {
        string? found = null;
        foreach (var a in Assets(g)) if (a.Name == name) found = a.Url;
        if (found != null) return found;
        if (g.UiLinks != null && g.UiLinks.TryGetValue(name, out var linked)) return linked;
        return name;
    }
}

public readonly record struct UiAssetPicture(string Name, string Url);
