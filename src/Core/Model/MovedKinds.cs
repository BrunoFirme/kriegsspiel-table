namespace KTable.Core.Model;

public static class MovedKinds
{
    public const string Effects = "std.effects", OldTracker = Effects + ".round-tracker";

    static readonly string[] Retired = ["std.adventurer", "std.adventuring"];

    static readonly HashSet<string> Names =
        ["tavern", "front-door", "balcony-door", "west-door", "east-door", "hearth", "hearth-small", "torch-flame", "lamp-flame", "candle-flame", "chimney-smoke", "round-tracker", "fresh-body"];

    static readonly Dictionary<string, string> Merged = new() { ["balcony-door"] = "east-door", ["west-door"] = "east-door" };

    public static IReadOnlyList<string> RetiredPacks => Retired;

    static string? Name(string def)
    {
        foreach (var pack in Retired.Append(Effects))
        {
            if (!def.StartsWith(pack + ".", StringComparison.Ordinal)) continue;
            var name = def[(pack.Length + 1)..];
            if (pack == Effects ? Merged.ContainsKey(name) : Names.Contains(name)) return Merged.GetValueOrDefault(name, name);
        }
        return null;
    }

    public static bool Moved(string def) => Name(def) != null;

    public static string Now(string def) => Name(def) is { } name ? Effects + "." + name : def;

    public static List<string> Earlier(string def)
    {
        if (!def.StartsWith(Effects + ".", StringComparison.Ordinal)) return [];
        var name = def[(Effects.Length + 1)..];
        var names = Merged.Where(m => m.Value == name).Select(m => m.Key).Append(name).Where(Names.Contains).ToList();
        return Retired.SelectMany(p => names.Select(n => p + "." + n)).Concat(Merged.Where(m => m.Value == name).Select(m => Effects + "." + m.Key)).ToList();
    }

    public static int Move(GameState g)
    {
        int moved = 0;
        foreach (var p in g.Layers.Values.SelectMany(l => l).SelectMany(p => p.AllStowed().Prepend(p)))
        {
            if (!Moved(p.Def)) continue;
            p.Def = Now(p.Def);
            moved++;
        }
        int dropped = g.Packs.RemoveAll(Retired.Contains);
        if ((moved > 0 || dropped > 0) && !g.Packs.Contains(Effects)) g.Packs.Add(Effects);
        return moved;
    }
}
