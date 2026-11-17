namespace KTable.Core.Model;

public static class Streetlife
{
    public const string Puddle = "puddle", Gutter = "gutter", Donkeys = "donkeys", Camels = "camels", SteppingStones = "steppingstones";
    const double StepM = 8, EdgeInM = 0.6, MiddleShare = 0.35, StonesApartM = 70, StonesFromEndM = 18, FrontM = 3;
    static readonly string[] Wares = ["barrels", "jars", "sacks", "crates", "woodpile", "racks", "nets", "logs", "hens", Markets.Baskets, Markets.Barrow];
    const int Salt = 83;

    public static readonly string[] Beasts = [Donkeys, Camels];

    readonly record struct Life(string Class, double PerHundredM, bool Edge);

    static Life[] Of(int manner) => manner switch
    {
        Manners.Gothic => [new(Puddle, 2.2, false), new("well", 0.25, true), new("dungheap", 1.6, true), new("pigs", 0.7, false), new("hens", 0.5, true), new("geese", 0.3, false), new("woodpile", 0.6, true),
            new("barrels", 0.4, true), new("dog", 0.3, false), new(Markets.Barrow, 0.3, true), new("cart", 0.15, true)],
        Manners.Rus => [new(Puddle, 2.4, true), new("dungheap", 0.8, true), new("pigs", 0.4, false), new("geese", 0.6, false), new("hens", 0.4, true), new("woodpile", 1.2, true),
            new("logs", 0.3, true), new("cart", 0.3, true), new("dog", 0.3, false)],
        Manners.Byzantine => [new(Puddle, 0.4, false), new("jars", 0.5, true), new("hens", 0.3, true), new("dog", 0.5, false), new("barrels", 0.2, true), new(Donkeys, 0.3, false), new("sacks", 0.3, true)],
        Manners.Saracen => [new(Donkeys, 0.6, false), new("sacks", 0.5, true), new("jars", 0.4, true), new("dog", 0.3, false), new(Camels, 0.2, false)],
        Manners.Roman => [new("jars", 0.3, true), new("cart", 0.15, true), new("dog", 0.2, false), new(Donkeys, 0.2, false)],
        Manners.Tribal => [new(Puddle, 2, false), new("dungheap", 1.5, true), new("pigs", 0.6, false), new("cattle", 0.2, false), new("hens", 0.4, true), new("dog", 0.4, false), new("woodpile", 0.4, true)],
        Manners.Persian => [new(Donkeys, 0.4, false), new("sacks", 0.4, true), new("jars", 0.3, true), new(Camels, 0.15, false)],
        Manners.Egyptian => [new(Donkeys, 0.4, false), new("jars", 0.5, true), new("sacks", 0.4, true), new("geese", 0.3, false), new("dog", 0.3, false)],
        Manners.Chinese => [new(Markets.Barrow, 0.5, true), new("sacks", 0.4, true), new(Markets.Baskets, 0.3, true), new("cart", 0.2, true), new("dog", 0.2, false), new("pigs", 0.2, false)],
        Manners.Continental => [new(Puddle, 0.6, false), new("cart", 0.3, true), new("barrels", 0.3, true), new("dog", 0.2, false), new(Markets.Barrow, 0.2, true), new("woodpile", 0.3, true)],
        Manners.Northern => [new(Puddle, 1.8, false), new("sweep", 0.3, true), new("woodpile", 1, true), new("geese", 0.6, false), new("hens", 0.4, true), new("cart", 0.3, true), new("dungheap", 0.5, true), new("dog", 0.3, false)],
        Manners.Ottoman => [new(Donkeys, 0.4, false), new("sacks", 0.3, true), new("dog", 0.6, false), new("jars", 0.2, true), new("cart", 0.15, true)],
        Manners.American => [new(Puddle, 0.8, false), new("cart", 0.5, true), new("barrels", 0.4, true), new("horses", 0.2, false), new("dog", 0.2, false), new("crates", 0.3, true)],
        Manners.Japanese => [new(Markets.Barrow, 0.2, true), new("sacks", 0.3, true), new("crates", 0.2, true), new(Markets.Baskets, 0.3, true), new("dog", 0.1, false)],
        Manners.Western => [new(Hallmarks.AdColumn, 0.12, true), new("cart", 0.2, true), new(Markets.Barrow, 0.2, true), new("crates", 0.2, true), new("dog", 0.1, false)],
        Manners.Soviet => [new(Puddle, 1.6, false), new("pump", 0.3, true), new("cart", 0.4, true), new("geese", 0.4, false), new("hens", 0.3, true), new("woodpile", 0.6, true), new("dog", 0.3, false)],
        Manners.NorthAfrican => [new(Donkeys, 0.5, false), new("sacks", 0.4, true), new("jars", 0.3, true), new("dog", 0.2, false), new(Camels, 0.2, false)],
        Manners.Pacific => [new("racks", 0.4, true), new("hens", 0.6, true), new("pigs", 0.4, false), new("dog", 0.4, false), new("nets", 0.2, true), new(Markets.Baskets, 0.3, true)],
        Manners.Nippon => [new(Markets.Barrow, 0.3, true), new("crates", 0.3, true), new("sacks", 0.2, true)],
        _ => [],
    };

    static bool Guttered(int manner) => manner is Manners.Gothic or Manners.Continental;

    static double Heading(Spot way) => Math.Atan2(way.Y, way.X);

    internal static bool Fronted(BuiltSite site, Stood stood, Spot at, Spot aside, double half)
    {
        double front = site.M(FrontM + site.Ways.SetbackM) / 2;
        return stood.Fronted(at + aside * (half + front), front);
    }

    internal static void Lay(BuiltSite site, IEnumerable<Settlements.Street> streets, List<BuiltPart> parts)
    {
        var lives = Of(site.Manner);
        var stood = Stood.Of(site, parts);
        foreach (var street in streets.Where(s => s.Laid && s.Points.Count > 1))
        {
            var dice = BuiltSite.Roll(street.Points[0], Salt);
            double half = Homes.HalfWidth(site, street.Class), step = site.M(StepM), run = 0, stones = site.M(StonesFromEndM);
            double whole = Enumerable.Range(0, street.Points.Count - 1).Sum(i => (street.Points[i + 1] - street.Points[i]).Length);
            if (Guttered(site.Manner) && street.Class == "street") parts.Add(BuiltSite.Line(Gutter, street.Points));
            for (int i = 0; i + 1 < street.Points.Count; i++)
            {
                var leg = street.Points[i + 1] - street.Points[i];
                double length = leg.Length;
                if (length <= 0) continue;
                var way = leg.Unit;
                for (double on = run == 0 ? step / 2 : 0; on < length; on += step)
                {
                    var at = street.Points[i] + way * on;
                    double along = run + on;
                    if (site.Manner == Manners.Roman && street.Class == "street" && along >= stones && along < whole - site.M(StonesFromEndM))
                    {
                        parts.Add(BuiltSite.Sign(SteppingStones, at, Heading(way.Left)));
                        stones = along + site.M(StonesApartM);
                    }
                    foreach (var life in lives)
                    {
                        if (!dice.Chance(life.PerHundredM * StepM / 100)) continue;
                        double side = dice.Chance(0.5) ? 1 : -1;
                        if (life.Edge && Array.IndexOf(Wares, life.Class) >= 0)
                        {
                            if (!Fronted(site, stood, at, way.Left * side, half)) side = -side;
                            if (!Fronted(site, stood, at, way.Left * side, half)) continue;
                        }
                        var spot = at + way.Left * (life.Edge ? side * Math.Max(half - site.M(EdgeInM), half * 0.5) : dice.Between(-MiddleShare, MiddleShare) * half);
                        if (!site.Inside(spot) || site.KindAt(spot) == GroundKinds.Water) continue;
                        parts.Add(BuiltSite.Sign(life.Class, spot, life.Edge ? Heading(way) : dice.Between(0, 2 * Math.PI)));
                    }
                }
                run += length;
            }
        }
    }
}
