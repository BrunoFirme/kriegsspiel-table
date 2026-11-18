using KTable.Core.Model;

namespace KTable.Core.Protocol;

static class TableRules
{
    public const int MostPlayers = 64, LongestName = 64;

    public static string? Apply(GameState g, Command c, ref Command? inverse) => c switch
    {
        SetPointerRule pr => Pointers(g, pr, ref inverse),
        SetPlayerRights pr => Rights(g, pr, ref inverse),
        AllowOrders ao => Orders(g, ao, ref inverse),
        _ => "not a table rule",
    };

    static string? Pointers(GameState g, SetPointerRule pr, ref Command? inverse)
    {
        inverse = new SetPointerRule { AlwaysShown = g.PointersAlwaysShown, ToPlayers = pr.ToPlayers != null ? g.PointersToPlayers : null };
        (g.PointersAlwaysShown, g.PointersToPlayers) = (pr.AlwaysShown, pr.ToPlayers ?? g.PointersToPlayers);
        return null;
    }

    static string? Orders(GameState g, AllowOrders ao, ref Command? inverse)
    {
        inverse = new AllowOrders { On = g.OrdersOn };
        g.OrdersOn = ao.On;
        return null;
    }

    static string? Rights(GameState g, SetPlayerRights pr, ref Command? inverse)
    {
        if (pr.Rights is { } named && (named.Count > MostPlayers || named.Any(p => p.Key.Trim().Length == 0 || p.Key.Length > LongestName || p.Value == null)))
            return "bad player rights";
        inverse = new SetPlayerRights { Everyone = g.Everyone?.Clone(), Rights = Copy(g.Rights) };
        g.Everyone = pr.Everyone?.Clone();
        g.Rights = pr.Rights is { Count: > 0 } ? Copy(pr.Rights) : null;
        return null;
    }

    static Dictionary<string, PlayerRights>? Copy(Dictionary<string, PlayerRights>? rights) =>
        rights?.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
}
