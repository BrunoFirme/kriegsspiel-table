namespace KTable.Core.Model;

public static class Manners
{
    public const string Setting = "manner", Builders = "builders", Adorners = "adorners", Ward = "ward", WardShare = "wardshare";
    public const double LeastWard = 0.1, UsualWard = 0.25, MostWard = 0.5;
    const int Parts = 5;
    public const int Continental = 0, Northern = 2, Roman = 4, Egyptian = 5, Tribal = 6, Persian = 7, Chinese = 8, Ottoman = 9, American = 10, Japanese = 11;
    public const int Western = 12, Soviet = 13, NorthAfrican = 14, Pacific = 15, Nippon = 16;
    public const int Ancient = 0, Later = 1, Modern = 2;
    public const int West = 0, North = 1, NearEast = 2, South = 3, FarEast = 4;

    public readonly record struct Culture(int Number, string Id, string Name, string Tip, int Epoch, int Seat, int Kin);

    public static readonly IReadOnlyList<Culture> All =
    [
        new(Continental, "continental", "Continental", "Plaster, brick and stone under tile, some slate and thatch; steep roofs, a spire on the church", Later, West, Continental),
        new(Northern, "northern", "Northern", "Timber houses under thatch, shingle and turf; a wooden church with its domes", Later, North, Northern),
        new(Ottoman, "ottoman", "Ottoman", "Low tiled houses and walled courts, a mosque under lead domes with its minarets, a covered market", Later, NearEast, Continental),
        new(American, "american", "American", "Timber houses with porches on a grid of streets, a white church with its steeple, a courthouse on the square", Later, South, Northern),
        new(Japanese, "japanese", "Japanese", "Rows of narrow houses under grey tile and thatch, walled compounds, a temple hall with its pagoda", Later, FarEast, Northern),
        new(Roman, "roman", "Roman", "Tiled houses round an atrium, blocks round a court, a temple on its podium, a forum, baths", Ancient, West, Roman),
        new(Tribal, "tribal", "Tribal", "Round thatched houses, a great house, raised granaries, standing stones", Ancient, North, Tribal),
        new(Persian, "persian", "Persian", "Mud-brick courts and domed houses, a columned hall on its terrace, walled gardens, cypresses", Ancient, NearEast, Egyptian),
        new(Egyptian, "egyptian", "Egyptian", "Flat mud-brick roofs, walled courts, a temple behind its pylon, granary domes, palms", Ancient, South, Egyptian),
        new(Chinese, "chinese", "Chinese", "Courtyard houses in ranks under grey tile, a hall with tiered roofs and a pagoda, streets on a grid", Ancient, FarEast, Roman),
        new(Western, "western", "Continental", "Blocks of flats round their courts, tiled houses, works with their chimneys, a church", Modern, West, Continental),
        new(Soviet, "soviet", "Soviet", "Log houses along one street under shingle and tin, great barns, an onion-domed church", Modern, North, Northern),
        new(NorthAfrican, "north-african", "North African", "Flat whitewashed houses round courts, blind alleys, a mosque with a square minaret, palms", Modern, NearEast, Continental),
        new(Pacific, "pacific", "Pacific", "Houses on stilts under palm thatch, a mission church, palms", Modern, South, Northern),
        new(Nippon, "nippon", "Japanese", "Wood and grey tile in tight lots, some tin roofs, a temple hall with its pagoda", Modern, FarEast, Northern),
    ];

    public static bool Old(int manner) => Named(manner).Epoch == Ancient;

    public static IEnumerable<int> In(int epoch) => All.Where(m => m.Epoch == epoch).Select(m => m.Number);

    public static int Known(int stored) => All.Any(m => m.Number == stored) ? stored : Continental;

    public static Culture Named(int manner) => All.First(m => m.Number == Known(manner));

    public static int Kin(int manner) => Named(manner).Kin;

    public static int Of(string? id) => All.FirstOrDefault(m => m.Id == id).Number;

    public static string Id(int manner) => Named(manner).Id;

    public static int For(int epoch, int wanted)
    {
        var asked = Named(wanted);
        return asked.Epoch == epoch ? asked.Number : All.First(m => m.Epoch == epoch && m.Seat == asked.Seat).Number;
    }

    public readonly record struct Sides(int Lays, int Builds, int Adorns);

    static int Side(int lays, double stored) => Named((int)stored).Epoch == Named(lays).Epoch ? Known((int)stored) : lays;

    public static Sides SidesOf(BuiltThing thing)
    {
        int lays = Of(thing);
        return new(lays, Side(lays, thing.Setting(Builders, lays)), Side(lays, thing.Setting(Adorners, lays)));
    }

    public readonly record struct Mix(int Lays, int Builds, int Adorns, int Ward, double Share)
    {
        public bool Pure => Builds == Lays && Adorns == Lays && Ward < 0;

        public string Code => Pure ? Id(Lays) : FormattableString.Invariant($"{Id(Lays)}+{Id(Builds)}+{Id(Adorns)}+{(Ward < 0 ? "" : Id(Ward))}+{Share:0.##}");

        public string Name => Named(Lays).Name + (Builds == Lays ? "" : ", " + Named(Builds).Name + " builders") + (Adorns == Builds ? "" : ", " + Named(Adorns).Name + " adorners")
            + (Ward < 0 ? "" : ", " + Named(Ward).Name + " quarter");

        public void Onto(BuiltThing thing)
        {
            if (Pure || Named(Of(thing)).Epoch != Named(Lays).Epoch) return;
            thing.Settings[Builders] = Builds;
            thing.Settings[Adorners] = Adorns;
            if (Ward < 0) return;
            thing.Settings[Manners.Ward] = Ward;
            thing.Settings[WardShare] = Share;
        }
    }

    public static Mix MixOf(BuiltThing thing)
    {
        var sides = SidesOf(thing);
        int ward = (int)thing.Setting(Ward, -1);
        bool warded = ward >= 0 && All.Any(m => m.Number == ward) && Named(ward).Epoch == Named(sides.Lays).Epoch;
        return new(sides.Lays, sides.Builds, sides.Adorns, warded ? ward : -1, warded ? Math.Clamp(thing.Setting(WardShare, UsualWard), LeastWard, MostWard) : 0);
    }

    public static Mix Parse(string? code)
    {
        var ids = (code ?? "").Split('+');
        if (ids.Length != Parts) return new(Of(ids[0]), Of(ids[0]), Of(ids[0]), -1, 0);
        int lays = Of(ids[0]);
        bool warded = ids[3].Length > 0 && Named(Of(ids[3])).Epoch == Named(lays).Epoch;
        double share = double.TryParse(ids[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double read) ? read : UsualWard;
        return new(lays, Side(lays, Of(ids[1])), Side(lays, Of(ids[2])), warded ? Of(ids[3]) : -1, warded ? Math.Clamp(share, LeastWard, MostWard) : 0);
    }

    public static int Of(BuiltThing thing) => Known((int)thing.Setting(Setting, thing.Setting(Settlements.Old, 0) >= 1 ? Roman : Continental));
}
