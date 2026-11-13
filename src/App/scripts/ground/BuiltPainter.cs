using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class BuiltPainter
{
    public readonly record struct Shape(string Kind, string Class, Vector2[] Uv, Rect2 Box, int Turn, int Rank, float PerMetre, int Manner);

    readonly List<Shape> _shapes = new();
    float _fullWidth = 1, _fullHeight = 1, _perMetre = BuiltHand.UsualPerMetre;
    long _key;

    public bool Any => _shapes.Count > 0;

    static long KeyOf(IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double perMetre)
    {
        const long Prime = 1099511628211;
        long h = unchecked((long)14695981039346656037UL) ^ BitConverter.DoubleToInt64Bits(fullWidth) ^ (BitConverter.DoubleToInt64Bits(fullHeight) * Prime);
        h = unchecked((h ^ BitConverter.DoubleToInt64Bits(perMetre)) * Prime);
        foreach (var thing in things)
        {
            h = unchecked((h ^ BitConverter.DoubleToInt64Bits(thing.Setting(BuiltThings.Scale, 0))) * Prime);
            h = unchecked((h ^ Manners.Of(thing)) * Prime);
            foreach (var part in thing.Parts)
            {
                h = unchecked((h ^ part.Kind.GetHashCode(StringComparison.Ordinal)) * Prime);
                h = unchecked((h ^ part.Class.GetHashCode(StringComparison.Ordinal)) * Prime);
                h = unchecked((h ^ part.Turn) * Prime);
                foreach (int v in part.At) h = unchecked((h ^ v) * Prime);
            }
        }
        return h;
    }

    public Rect2? Set(IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double perMetre)
    {
        long key = KeyOf(things, fullWidth, fullHeight, perMetre);
        if (key == _key) return null;
        _key = key;
        var before = Covered();
        _shapes.Clear();
        _fullWidth = (float)fullWidth;
        _fullHeight = (float)fullHeight;
        _perMetre = perMetre > 0 ? (float)perMetre : BuiltHand.UsualPerMetre;
        var scale = new Vector2((float)(1 / (fullWidth * BuiltGeometry.PerUnit)), (float)(1 / (fullHeight * BuiltGeometry.PerUnit)));
        foreach (var thing in things)
        {
            float laidAt = (float)thing.Setting(BuiltThings.Scale, 0);
            int manner = Manners.Of(thing);
            foreach (var part in thing.Parts)
            {
                var uv = BuiltGeometry.Points(part.At).Select(p => new Vector2(p.X, p.Y) * scale).ToArray();
                if (uv.Length == 0) continue;
                var box = new Rect2(uv[0], Vector2.Zero);
                foreach (var p in uv) box = box.Expand(p);
                _shapes.Add(new Shape(part.Kind, part.Class, uv, box, part.Turn, BuiltHand.Rank(part.Kind, part.Class), laidAt > 0 ? laidAt : _perMetre, manner));
            }
        }
        _shapes.Sort((a, b) => a.Rank.CompareTo(b.Rank));
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

    public Node2D? Layer(Rect2 uv, float pixels, int style)
    {
        var reach = uv.Grow(Widest / _fullWidth);
        var seen = _shapes.Where(s => s.Box.Intersects(reach, true)).ToList();
        float perUnit = pixels / (uv.Size.X * _fullWidth);
        return seen.Count == 0 ? null
            : new BuiltLayer(seen, uv, pixels, perUnit, pixels / (uv.Size.Y * _fullHeight) / perUnit, style) { Material = new ShaderMaterial { Shader = SurfaceBook.Shader } };
    }
}

public sealed class BuiltHand
{
    public const float Fine = 0.012f, Medium = 0.025f, Heavy = 0.05f, WidestUnits = 1.2f, WidestM = 24, UsualPerMetre = 0.02f;
    const float GardenWash = 0.5f, TroddenWash = 0.45f, GlacisWash = 0.3f, PlotEdge = 0.28f;
    public const float LeastPx = 0.7f, FineMostPx = 1.5f, MediumMostPx = 2.2f;

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
    public bool Shaded => Shadows;
    public bool Crosses => !RoadsAsLines;
    bool Shadows { get; init; }
    bool RoadsAsLines { get; init; }

    static Color C(string html) => Color.FromHtml(html);

    static readonly Color None = new(0, 0, 0, 0);

    static readonly BuiltHand Survey = new()
    {
        Stone = C("#837C70"), Ink = C("#1F1A12"), Signal = C("#86291E"), Paper = C("#C3B694"), Masonry = C("#74331F"), Church = C("#86291E"), Road = C("#CFC096"),
        MainRoad = C("#B29462"), Water = C("#456884"), WaterLine = C("#1F354B"), Garden = C("#76825A"), Yard = C("#A8956C"), Hedge = C("#303E27"), Timber = C("#46382A"),
        Tree = C("#35452A"), Straw = C("#B19140"), Wet = C("#1F354B"), Tile = C("#7A3622"), Slate = C("#4B525C"), Thatch = C("#8C7642"), Shingle = C("#66533F"),
        Lead = C("#868C8E"), Mud = C("#A58C62"), Lime = C("#D2C8AC"), Brick = C("#8A4A32"), Turf = C("#5F7146"), Copper = C("#5E8474"), Shadows = true,
    };

    static readonly BuiltHand Modern = new()
    {
        Stone = C("#908C84"), Ink = C("#1E1C1A"), Signal = C("#87341F"), Paper = C("#CAC1A0"), Masonry = C("#1E1C1A"), Church = C("#1E1C1A"), Road = C("#E0D8BC"),
        MainRoad = C("#87341F"), Water = C("#7490B5"), WaterLine = C("#2B3F6A"), Garden = C("#A5B08C"), Yard = C("#CAC1A0"), Hedge = C("#1E1C1A"), Timber = C("#403D3A"),
        Tree = C("#64825A"), Straw = C("#A68D4E"), Wet = C("#2B3F6A"), Tile = C("#A0705C"), Slate = C("#73787F"), Thatch = C("#A59868"), Shingle = C("#8A7E70"),
        Lead = C("#9A9E9F"), Mud = C("#B8A88A"), Lime = C("#DDD6C2"), Brick = C("#9C6650"), Turf = C("#8AA07A"), Copper = C("#7FA595"),
    };

    static readonly BuiltHand Ancient = new()
    {
        Stone = C("#998668"), Ink = C("#1B1815"), Signal = C("#73301C"), Paper = C("#B09262"), Masonry = C("#7C3B27"), Church = C("#E4DAC2"), Road = C("#73301C"),
        MainRoad = C("#73301C"), Water = C("#3B6876"), WaterLine = C("#264E58"), Garden = C("#838A62"), Yard = C("#B09262"), Hedge = C("#1B1815"), Timber = C("#58412D"),
        Tree = C("#485B38"), Straw = C("#A37F36"), Wet = C("#264E58"), Tile = C("#96482C"), Slate = C("#5A5A56"), Thatch = C("#927A40"), Shingle = C("#6A5540"),
        Lead = C("#8A8C88"), Mud = C("#A68658"), Lime = C("#DDD2B6"), Brick = C("#8C5236"), Turf = C("#66763E"), Copper = C("#4E8A80"), RoadsAsLines = true,
    };

    static readonly BuiltHand Staff = new()
    {
        Stone = C("#86847C"), Ink = C("#1A1816"), Signal = C("#1A1816"), Paper = C("#C5C1AB"), Masonry = C("#1A1816"), Church = C("#1A1816"), Road = C("#DAD6C0"),
        MainRoad = C("#DAD6C0"), Water = C("#A4A9A2"), WaterLine = C("#2A2826"), Garden = C("#AEAC9A"), Yard = C("#C5C1AB"), Hedge = C("#1A1816"), Timber = C("#4A4844"),
        Tree = C("#75746B"), Straw = C("#96947F"), Wet = C("#2A2826"), Tile = C("#7C7A72"), Slate = C("#5E5D58"), Thatch = C("#9A9886"), Shingle = C("#75736A"),
        Lead = C("#92918A"), Mud = C("#ABA896"), Lime = C("#DAD7C6"), Brick = C("#86847A"), Turf = C("#8E8D7E"), Copper = C("#8A8E88"),
    };

    public static BuiltHand Of(int style) => style == 1 ? Ancient : style == 2 ? Modern : style == 3 ? Staff : Survey;

    static readonly string[] RoadClasses = ["track", "lane", "street", "road", "chaussee"];

    public static bool IsRoad(BuiltPainter.Shape s) => s.Kind == PartKinds.Line && Array.IndexOf(RoadClasses, s.Class) >= 0;

    static readonly string[] LineOrder =
        ["moat", "ditch", "stream", "river", "hedge", "fence", "palisade", "track", "lane", "street", "road", "chaussee", "quay", "mole", "jetty", "glacis", "rampart", "wall"];

    public static int Rank(string kind, string cls) => kind switch
    {
        PartKinds.Area => 0,
        PartKinds.Line => 100 + Math.Max(0, Array.IndexOf(LineOrder, cls)),
        PartKinds.Block => 300,
        _ => 400,
    };

    public AreaLook Area(string cls) => cls switch
    {
        "garden" or "orchard" => new(Garden with { A = GardenWash }, Ink with { A = PlotEdge }),
        "square" => new(Paper with { A = TroddenWash }, None),
        "green" => new(Garden with { A = GardenWash }, None),
        "quarter" => new(None, None),
        "moat" or "pond" => new(Water, WaterLine),
        "glacis" => new(Yard with { A = GlacisWash }, None),
        "field" => new(None, Ink with { A = 0.55f }),
        "pen" => new(Timber.Lerp(Yard, 0.5f) with { A = 0.55f }, None),
        "forum" => new(Stone.Lightened(0.22f), Ink with { A = 0.5f }),
        "threshing" => new(Straw.Lerp(Yard, 0.5f) with { A = 0.8f }, Ink with { A = 0.4f }),
        "vineyard" => new(Yard with { A = 0.5f }, Ink with { A = PlotEdge }),
        "osiers" => new(Garden with { A = GardenWash }, None),
        _ => new(Yard with { A = TroddenWash }, None),
    };

    public BlockLook Block(string cls) => cls switch
    {
        "church" or "public" or "steeple" or "basilica" or "baths" => new(Church, Ink, Shadows ? Ink : None),
        "shed" or "barn" or "smithy" or "privy" or "stable" or "byre" or "granary" or "cartshed" or "coop" or "pigsty" or "kennel" or "dovecote" or "warehouse" or "roundhouse" or "fisher"
            or "greathouse" or "longhouse" or "outshut" => new(Timber, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
        "keep" or "gatehouse" or "watchtower" or "magazine" or "silo" or "kiln" or "arsenal" => new(Stone, Ink, Shadows ? Ink : None),
        "tower" or "bastion" => new(Ink, Ink, None),
        _ => new(Masonry, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
    };

    public LineLook Line(string cls) => cls switch
    {
        "track" => RoadsAsLines ? new(Road, None, 2.6f, 0.9f, 0, Yard) : new(Ink with { A = 0.8f }, None, 2.6f, 0.9f, 0.14f, Road with { A = 0.75f }),
        "lane" => RoadsAsLines ? new(Road, None, 3.6f, 0.9f, 0, Yard) : new(Ink, Road, 3.6f, 1.8f),
        "street" => RoadsAsLines ? new(Road, None, 6.5f, 1, 0, Yard.Lightened(0.1f)) : new(Ink, Road, 6.5f, 2.2f),
        "road" => RoadsAsLines ? new(Road, None, 6.5f, 1.4f, 0, Yard.Lightened(0.1f)) : new(Ink, Road, 6.5f, 2.8f),
        "chaussee" => RoadsAsLines ? new(Road, None, 9.5f, 1.9f, 0, Stone.Lightened(0.15f)) : new(Ink, MainRoad, 9.5f, 3.6f),
        "wall" => new(Ink, None, 2.2f, 1.2f, 0, Stone),
        "rampart" => new(Ink, Yard, 11, 2.6f),
        "palisade" => new(Ink, None, 0.6f, 1, 0.05f),
        "glacis" => new(Ink with { A = 0.45f }, None, 0.2f, LeastPx),
        "hedge" => new(Hedge, None, 2.4f, 0.9f, 0.04f),
        "fence" => new(Ink with { A = 0.7f }, None, 0.15f, LeastPx),
        "ditch" => new(WaterLine, None, 2.6f, 0.9f, 0, Water),
        "stream" => new(WaterLine, None, 3.6f, 0.9f, 0, Water),
        "moat" or "river" => new(WaterLine, Water, 15, 2.4f),
        "jetty" => new(Ink, Timber, 1.8f, 1),
        "quay" => new(Ink, Stone.Lightened(0.12f), 6, 1.6f),
        "mole" => new(Ink, Stone, 4.5f, 1.4f),
        "rope" => new(Ink with { A = 0.7f }, None, 0.12f, LeastPx, 0.03f),
        _ => new(Ink, None, 0.2f, LeastPx),
    };
}
