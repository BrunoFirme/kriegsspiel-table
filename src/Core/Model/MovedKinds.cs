namespace KTable.Core.Model;

public static class MovedKinds
{
    public const string Adventurer = "std.adventurer", Effects = "std.effects.", OldTracker = Effects + "round-tracker";

    static readonly HashSet<string> Names =
        ["tavern", "front-door", "balcony-door", "west-door", "east-door", "hearth", "hearth-small", "torch-flame", "lamp-flame", "candle-flame", "chimney-smoke", "round-tracker"];

    public static bool Moved(string def) => def.StartsWith(Effects, StringComparison.Ordinal) && Names.Contains(def[Effects.Length..]);

    public static string Now(string def) => Moved(def) ? Adventurer + "." + def[Effects.Length..] : def;

    public static int Move(GameState g)
    {
        int moved = 0;
        foreach (var p in g.Layers.Values.SelectMany(l => l).SelectMany(p => p.AllStowed().Prepend(p)))
        {
            if (!Moved(p.Def)) continue;
            p.Def = Now(p.Def);
            moved++;
        }
        if (moved > 0 && !g.Packs.Contains(Adventurer)) g.Packs.Add(Adventurer);
        return moved;
    }
}
