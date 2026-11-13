using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class BuiltPainter
{
    public readonly record struct Shape(string Kind, string Class, Vector2[] Uv, Rect2 Box, int Turn, int Rank, float PerMetre);

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
            foreach (var part in thing.Parts)
            {
                var uv = BuiltGeometry.Points(part.At).Select(p => new Vector2(p.X, p.Y) * scale).ToArray();
                if (uv.Length == 0) continue;
                var box = new Rect2(uv[0], Vector2.Zero);
                foreach (var p in uv) box = box.Expand(p);
                _shapes.Add(new Shape(part.Kind, part.Class, uv, box, part.Turn, BuiltHand.Rank(part.Kind, part.Class), laidAt > 0 ? laidAt : _perMetre));
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
        return seen.Count == 0 ? null : new BuiltLayer(seen, uv, pixels, perUnit, pixels / (uv.Size.Y * _fullHeight) / perUnit, style);
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
    public bool Shaded => Shadows;
    public bool Crosses => !RoadsAsLines;
    bool Shadows { get; init; }
    bool RoadsAsLines { get; init; }

    static Color C(string html) => Color.FromHtml(html);

    static readonly Color None = new(0, 0, 0, 0);

    static readonly BuiltHand Survey = new()
    {
        Stone = C("#8A8378"), Ink = C("#1F1A12"), Signal = C("#8F2E22"), Paper = C("#C9BD9E"), Masonry = C("#7C3A2E"), Church = C("#8F2E22"), Road = C("#D8CBA6"),
        MainRoad = C("#BDA274"), Water = C("#58758A"), WaterLine = C("#22394F"), Garden = C("#848C68"), Yard = C("#B3A27E"), Hedge = C("#3A4731"), Timber = C("#4A3C2E"),
        Tree = C("#3F4E33"), Straw = C("#B89A4C"), Wet = C("#22394F"), Shadows = true,
    };

    static readonly BuiltHand Modern = new()
    {
        Stone = C("#96928A"), Ink = C("#1E1C1A"), Signal = C("#8E3A24"), Paper = C("#D2CAAE"), Masonry = C("#1E1C1A"), Church = C("#1E1C1A"), Road = C("#E6DFC6"),
        MainRoad = C("#8E3A24"), Water = C("#8AA0BE"), WaterLine = C("#2F4470"), Garden = C("#B2BC9C"), Yard = C("#D2CAAE"), Hedge = C("#1E1C1A"), Timber = C("#403D3A"),
        Tree = C("#6F8C62"), Straw = C("#AC9456"), Wet = C("#2F4470"),
    };

    static readonly BuiltHand Ancient = new()
    {
        Stone = C("#A08E72"), Ink = C("#1B1815"), Signal = C("#7E3822"), Paper = C("#B89C6E"), Masonry = C("#84432F"), Church = C("#E8DFC9"), Road = C("#7E3822"),
        MainRoad = C("#7E3822"), Water = C("#4A7480"), WaterLine = C("#2B5560"), Garden = C("#8F9670"), Yard = C("#B89C6E"), Hedge = C("#1B1815"), Timber = C("#5E4732"),
        Tree = C("#516440"), Straw = C("#AA883E"), Wet = C("#2B5560"), RoadsAsLines = true,
    };

    static readonly BuiltHand Staff = new()
    {
        Stone = C("#8C8A82"), Ink = C("#1A1816"), Signal = C("#1A1816"), Paper = C("#CFCBB6"), Masonry = C("#1A1816"), Church = C("#1A1816"), Road = C("#E0DCC8"),
        MainRoad = C("#E0DCC8"), Water = C("#B2B6AE"), WaterLine = C("#2A2826"), Garden = C("#BAB8A6"), Yard = C("#CFCBB6"), Hedge = C("#1A1816"), Timber = C("#4A4844"),
        Tree = C("#807F76"), Straw = C("#9C9A8A"), Wet = C("#2A2826"),
    };

    public static BuiltHand Of(int style) => style == 1 ? Ancient : style == 2 ? Modern : style == 3 ? Staff : Survey;

    static readonly string[] RoadClasses = ["track", "lane", "street", "road", "chaussee"];

    public static bool IsRoad(BuiltPainter.Shape s) => s.Kind == PartKinds.Line && Array.IndexOf(RoadClasses, s.Class) >= 0;

    static readonly string[] LineOrder =
        ["moat", "ditch", "stream", "river", "hedge", "fence", "palisade", "track", "lane", "street", "road", "chaussee", "jetty", "glacis", "rampart", "wall"];

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
        _ => new(Yard with { A = TroddenWash }, None),
    };

    public BlockLook Block(string cls) => cls switch
    {
        "church" or "public" or "steeple" => new(Church, Ink, Shadows ? Ink : None),
        "shed" or "barn" or "smithy" or "privy" => new(Timber, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
        "keep" => new(Stone, Ink, Shadows ? Ink : None),
        "tower" or "bastion" => new(Ink, Ink, None),
        _ => new(Masonry, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
    };

    public LineLook Line(string cls) => cls switch
    {
        "track" => RoadsAsLines ? new(Road, None, 2.6f, 0.9f) : new(Ink with { A = 0.8f }, None, 2.6f, 0.9f, 0.14f, Road with { A = 0.75f }),
        "lane" => RoadsAsLines ? new(Road, None, 3.6f, 0.9f) : new(Ink, Road, 3.6f, 1.8f),
        "street" => RoadsAsLines ? new(Road, None, 6.5f, 1) : new(Ink, Road, 6.5f, 2.2f),
        "road" => RoadsAsLines ? new(Road, None, 6.5f, 1.4f) : new(Ink, Road, 6.5f, 2.8f),
        "chaussee" => RoadsAsLines ? new(Road, None, 9.5f, 1.9f) : new(Ink, MainRoad, 9.5f, 3.6f),
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
        _ => new(Ink, None, 0.2f, LeastPx),
    };
}
