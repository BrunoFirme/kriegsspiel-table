namespace KTable.Core.Model;

public static class WealthLooks
{
    const string Plain = "house";
    const int Corners = 8;

    static readonly string[] Dwellings = [Plain, "shop", "longhouse", Callings.TownHouse, Callings.Cottage, "villa", "domus", "insula", "taberna"];

    public static bool Dresses(string cls) => Dwellings.Contains(cls);

    public static string ClassOf(BuiltPart part, int manner, double perMetre)
    {
        if (PlaceZones.Level(part.Wealth) is not { } level || !Dresses(part.Class)) return part.Class;
        return level switch
        {
            WealthLevel.Rich => Callings.RichClass(manner) ?? part.Class,
            WealthLevel.Poor => Callings.PoorClass(manner, ShortM(part, perMetre)) ?? part.Class,
            _ => PlaceZones.Laid(part) == WealthLevel.Middling ? part.Class : Plain,
        };
    }

    static double ShortM(BuiltPart part, double perMetre)
    {
        if (part.At.Count < Corners || perMetre <= 0) return 0;
        var (along, deep) = BuiltThings.Sides(part);
        return Math.Min(along, deep) / perMetre;
    }
}
