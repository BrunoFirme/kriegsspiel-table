using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Panels;

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
        Owner(list, g.UiAssets);
        foreach (var p in g.Layers.Values.SelectMany(l => l)) Owner(list, p.UiAssets);
        return list;
    }

    static void Owner(List<UiAssetPicture> into, List<UiAsset>? assets)
    {
        var urls = new HashSet<string>();
        foreach (var a in assets ?? new())
            if (a.Type == PanelState.ImageType && a.Name.Length > 0 && urls.Add(a.Url)) into.Add(new(a.Name, a.Url));
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
