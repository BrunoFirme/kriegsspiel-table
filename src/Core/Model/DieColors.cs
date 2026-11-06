namespace KTable.Core.Model;

public static class DieColors
{
    public const string Red = "#D9443C", Blue = "#4A78D8";

    public static readonly (string Name, string Hex)[] All =
    {
        ("White", "#FFFFFF"), ("Red", Red), ("Blue", Blue), ("Green", "#3FA35A"), ("Yellow", "#E8C83A"), ("Orange", "#E8873A"), ("Purple", "#9A5AC8"),
    };

    static readonly Dictionary<string, (string Def, string Tint)> Retired = new()
    {
        ["std.dice.d6-red"] = ("std.dice.d6", Red),
        ["std.dice.d6-blue"] = ("std.dice.d6", Blue),
    };

    public static int Recolor(GameState g)
    {
        int changed = 0;
        foreach (var p in g.Layers.Values.SelectMany(l => l))
        {
            if (!Retired.TryGetValue(p.Def, out var now)) continue;
            p.Def = now.Def;
            p.TintOverride ??= now.Tint;
            changed++;
        }
        return changed;
    }
}
