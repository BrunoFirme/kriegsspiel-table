namespace KTable.Core.Model;

public static class Camps
{
    const double Wide = 0.68, Corner = 0.3, ForwardAt = 0.3, GateHalfM = 6, DitchOffM = 10, ClearM = 12, StreetM = 6, RowDeepM = 8, RowPitchM = 14, RowLeastM = 16;
    const double HeadShare = 0.42, HeadLeastM = 16, HeadMostM = 38, HouseShare = 0.8, StoreLongShare = 1.1, StoreDeepM = 9, StoreGapM = 4, TowersApartM = 45;
    const double LinkFrom = 1.15, SquareCorner = 0.04, CloseTowersM = 24, WideLeast = 0.55, WideMost = 0.92, RoundedShare = 0.3;
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

    static BuiltPart Tower(BuiltSite site, Spot at, Spot centre, bool square, double size) =>
        square ? BuiltSite.Block("watchtower", at + (centre - at).Unit * (size * 0.2), (centre - at).Unit, size, size) : BuiltSite.Sign("tower", at);

    static void Rampart(BuiltSite site, List<Spot> ring, Spot[] gates, double towersApartM, List<BuiltPart> parts, bool square = false, double size = 0)
    {
        var stepped = new List<Spot>();
        for (int i = 0; i < ring.Count; i++)
        {
            Spot a = ring[i], b = ring[(i + 1) % ring.Count];
            int steps = Math.Max(1, (int)Math.Ceiling((b - a).Length / site.M(GateHalfM)));
            for (int s = 0; s < steps; s++) stepped.Add(a + (b - a) * (s / (double)steps));
        }
        bool Shut(int i) => !gates.Any(g => (g - stepped[i % stepped.Count]).Length < site.M(GateHalfM));
        int first = Enumerable.Range(0, stepped.Count).First(i => !Shut(i)), every = Math.Max(2, (int)(towersApartM / GateHalfM));
        var run = new List<Spot>();
        for (int k = 1; k <= stepped.Count; k++)
        {
            int i = (first + k) % stepped.Count;
            if (Shut(i))
            {
                run.Add(stepped[i]);
                if (run.Count % every == 0) parts.Add(Tower(site, stepped[i], Middle(ring), square, size));
                if (k < stepped.Count) continue;
            }
            if (run.Count > 1)
            {
                parts.Add(BuiltSite.Line("wall", run));
                parts.AddRange(new[] { run[0], run[^1] }.Select(p => Tower(site, p, Middle(ring), square, size)));
            }
            run = new();
        }
    }

    static Spot Middle(IReadOnlyList<Spot> ring) => new(ring.Average(p => p.X), ring.Average(p => p.Y));

    static void Inner(BuiltSite site, Dice dice, Spot centre, Spot ahead, Spot aside, double length, double width, List<BuiltPart> parts)
    {
        var corner = centre - ahead * (length * 0.72) + aside * (width * 0.7 * (dice.Chance(0.5) ? 1 : -1));
        if (site.Manner is Manners.Persian or Manners.Byzantine or Manners.Saracen && dice.Chance(0.7))
            parts.Add(BuiltSite.Block("keep", corner, ahead, site.M(dice.Between(14, 20)), site.M(dice.Between(12, 18))));
        var holy = centre - ahead * (length * 0.45) - aside * (width * 0.45);
        if (site.Manner is Manners.Byzantine or Manners.Saracen or Manners.Ottoman or Manners.NorthAfrican)
            Settlements.Church(site, holy, ahead, aside, centre, length * 2, parts, [], 0.45);
        if (site.Manner is Manners.Byzantine or Manners.Saracen or Manners.Persian) parts.Add(BuiltSite.Sign("cistern", centre + aside * (width * 0.3)));
        if (site.Manner == Manners.Egyptian)
            for (int i = 0; i < 2 + dice.Below(4); i++) parts.Add(BuiltSite.Block("silo", corner + ahead * site.M(i * 6), ahead, site.M(5), site.M(5)));
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
        bool legion = site.Manner == Manners.Roman;
        var centre = site.DryNear(asked, radius, radius * Forts.ShiftReach) ?? asked;
        var thing = new BuiltThing
        {
            Sheet = sheet, Kind = BuiltKinds.Fort, Seed = seed, Settings = { [Forts.Bastions] = 0, [Forts.Outworks] = 0, ["turn"] = turn, [Forts.Form] = 1, [Manners.Setting] = site.Manner },
            Outline = BuiltGeometry.Flat([centre.Stored, (centre + Spot.Toward(turn) * radius).Stored]),
        };
        if (!site.Dry(centre, radius)) return thing;
        Spot ahead = Spot.Toward(turn), aside = ahead.Left;
        var dice = new Dice(seed);
        double length = radius, width = radius * (legion ? Wide : dice.Between(WideLeast, WideMost)), cross = length * ForwardAt, clear = site.M(ClearM), street = site.M(StreetM);
        double round = width * (legion ? Corner : dice.Chance(RoundedShare) ? dice.Between(SquareCorner, Corner) : SquareCorner);
        bool square = !legion && site.Manner is Manners.Chinese or Manners.Byzantine or Manners.Egyptian || site.Manner == Manners.Persian && dice.Chance(0.3);
        double apart = legion ? TowersApartM : dice.Between(CloseTowersM * 0.75, CloseTowersM * 1.35), tower = site.M(dice.Between(6, 10));
        var ring = Card(centre, ahead, aside, length, width, round);
        Spot front = centre + ahead * length, rear = centre - ahead * length, left = centre + ahead * cross + aside * width, right = centre + ahead * cross - aside * width;
        var parts = thing.Parts;
        var ditch = Card(centre, ahead, aside, length + site.M(DitchOffM), width + site.M(DitchOffM), round + site.M(DitchOffM));
        parts.Add(BuiltSite.Line("fosse", ditch.Append(ditch[0])));
        Spot[] gates = legion || site.Manner == Manners.Chinese ? [front, rear, left, right] : dice.Chance(0.5) ? [front, rear] : [front];
        Rampart(site, ring, gates, apart, parts, square, tower);
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
        if (legion) return thing;
        int from = parts.Count;
        Inner(site, dice, centre, ahead, aside, length, width, parts);
        var added = parts.Skip(from).Where(p => p.Kind == PartKinds.Block).Select(BuiltSite.Round).ToList();
        parts.RemoveAll(p => p.Kind == PartKinds.Block && p.Class == "barracks" && added.Any(q => (BuiltSite.Round(p).At - q.At).Length < BuiltSite.Round(p).Reach * 0.6 + q.Reach));
        return thing;
    }
}
