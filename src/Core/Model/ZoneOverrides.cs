namespace KTable.Core.Model;

public static class ZoneOverrides
{
    public static readonly IReadOnlyList<string> TownKeys = [Settlements.Farming, PlaceZones.War, PlaceZones.HeldBy, PlaceZones.Coat];

    public static string? CultureId(string? asked) => Manners.All.Any(m => m.Id == asked) ? asked : null;

    public static BuiltThing WithCulture(BuiltThing thing, SheetDef sheet, IReadOnlyList<int> parts, string? culture)
    {
        var set = thing.Clone();
        foreach (int i in parts.Where(i => i >= 0 && i < set.Parts.Count))
        {
            set.Parts[i].Culture = CultureId(culture) is { } id && Manners.Of(id) != PlaceZones.LaidCulture(thing, sheet, thing.Parts[i]) ? id : null;
            set.Parts[i].Kept = true;
        }
        return set;
    }

    public static BuiltThing WithWealth(BuiltThing thing, IReadOnlyList<int> parts, WealthLevel? wealth)
    {
        var set = thing.Clone();
        foreach (int i in parts.Where(i => i >= 0 && i < set.Parts.Count))
        {
            var part = set.Parts[i];
            part.Wealth = wealth is { } level && level != PlaceZones.Laid(part) ? PlaceZones.Stored(level) : null;
            part.Kept = true;
        }
        return set;
    }

    public static BuiltThing WithWar(BuiltThing thing, bool war)
    {
        var set = thing.Clone();
        if (war) set.Settings[PlaceZones.War] = 1;
        else set.Settings.Remove(PlaceZones.War);
        return set;
    }

    public static BuiltThing WithHolder(BuiltThing thing, int? culture, double? coat)
    {
        var set = thing.Clone();
        if (culture is { } held && Manners.All.Any(m => m.Number == held) && held != Manners.Of(thing)) set.Settings[PlaceZones.HeldBy] = held;
        else set.Settings.Remove(PlaceZones.HeldBy);
        if (coat is { } colour) set.Settings[PlaceZones.Coat] = colour;
        else set.Settings.Remove(PlaceZones.Coat);
        return set;
    }

    static WealthLevel Step(WealthLevel level, bool richer) => (WealthLevel)Math.Clamp((int)level + (richer ? 1 : -1), (int)WealthLevel.Poor, (int)WealthLevel.Rich);

    public static BuiltThing? Brushed(BuiltThing thing, IReadOnlyList<Spot> stroke, double radius, bool richer)
    {
        if (stroke.Count == 0) return null;
        var touched = Enumerable.Range(0, thing.Parts.Count).Where(i => Looks.Takes(thing.Parts[i]) && Under(BuiltSite.Round(thing.Parts[i]).At, stroke, radius))
            .Where(i => Step(PlaceZones.WealthOf(thing.Parts[i]), richer) != PlaceZones.WealthOf(thing.Parts[i])).ToList();
        if (touched.Count == 0) return null;
        var set = thing.Clone();
        foreach (int i in touched)
        {
            var part = set.Parts[i];
            var level = Step(PlaceZones.WealthOf(part), richer);
            part.Wealth = level == PlaceZones.Laid(part) ? null : PlaceZones.Stored(level);
        }
        return set;
    }

    static bool Under(Spot at, IReadOnlyList<Spot> stroke, double radius)
    {
        if (stroke.Count == 1) return (at - stroke[0]).Length <= radius;
        for (int i = 0; i + 1 < stroke.Count; i++)
            if (at.FromSegment(stroke[i], stroke[i + 1]) <= radius) return true;
        return false;
    }

    static bool Overridden(BuiltPart part) => part.Culture != null || part.Wealth != null;

    static (string Class, string? Culture, string? Wealth) Key(BuiltPart part) => (part.Class, part.Culture, part.Wealth);

    public static bool Introduced(BuiltThing? before, BuiltThing after)
    {
        foreach (string key in TownKeys)
        {
            bool now = after.Settings.TryGetValue(key, out double value), was = before != null && before.Settings.TryGetValue(key, out double old) && old.Equals(value);
            if (now ? !was : before != null && before.Settings.ContainsKey(key)) return true;
        }
        var had = (before?.Parts ?? []).Where(Overridden).GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        return after.Parts.Where(Overridden).GroupBy(Key).Any(g => g.Count() > had.GetValueOrDefault(g.Key));
    }
}
