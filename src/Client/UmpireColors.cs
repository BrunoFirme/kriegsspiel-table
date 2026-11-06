using KTable.Core.Model;

namespace KTable.Client;

public static class UmpireColors
{
    public const int HostSlot = 0;

    public static int? Slot(string name, IEnumerable<PeerInfo> peers, GameAccess? access)
    {
        if (name.Trim().Length == 0) return null;
        if (peers.FirstOrDefault(p => p.Role == Role.HostUmpire) is { } host && Same(host.Name, name)) return HostSlot;
        var i = access?.Approved.FindIndex(a => Same(a.Name, name)) ?? -1;
        return i < 0 ? null : i + 1;
    }

    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
