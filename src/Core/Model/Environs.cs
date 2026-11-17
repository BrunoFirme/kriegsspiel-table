namespace KTable.Core.Model;

static class Environs
{
    const double WellsApartM = 28, QanatFrom = 1.06, GardenM = 52, ChannelM = 1.5, GraveLongM = 54, GraveDeepM = 38, CypressApartM = 9, TownFromM = 200;
    const double MoundsFrom = 1.18, MoundsTo = 1.5, MoundsApartM = 22, GroveM = 19, ShrineLongM = 8, ShrineDeepM = 6, ToriiOffM = 24, TombsApartM = 16, TombsOffM = 9;
    const int MostWells = 14, MostMounds = 5, GroveTrees = 11, MostTombs = 7, MostCypresses = 14;

    const double SpitalM = 22, SpitalHallM = 20, SpitalGrand = 0.55, FishpondLongM = 26, FishpondDeepM = 12, FishpondsApartM = 4;

    public static void Lay(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        var roll = BuiltSite.Roll(centre, 211);
        if (site.Manner is Manners.Saracen or Manners.NorthAfrican or Manners.Persian || Slopes.Of(site.Manner).Fields == Terracing.Paddy) Gardens.Acequia(site, parts);
        switch (site.Manner)
        {
            case Manners.Persian:
                Garden(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Ottoman:
                Graveyard(site, roll, centre, within, streets, parts, taken);
                Standing(site, roll, streets, "fountain", 3, parts);
                break;
            case Manners.NorthAfrican:
                Graveyard(site, roll, centre, within, streets, parts, taken);
                Industry.Plantation(site, roll, centre, within, streets, parts, taken, false);
                Standing(site, roll, streets, "cistern", 2, parts);
                break;
            case Manners.Chinese or Manners.Tribal:
                Mounds(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Japanese or Manners.Nippon:
                Grove(site, roll, centre, within, streets, parts, taken);
                Graveyard(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Western or Manners.Soviet:
                Graveyard(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Roman:
                Tombs(site, roll, centre, within, streets, parts);
                Standing(site, roll, streets, "fountain", 4, parts);
                break;
            case Manners.Gothic:
                Spital(site, roll, centre, within, streets, parts, taken);
                Fishponds(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Rus:
                Standing(site, roll, streets, "cross", 2, parts);
                break;
            case Manners.Byzantine:
                Graveyard(site, roll, centre, within, streets, parts, taken);
                Standing(site, roll, streets, "shrine", 2, parts);
                break;
            case Manners.Saracen:
                Graveyard(site, roll, centre, within, streets, parts, taken);
                Garden(site, roll, centre, within, streets, parts, taken);
                Standing(site, roll, streets, "fountain", 4, parts);
                break;
            case Manners.Mongol:
                Mounds(site, roll, centre, within, streets, parts, taken);
                break;
        }
    }

    static void Standing(BuiltSite site, Dice roll, List<Settlements.Street> streets, string sign, int most, List<BuiltPart> parts)
    {
        var ways = streets.Where(s => s.Laid && s.Points.Count > 1).ToList();
        for (int i = 0; i < most && ways.Count > 0; i++)
        {
            var way = ways[roll.Below(ways.Count)];
            int leg = roll.Below(way.Points.Count - 1);
            Spot a = way.Points[leg], b = way.Points[leg + 1];
            var at = a + (b - a) * roll.Between(0.2, 0.8) + (b - a).Unit.Left * (Homes.HalfWidth(site, way.Class) + site.M(1.2));
            if (site.Buildable(at)) parts.Add(BuiltSite.Sign(sign, at));
        }
    }

    static void Garden(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Industry.Edge(site, roll, centre, within, site.M(GardenM * 0.75), taken, streets, 1.02, 1.3) is not { } at) return;
        if (Gardens.Terraced(site, at, parts, taken)) return;
        Spot along = (at - centre).Unit, aside = along.Left;
        double half = site.M(GardenM / 2);
        var ring = Industry.Rect(at, along, half * 2, half * 2);
        if (ring.Any(p => !site.Free(p, 0))) return;
        parts.Add(BuiltSite.Area("garden", ring));
        parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
        parts.Add(BuiltSite.Line("ditch", [at - along * (half - site.M(ChannelM)), at + along * (half - site.M(ChannelM))]));
        parts.Add(BuiltSite.Line("ditch", [at - aside * (half - site.M(ChannelM)), at + aside * (half - site.M(ChannelM))]));
        parts.Add(BuiltSite.Sign("fountain", at));
        foreach (var (u, v) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            foreach (double far in new[] { 0.3, 0.72 }) parts.Add(BuiltSite.Sign("tree", at + along * (u * half * far) + aside * (v * half * far)));
        taken.Add((at, half * 1.5));
        var fall = site.Downhill(at);
        var uphill = fall.Length > 0.01 ? fall.Unit * -1 : along;
        for (int i = 1; i <= MostWells; i++)
        {
            var well = at + uphill * (half + site.M(WellsApartM) * i);
            if (!site.Free(well, 0)) break;
            parts.Add(BuiltSite.Sign(Hallmarks.Qanat, well));
        }
    }

    static void Graveyard(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        using var tie = Groups.Open(parts);
        if (Industry.Edge(site, roll, centre, within, site.M(GraveLongM * 0.6), taken, streets, 1.03, 1.3) is not { } at) return;
        var along = (at - centre).Unit.Left;
        var ring = Industry.Rect(at, along, site.M(GraveLongM), site.M(GraveDeepM));
        if (ring.Any(p => !site.Free(p, 0))) return;
        parts.Add(BuiltSite.Area("churchyard", ring));
        int cols = (int)(GraveLongM / CypressApartM), rows = (int)(GraveDeepM / CypressApartM);
        for (int i = 0, laid = 0; i < cols * rows && laid < MostCypresses; i++)
        {
            if (!roll.Chance(0.5)) continue;
            parts.Add(BuiltSite.Sign("tree", ring[0] + along * site.M((i % cols + 0.5) * CypressApartM) + along.Left * site.M((i / cols + 0.5) * CypressApartM)));
            laid++;
        }
        taken.Add((at, site.M(GraveLongM * 0.6)));
    }

    static void Mounds(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Industry.Edge(site, roll, centre, within, site.M(MoundsApartM * 1.5), taken, streets, MoundsFrom, MoundsTo) is not { } at) return;
        int mounds = 2 + roll.Below(MostMounds - 1);
        for (int i = 0; i < mounds; i++)
        {
            var mound = at + Spot.Toward(roll.Between(0, 2 * Math.PI)) * (site.M(MoundsApartM) * Math.Sqrt(i));
            if (site.Free(mound, 0)) parts.Add(BuiltSite.Sign("mound", mound, roll.Between(0, 2 * Math.PI)));
        }
        taken.Add((at, site.M(MoundsApartM * 2)));
    }

    static void Grove(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Industry.Edge(site, roll, centre, within, site.M(GroveM * 1.3), taken, streets, 1.04, 1.3) is not { } at) return;
        var toward = (centre - at).Unit;
        for (int i = 0; i < GroveTrees; i++)
        {
            double bearing = Math.Atan2(toward.Y, toward.X) + 0.5 + i * (2 * Math.PI - 1) / (GroveTrees - 1);
            parts.Add(BuiltSite.Sign("tree", at + Spot.Toward(bearing) * site.M(GroveM * roll.Between(0.75, 1.1))));
        }
        parts.Add(BuiltSite.Block("public", at, toward.Left, site.M(ShrineLongM), site.M(ShrineDeepM)));
        parts.Add(BuiltSite.Sign("torii", at + toward * site.M(ToriiOffM), Math.Atan2(toward.Left.Y, toward.Left.X)));
        parts.Add(BuiltSite.Line("track", [at + toward * site.M(ShrineDeepM), at + toward * site.M(ToriiOffM + 8)]));
        taken.Add((at, site.M(GroveM * 1.3)));
    }

    static void Spital(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        using var tie = Groups.Open(parts);
        if (within < site.M(TownFromM) || Industry.Edge(site, roll, centre, within, site.M(SpitalM), taken, streets, 1.15, 1.5) is not { } at) return;
        var east = new Spot(1, 0);
        var ring = Industry.Rect(at, east, site.M(SpitalM * 2), site.M(SpitalM * 1.4));
        if (ring.Any(p => !site.Free(p, 0))) return;
        parts.Add(BuiltSite.Area("churchyard", ring));
        parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
        Settlements.Chapel(site, at + east * site.M(SpitalM * 0.45), east, parts, SpitalGrand);
        parts.Add(BuiltSite.Block("public", at - east * site.M(SpitalM * 0.5), east, site.M(SpitalHallM), site.M(SpitalHallM * 0.45)));
        taken.Add((at, site.M(SpitalM * 1.2)));
    }

    static void Fishponds(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        using var tie = Groups.Open(parts);
        if (Industry.Edge(site, roll, centre, within, site.M(FishpondLongM * 1.6), taken, streets, 1.35, 1.7) is not { } at) return;
        if (!Ponds.Flat(site, at))
        {
            Gardens.Stews(site, at, within, parts, taken);
            return;
        }
        var along = (at - centre).Unit.Left;
        for (int i = -1; i <= 1; i++)
        {
            var ring = Industry.Rect(at + along * (i * site.M(FishpondLongM + FishpondsApartM)), along, site.M(FishpondLongM), site.M(FishpondDeepM));
            if (ring.All(p => site.Free(p, 0)) && Ponds.Level(site, ring)) parts.Add(BuiltSite.Area("pond", ring));
        }
        taken.Add((at, site.M(FishpondLongM * 1.6)));
    }

    static void Tombs(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        if (within < site.M(TownFromM) || streets.FirstOrDefault(s => s.Class != "lane" && s.Points.Count > 1) is not { } way) return;
        Spot end = way.Points[^1], along = (way.Points[^1] - way.Points[^2]).Unit;
        for (int i = 1; i <= MostTombs; i++)
        {
            var at = end + along * site.M(TombsApartM * i) + along.Left * (site.M(TombsOffM) * (i % 2 == 0 ? 1 : -1));
            if (site.Free(at, 0)) parts.Add(BuiltSite.Sign(roll.Chance(0.5) ? "altar" : "column", at));
        }
    }
}
