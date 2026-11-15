namespace KTable.Core.Model;

public static class Hallmarks
{
    public const string Theatre = "theatre", Aqueduct = "aqueduct", Qanat = "qanat", Sphinx = "sphinx", DrumTower = "drumtower", BellTower = "belltower", Paifang = "paifang",
        Parterre = "parterre", Bedesten = "bedesten", FireTower = "firetower", Lantern = "lantern", Hitch = "hitch", Tramway = "tramway", AdColumn = "adcolumn", SchoolYard = "schoolyard",
        Memorial = "memorial", Shelter = "shelter", Garage = "garage", FuelPump = "fuelpump", Tractor = "tractor", Boardwalk = "boardwalk";
    const double FillingStepM = 10, FillingAt = 0.8, GarageLongM = 12, GarageDeepM = 8, FarmYardLongM = 90, FarmYardDeepM = 60, BoardwalkInM = 1;
    const double TownFromM = 200, CityFromM = 380, TheatreM = 28, AqueductFrom = 0.78, AqueductTo = 2.1, QanatFrom = 1.05, QanatTo = 2.6, ShaftsApartM = 26, DovecoteM = 7;
    const double SphinxApartM = 6.5, SphinxAsideM = 5, DromosFromM = 9, LakeLongM = 34, LakeDeepM = 18, DrumLongM = 26, DrumDeepM = 17, BellM = 15, KraalM = 15, ParterreLongM = 70;
    const double ParterreDeepM = 46, PalaceLongM = 46, PalaceDeepM = 14, AvenueApartM = 12, BedestenLongM = 32, BedestenDeepM = 21, TurbeM = 8, LanternAsideM = 3.2, HitchOffM = 2.2;
    const double RoomFromStreets = 0.55, MaraboutM = 7, PalmsApartM = 10, PalmsFrom = 0.4, MeetingLongM = 20, MeetingDeepM = 11, ShoreStepM = 6, YardLongM = 60, YardDeepM = 40;
    const double SchoolLongM = 44, SchoolDeepM = 12, SentoM = 16, TempleRoomM = 22, LeastM = 60;
    const int MostCanoes = 6;
    const int Rings = 6, Bearings = 12, MostSphinxes = 9, MostArches = 3, MostTowers = 3, MostHitches = 6, AvenueTrees = 7, Salt = 233;

    static double Heading(Spot way) => Math.Atan2(way.Y, way.X);

    static void Stand(BuiltSite site, List<BuiltPart> parts, BuiltPart block)
    {
        if (BuiltGeometry.Points(block.At).Select(Spot.Of).All(corner => site.Buildable(corner) && !site.OnWay(corner, 0))) parts.Add(block);
    }

    static bool Room(BuiltSite site, List<(Spot At, double Reach)> taken, Spot p, double reach) => site.Free(p, reach) && !taken.Any(o => (o.At - p).Length < o.Reach + reach);

    static Spot Uphill(BuiltSite site, Spot centre, double reach) =>
        Enumerable.Range(0, Bearings).Select(i => Spot.Toward(i * 2 * Math.PI / Bearings)).MaxBy(way => site.HeightAt(centre + way * reach) + (site.Inside(centre + way * reach) ? 0 : -1e9));

    internal static void Lay(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (within < site.M(LeastM)) return;
        var roll = BuiltSite.Roll(centre, Salt);
        bool town = within >= site.M(TownFromM);
        switch (site.Manner)
        {
            case Manners.Roman when town:
                Stage(site, roll, centre, within, streets, parts, taken);
                Aqueducts(site, centre, within, parts);
                break;
            case Manners.Persian:
                Qanats(site, roll, centre, within, parts);
                Dovecotes(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Egyptian:
                Dromos(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Chinese when town:
                Towers(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Tribal:
                Kraal(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.NorthAfrican:
                Marabouts(site, roll, centre, within, streets, parts, taken);
                if (town) Settlements.Colonnade(site, streets, centre, within, parts);
                break;
            case Manners.Pacific:
                Meeting(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Nippon or Manners.Western or Manners.Soviet or Manners.NorthAfrican when town:
                School(site, roll, centre, within, streets, parts, taken);
                if (site.Manner == Manners.Nippon) Sento(site, roll, centre, within, streets, parts, taken);
                if (site.Manner != Manners.Soviet) Filling(site, roll, centre, within, streets, parts, taken);
                if (site.Manner == Manners.Soviet) Kolkhoz(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Soviet:
                Kolkhoz(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Continental when town:
                Garden(site, roll, centre, within, streets, parts, taken);
                break;
            case Manners.Ottoman when town:
                Bazaar(site, roll, centre, within, streets, parts, taken);
                break;
        }
    }

    internal static void Dress(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        if (within < site.M(LeastM)) return;
        var roll = BuiltSite.Roll(centre, Salt + 1);
        switch (site.Manner)
        {
            case Manners.Chinese when within >= site.M(TownFromM):
                Arches(site, roll, centre, within, streets, parts);
                break;
            case Manners.Japanese or Manners.Nippon:
                Watch(site, roll, centre, within, streets, parts);
                Lanterns(parts, site);
                if (site.Modern && within >= site.M(TownFromM)) Shelters(site, roll, centre, within, streets, parts);
                break;
            case Manners.NorthAfrican:
                Palms(site, centre, within, streets, parts);
                break;
            case Manners.Pacific:
                Canoes(site, roll, centre, within, parts);
                break;
            case Manners.American:
                Hitches(site, centre, parts);
                break;
            case Manners.Western when within >= site.M(CityFromM):
                Tramways(streets, parts);
                Shelters(site, roll, centre, within, streets, parts);
                break;
            case Manners.Western when within >= site.M(TownFromM):
                Shelters(site, roll, centre, within, streets, parts);
                break;
            case Manners.Soviet:
                Boardwalks(site, streets, parts);
                if (within >= site.M(TownFromM)) Shelters(site, roll, centre, within, streets, parts);
                break;
        }
    }

    static void Stage(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Roomiest(site, centre, within * 0.3, within * 0.8, site.M(TheatreM), taken, streets) is not { } at) return;
        var facing = Spot.Toward(roll.Between(0, 2 * Math.PI));
        const int steps = 14;
        var bowl = Enumerable.Range(0, steps + 1).Select(i => at + Spot.Toward(Heading(facing) + Math.PI / 2 + i * Math.PI / steps) * site.M(TheatreM)).ToList();
        parts.Add(BuiltSite.Area(Theatre, bowl));
        taken.Add((at, site.M(TheatreM)));
    }

    static Spot? Roomiest(BuiltSite site, Spot centre, double from, double to, double reach, List<(Spot At, double Reach)> taken, List<Settlements.Street> streets)
    {
        Spot? best = null;
        double most = reach * RoomFromStreets;
        for (int ring = 0; ring < Rings; ring++)
            for (int i = 0; i < Bearings * 2; i++)
            {
                var at = centre + Spot.Toward(i * Math.PI / Bearings + ring * 0.37) * (from + (to - from) * ring / (Rings - 1.0));
                double clear = Settlements.FromStreets(streets, at);
                if (clear <= most || !Room(site, taken, at, reach * RoomFromStreets)) continue;
                (best, most) = (at, clear);
            }
        return best;
    }

    static void Aqueducts(BuiltSite site, Spot centre, double within, List<BuiltPart> parts)
    {
        var way = Uphill(site, centre, within * AqueductTo);
        Spot from = centre + way * (within * AqueductTo), to = centre + way * (within * AqueductFrom);
        if (!site.Inside(from)) from = centre + way * (within * (AqueductFrom + 0.6));
        var bend = (from + to) * 0.5 + way.Left * site.M(18);
        parts.Add(BuiltSite.Line(Aqueduct, [from, bend, to]));
        parts.Add(BuiltSite.Sign("cistern", to - way * site.M(4), Heading(way)));
    }

    static void Qanats(BuiltSite site, Dice roll, Spot centre, double within, List<BuiltPart> parts)
    {
        var way = Uphill(site, centre, within * QanatTo);
        int runs = 1 + roll.Below(2);
        for (int r = 0; r < runs; r++)
        {
            var line = way.Turned(roll.Between(-0.5, 0.5));
            for (double far = within * QanatFrom; far < within * QanatTo; far += site.M(ShaftsApartM))
            {
                var shaft = centre + line * far + line.Left * site.M(Math.Sin(far / site.M(90)) * 12);
                if (site.Inside(shaft) && site.Buildable(shaft)) parts.Add(BuiltSite.Sign(Qanat, shaft, Heading(line)));
            }
        }
    }

    static void Dovecotes(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        for (int i = 0; i < 2 + roll.Below(3); i++)
            if (Callings.Found(site, roll, centre, within * 1.1, within * 1.6, site.M(DovecoteM * 1.5), taken, streets) is { } at)
            {
                Stand(site, parts, BuiltSite.Block("dovecote", at, new Spot(1, 0), site.M(DovecoteM), site.M(DovecoteM)));
                taken.Add((at, site.M(DovecoteM * 1.5)));
            }
    }

    static (Spot At, Spot Along, double Long, double Deep) Laid(BuiltPart block)
    {
        var p = BuiltGeometry.Points(block.At).Select(Spot.Of).ToList();
        return ((p[0] + p[2]) * 0.5, (p[1] - p[0]).Unit, (p[1] - p[0]).Length, (p[3] - p[0]).Length);
    }

    static BuiltPart? Temple(List<BuiltPart> parts) => parts.Where(p => p.Kind == PartKinds.Block && p.Class == "church" && p.At.Count == 8).MaxBy(p => Laid(p).Long);

    static void Dromos(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Temple(parts) == null && Roomiest(site, centre, 0, within * 0.6, site.M(TempleRoomM), taken, streets) is { } ground && Spot.Toward(roll.Between(0, Math.PI)) is var axis)
            Settlements.Church(site, ground, axis, axis.Left, centre, within, parts, taken);
        if (Temple(parts) is not { } temple) return;
        var (at, along, length, deep) = Laid(temple);
        int pairs = 0;
        for (; pairs < MostSphinxes; pairs++)
        {
            var on = at + along * (length / 2 + site.M(DromosFromM + pairs * SphinxApartM));
            if (!site.Free(on, site.M(SphinxAsideM))) break;
            foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Sign(Sphinx, on + along.Left * (side * site.M(SphinxAsideM)), Heading(along.Left * -side)));
        }
        double avenue = site.M(DromosFromM + pairs * SphinxApartM);
        if (pairs > 0) taken.Add((at + along * (length / 2 + avenue / 2), avenue / 2 + site.M(SphinxAsideM)));
        var lake = at + along.Left * (deep / 2 + site.M(LakeDeepM / 2 + 10));
        if (Industry.Rect(lake, along, site.M(LakeLongM), site.M(LakeDeepM)) is var ring && ring.All(p => site.Free(p, 0)) && Room(site, taken, lake, site.M(LakeLongM / 2)))
        {
            parts.Add(BuiltSite.Area("pond", ring));
            taken.Add((lake, site.M(LakeLongM / 2)));
        }
    }

    static void Towers(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        foreach (var (cls, longM, deepM, off) in new[] { (DrumTower, DrumLongM, DrumDeepM, 0.22), (BellTower, BellM, BellM, -0.22) })
        {
            var at = centre + new Spot(0, within * off);
            if (!Room(site, taken, at, site.M(longM / 2)) && Callings.Found(site, roll, at, site.M(10), within * 0.3, site.M(longM / 2), taken, streets) is { } moved) at = moved;
            if (!Room(site, taken, at, site.M(longM / 2))) continue;
            Stand(site, parts, BuiltSite.Block(cls, at, new Spot(1, 0), site.M(longM), site.M(deepM)));
            taken.Add((at, site.M(longM / 2)));
        }
    }

    static void Arches(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        int laid = 0;
        foreach (var street in streets.Where(s => s.Main && s.Laid && s.Points.Count > 1).OrderBy(_ => roll.Next()))
        {
            if (laid >= MostArches) break;
            var (at, along) = TownStreets.Along(street.Points, TownStreets.Long(street.Points) * roll.Between(0.3, 0.7));
            if ((at - centre).Length > within * 0.7) continue;
            parts.Add(BuiltSite.Sign(Paifang, at, Heading(along.Left)));
            laid++;
        }
    }

    static void Kraal(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, within * 0.1, within * 0.5, site.M(KraalM), taken, streets) is not { } at) return;
        var ring = Enumerable.Range(0, 12).Select(i => at + Spot.Toward(i * Math.PI / 6) * site.M(KraalM * (0.9 + 0.1 * Math.Sin(i * 1.7)))).ToList();
        parts.Add(BuiltSite.Area("pen", ring));
        parts.Add(BuiltSite.Line("fence", ring.Append(ring[0])));
        parts.Add(BuiltSite.Sign("cattle", at, roll.Between(0, 2 * Math.PI)));
        taken.Add((at, site.M(KraalM)));
    }

    static void Garden(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, within * 0.85, within * 1.2, site.M(ParterreLongM * 0.6), taken, streets) is not { } at) return;
        var outward = (at - centre).Unit;
        var ring = Industry.Rect(at, outward.Left, site.M(ParterreLongM), site.M(ParterreDeepM));
        if (ring.Any(p => !site.Free(p, 0))) return;
        parts.Add(BuiltSite.Area(Parterre, ring));
        var palace = at - outward * site.M(ParterreDeepM / 2 + PalaceDeepM / 2 + 3);
        Stand(site, parts, BuiltSite.Block("public", palace, outward.Left, site.M(PalaceLongM), site.M(PalaceDeepM)));
        for (int i = 1; i <= AvenueTrees; i++)
            foreach (int side in new[] { 1, -1 })
            {
                var tree = at + outward * site.M(ParterreDeepM / 2 + i * AvenueApartM) + outward.Left * (side * site.M(7));
                if (site.Free(tree, 0)) parts.Add(BuiltSite.Sign("tree", tree));
            }
        taken.Add((at, site.M(ParterreLongM * 0.6)));
        taken.Add((palace, site.M(PalaceLongM / 2)));
    }

    static void Bazaar(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, within * 0.05, within * 0.4, site.M(BedestenLongM / 2), taken, streets) is { } hall)
        {
            Stand(site, parts, BuiltSite.Block(Bedesten, hall, Spot.Toward(roll.Between(0, Math.PI)), site.M(BedestenLongM), site.M(BedestenDeepM)));
            taken.Add((hall, site.M(BedestenLongM / 2)));
        }
        if (parts.Where(p => p.Kind == PartKinds.Block && p.Class == "church" && p.At.Count == 8).MaxBy(p => Laid(p).Long) is not { } mosque) return;
        var (at, along, length, deep) = Laid(mosque);
        var turbe = at - along * (length / 2 + site.M(TurbeM / 2 + 4)) + along.Left * (deep * 0.3);
        if (Room(site, taken, turbe, site.M(TurbeM / 2))) Stand(site, parts, BuiltSite.Block(Suburbs.Tomb, turbe, along, site.M(TurbeM), site.M(TurbeM)));
    }

    static void Watch(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        int towers = Math.Clamp((int)(within / site.M(160)), 1, MostTowers);
        foreach (var street in streets.Where(s => s.Laid && s.Points.Count > 1).OrderBy(_ => roll.Next()).Take(towers))
        {
            var (at, along) = TownStreets.Along(street.Points, TownStreets.Long(street.Points) * roll.Between(0.2, 0.8));
            var foot = at + along.Left * (Homes.HalfWidth(site, street.Class) - site.M(1.2));
            if ((foot - centre).Length < within) parts.Add(BuiltSite.Sign(FireTower, foot, Heading(along)));
        }
    }

    static void Lanterns(List<BuiltPart> parts, BuiltSite site)
    {
        foreach (var gate in parts.Where(p => p.Kind == PartKinds.Sign && p.Class == "torii").ToList())
        {
            var at = Spot.Of(BuiltGeometry.Points(gate.At).First());
            var across = Spot.Toward(gate.Turn * Math.PI / 1800);
            foreach (int side in new[] { 1, -1 }) parts.Add(BuiltSite.Sign(Lantern, at + across * (side * site.M(LanternAsideM)) + across.Left * site.M(3)));
            if (site.Modern) parts.Add(BuiltSite.Sign(Memorial, at - across.Left * site.M(6) + across * site.M(LanternAsideM * 2)));
        }
    }

    static void Hitches(BuiltSite site, Spot centre, List<BuiltPart> parts)
    {
        foreach (var store in parts.Where(p => p.Kind == PartKinds.Block && p.Class is "shop" or "inn" or "tavern" or "public" && p.At.Count == 8)
                     .OrderBy(p => (Laid(p).At - centre).Length).Take(MostHitches).ToList())
        {
            var points = BuiltGeometry.Points(store.At).Select(Spot.Of).ToList();
            Spot front = (points[0] + points[1]) * 0.5, outward = (points[0] - points[3]).Unit;
            var rail = front + outward * site.M(HitchOffM);
            parts.Add(BuiltSite.Sign(Hitch, rail, Heading(points[1] - points[0])));
            parts.Add(BuiltSite.Sign("horses", rail + outward * site.M(2), Heading(outward)));
        }
    }

    static void Marabouts(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        for (int i = 0; i < 2 + roll.Below(3); i++)
        {
            if (Callings.Found(site, roll, centre, within * 1.1, within * 1.7, site.M(MaraboutM * 2), taken, streets) is not { } at) continue;
            Stand(site, parts, BuiltSite.Block(Suburbs.Tomb, at, Spot.Toward(roll.Between(0, Math.PI)), site.M(MaraboutM), site.M(MaraboutM)));
            for (int k = 0; k < 2 + roll.Below(3); k++) parts.Add(BuiltSite.Sign("tree", at + Spot.Toward(roll.Between(0, 2 * Math.PI)) * site.M(roll.Between(6, 11))));
            taken.Add((at, site.M(MaraboutM * 2)));
        }
    }

    static void Palms(BuiltSite site, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        if (streets.Where(s => s.Main && s.Laid && s.Points.Count > 1).MaxBy(s => TownStreets.Long(s.Points)) is not { } avenue) return;
        double half = Homes.HalfWidth(site, avenue.Class) - site.M(1);
        for (double on = 0; on < TownStreets.Long(avenue.Points); on += site.M(PalmsApartM))
        {
            var (at, along) = TownStreets.Along(avenue.Points, on);
            if ((at - centre).Length < within * PalmsFrom) continue;
            foreach (int side in new[] { 1, -1 })
                if (site.Free(at + along.Left * (side * half), 0)) parts.Add(BuiltSite.Sign("tree", at + along.Left * (side * half)));
        }
    }

    static void Meeting(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, site.M(10), within * 0.4, site.M(MeetingLongM / 2), taken, streets) is not { } at) return;
        Stand(site, parts, BuiltSite.Block("greathouse", at, Spot.Toward(roll.Between(0, Math.PI)), site.M(MeetingLongM), site.M(MeetingDeepM)));
        taken.Add((at, site.M(MeetingLongM / 2)));
    }

    static void Canoes(BuiltSite site, Dice roll, Spot centre, double within, List<BuiltPart> parts)
    {
        int laid = 0;
        for (int i = 0; i < Bearings * 4 && laid < MostCanoes; i++)
        {
            var way = Spot.Toward(i * Math.PI / (Bearings * 2));
            for (double far = within * 0.3; far < within * 1.3; far += site.M(ShoreStepM))
            {
                var at = centre + way * far;
                if (!site.Inside(at) || site.KindAt(at + way * site.M(ShoreStepM)) != GroundKinds.Water) continue;
                if (site.Buildable(at) && roll.Chance(0.5)) parts.Add(BuiltSite.Sign("boat", at, Heading(way) + roll.Between(-0.3, 0.3)));
                laid++;
                break;
            }
        }
    }

    static void School(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, within * 0.75, within * 1.1, site.M(YardLongM / 2), taken, streets) is { } at)
        {
            var along = Spot.Toward(roll.Between(0, Math.PI));
            parts.Add(BuiltSite.Area(SchoolYard, Industry.Rect(at, along, site.M(YardLongM), site.M(YardDeepM))));
            Stand(site, parts, BuiltSite.Block("public", at + along.Left * site.M(YardDeepM / 2 + SchoolDeepM / 2 + 2), along, site.M(SchoolLongM), site.M(SchoolDeepM)));
            taken.Add((at, site.M(YardLongM / 2 + SchoolDeepM)));
        }
    }

    static void Sento(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, within * 0.2, within * 0.7, site.M(SentoM * 0.7), taken, streets) is { } sento)
        {
            Stand(site, parts, BuiltSite.Block("baths", sento, Spot.Toward(roll.Between(0, Math.PI)), site.M(SentoM), site.M(SentoM * 0.7)));
            taken.Add((sento, site.M(SentoM * 0.7)));
        }
    }

    static void Filling(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        foreach (var street in streets.Where(s => s.Main && s.Laid && s.Points.Count > 1).OrderBy(_ => roll.Next()))
        {
            double whole = TownStreets.Long(street.Points);
            for (double on = 0; on < whole; on += site.M(FillingStepM))
            {
                var (at, along) = TownStreets.Along(street.Points, on);
                if (Math.Abs((at - centre).Length - within * FillingAt) > within * 0.1) continue;
                var side = along.Left * (roll.Chance(0.5) ? 1 : -1);
                var yard = at + side * (Homes.HalfWidth(site, street.Class) + site.M(GarageDeepM));
                if (!Room(site, taken, yard, site.M(GarageLongM * 0.7))) continue;
                Stand(site, parts, BuiltSite.Block(Garage, yard + side * site.M(GarageDeepM / 2), along, site.M(GarageLongM), site.M(GarageDeepM)));
                foreach (int k in new[] { -1, 1 }) parts.Add(BuiltSite.Sign(FuelPump, yard - side * site.M(2) + along * (k * site.M(2.5)), Heading(along)));
                taken.Add((yard, site.M(GarageLongM * 0.7)));
                return;
            }
        }
    }

    static void Kolkhoz(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts, List<(Spot At, double Reach)> taken)
    {
        if (Callings.Found(site, roll, centre, within * 1.0, within * 1.5, site.M(FarmYardLongM * 0.6), taken, streets) is not { } at) return;
        var along = (at - centre).Unit.Left;
        var yard = Industry.Rect(at, along, site.M(FarmYardLongM), site.M(FarmYardDeepM));
        if (yard.Any(p => !site.Free(p, 0))) return;
        parts.Add(BuiltSite.Area("yard", yard));
        parts.Add(BuiltSite.Line("fence", yard.Append(yard[0])));
        int barns = 2 + roll.Below(3);
        for (int i = 0; i < barns; i++)
            Stand(site, parts, BuiltSite.Block("barn", at + along.Left * site.M((i - (barns - 1) / 2.0) * 16) + along * site.M(FarmYardLongM * 0.18), along, site.M(40), site.M(11)));
        Stand(site, parts, BuiltSite.Block(Garage, at - along * site.M(FarmYardLongM * 0.32), along.Left, site.M(30), site.M(13)));
        for (int i = 0; i < 3 + roll.Below(3); i++) parts.Add(BuiltSite.Sign(Tractor, at - along * site.M(FarmYardLongM * 0.12) + along.Left * site.M((i - 2) * 6), Heading(along)));
        taken.Add((at, site.M(FarmYardLongM * 0.6)));
    }

    static void Shelters(BuiltSite site, Dice roll, Spot centre, double within, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        foreach (var street in streets.Where(s => s.Laid && s.Points.Count > 1 && s.Class != "lane").OrderBy(_ => roll.Next()).Take(2 + roll.Below(4)))
        {
            var (at, along) = TownStreets.Along(street.Points, TownStreets.Long(street.Points) * roll.Between(0.2, 0.8));
            var spot = at + along.Left * ((roll.Chance(0.5) ? 1 : -1) * (Homes.HalfWidth(site, street.Class) - site.M(1.5)));
            if ((spot - centre).Length < within && site.Buildable(spot)) parts.Add(BuiltSite.Sign(Shelter, spot, Heading(along)));
        }
    }

    static void Boardwalks(BuiltSite site, List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        foreach (var street in streets.Where(s => s.Laid && s.Class == "street" && s.Points.Count > 1))
            foreach (int side in new[] { 1, -1 })
            {
                double off = side * (Homes.HalfWidth(site, street.Class) - site.M(BoardwalkInM));
                var walk = Enumerable.Range(0, street.Points.Count).Select(i =>
                    street.Points[i] + (street.Points[Math.Min(i + 1, street.Points.Count - 1)] - street.Points[Math.Max(i - 1, 0)]).Unit.Left * off).ToList();
                parts.Add(BuiltSite.Line(Boardwalk, walk));
            }
    }

    static void Tramways(List<Settlements.Street> streets, List<BuiltPart> parts)
    {
        foreach (var street in streets.Where(s => s.Main && s.Laid && s.Class == "street" && s.Points.Count > 1)) parts.Add(BuiltSite.Line(Tramway, street.Points));
    }
}
