using System;
using System.Collections.Generic;
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
        ("Take over pieces", r => r.Take, (r, on) => r.Take = on),
        ("Dice", r => r.Dice, (r, on) => r.Dice = on),
        ("Measure", r => r.Measure, (r, on) => r.Measure = on),
        ("Ping", r => r.Ping, (r, on) => r.Ping = on),
        ("Write", r => r.Write, (r, on) => r.Write = on),
        ("Speak", r => r.Speak, (r, on) => r.Speak = on),
        ("Dress their body", r => r.Dress, (r, on) => r.Dress = on),
    ];

    const string Whole = "Whole wardrobe", AnyWealth = "Any wealth";

    static IReadOnlyList<(string? Key, string Name)> Peoples => [(null, Whole), .. Nationalities.All.Select(n => ((string?)n.Id, n.Name ?? n.Id))];

    static IReadOnlyList<(string? Key, string Name)> Wealths => [(null, AnyWealth), .. CrowdBaseline.Wealths.Select(w => ((string?)w, char.ToUpperInvariant(w[0]) + w[1..]))];

    public static void Add(PopupMenu menu, PlayerRights rights, Func<GameState> view, Action<SetPlayerRights> send)
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            menu.AddCheckItem(Rows[i].Name, First + i);
            menu.SetItemChecked(menu.GetItemIndex(First + i), Rows[i].Has(rights));
        }
        Choices(menu, "Dress from", Peoples, rights.DressAs, key => send(Chosen(view(), null, r => r.DressAs = key)));
        Choices(menu, "Wealth", Wealths, rights.DressWealth, key => send(Chosen(view(), null, r => r.DressWealth = key)));
    }

    static void Choices(PopupMenu menu, string name, IReadOnlyList<(string? Key, string Name)> choices, string? now, Action<string?> pick)
    {
        var sub = new PopupMenu();
        for (int i = 0; i < choices.Count; i++)
        {
            sub.AddRadioCheckItem(choices[i].Name, i);
            sub.SetItemChecked(i, choices[i].Key == now);
        }
        sub.IdPressed += id => pick(choices[(int)id].Key);
        menu.AddSubmenuNodeItem(name, sub);
    }

    public static SetPlayerRights Chosen(GameState view, string? player, Action<PlayerRights> set)
    {
        var everyone = view.Everyone?.Clone();
        var named = view.Rights?.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        if (player == null)
        {
            set(everyone ??= new PlayerRights());
            foreach (var each in named.Values) set(each);
        }
        else
        {
            var now = view.RightsOf(player).Clone();
            set(now);
            named[player] = now;
        }
        return new SetPlayerRights { Everyone = everyone, Rights = named.Count > 0 ? named : null };
    }

    public static void Add(MenuCard card, Func<GameState> view, string player, Action<SetPlayerRights> send)
    {
        var now = view().RightsOf(player);
        for (int i = 0; i < Rows.Length; i++)
        {
            int id = First + i;
            card.Toggle(Rows[i].Name, Rows[i].Has(now), _ => send(Flipped(view(), player, id)));
        }
        card.Page("Dress from", () =>
        {
            foreach (var (key, name) in Peoples) card.Check(name, now.DressAs == key, () => send(Chosen(view(), player, r => r.DressAs = key)));
        });
        card.Page("Wealth", () =>
        {
            foreach (var (key, name) in Wealths) card.Check(name, now.DressWealth == key, () => send(Chosen(view(), player, r => r.DressWealth = key)));
        });
    }

    public static SetPlayerRights Flipped(GameState view, string? player, long id)
    {
        var row = Rows[id - First];
        bool on = !row.Has(player == null ? view.Everyone ?? new PlayerRights() : view.RightsOf(player));
        return Chosen(view, player, r => row.Set(r, on));
    }
}
