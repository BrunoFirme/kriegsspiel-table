namespace KTable.Core.Model;

public static class Camps
{
    const double Wide = 0.68, Corner = 0.3, ForwardAt = 0.3, GateHalfM = 6, DitchOffM = 10, ClearM = 12, StreetM = 6, RowDeepM = 8, RowPitchM = 14, RowLeastM = 16;
    const double HeadShare = 0.42, HeadLeastM = 16, HeadMostM = 38, HouseShare = 0.8, StoreLongShare = 1.1, StoreDeepM = 9, StoreGapM = 4, TowersApartM = 45;
    const double LinkFrom = 1.15;
    const int CornerSteps = 4;

    public static bool Is(BuiltThing thing) => thing.Kind == BuiltKinds.Fort && thing.Setting(Forts.Form, 0) >= 1;

    static List<Spot> Card(Spot centre, Spot ahead, Spot aside, double length, double width, double round)
    {
        var ring = new List<Spot>();
        (int X, int Y)[] corners = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
        for (int c = 0; c < corners.Length; c++)
        {
            var (x, y) = corners[c];
            var hub = centre + ahead * (x * (length - round)) + aside * (y * (width - round));
            for (int s = 0; s <= CornerSteps; s++)
            {
                double a = (c + s / (double)CornerSteps) * Math.PI / 2;
                ring.Add(hub + ahead * (Math.Cos(a) * round) + aside * (Math.Sin(a) * round));
            }
        }
        return ring;
    }

    static void Rampart(BuiltSite site, List<Spot> ring, Spot[] gates, List<BuiltPart> parts)
    {
        var stepped = new List<Spot>();
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / site.M(GateHalfM)));
            for (int s = 0; s < steps; s++) stepped.Add(a + (b - a) * (s / (double)steps));
        }
        bool Shut(int i) => !gates.Any(g => (g - stepped[i % stepped.Count]).Length < site.M(GateHalfM));
        int first = Enumerable.Range(0, stepped.Count).First(i => !Shut(i)), every = Math.Max(2, (int)(TowersApartM / GateHalfM));
        var run = new List<Spot>();
        for (int k = 1; k <= stepped.Count; k++)
        {
            int i = (first + k) % stepped.Count;
            if (Shut(i))
            {
                run.Add(stepped[i]);
                if (run.Count % every == 0) parts.Add(BuiltSite.Sign("tower", stepped[i]));
                if (k < stepped.Count) continue;
            }
            if (run.Count > 1)
            {
                parts.Add(BuiltSite.Line("wall", run));
                parts.AddRange(new[] { run[0], run[^1] }.Select(p => BuiltSite.Sign("tower", p)));
            }
            run = new();
        }
    }

    static void Rows(BuiltSite site, Spot from, Spot ahead, Spot aside, double deep, double width, List<BuiltPart> parts)
    {
        if (width < site.M(RowLeastM)) return;
        for (double at = site.M(RowDeepM / 2); at + site.M(RowDeepM / 2) <= deep; at += site.M(RowPitchM))
        {
            var mid = from + ahead * at + aside * (width / 2);
            if (site.Buildable(mid)) parts.Add(BuiltSite.Block("barracks", mid, aside, width, site.M(RowDeepM)));
        }
    }

    public static BuiltThing Lay(BuiltSite site, string sheet, Spot asked, double radius, int seed, double turn)
    {
        var centre = site.DryNear(asked, radius, radius * Forts.ShiftReach) ?? asked;
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Fort, Seed = seed, Settings = { [Forts.Bastions] = 0, [Forts.Outworks] = 0, ["turn"] = turn, [Forts.Form] = 1, [Manners.Setting] = site.Manner },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + Spot.Toward(turn) * radius).Stored]),
        };
        if (!site.Dry(centre, radius)) return thing;
        Spot ahead = Spot.Toward(turn), aside = ahead.Left;
        double length = radius, width = radius * Wide, cross = length * ForwardAt, clear = site.M(ClearM), street = site.M(StreetM);
        var ring = Card(centre, ahead, aside, length, width, width * Corner);
        Spot front = centre + ahead * length, rear = centre - ahead * length, left = centre + ahead * cross + aside * width, right = centre + ahead * cross - aside * width;
        var parts = thing.Parts;
        var ditch = Card(centre, ahead, aside, length + site.M(DitchOffM), width + site.M(DitchOffM), width * Corner + site.M(DitchOffM));
        parts.Add(BuiltSite.Line("ditch", ditch.Append(ditch[0])));
        Rampart(site, ring, [front, rear, left, right], parts);
        parts.Add(BuiltSite.Line("street", [left, right]));
        parts.Add(BuiltSite.Line("street", [front, centre + ahead * cross]));
        double head = Math.Clamp(width * HeadShare, site.M(HeadLeastM), site.M(HeadMostM));
        var hall = centre + ahead * (cross - street - head / 2);
        parts.Add(BuiltSite.Block("public", hall, aside, head, head));
        parts.Add(BuiltSite.Block("house", hall + aside * (head * (0.5 + HouseShare / 2) + street), aside, head * HouseShare, head * HouseShare));
        foreach (int row in new[] { 0, 1 })
            parts.Add(BuiltSite.Block("magazine", hall - aside * (head / 2 + street + site.M(StoreDeepM / 2) + row * site.M(StoreDeepM + StoreGapM)), ahead, head * StoreLongShare, site.M(StoreDeepM)));
        if (site.NearestRoad(centre) is var (to, off) && off > radius * LinkFrom && off < radius * Forts.LinkReach)
            parts.Add(BuiltSite.Line("road", [new[] { front, rear, left, right }.MinBy(g => (g - to).Length), to]));
        double back = cross - 2 * street - head;
        parts.Add(BuiltSite.Line("lane", [rear, centre + ahead * back]));
        foreach (int side in new[] { 1, -1 })
        {
            var along = aside * side;
            double room = width - clear - street;
            Rows(site, centre + ahead * (cross + street) + along * street, ahead, along, length - clear - cross - street, room, parts);
            Rows(site, centre + ahead * (-length + clear) + along * street, ahead, along, back - street + length - clear, room, parts);
        }
        return thing;
    }
}
