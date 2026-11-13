namespace KTable.Core.Model;

public static class Catalogue
{
    public const int Any = 0, LaterOnly = 1, OldOnly = 2;

    public sealed record Entry(string Id, string Name, double LongM = 0, double DeepM = 0, double ViewM = 7, int Era = Any, string? Class = null, string? With = null)
    {
        public bool Block => LongM > 0;
        public string Laid => Class ?? Id;
    }

    public sealed record Section(string Id, string Name, string Tip, IReadOnlyList<Entry> Entries);

    public static readonly IReadOnlyList<Section> Sections =
    [
        new("homes", "Homes", "Houses and what stands with them",
        [
            new("house", "House", 12, 8, 19), new("farm", "Farmhouse", 18, 9, 26), new("inn", "Inn", 16, 10, 24), new("tavern", "Tavern", 12, 9, 19), new("shop", "Shop", 7, 10, 17),
            new("villa", "Villa", 18, 18, 27), new("domus", "Atrium house", 13, 24, 34, OldOnly), new("insula", "Block", 18, 20, 30, OldOnly), new("taberna", "Booth", 5, 7, 12, OldOnly),
            new("roundhouse", "Round house", 8, 8, 13, OldOnly), new("longhouse", "Long house", 16, 7, 23, OldOnly), new("greathouse", "Great house", 13, 13, 20, OldOnly),
            new("fisher", "Fisher's hut", 6.5, 5, 11), new("shed", "Shed", 6, 4.5, 10), new("privy", "Privy", 1.4, 1.4, 5, LaterOnly),
        ]),
        new("farm", "Farm", "Farm buildings, stock and gear",
        [
            new("barn", "Barn", 18, 9, 26), new("stable", "Stable", 16, 8, 24), new("byre", "Byre", 16, 8, 24), new("granary", "Granary", 9, 6, 14), new("cartshed", "Cart shed", 12, 5, 18),
            new("dovecote", "Dovecote", 4.2, 4.2, 8, LaterOnly), new("coop", "Coop", 2.2, 1.6, 5), new("pigsty", "Pigsty", 3, 2, 6), new("silo", "Grain bin", 3.2, 3.2, 6, OldOnly),
            new("rick", "Rick", ViewM: 8), new("stooks", "Stooks", ViewM: 9), new("dungheap", "Dung heap", ViewM: 6), new("plough", "Plough", ViewM: 6), new("cart", "Cart", ViewM: 8),
            new("trough", "Trough", ViewM: 5), new("hive", "Hive", ViewM: 3), new("hens", "Hens", ViewM: 5), new("geese", "Geese", ViewM: 6), new("pigs", "Pigs", ViewM: 6),
            new("sheep", "Sheep", ViewM: 12), new("cattle", "Cattle", ViewM: 13), new("horses", "Horses", ViewM: 13), new("dog", "Dog", ViewM: 4),
        ]),
        new("trade", "Trade", "Workshops, stores and goods",
        [
            new("public", "Hall", 20, 13, 29), new("smithy", "Smithy", 10, 8, 16), new("bakery", "Bakery", 9, 8, 15), new("markethall", "Market hall", 18, 10, 26, LaterOnly), new("warehouse", "Warehouse", 22, 11, 31),
            new("windmill", "Windmill", 8, 8, 20, LaterOnly, "mill", "mill"), new("watermill", "Watermill", 14, 9, 21, Any, "mill", "wheel"), new("basilica", "Basilica", 26, 12, 36, OldOnly),
            new("baths", "Baths", 26, 14, 36, OldOnly), new("kiln", "Kiln", 4.5, 4.5, 8), new("stall", "Stall", ViewM: 6), new("barrels", "Barrels", ViewM: 4), new("crates", "Crates", ViewM: 4),
            new("sacks", "Sacks", ViewM: 4), new("jars", "Jars", ViewM: 4), new("wheels", "Wheels", ViewM: 5), new("logs", "Logs", ViewM: 6), new("vats", "Vats", ViewM: 6),
            new("racks", "Drying racks", ViewM: 6), new("pits", "Tan pits", ViewM: 7), new("signboard", "Signboard", ViewM: 4), new("anvil", "Anvil", ViewM: 4),
        ]),
        new("arms", "Arms", "Soldiers' buildings and gear",
        [
            new("barracks", "Barracks", 28, 9, 38), new("arsenal", "Arsenal", 18, 11, 26), new("magazine", "Magazine", 8, 6, 13), new("watchtower", "Watchtower", 5, 5, 9),
            new("keep", "Keep", 13, 13, 20), new("tent", "Tent", ViewM: 7), new("gun", "Gun", ViewM: 6, Era: LaterOnly), new("shot", "Shot", ViewM: 3, Era: LaterOnly),
            new("sentrybox", "Sentry box", ViewM: 4, Era: LaterOnly), new("flag", "Flag", ViewM: 12), new("stocks", "Stocks", ViewM: 5, Era: LaterOnly), new("gallows", "Gallows", ViewM: 7, Era: LaterOnly),
        ]),
        new("sacred", "Sacred", "Churches, temples and monuments",
        [
            new("church", "Church", 24, 11, 34), new("steeple", "Tower", 8, 8, 13, LaterOnly), new("shrine", "Shrine", ViewM: 5), new("cross", "Cross", ViewM: 9, Era: LaterOnly),
            new("marketcross", "Market cross", ViewM: 6, Era: LaterOnly), new("statue", "Statue", ViewM: 5), new("column", "Column", ViewM: 5), new("altar", "Altar", ViewM: 5, Era: OldOnly),
            new("obelisk", "Obelisk", ViewM: 5, Era: OldOnly), new("menhir", "Standing stone", ViewM: 5),
        ]),
        new("water", "Water", "Wells, basins and boats",
        [
            new("well", "Well", ViewM: 5), new("sweep", "Well sweep", ViewM: 11), new("pump", "Pump", ViewM: 5, Era: LaterOnly), new("fountain", "Fountain", ViewM: 7), new("cistern", "Cistern", ViewM: 7),
            new("wheel", "Waterwheel", ViewM: 8), new("boat", "Boat", ViewM: 8), new("ferryboat", "Ferry", ViewM: 10), new("nets", "Nets", ViewM: 7),
        ]),
        new("green", "Green", "Trees, stones and things about the house",
        [
            new("tree", "Tree", ViewM: 10), new("rock", "Rock", ViewM: 6), new("woodpile", "Woodpile", ViewM: 5), new("bench", "Bench", ViewM: 4), new("washing", "Washing", ViewM: 7, Era: LaterOnly),
        ]),
    ];

    public static readonly IReadOnlyList<Entry> All = Sections.SelectMany(s => s.Entries).ToList();

    public static Entry? Find(string? id) => All.FirstOrDefault(e => e.Id == id);

    public static bool Fits(Entry entry, bool ancient) => entry.Era == Any || entry.Era == (ancient ? OldOnly : LaterOnly);

    public static IEnumerable<Section> For(bool ancient, bool blocksOnly = false) =>
        Sections.Select(s => s with { Entries = s.Entries.Where(e => Fits(e, ancient) && (!blocksOnly || e.Block)).ToList() }).Where(s => s.Entries.Count > 0);
}
