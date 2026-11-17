namespace KTable.Core.Model;

public static class Markets
{
    public const string Stall = "stall", Booth = "booth", Trestle = "trestle", Rugs = "rugs", Baskets = "baskets", Jars = "jars", Parasol = "parasol", Hurdles = "hurdles",
        Kiosk = "kiosk", Barrow = "barrow", Cart = "cart";
    const double RowInM = 3.5, Gap = 0.8, Wander = 0.12, GoodsShare = 0.45, MiddleRowFrom = 0.5;
    const int MostWares = 14, MostBazaar = 24, BazaarSalt = 89;
    const double BazaarFromM = 150, BazaarShare = 0.4, BazaarStepM = 4, EdgeInM = 0.9;

    public static readonly string[] Wares = [Stall, Booth, Trestle, Rugs, Baskets, Parasol, Hurdles, Kiosk, Barrow];

    public static double LongM(string cls) => cls switch
    {
        Booth or Stall => 3.6,
        Trestle => 2.8,
        Rugs => 3.2,
        Baskets or Jars => 2.2,
        Parasol => 3,
        Hurdles => 6,
        Kiosk => 2.8,
        Barrow => 2,
        Cart => 3.6,
        _ => 3,
    };

    static (string Class, int Weight)[] Kit(int manner) => manner switch
    {
        Manners.Gothic => [(Booth, 3), (Trestle, 3), (Stall, 1), (Hurdles, 1), (Barrow, 1)],
        Manners.Rus => [(Booth, 4), (Trestle, 2), (Cart, 1), (Hurdles, 1)],
        Manners.Byzantine => [(Stall, 2), (Trestle, 2), (Jars, 2), (Baskets, 1)],
        Manners.Saracen => [(Rugs, 3), (Baskets, 3), (Stall, 1)],
        Manners.Mongol => [(Hurdles, 3), (Cart, 1)],
        Manners.Roman => [(Stall, 3), (Jars, 2), (Trestle, 2)],
        Manners.Tribal => [(Baskets, 2), (Hurdles, 2)],
        Manners.Persian => [(Rugs, 2), (Baskets, 2), (Stall, 1)],
        Manners.Egyptian => [(Baskets, 3), (Jars, 2), (Rugs, 1)],
        Manners.Chinese => [(Parasol, 3), (Trestle, 2), (Baskets, 1)],
        Manners.Northern => [(Booth, 3), (Trestle, 2), (Cart, 1)],
        Manners.Ottoman => [(Rugs, 2), (Stall, 2), (Baskets, 2)],
        Manners.American => [(Cart, 2), (Trestle, 2), (Stall, 1)],
        Manners.Japanese => [(Parasol, 2), (Trestle, 2), (Baskets, 1)],
        Manners.Western => [(Stall, 3), (Kiosk, 1), (Trestle, 1), (Barrow, 1)],
        Manners.Soviet => [(Trestle, 2), (Kiosk, 1), (Cart, 1), (Stall, 1)],
        Manners.NorthAfrican => [(Rugs, 2), (Baskets, 2), (Stall, 1)],
        Manners.Pacific => [(Baskets, 2), (Parasol, 1), (Rugs, 1)],
        Manners.Nippon => [(Parasol, 1), (Trestle, 2), (Barrow, 1)],
        _ => [(Stall, 3), (Trestle, 2), (Booth, 1), (Hurdles, 1)],
    };

    static string[] Goods(int manner) => manner switch
    {
        Manners.Saracen or Manners.Persian or Manners.Egyptian or Manners.NorthAfrican or Manners.Ottoman => ["sacks", "jars"],
        Manners.Roman or Manners.Byzantine => ["jars", "crates"],
        Manners.Tribal or Manners.Pacific or Manners.Mongol => ["sacks"],
        Manners.Chinese or Manners.Japanese or Manners.Nippon => ["sacks", "crates"],
        _ => ["barrels", "crates", "sacks"],
    };

    public static bool Fountained(int manner) =>
        manner is Manners.Gothic or Manners.Continental or Manners.Roman or Manners.Byzantine or Manners.Western or Manners.Soviet or Manners.Ottoman or Manners.NorthAfrican
            or Manners.Saracen;

    public static string Centre(int manner) => manner switch
    {
        Manners.Gothic or Manners.Chinese or Manners.Japanese or Manners.Nippon or Manners.Pacific or Manners.Egyptian => "well",
        Manners.Rus or Manners.Northern => "sweep",
        Manners.American => "pump",
        Manners.Persian => "cistern",
        Manners.Soviet => "statue",
        Manners.Tribal or Manners.Mongol => "trough",
        _ => "fountain",
    };

    public static string Pick(int manner, Dice dice)
    {
        var kit = Kit(manner);
        int roll = dice.Below(kit.Sum(k => k.Weight));
        foreach (var (cls, weight) in kit)
        {
            if (roll < weight) return cls;
            roll -= weight;
        }
        return kit[0].Class;
    }

    static double Heading(Spot way) => Math.Atan2(way.Y, way.X);

    static bool Streeted(BuiltSite site) => site.Ways.Open is Ways.NoPlace or Ways.Court || site.Manner is Manners.Persian or Manners.NorthAfrican;

    internal static void Bazaar(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        if (!Streeted(site) || within < site.M(BazaarFromM) || streets.Where(s => s.Laid && s.Points.Count > 1).MinBy(s => Settlements.FromStreets([s], centre)) is not { } street) return;
        var dice = BuiltSite.Roll(centre, BazaarSalt);
        var stood = Stood.Of(site, parts);
        double edge = Homes.HalfWidth(site, street.Class), half = edge - site.M(EdgeInM), whole = TownStreets.Long(street.Points), mid = whole / 2;
        int laid = 0;
        for (double on = Math.Max(0, mid - within * BazaarShare); on < Math.Min(whole, mid + within * BazaarShare) && laid < MostBazaar; on += site.M(BazaarStepM + dice.Between(0, 3)))
        {
            var (at, along) = TownStreets.Along(street.Points, on);
            double side = dice.Chance(0.5) ? 1 : -1;
            string cls = Pick(site.Manner, dice);
            if (cls is Hurdles or Cart) continue;
            if (!Streetlife.Fronted(site, stood, at, along.Left * side, edge)) side = -side;
            if (!Streetlife.Fronted(site, stood, at, along.Left * side, edge)) continue;
            parts.Add(BuiltSite.Sign(cls, at + along.Left * (side * half), Heading(along) + (side < 0 ? Math.PI : 0)));
            laid++;
        }
    }

    public static void Lay(BuiltSite site, Dice dice, Spot square, Spot along, Spot aside, double wide, double deep, bool town, List<BuiltPart> parts)
    {
        int wanted = Math.Min(MostWares, town ? 5 + dice.Below(6) : 1 + dice.Below(3)), laid = 0;
        var rows = new List<double> { 1, -1 };
        if (town && deep >= site.M(RowInM * 6) && dice.Chance(MiddleRowFrom)) rows.Add(0);
        var goods = Goods(site.Manner);
        int each = (wanted + rows.Count - 1) / rows.Count;
        foreach (double row in rows)
        {
            double reach = wide / 2 - site.M(RowInM), run = -reach + site.M(dice.Between(0, 2));
            int inRow = 0;
            while (laid < wanted && inRow++ < each && run < reach)
            {
                string cls = Pick(site.Manner, dice);
                double length = site.M(LongM(cls));
                if (run + length > reach) break;
                var at = square + along * (run + length / 2) + aside * (row * (deep / 2 - site.M(RowInM)) + site.M(dice.Between(-0.6, 0.6)));
                parts.Add(BuiltSite.Sign(cls, at, Heading(along) + dice.Between(-Wander, Wander) + (row < 0 ? Math.PI : 0)));
                if (cls is not (Hurdles or Kiosk or Cart) && dice.Chance(GoodsShare))
                    parts.Add(BuiltSite.Sign(goods[dice.Below(goods.Length)], at + aside * (row == 0 ? site.M(2) : -row * site.M(2)) + along * site.M(dice.Between(-1, 1)), dice.Between(0, Math.PI)));
                laid++;
                run += length + site.M(Gap + dice.Between(0, 2.4));
            }
        }
    }
}
