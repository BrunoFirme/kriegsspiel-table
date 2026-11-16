namespace KTable.Core.Model;

static class Industry
{
    const double StationLongM = 44, StationDeepM = 11, TrackOffM = 13, TrackAt = 1.12, TrackLong = 1.7, TrackReach = 1.6, HaltShare = 0.3, TownFrom = 0.25, CityFrom = 0.6;
    const double SidingM = 5.2, YardHalfM = 95, YardThroatM = 26, ShedLongM = 38, ShedDeepM = 11, ElevatorLongM = 30, ElevatorDeepM = 12, ForecourtM = 16;
    const double WorksPerM = 210, WorksApartM = 96, FactoryLeastM = 40, FactoryMostM = 72, FactoryDeepLeastM = 24, FactoryDeepMostM = 36, GasM = 20, EdgeAt = 1.1;
    const double RowLongLeastM = 44, RowLongMostM = 66, RowDeepM = 8.5, RowApartM = 17, FlatsLongM = 46, FlatsDeepM = 12;
    const double ByreLongM = 46, ByreDeepM = 10, ByreApartM = 17, MachineLongM = 30, MachineDeepM = 9, SiloM = 5;
    const double GroundLongM = 100, GroundDeepM = 62, PlotsLongM = 72, PlotsDeepM = 48, GravesLongM = 64, GravesDeepM = 46, SchoolLongM = 28, SchoolDeepM = 10;
    const double GroveLongM = 130, GroveDeepM = 84, PalmsApartM = 9, CopraLongM = 30, CopraDeepM = 12;
    const int TrackTries = 8, MostWorks = 5, MostPalms = 70, Bearings = 12, PlantCounts = 2;

    public static bool Outlying(string cls) => cls is "factory" or "gasholder" or "station" or "elevator" or "terrace" or "flats" or "goodsshed" or "dockshed" or "powerhouse" or "coolingtower";

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    internal static Spot[] Rect(Spot mid, Spot along, double wide, double deep) =>
        [mid - along * (wide / 2) - along.Left * (deep / 2), mid + along * (wide / 2) - along.Left * (deep / 2), mid + along * (wide / 2) + along.Left * (deep / 2), mid - along * (wide / 2)
            + along.Left * (deep / 2)];

    static bool Stood(BuiltSite site, string cls, Spot at, Spot along, double longM, double deepM, List<BuiltPart> parts, List<(Spot At, double Reach)> taken, double snug = 0.5)
    {
        double reach = site.M(Math.Max(longM, deepM)) * snug;
        if (!Room(site, taken, at, reach) || !site.Free(at + along * (site.M(longM) * 0.45), 0) || !site.Free(at - along * (site.M(longM) * 0.45), 0)) return false;
        parts.Add(BuiltSite.Block(cls, at, along, site.M(longM), site.M(deepM)));
        taken.Add((at, reach));
        return true;
    }

    internal static Spot? Edge(BuiltSite site, Dice roll, Spot centre, double within, double reach, List<(Spot At, double Reach)> taken, List<Settlements.Street> streets, double from = EdgeAt,
        double to = EdgeAt
        + 0.35)
    {
        double turn = roll.Between(0, 2 * Math.PI);
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < Bearings; i++)
            {
                var at = centre + Spot.Toward(turn + i * 2 * Math.PI / Bearings) * (within * Settlements.Lerp(from, to, ring / 2.0));
                if (Room(site, taken, at, reach) && Settlements.FromStreets(streets, at) > reach * 0.8) return at;
            }
        return null;
    }

    public static void Lay(BuiltSite site, Dice dice, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (!site.Modern) return;
        var roll = BuiltSite.Roll(centre, 131);
        bool town = site.Town > TownFrom;
        var line = site.Manner == Manners.Pacific ? null : Track(site, roll, centre, within, town, parts);
        Railworks.Taken(parts, taken);
        if (line is var (at, along)) Halt(site, at, along, centre, town, streets, parts, taken);
        if (site.Manner == Manners.Soviet) Collective(site, roll, centre, within, streets, parts, taken);
        if (site.Manner == Manners.Pacific) Plantation(site, roll, centre, within, streets, parts, taken, true);
        if (!town)
        {
            Village(site, roll, centre, within, streets, parts, taken);
            return;
        }
        int works = Math.Min(MostWorks, (int)Math.Round(site.Town * site.Ways.Industry * Math.Clamp(within / site.M(WorksPerM), 1, 4)));
        if (works > 0 && Plants.Heavy(site.Manner) && Plants.Lay(site, roll, centre, within, line, streets, parts, taken)) works = Math.Max(0, works - PlantCounts);
        for (int i = 0; i < works; i++) Works(site, roll, centre, within, line, i, streets, parts, taken);
        Civic(site, roll, centre, within, streets, parts, taken);
    }

    static (Spot At, Spot Along)? Track(BuiltSite site, Dice roll, Spot centre, double within, bool town, List<BuiltPart> parts)
    {
        (Spot At, Spot Along, double Reach)? known = null;
        foreach (var (a, b) in site.RailLegs)
        {
            var ab = b - a;
            var near = a + ab * Math.Clamp((centre - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if (known is not { } best || (near - centre).Length < best.Reach) known = (near, ab.Unit, (near - centre).Length);
        }
        if (known is { } found && found.Reach < within * TrackReach) return (found.At, found.Along);
        if (!town && !roll.Chance(HaltShare)) return null;
        double turn = roll.Between(0, 2 * Math.PI);
        (List<BuiltPart> Parts, Spot At, Spot Along)? heavy = null;
        for (int i = 0; i < TrackTries; i++)
        {
            Spot away = Spot.Toward(turn + i * 2 * Math.PI / TrackTries), along = away.Left, at = centre + away * (within * TrackAt);
            Spot from = at - along * (within * TrackLong), to = at + along * (within * TrackLong);
            if (!Enumerable.Range(0, 9).All(k => site.Buildable(from + (to - from) * (k / 8.0)))) continue;
            var laid = Railworks.Laid(site, [from, at, to], "railway", out bool light);
            if (!light)
            {
                heavy ??= (laid, at, along);
                continue;
            }
            parts.AddRange(laid);
            return (at, along);
        }
        if (heavy is not { } kept) return null;
        parts.AddRange(kept.Parts);
        return (kept.At, kept.Along);
    }

    static void Halt(BuiltSite site, Spot at, Spot along, Spot centre, bool town, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (along.Left.Dot(at - centre) < 0) along = along * -1;
        var inward = along.Left * -1;
        double scale = town ? 1 : 0.6;
        var station = at + inward * site.M(TrackOffM);
        if (!site.Free(station, site.M(StationDeepM))) return;
        parts.Add(BuiltSite.Block("station", station, along, site.M(StationLongM * scale), site.M(StationDeepM)));
        taken.Add((station, site.M(StationLongM * scale * 0.55)));
        var front = station + inward * site.M(StationDeepM / 2 + ForecourtM / 2);
        parts.Add(BuiltSite.Area("square", Rect(front, along, site.M(StationLongM * scale), site.M(ForecourtM))));
        taken.Add((front, site.M(ForecourtM * 0.6)));
        var (near, _, reach) = Settlements.Nearest(streets, front);
        if (streets.Count > 0 && reach > site.M(ForecourtM)) streets.Add(new Settlements.Street([front + inward * site.M(ForecourtM / 2), near], "street", true));
        parts.Add(BuiltSite.Sign("watertower", at + along * site.M(StationLongM * scale * 0.5 + 12) + inward * site.M(7)));
        if (!town) return;
        foreach (int track in new[] { 1, 2 })
        {
            var aside = along.Left * site.M(SidingM * track);
            parts.Add(BuiltSite.Line("railway", [at - along * site.M(YardHalfM + YardThroatM * track), at - along * site.M(YardHalfM) + aside, at + along * site.M(YardHalfM) + aside,
                at + along * site.M(YardHalfM + YardThroatM * track)]));
        }
        Stood(site, "goodsshed", at - along * site.M(StationLongM * 0.5 + ShedLongM * 0.5 + 14) + inward * site.M(TrackOffM - 2), along, ShedLongM, ShedDeepM, parts, taken, 0.4);
        if (site.Manner == Manners.Soviet)
            Stood(site, "elevator", at + along * site.M(YardHalfM * 0.7) + along.Left * site.M(SidingM * 2 + ElevatorDeepM / 2 + 4), along, ElevatorLongM, ElevatorDeepM, parts, taken, 0.4);
    }

    static void Works(BuiltSite site, Dice roll, Spot centre, double within, (Spot At, Spot Along)? line, int which, List<Settlements.Street> streets, List<BuiltPart> parts,
        List<(Spot At, double Reach)> taken)
    {
        double longM = roll.Between(FactoryLeastM, FactoryMostM), deepM = roll.Between(FactoryDeepLeastM, FactoryDeepMostM);
        Spot at, along;
        if (line is var (rail, way))
        {
            if (way.Left.Dot(rail - centre) < 0) way = way * -1;
            int side = which % 2 == 0 ? 1 : -1;
            along = way;
            at = rail + way * (side * site.M(StationLongM + 60 + WorksApartM * (which / 2))) - way.Left * site.M(TrackOffM + deepM / 2 + 2);
            if (!Room(site, taken, at, site.M(longM * 0.5))) return;
        }
        else
        {
            if (Edge(site, roll, centre, within, site.M(longM * 0.6), taken, streets) is not { } found) return;
            at = found;
            along = (found - centre).Unit.Left;
        }
        parts.Add(BuiltSite.Block("factory", at, along, site.M(longM), site.M(deepM)));
        taken.Add((at, site.M(longM * 0.55)));
        var inward = (centre - at).Unit;
        if (which == 0) Stood(site, "gasholder", at + along * site.M(longM / 2 + GasM * 0.8) + inward * site.M(4), along, GasM, GasM, parts, taken);
        string rows = site.Manner is Manners.Soviet ? "flats" : "terrace";
        if (site.Manner is Manners.NorthAfrican or Manners.Pacific) return;
        var aside = along.Left.Dot(inward) > 0 ? along.Left : along.Left * -1;
        for (int row = 0; row < 3; row++)
        {
            double rowLong = rows == "flats" ? FlatsLongM : roll.Between(RowLongLeastM, RowLongMostM);
            Stood(site, rows, at + aside * site.M(deepM / 2 + 14 + row * (rows == "flats" ? RowApartM * 1.5 : RowApartM)) + along * site.M(roll.Between(-8, 8)), along, rowLong,
                rows == "flats" ? FlatsDeepM : RowDeepM, parts, taken, 0.32);
        }
    }

    static void Civic(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        void Ground(string cls, double longM, double deepM, bool fenced)
        {
            if (Edge(site, roll, centre, within, site.M(longM * 0.6), taken, streets, 1.04, 1.3) is not { } at) return;
            var ring = Rect(at, (at - centre).Unit.Left, site.M(longM), site.M(deepM));
            if (ring.Any(p => !site.Free(p, 0))) return;
            parts.Add(BuiltSite.Area(cls, ring));
            if (fenced) parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
            taken.Add((at, site.M(longM * 0.6)));
        }
        if (site.Manner is not (Manners.Pacific or Manners.NorthAfrican)) Ground("allotments", PlotsLongM, PlotsDeepM, true);
        if (site.Town > CityFrom && site.Manner != Manners.Pacific) Ground("pitch", GroundLongM, GroundDeepM, false);
        if (site.Manner is Manners.Western or Manners.Soviet) Ground("churchyard", GravesLongM, GravesDeepM, true);
        if (Edge(site, roll, centre, within, site.M(SchoolLongM * 0.6), taken, streets, 0.45, 0.85) is { } school)
        {
            var along = Settlements.Nearest(streets, school).Along;
            if (Stood(site, "public", school, along, SchoolLongM, SchoolDeepM, parts, taken, 0.4)) parts.Add(BuiltSite.Area("square",
                Rect(school + along.Left * site.M(SchoolDeepM / 2 + 9), along, site.M(SchoolLongM), site.M(16))));
        }
    }

    static void Village(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (site.Manner is Manners.Pacific or Manners.NorthAfrican || Edge(site, roll, centre, within, site.M(MachineLongM * 0.6), taken, streets, 0.9, 1.15) is not { } at) return;
        var along = (at - centre).Unit.Left;
        if (!Stood(site, "cartshed", at, along, MachineLongM * 0.7, MachineDeepM, parts, taken)) return;
        parts.Add(BuiltSite.Block("silo", at + along * site.M(MachineLongM * 0.35 + SiloM), along, site.M(SiloM), site.M(SiloM)));
        parts.Add(BuiltSite.Sign("pump", at - along * site.M(MachineLongM * 0.35 + 4)));
    }

    static void Collective(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Edge(site, roll, centre, within, site.M(ByreLongM * 0.75), taken, streets, 1.02, 1.3) is not { } yard) return;
        Spot along = (yard - centre).Unit.Left, aside = along.Left;
        int byres = 3 + roll.Below(2);
        for (int i = 0; i < byres; i++)
        {
            var at = yard + aside * site.M((i - (byres - 1) / 2.0) * ByreApartM);
            if (site.Free(at, site.M(ByreDeepM))) parts.Add(BuiltSite.Block("byre", at, along, site.M(ByreLongM * roll.Between(0.85, 1)), site.M(ByreDeepM)));
        }
        var shed = yard + along * site.M(ByreLongM / 2 + MachineDeepM / 2 + 12);
        if (site.Free(shed, site.M(MachineDeepM))) parts.Add(BuiltSite.Block("cartshed", shed, aside, site.M(MachineLongM), site.M(MachineDeepM)));
        foreach (int k in new[] { 0, 1 })
            parts.Add(BuiltSite.Block("silo", yard - along * site.M(ByreLongM / 2 + SiloM + 4) + aside * site.M((k - 0.5) * SiloM * 1.4), along, site.M(SiloM), site.M(SiloM)));
        parts.Add(BuiltSite.Sign("cart", shed + along * site.M(MachineDeepM), roll.Between(0, 2 * Math.PI)));
        parts.Add(BuiltSite.Sign("dungheap", yard + aside * site.M(byres * ByreApartM / 2 + 6)));
        taken.Add((yard, site.M(ByreLongM * 0.75)));
    }

    internal static void Plantation(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken, bool shed)
    {
        if (Edge(site, roll, centre, within, site.M(GroveLongM * 0.55), taken, streets, 1.05, 1.4) is not { } grove) return;
        Spot along = (grove - centre).Unit.Left, aside = along.Left;
        var ring = Rect(grove, along, site.M(GroveLongM), site.M(GroveDeepM));
        if (ring.Any(p => !site.Free(p, 0))) return;
        parts.Add(BuiltSite.Area("plantation", ring));
        int cols = (int)(GroveLongM / PalmsApartM), rows = (int)(GroveDeepM / PalmsApartM);
        for (int i = 0, laid = 0; i < cols * rows && laid < MostPalms; i++)
        {
            if (cols * rows > MostPalms && !roll.Chance(MostPalms / (double)(cols * rows))) continue;
            parts.Add(BuiltSite.Sign("tree", ring[0] + along * site.M((i % cols + 0.5) * PalmsApartM) + aside * site.M((i / cols + 0.5) * PalmsApartM)));
            laid++;
        }
        taken.Add((grove, site.M(GroveLongM * 0.55)));
        if (shed) Stood(site, "dockshed", grove - aside * site.M(GroveDeepM / 2 + CopraDeepM), along, CopraLongM, CopraDeepM, parts, taken, 0.4);
    }
}
