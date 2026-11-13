namespace KTable.Core.Model;

public static class Manners
{
    public const string Setting = "manner";
    public const int Continental = 0, Northern = 2, Roman = 4, Egyptian = 5, Tribal = 6;

    public static readonly IReadOnlyList<(int Number, string Id, string Name, string Tip)> All =
    [
        (Continental, "continental", "Continental", "Plaster, brick and stone under tile, some slate and thatch; steep roofs, a spire on the church"),
        (Northern, "northern", "Northern", "Timber houses under thatch, shingle and turf; a wooden church with its domes"),
        (Roman, "roman", "Roman", "Tiled houses round an atrium, blocks round a court, a temple on its podium, a forum, baths"),
        (Egyptian, "egyptian", "Egyptian", "Flat mud-brick roofs, walled courts, a temple behind its pylon, granary domes, palms"),
        (Tribal, "tribal", "Tribal", "Round thatched houses, a great house, raised granaries, standing stones"),
    ];

    public static bool Old(int manner) => manner >= Roman;

    public static IEnumerable<int> Of(bool ancient) => All.Select(m => m.Number).Where(m => Old(m) == ancient);

    public static int Known(int stored) => All.Any(m => m.Number == stored) ? stored : Continental;

    public static (int Number, string Id, string Name, string Tip) Named(int manner) => All.First(m => m.Number == Known(manner));

    public static int Of(string? id) => All.FirstOrDefault(m => m.Id == id).Number;

    public static string Id(int manner) => Named(manner).Id;

    public static int For(bool ancient, int wanted) => Old(Known(wanted)) == ancient ? Known(wanted) : ancient ? Roman : Continental;

    public static int Of(BuiltThing thing) => Known((int)thing.Setting(Setting, thing.Setting(Settlements.Old, 0) >= 1 ? Roman : Continental));
}
