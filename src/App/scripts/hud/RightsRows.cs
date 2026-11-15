using System;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class RightsRows
{
    const int First = 10;

    static readonly (string Name, Func<PlayerRights, bool> Has, Action<PlayerRights, bool> Set)[] Rows =
    [
        ("Fly", r => r.Fly, (r, on) => r.Fly = on),
        ("Take over pieces", r => r.Take, (r, on) => r.Take = on),
        ("Change body", r => r.Body, (r, on) => r.Body = on),
        ("Third person", r => r.View, (r, on) => r.View = on),
        ("Dice", r => r.Dice, (r, on) => r.Dice = on),
        ("Measure", r => r.Measure, (r, on) => r.Measure = on),
        ("Ping", r => r.Ping, (r, on) => r.Ping = on),
    ];

    public static void Add(PopupMenu menu, PlayerRights rights)
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            menu.AddCheckItem(Rows[i].Name, First + i);
            menu.SetItemChecked(menu.GetItemIndex(First + i), Rows[i].Has(rights));
        }
    }

    public static bool Is(long id) => id >= First && id < First + Rows.Length;

    public static SetPlayerRights Flipped(GameState view, string? player, long id)
    {
        var row = Rows[id - First];
        var everyone = view.Everyone?.Clone();
        var named = view.Rights?.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        if (player == null)
        {
            everyone ??= new PlayerRights();
            bool on = !row.Has(everyone);
            row.Set(everyone, on);
            foreach (var each in named.Values) row.Set(each, on);
        }
        else
        {
            var now = view.RightsOf(player).Clone();
            row.Set(now, !row.Has(now));
            named[player] = now;
        }
        return new SetPlayerRights { Everyone = everyone, Rights = named.Count > 0 ? named : null };
    }
}
