using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

static class AgentPlaces
{
    const string Usual = "usual", Peace = "peace", War = "war";
    const int Places = 2;

    static int? Culture(Session s, string? asked)
    {
        var id = asked?.Trim().ToLowerInvariant() ?? "";
        return Manners.All.Any(m => m.Id == id) ? Manners.For(BuildTool.Epoch(s), Manners.Of(id)) : null;
    }

    static string Cultures(Session s) => "a culture of the map's age: " + string.Join(", ", Manners.In(BuildTool.Epoch(s)).Select(Manners.Id)) + ".";

    static bool Coat(string asked, out double? coat)
    {
        coat = null;
        if (asked.Trim().Equals(Usual, StringComparison.OrdinalIgnoreCase)) return true;
        var named = ColorChoices.All.FirstOrDefault(c => c.Name.Equals(asked.Trim(), StringComparison.OrdinalIgnoreCase)).Hex;
        coat = PlaceZones.CoatOf(named ?? asked);
        return coat != null;
    }

    public static (BuiltThing?, string?) Placed(Session s, SheetDef sheet, BuiltThing thing, int part, JsonElement a)
    {
        if (!s.IsLead) return (null, BuildZone.NotLead);
        return part >= 0 ? Building(s, sheet, thing, part, a) : Town(s, sheet, thing, a);
    }

    static (BuiltThing?, string?) Building(Session s, SheetDef sheet, BuiltThing thing, int part, JsonElement a)
    {
        if (!Looks.Takes(thing.Parts[part])) return (null, "place with part takes a building part.");
        var set = thing;
        if (a.Str("culture") is { Length: > 0 } asked)
        {
            if (Culture(s, asked) is not { } culture) return (null, "culture is " + Cultures(s));
            set = ZoneOverrides.WithCulture(set, sheet, [part], Manners.Id(culture));
        }
        if (a.Str("wealth") is { Length: > 0 } wealth)
        {
            if (PlaceZones.Level(wealth.Trim()) is not { } level) return (null, "wealth is poor, middling or rich.");
            set = ZoneOverrides.WithWealth(set, [part], level);
        }
        return ReferenceEquals(set, thing) ? (null, "place with part takes culture or wealth.") : (set, null);
    }

    static (BuiltThing?, string?) Town(Session s, SheetDef sheet, BuiltThing thing, JsonElement a)
    {
        if (thing.Kind != BuiltKinds.Settlement) return (null, "lives_on, state, held_by and coat are a settlement's; culture and wealth take a building part.");
        var set = thing;
        if (a.Str("state")?.Trim().ToLowerInvariant() is { Length: > 0 } state)
        {
            if (state is not (Peace or War)) return (null, "state is peace or war.");
            set = ZoneOverrides.WithWar(set, state == War);
        }
        int held = set.Settings.TryGetValue(PlaceZones.HeldBy, out double by) ? (int)by : Manners.Of(set);
        double? coat = set.Settings.TryGetValue(PlaceZones.Coat, out double kept) ? kept : null;
        if (a.Str("held_by") is { Length: > 0 } holder)
        {
            if (Culture(s, holder) is not { } culture) return (null, "held_by is " + Cultures(s));
            held = culture;
        }
        if (a.Str("coat") is { Length: > 0 } asked && !Coat(asked, out coat)) return (null, "coat is #rrggbb, one of " + string.Join(", ", ColorChoices.All.Select(c => c.Name)) + ", or usual.");
        if (a.Str("held_by") != null || a.Str("coat") != null) set = ZoneOverrides.WithHolder(set, held, coat);
        if (a.Num("lives_on") is { } share)
        {
            if (BuiltThings.Fixed(thing)) return (null, "It is kept as it is: edit_built with free lets it be laid afresh.");
            set = set.Clone();
            set.Settings[Settlements.Farming] = Math.Clamp(share, 0, 1);
            set = BuiltThings.Remade(BuildTool.Site(s, sheet, thing.Id), set, thing.Seed);
        }
        return ReferenceEquals(set, thing) ? (null, "place on a settlement takes lives_on, state, held_by or coat.") : (set, null);
    }

    public static void Held(BuiltThing thing, JsonObject row)
    {
        if (thing.Settings.TryGetValue(Settlements.Farming, out double farming)) row["lives_on"] = Math.Round(farming, Places);
        if (thing.Setting(PlaceZones.War, 0) >= 1) row["state"] = War;
        if (thing.Settings.TryGetValue(PlaceZones.HeldBy, out double held)) row["held_by"] = Manners.Id((int)held);
        if (thing.Settings.TryGetValue(PlaceZones.Coat, out double coat)) row["coat"] = PlaceZones.CoatText(coat);
    }

    static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();

    public static JsonNode? Row(PlaceZone? zone) => zone == null ? null : new JsonObject
    {
        ["built"] = zone.Thing, ["part"] = zone.Part >= 0 ? zone.Part : null, ["age"] = Manners.AgeName(zone.Culture.Age), ["culture"] = Manners.Id(zone.Culture.Own),
        ["town_culture"] = Manners.Id(zone.Culture.Town), ["year"] = zone.Culture.Year, ["kind"] = Lower(zone.Kind), ["wealth"] = Lower(zone.Wealth),
        ["lives_on"] = Math.Round(zone.Living.Farming, Places), ["trades"] = new JsonArray(zone.Living.Trades.Select(t => (JsonNode?)t).ToArray()),
        ["industry"] = zone.Living.Industry, ["docks"] = zone.Living.Docks, ["garrison"] = zone.Living.Garrison, ["clock_of"] = zone.Time.Clock,
        ["hour"] = Math.Round(zone.Time.Hour, Places), ["week_day"] = zone.Time.WeekDay, ["day"] = Lower(zone.Time.Day), ["holy_day"] = zone.Time.HolyDay,
        ["season"] = Lower(zone.Time.Season), ["state"] = Lower(zone.Hold.State), ["ravaged"] = Math.Round(zone.Hold.Ravaged, Places), ["held_by"] = Manners.Id(zone.Hold.HeldBy),
        ["coat"] = zone.Hold.Coat ?? Usual,
    };
}
