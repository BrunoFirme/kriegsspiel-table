namespace KTable.Core.Model;

public enum PlaceKind { Sheet, Built, Building, Pin }

public sealed class MapPlace
{
    public PlaceKind Kind { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public Vec2 At { get; init; }
    public double Span { get; init; }
    public float[]? View { get; init; }
    public List<MapPlace> Inside { get; } = new();
    public List<string> Here { get; } = new();
    internal Func<Vec2, bool> Holds { get; init; } = _ => false;

    public string Key => $"{Kind}:{Id}";

    public bool Contains(Vec2 p) => Holds(p);
}

public readonly record struct PlaceBody(string Piece, string Name);

public static class PlaceTree
{
    public const double PinMetres = 25, PinUnits = 2, SpacingMetres = 1.5, SpacingUnits = 1;
    const string UnnamedSheet = "Map sheet";

    public static List<MapPlace> Of(GameState g, Func<string, PieceDef?> def, IEnumerable<PlaceBody>? bodies = null)
    {
        var top = new List<MapPlace>();
        var sheets = new Dictionary<string, MapPlace>();
        foreach (var s in g.Sheets)
        {
            var sheet = SheetPlace(s);
            sheets[s.Id] = sheet;
            top.Add(sheet);
        }
        var towns = new List<(MapPlace MapPlace, string Sheet, double Area)>();
        foreach (var thing in g.Built.Where(b => !string.IsNullOrWhiteSpace(b.Name)))
        {
            if (!sheets.TryGetValue(thing.Sheet, out var on) || BuiltPlace(g.Sheets.First(s => s.Id == thing.Sheet), thing) is not ({ } town, var area)) continue;
            on.Inside.Add(town);
            towns.Add((town, thing.Sheet, area));
        }
        foreach (var p in g.Layers.GetValueOrDefault(Layers.Umpire) ?? [])
            if (def(p.Def) is { } d && d.Tags.Contains(Doors.BuildingTag))
                Home(g, sheets, towns, top, SheetUnder(g, p.Pos), BuildingPlace(p, d, def));
        foreach (var pin in g.Places ?? [])
            Home(g, sheets, towns, top, pin.Sheet != null && sheets.ContainsKey(pin.Sheet) ? pin.Sheet : SheetUnder(g, pin.Pos), PinPlace(g, pin));
        foreach (var sheet in sheets.Values) Sort(sheet);
        top.Sort((a, b) => Order(a, b, g));
        if (bodies != null) Count(top, g, bodies);
        return top;
    }

    static int Order(MapPlace a, MapPlace b, GameState g)
    {
        if (a.Kind == PlaceKind.Sheet && b.Kind == PlaceKind.Sheet) return g.Sheets.FindIndex(s => s.Id == a.Id).CompareTo(g.Sheets.FindIndex(s => s.Id == b.Id));
        if (a.Kind == PlaceKind.Sheet || b.Kind == PlaceKind.Sheet) return a.Kind == PlaceKind.Sheet ? -1 : 1;
        return ByName(a, b);
    }

    static int ByName(MapPlace a, MapPlace b)
    {
        int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return byName != 0 ? byName : string.CompareOrdinal(a.Key, b.Key);
    }

    static void Sort(MapPlace p)
    {
        p.Inside.Sort(ByName);
        foreach (var inner in p.Inside) Sort(inner);
    }

    static void Home(GameState g, Dictionary<string, MapPlace> sheets, List<(MapPlace MapPlace, string Sheet, double Area)> towns, List<MapPlace> top, string? sheet, MapPlace place)
    {
        if (sheet == null || !sheets.TryGetValue(sheet, out var on))
        {
            top.Add(place);
            return;
        }
        var town = towns.Where(t => t.Sheet == sheet && t.MapPlace.Contains(place.At)).OrderBy(t => t.Area).Select(t => t.MapPlace).FirstOrDefault();
        (town ?? on).Inside.Add(place);
    }

    public static string? SheetUnder(GameState g, Vec2 p) =>
        g.Sheets.Where(s => Ground.Uv(s, p) != null).OrderByDescending(s => s.Z).Select(s => s.Id).FirstOrDefault();

    static MapPlace SheetPlace(SheetDef s) => new()
    {
        Kind = PlaceKind.Sheet, Id = s.Id, Name = string.IsNullOrWhiteSpace(s.Name) ? UnnamedSheet : s.Name, At = s.Pos, Span = Math.Max(s.Size.W, s.Size.H),
        Holds = p => Ground.Uv(s, p) != null,
    };

    static (MapPlace MapPlace, double Area)? BuiltPlace(SheetDef sheet, BuiltThing thing)
    {
        if (BuiltGeometry.Box(thing) is not var (low, high)) return null;
        var corners = thing.Outline.Count >= 6 ? BuiltGeometry.Points(thing.Outline) : [low, new SheetPoint(high.X, low.Y), high, new SheetPoint(low.X, high.Y)];
        var ring = corners.Select(c => BuiltGeometry.ToWorld(sheet, c)).Select(w => new Spot(w.X, w.Z)).ToList();
        var a = BuiltGeometry.ToWorld(sheet, low);
        var b = BuiltGeometry.ToWorld(sheet, high);
        var middle = BuiltGeometry.ToWorld(sheet, new SheetPoint((low.X + high.X) / 2, (low.Y + high.Y) / 2));
        var place = new MapPlace
        {
            Kind = PlaceKind.Built, Id = thing.Id, Name = thing.Name!.Trim(), At = middle, Span = (b - a).Length,
            Holds = p => Enclosures.Inside(ring, new Spot(p.X, p.Z)),
        };
        return (place, Math.Abs(Area(ring)));
    }

    static double Area(List<Spot> ring)
    {
        double twice = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) twice += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
        return twice / 2;
    }

    static MapPlace BuildingPlace(PieceInstance p, PieceDef d, Func<string, PieceDef?> def)
    {
        var b = new Building(p, d, def);
        return new MapPlace
        {
            Kind = PlaceKind.Building, Id = p.Id, Name = p.Label is { Length: > 0 } label ? label : d.Name, At = p.Pos, Span = Math.Max(b.Wide, b.Deep), Holds = b.Holds,
        };
    }

    public static double PinReach(GameState g) => g.Calibration.UnitsPerMeter is > 0 and var u ? PinMetres * u : PinUnits;

    public static double Spacing(GameState g) => g.Calibration.UnitsPerMeter is > 0 and var u ? SpacingMetres * u : SpacingUnits;

    static MapPlace PinPlace(GameState g, PlacePin pin)
    {
        double reach = PinReach(g);
        var at = pin.Pos;
        return new MapPlace
        {
            Kind = PlaceKind.Pin, Id = pin.Id, Name = pin.Name, At = at, Span = reach * 2, View = pin.View?.ToArray(),
            Holds = q => (q - at).Length <= reach,
        };
    }

    public static IEnumerable<MapPlace> All(IEnumerable<MapPlace> tree)
    {
        foreach (var p in tree)
        {
            yield return p;
            foreach (var inner in All(p.Inside)) yield return inner;
        }
    }

    public static void Count(List<MapPlace> tree, GameState g, IEnumerable<PlaceBody> bodies)
    {
        var standing = bodies.Select(b => (b.Name, Piece: g.Find(Layers.Umpire, b.Piece))).Where(b => b.Piece != null).Select(b => (b.Name, b.Piece!.Pos)).ToList();
        foreach (var place in All(tree))
        {
            place.Here.Clear();
            place.Here.AddRange(standing.Where(b => place.Contains(b.Pos)).Select(b => b.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        }
    }

    public static MapPlace? Find(IEnumerable<MapPlace> tree, string name)
    {
        var all = All(tree).ToList();
        return all.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(p => p.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static Protocol.Command? Send(GameState g, MapPlace place, IEnumerable<PlaceBody> bodies)
    {
        var going = bodies.Select(b => g.Find(Layers.Umpire, b.Piece)).OfType<PieceInstance>().DistinctBy(p => p.Id).ToList();
        if (going.Count == 0) return null;
        var spots = Spread(place.At, going.Count, Spacing(g));
        var moves = going.Select((p, i) => (Protocol.Command)new Protocol.MovePiece { Layer = Layers.Umpire, Id = p.Id, Pos = spots[i], RotDeg = p.RotDeg }).ToList();
        return moves.Count == 1 ? moves[0] : new Protocol.Batch { Items = moves };
    }

    public static List<Vec2> Spread(Vec2 at, int count, double spacing)
    {
        int side = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, count)));
        double from = (side - 1) / 2.0;
        return Enumerable.Range(0, count).Select(i => at + new Vec2((i % side - from) * spacing, (i / side - from) * spacing)).ToList();
    }
}
