using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class BuiltPainter
{
    public readonly record struct Shape(string Kind, string Class, Vector2[] Uv, Rect2 Box, int Turn, int Rank, float PerMetre, int Manner, int Builds = -1, int Adorns = -1, float Town = -1)
    {
        public int Built => Builds < 0 ? Manner : Builds;

        public int Adorned => Adorns < 0 ? Manner : Adorns;
    }

    const float WardTurn = 2.39996f, TownTurn = 0.6180339f, SignHalfM = 30;

    readonly List<Shape> _shapes = new();
    float _fullWidth = 1, _fullHeight = 1, _perMetre = BuiltHand.UsualPerMetre;
    long _key;

    public bool Any => _shapes.Count > 0;

    public long Key => _key;

    const long Prime = 1099511628211;

    sealed record Made(long Key, Shape[] Shapes, Rect2? Box, int Parts, double FullWidth, double FullHeight, float PerMetre);

    const int MostChangedAtOnce = 8;
    const float NearReaches = 2.1f;

    List<Made> _laid = new();
    readonly List<Rect2> _changed = new();
    BuiltNear.Field? _near;
    bool _nearWhole = true;

    readonly ConditionalWeakTable<BuiltThing, Made> _made = new();

    public long ThingsMade { get; private set; }

    static long KeyOf(BuiltThing thing)
    {
        long h = unchecked((long)14695981039346656037UL) ^ BitConverter.DoubleToInt64Bits(thing.Setting(BuiltThings.Scale, 0));
        h = unchecked((h ^ Manners.MixOf(thing).GetHashCode()) * Prime);
        foreach (var part in thing.Parts)
        {
            h = unchecked((h ^ part.Kind.GetHashCode(StringComparison.Ordinal)) * Prime);
            h = unchecked((h ^ part.Class.GetHashCode(StringComparison.Ordinal)) * Prime);
            h = unchecked((h ^ part.Turn) * Prime);
            h = unchecked((h ^ part.SizePercent) * Prime);
            foreach (int v in part.At) h = unchecked((h ^ v) * Prime);
        }
        return h;
    }

    Made MadeOf(BuiltThing thing, double fullWidth, double fullHeight, float perMetre)
    {
        if (_made.TryGetValue(thing, out var had) && had.Parts == thing.Parts.Count && had.FullWidth == fullWidth && had.FullHeight == fullHeight && had.PerMetre == perMetre) return had;
        var shapes = new List<Shape>(thing.Parts.Count);
        var scale = new Vector2((float)(1 / (fullWidth * BuiltGeometry.PerUnit)), (float)(1 / (fullHeight * BuiltGeometry.PerUnit)));
        float laidAt = (float)thing.Setting(BuiltThings.Scale, 0);
        var mix = Manners.MixOf(thing);
        var blocks = thing.Parts.Where(p => p.Kind == PartKinds.Block && p.At.Count >= 2).Select(p => new Vector2(p.At[0], p.At[1]) * scale).ToList();
        var middle = blocks.Count > 0 ? blocks.Aggregate(Vector2.Zero, (sum, p) => sum + p) / blocks.Count : Vector2.Zero;
        float bearing = thing.Seed * WardTurn % Mathf.Tau;
        float town = thing.Kind is BuiltKinds.Settlement or BuiltKinds.Details or BuiltKinds.Fort ? Mathf.PosMod(thing.Seed * TownTurn, 1) : -1;
        bool Warded(Vector2 at) => mix.Ward >= 0 && Mathf.Abs(Mathf.AngleDifference(bearing, (at - middle).Angle())) < mix.Share * Mathf.Pi;
        foreach (var part in thing.Parts)
        {
            var uv = BuiltGeometry.Points(part.At).Select(p => new Vector2(p.X, p.Y) * scale).ToArray();
            if (uv.Length == 0) continue;
            var box = new Rect2(uv[0], Vector2.Zero);
            foreach (var p in uv) box = box.Expand(p);
            bool warded = Warded(uv[0]);
            float per = laidAt > 0 ? laidAt : perMetre, size = part.Kind == PartKinds.Sign ? (float)part.Size : 1;
            if (size > 1) box = box.Grow(SignHalfM * per * size / (float)Math.Max(1e-9, Math.Min(fullWidth, fullHeight)));
            shapes.Add(new Shape(part.Kind, part.Class, uv, box, part.Turn, BuiltHand.Rank(part.Kind, part.Class), per * size, mix.Lays, warded ? mix.Ward : mix.Builds,
                warded ? mix.Ward : mix.Adorns, warded ? Mathf.PosMod(town + 0.5f, 1) : town));
        }
        Rect2? over = null;
        foreach (var shape in shapes) over = over?.Merge(shape.Box) ?? shape.Box;
        var made = new Made(KeyOf(thing), shapes.ToArray(), over, thing.Parts.Count, fullWidth, fullHeight, perMetre);
        _made.AddOrUpdate(thing, made);
        ThingsMade++;
        return made;
    }

    public Rect2? Set(IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double perMetre)
    {
        float per = perMetre > 0 ? (float)perMetre : BuiltHand.UsualPerMetre;
        var made = things.Select(thing => MadeOf(thing, fullWidth, fullHeight, per)).ToList();
        long key = unchecked((long)14695981039346656037UL) ^ BitConverter.DoubleToInt64Bits(fullWidth) ^ (BitConverter.DoubleToInt64Bits(fullHeight) * Prime);
        key = unchecked((key ^ BitConverter.DoubleToInt64Bits(perMetre)) * Prime);
        foreach (var one in made) key = unchecked((key ^ one.Key) * Prime);
        if (key == _key) return null;
        _key = key;
        var before = Covered();
        var was = new HashSet<Made>(_laid, ReferenceEqualityComparer.Instance);
        var now = new HashSet<Made>(made, ReferenceEqualityComparer.Instance);
        foreach (var one in _laid.Where(m => !now.Contains(m)).Concat(made.Where(m => !was.Contains(m))))
            if (one.Box is { } box) _changed.Add(box);
        if (_fullWidth != (float)fullWidth || _fullHeight != (float)fullHeight || _perMetre != per || _changed.Count > MostChangedAtOnce) _nearWhole = true;
        _laid = made;
        _fullWidth = (float)fullWidth;
        _fullHeight = (float)fullHeight;
        _perMetre = per;
        var starts = new int[BuiltHand.MostRank + 2];
        foreach (var one in made)
            foreach (var shape in one.Shapes) starts[shape.Rank + 1]++;
        for (int rank = 1; rank < starts.Length; rank++) starts[rank] += starts[rank - 1];
        var ranked = new Shape[starts[^1]];
        foreach (var one in made)
            foreach (var shape in one.Shapes) ranked[starts[shape.Rank]++] = shape;
        _shapes.Clear();
        _shapes.AddRange(ranked);
        _grid = null;
        var after = Covered();
        return before is { } b && after is { } a ? b.Merge(a) : before ?? after;
    }

    Rect2? Covered()
    {
        if (_shapes.Count == 0) return null;
        var box = _shapes[0].Box;
        foreach (var s in _shapes) box = box.Merge(s.Box);
        return box.Grow(Widest / _fullWidth);
    }

    float Widest => Mathf.Max(BuiltHand.WidestUnits, BuiltHand.WidestM * _perMetre);

    internal BuiltNear.Field Near()
    {
        if (_nearWhole || _shapes.Count == 0 || _near is not { WideM: > 0 } field) _near = BuiltNear.Of(_shapes, _fullWidth, _fullHeight, _perMetre);
        else
        {
            float reach = NearReaches * (BuiltNear.ReachM + BuiltNear.WidestHalfM) * _perMetre;
            foreach (var box in _changed)
                BuiltNear.Again(field, Grid.Among(box.GrowIndividual(reach / _fullWidth, reach / _fullHeight, reach / _fullWidth, reach / _fullHeight)).Select(i => _shapes[i]), box);
        }
        _changed.Clear();
        _nearWhole = false;
        return _near.Value;
    }

    ShapeGrid? _grid;

    ShapeGrid Grid => _grid ??= new ShapeGrid(_shapes.Select(s => s.Box).ToArray());

    Rect2 Reach(Rect2 uv) => uv.Grow(Widest / _fullWidth);

    public bool Touches(Rect2 uv) => Grid.Among(Reach(uv), firstOnly: true).Count > 0;

    internal BuiltLayer? Layer(Rect2 uv, float pixels, int style)
    {
        var seen = Grid.Among(Reach(uv)).Select(i => _shapes[i]).ToList();
        float perUnit = pixels / (uv.Size.X * _fullWidth);
        return seen.Count == 0 ? null
            : new BuiltLayer(seen, uv, pixels, perUnit, pixels / (uv.Size.Y * _fullHeight) / perUnit, style) { Material = new ShaderMaterial { Shader = SurfaceBook.Shader } };
    }
}

public sealed class BuiltHand
{
    public const float Fine = 0.012f, Medium = 0.025f, Heavy = 0.05f, WidestUnits = 1.2f, WidestM = BuiltNear.ReachM + 2, UsualPerMetre = 0.02f;
    const float DryDarker = 0.14f, DryEdge = 0.6f;
    const float GardenWash = 0.5f, TroddenWash = 0.45f, GlacisWash = 0.3f, PlotEdge = 0.28f;
    public const float LeastPx = 0.7f, FineMostPx = 1.5f, MediumMostPx = 2.2f;
    const float Dotted = 0.01f;

    public readonly record struct AreaLook(Color Fill, Color Edge);
    public readonly record struct BlockLook(Color Fill, Color Edge, Color Shadow);
    public readonly record struct LineLook(Color Ink, Color Fill, float Metres, float LeastPx, float Dash = 0, Color Close = default);

    public Color Ink { get; private init; }
    public Color Signal { get; private init; }
    Color Paper { get; init; }
    Color Masonry { get; init; }
    Color Church { get; init; }
    Color Road { get; init; }
    Color MainRoad { get; init; }
    Color Water { get; init; }
    Color WaterLine { get; init; }
    Color Garden { get; init; }
    Color Yard { get; init; }
    Color Hedge { get; init; }
    public Color Hedging => Hedge;
    public Color Wood => Timber;
    public Color Cloth => Paper.Lightened(0.3f);
    public Color Stone { get; private init; }
    Color Timber { get; init; }
    public Color Tree { get; private init; }
    public Color Straw { get; private init; }
    public Color Wet { get; private init; }
    public Color Tile { get; private init; }
    public Color Slate { get; private init; }
    public Color Thatch { get; private init; }
    public Color Shingle { get; private init; }
    public Color Lead { get; private init; }
    public Color Mud { get; private init; }
    public Color Lime { get; private init; }
    public Color Brick { get; private init; }
    public Color Turf { get; private init; }
    public Color Copper { get; private init; }
    public Color Kawara { get; private init; }
    public Color Tin { get; private init; }
    public Color Rust { get; private init; }
    public Color Tar { get; private init; }
    public Color Palm { get; private init; }
    public Color Plank { get; private init; }
    public Color Sheet { get; private init; }
    public Color Pantile { get; private init; }
    public Color Silt { get; private init; }
    public Color Asphalt { get; private init; }
    public Color Concrete { get; private init; }
    public Color Earth => Yard.Lerp(Garden, 0.45f);
    public Color Pool => Water;
    public Color Rails => Ink.Lerp(Lead, 0.25f);
    public Color Paint => Lime.Lightened(0.3f);
    public Color Highway => MainRoad;
    public bool Shaded => Shadows;
    public bool Crosses => !RoadsAsLines;
    public bool Rubricated { get; private init; }
    public bool Grimed { get; private init; }
    bool Shadows { get; init; }
    bool RoadsAsLines { get; init; }

    static Color C(string html) => Color.FromHtml(html);

    static readonly Color None = new(0, 0, 0, 0);

    static readonly BuiltHand Survey = new()
    {
        Kawara = C("#2C3034"), Tin = C("#6F7472"), Rust = C("#6A3E2A"), Tar = C("#1E1E1C"), Palm = C("#6E6A44"), Plank = C("#5E5B52"), Sheet = C("#5A3226"), Pantile = C("#7A4A30"),
        Silt = C("#6A6248"), Asphalt = C("#2E2E2C"), Concrete = C("#8A8A7E"),
        Stone = C("#77776A"), Ink = C("#16180F"), Signal = C("#8A1E16"), Paper = C("#A9A67F"), Masonry = C("#2A2A25"), Church = C("#3A2B25"), Road = C("#7D7257"),
        MainRoad = C("#8B8574"), Water = C("#B2C8D2"), WaterLine = C("#EEF2EA"), Garden = C("#76825A"), Yard = C("#6B6B4B"), Hedge = C("#223519"), Timber = C("#2E2A20"),
        Tree = C("#24401E"), Straw = C("#B19140"), Wet = C("#DDE6DD"), Tile = C("#5E3125"), Slate = C("#30353A"), Thatch = C("#6B6040"), Shingle = C("#4A4236"),
        Lead = C("#5A6062"), Mud = C("#6E6448"), Lime = C("#9C9A88"), Brick = C("#5A3428"), Turf = C("#3F5A2C"), Copper = C("#4E7464"), Shadows = true,
    };

    static readonly BuiltHand Modern = new()
    {
        Kawara = C("#474B51"), Tin = C("#979DA0"), Rust = C("#8A5238"), Tar = C("#3A3837"), Palm = C("#A2946A"), Plank = C("#928E83"), Sheet = C("#7A4434"), Pantile = C("#B26C48"),
        Silt = C("#917E62"), Asphalt = C("#55534F"), Concrete = C("#B1AEA4"),
        Stone = C("#908C84"), Ink = C("#1E1C1A"), Signal = C("#87341F"), Paper = C("#CAC1A0"), Masonry = C("#1E1C1A"), Church = C("#1E1C1A"), Road = C("#E0D8BC"),
        MainRoad = C("#87341F"), Water = C("#6485AE"), WaterLine = C("#2B3F6A"), Garden = C("#93A47A"), Yard = C("#CAC1A0"), Hedge = C("#1E1C1A"), Timber = C("#403D3A"),
        Tree = C("#55744C"), Straw = C("#A68D4E"), Wet = C("#2B3F6A"), Tile = C("#8E4A36"), Slate = C("#5A6068"), Thatch = C("#9A8650"), Shingle = C("#7A6E60"),
        Lead = C("#8A9092"), Mud = C("#B09870"), Lime = C("#DDD6C2"), Brick = C("#8A4E3A"), Turf = C("#7C9468"), Copper = C("#7FA595"),
    };

    static readonly BuiltHand Ancient = new()
    {
        Kawara = C("#46474A"), Tin = C("#8F908A"), Rust = C("#7C4A2E"), Tar = C("#33302C"), Palm = C("#9C8850"), Plank = C("#857E6C"), Sheet = C("#6C3C2A"), Pantile = C("#A05E3A"),
        Silt = C("#86704C"), Asphalt = C("#4C4842"), Concrete = C("#A8A08C"),
        Stone = C("#998668"), Ink = C("#1B1815"), Signal = C("#73301C"), Paper = C("#B09262"), Masonry = C("#7C3B27"), Church = C("#E4DAC2"), Road = C("#73301C"),
        MainRoad = C("#73301C"), Water = C("#3B6876"), WaterLine = C("#264E58"), Garden = C("#838A62"), Yard = C("#B09262"), Hedge = C("#1B1815"), Timber = C("#58412D"),
        Tree = C("#485B38"), Straw = C("#A37F36"), Wet = C("#264E58"), Tile = C("#96482C"), Slate = C("#5A5A56"), Thatch = C("#927A40"), Shingle = C("#6A5540"),
        Lead = C("#8A8C88"), Mud = C("#A68658"), Lime = C("#DDD2B6"), Brick = C("#8C5236"), Turf = C("#66763E"), Copper = C("#4E8A80"), RoadsAsLines = true,
    };

    static readonly BuiltHand Staff = new()
    {
        Kawara = C("#4A4946"), Tin = C("#8C8B84"), Rust = C("#6E6C66"), Tar = C("#33322F"), Palm = C("#9C9A88"), Plank = C("#8A887E"), Sheet = C("#66645E"), Pantile = C("#84827A"),
        Silt = C("#9A9886"), Asphalt = C("#55544F"), Concrete = C("#AEAC9E"),
        Stone = C("#86847C"), Ink = C("#1A1816"), Signal = C("#1A1816"), Paper = C("#C5C1AB"), Masonry = C("#1A1816"), Church = C("#1A1816"), Road = C("#DAD6C0"),
        MainRoad = C("#DAD6C0"), Water = C("#A4A9A2"), WaterLine = C("#2A2826"), Garden = C("#AEAC9A"), Yard = C("#C5C1AB"), Hedge = C("#1A1816"), Timber = C("#4A4844"),
        Tree = C("#75746B"), Straw = C("#96947F"), Wet = C("#2A2826"), Tile = C("#7C7A72"), Slate = C("#5E5D58"), Thatch = C("#9A9886"), Shingle = C("#75736A"),
        Lead = C("#92918A"), Mud = C("#ABA896"), Lime = C("#DAD7C6"), Brick = C("#86847A"), Turf = C("#8E8D7E"), Copper = C("#8A8E88"),
    };

    static readonly BuiltHand Medieval = new()
    {
        Kawara = C("#2F363A"), Tin = C("#6A7476"), Rust = C("#703E2D"), Tar = C("#231F1C"), Palm = C("#70623F"), Plank = C("#62584D"), Sheet = C("#552F29"), Pantile = C("#724035"),
        Silt = C("#615243"), Asphalt = C("#3D3835"), Concrete = C("#807B74"), Stone = C("#A29A8F"), Ink = C("#221A15"), Signal = C("#872B27"), Paper = C("#594532"), Masonry = C("#706055"),
        Church = C("#712725"), Road = C("#6A5545"), MainRoad = C("#9E8E79"), Water = C("#00404D"), WaterLine = C("#002D39"), Garden = C("#2D4629"), Yard = C("#513E32"), Hedge = C("#0E2617"),
        Timber = C("#3A281E"), Tree = C("#15321C"), Straw = C("#967746"), Wet = C("#002D39"), Tile = C("#673732"), Slate = C("#36414A"), Thatch = C("#6B5339"), Shingle = C("#534439"),
        Lead = C("#656F71"), Mud = C("#765F47"), Lime = C("#A8A092"), Brick = C("#613730"), Turf = C("#314829"), Copper = C("#346253"), Shadows = true, Rubricated = true, Grimed = true,
    };

    static readonly BuiltHand[] ByStyle = [Survey, Ancient, Modern, Staff, Medieval];

    public static BuiltHand Of(int style) => style >= 0 && style < ByStyle.Length ? ByStyle[style] : Survey;

    static readonly string[] RoadClasses = ["path", "track", "lane", "causeway", "street", "road", "boulevard", "chaussee", "highway"];

    public static bool IsRoad(BuiltPainter.Shape s) => s.Kind == PartKinds.Line && Array.IndexOf(RoadClasses, s.Class) >= 0;

    static readonly string[] LineOrder =
        ["drymoat", "dryditch", "fosse", "moat", "ditch", "stream", "river", "trench", "hedge", "fence", "wire", "gardenwall", "palisade", "path", "track", "lane", "causeway", "street", "paving", "boardwalk", "road", "boulevard", "chaussee", "highway", "gutter", "tramway", "railway", "quay", "mole",
            "dam", "jetty", "glacis", "rampart", "wardwall", "wall", "aqueduct"];

    public static int Rank(string kind, string cls) => kind switch
    {
        PartKinds.Area => cls switch { "glacis" => 0, "moat" or "drymoat" => 1, "terreplein" or "ravelin" or "platform" => 2, _ => 3 },
        PartKinds.Line => 100 + Math.Max(0, Array.IndexOf(LineOrder, cls)),
        PartKinds.Block => 300,
        _ => MostRank,
    };

    public const int MostRank = 400;

    public AreaLook Area(string cls) => cls switch
    {
        "garden" or "orchard" => new(Garden with { A = GardenWash }, Ink with { A = PlotEdge }),
        "square" => new(Paper with { A = TroddenWash }, None),
        "green" or "meadow" => new(Garden with { A = GardenWash }, None),
        "quarter" => new(None, None),
        "moat" or "pond" => new(Water, WaterLine),
        "drymoat" => new(Mud.Darkened(DryDarker), Ink with { A = DryEdge }),
        "glacis" => new(Yard with { A = GlacisWash }, None),
        "field" => new(None, Ink with { A = 0.55f }),
        "pen" => new(Timber.Lerp(Yard, 0.5f) with { A = 0.55f }, None),
        "forum" => new(Stone.Lightened(0.22f), Ink with { A = 0.5f }),
        "threshing" => new(Straw.Lerp(Yard, 0.5f) with { A = 0.8f }, Ink with { A = 0.4f }),
        "vineyard" => new(Yard with { A = 0.5f }, Ink with { A = PlotEdge }),
        "osiers" => new(Garden with { A = GardenWash }, None),
        "quarry" => new(Stone.Darkened(0.12f), Ink with { A = 0.6f }),
        "saltpans" => new(Lime.Darkened(0.06f), Ink with { A = 0.5f }),
        "airstrip" => new(Stone.Lightened(0.06f), Ink with { A = 0.5f }),
        "terreplein" => new(Earth, None),
        "ravelin" => new(Earth.Darkened(0.05f), Ink with { A = 0.7f }),
        "platform" => new(Stone.Lerp(Yard, 0.5f), Ink),
        "plantation" => new(Garden with { A = 0.35f }, None),
        "allotments" => new(Garden with { A = 0.7f }, Ink with { A = PlotEdge }),
        "pitch" => new(Garden.Lerp(Tree, 0.25f) with { A = 0.85f }, None),
        "schoolyard" => new(Yard.Lerp(Stone, 0.3f) with { A = 0.85f }, Ink with { A = PlotEdge }),
        "minefield" => new(None, Ink with { A = 0.6f }),
        "works" => new(Asphalt.Lerp(Yard, 0.6f) with { A = 0.85f }, None),
        "slagheap" => new(Asphalt.Lerp(Mud, 0.3f), Ink with { A = 0.5f }),
        _ => new(Yard with { A = TroddenWash }, None),
    };

    public BlockLook Block(string cls) => cls switch
    {
        "church" or "public" or "steeple" or "basilica" or "baths" => new(Church, Ink, Shadows ? Ink : None),
        "shed" or "barn" or "smithy" or "privy" or "stable" or "byre" or "granary" or "cartshed" or "coop" or "pigsty" or "kennel" or "dovecote" or "warehouse" or "goodsshed" or "roundhouse" or "fisher" or "hut" or "cottage" or "ropewalk" or "cabin"
            or "greathouse" or "longhouse" or "outshut" => new(Timber, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
        "keep" or "gatehouse" or "watchtower" or "magazine" or "silo" or "kiln" or "arsenal" or "stonehouse" => new(Stone, Ink, Shadows ? Ink : None),
        "tower" or "bastion" => new(Ink, Ink, None),
        "bunker" => new(Concrete, Ink, Shadows ? Ink : None),
        "flats" or "elevator" => new(Concrete.Lerp(Masonry, 0.25f), Ink, Shadows ? Ink : None),
        _ => new(Masonry, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
    };

    public LineLook Line(string cls) => cls switch
    {
        "path" => RoadsAsLines ? new(Yard.Darkened(0.14f) with { A = 0.85f }, None, 1.4f, 0.6f, 0, Yard)
            : Rubricated ? new(Ink with { A = 0.6f }, None, 1.4f, 0.8f, Dotted, Road with { A = 0.7f }) : new(Ink with { A = 0.5f }, None, 1.4f, 0.6f, 0.08f, Road with { A = 0.7f }),
        "track" => RoadsAsLines ? new(Road, None, 2.6f, 0.9f, 0, Yard)
            : Rubricated ? new(Ink with { A = 0.85f }, None, 2.6f, 1.2f, Dotted, Road with { A = 0.75f }) : new(Ink with { A = 0.8f }, None, 2.6f, 0.9f, 0.14f, Road with { A = 0.75f }),
        "causeway" => RoadsAsLines ? new(Road, None, 3.6f, 0.9f, 0, Yard) : new(Ink, Road, 3.6f, 1.8f),
        "lane" => RoadsAsLines ? new(Road, None, 3.6f, 0.9f, 0, Yard) : new(Ink, Road, 3.6f, 1.8f),
        "street" => RoadsAsLines ? new(Road, None, 6.5f, 1, 0, Yard.Lightened(0.1f)) : new(Ink, Road, 6.5f, 2.2f),
        "road" => RoadsAsLines ? new(Road, None, 6.5f, 1.4f, 0, Yard.Lightened(0.1f)) : new(Ink, Road, 6.5f, 2.8f),
        "boulevard" => RoadsAsLines ? new(Road, None, 14, 2.2f, 0, Stone.Lightened(0.12f)) : new(Ink, MainRoad, 14, 3.8f),
        "chaussee" => RoadsAsLines ? new(Road, None, 9.5f, 1.9f, 0, Stone.Lightened(0.15f)) : new(Ink, MainRoad, 9.5f, 3.6f),
        "wall" => new(Ink, None, 2.2f, 1.2f, 0, Stone),
        "wardwall" => new(Ink with { A = 0.8f }, None, 1.4f, 0.9f, 0, Mud),
        "highway" => RoadsAsLines ? new(Road, None, 11, 2.2f, 0, Asphalt) : new(Ink, Highway, 11, 4.2f, 0, Asphalt),
        "railway" => new(Ink, None, 1.6f, 1.5f, 0, Stone.Darkened(0.1f)),
        "trench" => new(Timber.Lerp(Ink, 0.35f), None, 1.7f, 1.1f, 0, Mud.Darkened(0.3f)),
        "wire" => new(Ink with { A = 0.75f }, None, 0.2f, LeastPx),
        "fosse" => new(Ink with { A = DryEdge }, None, 3.4f, 1, 0, Mud.Darkened(DryDarker)),
        "rampart" => new(Ink, Yard, 11, 2.6f),
        "palisade" => new(Ink, None, 0.6f, 1, 0.05f),
        "glacis" => new(Ink with { A = 0.45f }, None, 0.2f, LeastPx),
        "hedge" => new(Hedge, None, 2.4f, 0.9f, 0.04f),
        "fence" => new(Ink with { A = 0.7f }, None, 0.15f, LeastPx),
        "ditch" => new(WaterLine, None, 2.6f, 0.9f, 0, Water),
        "dryditch" => new(Ink with { A = DryEdge }, None, 2.6f, 0.9f, 0, Mud.Darkened(DryDarker)),
        "drymoat" => new(Ink with { A = DryEdge }, Mud.Darkened(DryDarker), 15, 2.4f),
        "stream" => new(WaterLine, None, 3.6f, 0.9f, 0, Water),
        "moat" or "river" => new(WaterLine, Water, 15, 2.4f),
        "jetty" => new(Ink, Timber, 1.8f, 1),
        "quay" => new(Ink, Stone.Lightened(0.12f), 6, 1.6f),
        "mole" => new(Ink, Stone, 4.5f, 1.4f),
        "dam" => new(Ink, Stone.Darkened(0.08f), 3, 1.2f),
        "rope" => new(Ink with { A = 0.7f }, None, 0.12f, LeastPx, 0.03f),
        _ => new(Ink, None, 0.2f, LeastPx),
    };
}
