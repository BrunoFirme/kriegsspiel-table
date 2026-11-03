using KTable.Core.Model;

namespace KTable.Core;

public static class ZoneRule
{
    public static bool Binds(GameState g, Role role) => role == Role.Umpire && g.Zones.Count > 0;

    public static bool Owns(Zone z, string umpire) => string.Equals(z.Owner, umpire, StringComparison.OrdinalIgnoreCase);

    public static bool HasZone(GameState g, string umpire) => g.Zones.Any(z => Owns(z, umpire));

    public static bool Allows(GameState g, Role role, string umpire, string layer, Vec2 at) =>
        layer != Layers.Umpire || !Binds(g, role) || g.Zones.Any(z => Owns(z, umpire) && z.Contains(at));
}
