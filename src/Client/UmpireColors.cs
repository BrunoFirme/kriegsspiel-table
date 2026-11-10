using KTable.Core.Model;

namespace KTable.Client;

public static class UmpireColors
{
    public const int HostSlot = 0;

    public static int? Slot(string name, IEnumerable<PeerInfo> peers, GameAccess? access)
    {
        if (name.Trim().Length == 0) return null;
        if (peers.FirstOrDefault(p => p.Role == Role.HostUmpire) is { } host && Same(host.Name, name)) return HostSlot;
        return access?.Approved.FirstOrDefault(a => Same(a.Name, name)) is { ColorSlot: > 0 } a ? a.ColorSlot : null;
    }

    static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
