namespace KTable.Core.Model;

public static class Manners
{
    public const string Setting = "manner";
    public const int Continental = 0, English = 1, Northern = 2, Southern = 3, Roman = 4, Egyptian = 5, Tribal = 6;

    public static readonly IReadOnlyList<(string Id, string Name, string Tip)> All =
    [
        ("continental", "Continental", "Plastered houses under tile, some slate and thatch; steep roofs, a spire on the church"),
        ("english", "English", "Brick and slate, chimney stacks with their pots at the gable ends, a square church tower"),
        ("northern", "Northern", "Timber houses under thatch, shingle and turf; a wooden church with its domes"),
        ("southern", "Southern", "Low tiled roofs and flat whitewashed ones, courtyards, a bell tower beside the church"),
        ("roman", "Roman", "Tiled houses round an atrium, blocks round a court, a temple on its podium, a forum, baths"),
        ("egyptian", "Egyptian", "Flat mud-brick roofs, walled courts, a temple behind its pylon, granary domes, palms"),
        ("tribal", "Tribal", "Round thatched houses, a great house, raised granaries, standing stones"),
    ];

    public static bool Old(int manner) => manner >= Roman;

    public static IEnumerable<int> Of(bool ancient) => Enumerable.Range(0, All.Count).Where(m => Old(m) == ancient);

    public static int Of(string? id) => Math.Max(0, All.ToList().FindIndex(m => m.Id == id));

    public static string Id(int manner) => All[Math.Clamp(manner, 0, All.Count - 1)].Id;

    public static int For(bool ancient, int wanted) => wanted >= 0 && wanted < All.Count && Old(wanted) == ancient ? wanted : ancient ? Roman : Continental;

    public static int Of(BuiltThing thing) => Math.Clamp((int)thing.Setting(Setting, thing.Setting(Settlements.Old, 0) >= 1 ? Roman : Continental), 0, All.Count - 1);
}
