using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

static class UmpireHue
{
    public static readonly Color Unassigned = new(0.72f, 0.70f, 0.66f);

    static readonly Color[] Hues =
    [
        new(0.087f, 0.464f, 0.673f), new(0.854f, 0.614f, 0.080f), new(0.374f, 0.684f, 0.856f), new(0.767f, 0.487f, 0.636f),
        new(0.087f, 0.611f, 0.463f), new(0.797f, 0.397f, 0.080f), new(0.887f, 0.844f, 0.300f), new(0.540f, 0.197f, 0.363f),
    ];

    public static Color? Of(ClientSession s, string name) =>
        UmpireColors.Slot(name, s.Peers, s.Access) is { } slot ? Hues[slot % Hues.Length] : null;

    public static Color? Of(ClientSession s, KTable.Core.Model.PeerInfo p) => Visibility.IsUmpire(p.Role) ? Of(s, p.Name) : null;
}
