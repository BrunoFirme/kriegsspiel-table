using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class KindDrops
{
    static readonly List<(Control Zone, Action<string> Take)> Zones = new();

    public static void Add(Control zone, Action<string> take)
    {
        Zones.RemoveAll(z => !GodotObject.IsInstanceValid(z.Zone) || z.Zone.IsQueuedForDeletion());
        Zones.Add((zone, take));
    }

    public static Action<string>? At(Vector2 screen)
    {
        foreach (var (zone, take) in Zones)
            if (GodotObject.IsInstanceValid(zone) && !zone.IsQueuedForDeletion() && zone.IsVisibleInTree() && zone.GetGlobalRect().HasPoint(screen)) return take;
        return null;
    }
}
