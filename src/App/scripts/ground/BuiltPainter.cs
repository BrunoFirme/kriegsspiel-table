using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class BuiltPainter
{
    public readonly record struct Shape(string Kind, string Class, Vector2[] Uv, Rect2 Box, int Turn, int Rank);

    readonly List<Shape> _shapes = new();
    float _fullWidth = 1, _fullHeight = 1;
    long _key;

    public bool Any => _shapes.Count > 0;

    static long KeyOf(IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight)
    {
        const long Prime = 1099511628211;
        long h = unchecked((long)14695981039346656037UL) ^ BitConverter.DoubleToInt64Bits(fullWidth) ^ (BitConverter.DoubleToInt64Bits(fullHeight) * Prime);
        foreach (var thing in things)
            foreach (var part in thing.Parts)
            {
                h = unchecked((h ^ part.Kind.GetHashCode(StringComparison.Ordinal)) * Prime);
                h = unchecked((h ^ part.Class.GetHashCode(StringComparison.Ordinal)) * Prime);
                h = unchecked((h ^ part.Turn) * Prime);
                foreach (int v in part.At) h = unchecked((h ^ v) * Prime);
            }
        return h;
    }

    public Rect2? Set(IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight)
    {
        long key = KeyOf(things, fullWidth, fullHeight);
        if (key == _key) return null;
        _key = key;
        var before = Covered();
        _shapes.Clear();
        _fullWidth = (float)fullWidth;
        _fullHeight = (float)fullHeight;
        var scale = new Vector2((float)(1 / (fullWidth * BuiltGeometry.PerUnit)), (float)(1 / (fullHeight * BuiltGeometry.PerUnit)));
        foreach (var thing in things)
            foreach (var part in thing.Parts)
            {
                var uv = BuiltGeometry.Points(part.At).Select(p => new Vector2(p.X, p.Y) * scale).ToArray();
                if (uv.Length == 0) continue;
                var box = new Rect2(uv[0], Vector2.Zero);
                foreach (var p in uv) box = box.Expand(p);
                _shapes.Add(new Shape(part.Kind, part.Class, uv, box, part.Turn, BuiltHand.Rank(part.Kind, part.Class)));
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
        return box.Grow(BuiltHand.WidestUnits / _fullWidth);
    }

    public Node2D? Layer(Rect2 uv, float pixels, int style)
    {
        var reach = uv.Grow(BuiltHand.WidestUnits / _fullWidth);
        var seen = _shapes.Where(s => s.Box.Intersects(reach, true)).ToList();
        float perUnit = pixels / (uv.Size.X * _fullWidth);
        return seen.Count == 0 ? null : new BuiltLayer(seen, uv, pixels, perUnit, pixels / (uv.Size.Y * _fullHeight) / perUnit, style);
    }
}

sealed partial class BuiltLayer : Node2D
{
    readonly List<BuiltPainter.Shape> _shapes;
    readonly Rect2 _uv;
    readonly float _pixels, _perUnit, _squash;
    readonly int _style;

    public BuiltLayer(List<BuiltPainter.Shape> shapes, Rect2 uv, float pixels, float perUnit, float squash, int style)
    {
        _shapes = shapes;
        _uv = uv;
        _pixels = pixels;
        _perUnit = perUnit;
        _squash = squash;
        _style = style;
        Scale = new Vector2(1, squash);
    }

    Vector2[] Px(Vector2[] uv) => uv.Select(p => (p - _uv.Position) / _uv.Size * _pixels * new Vector2(1, 1 / _squash)).ToArray();

    public override void _Draw()
    {
        var hand = BuiltHand.Of(_style);
        foreach (var s in _shapes)
        {
            var px = Px(s.Uv);
            if (s.Kind == PartKinds.Area) Area(hand, s, px);
            else if (s.Kind == PartKinds.Line) Line(hand, s, px);
            else if (s.Kind == PartKinds.Block) Block(hand, s, px);
            else if (s.Kind == PartKinds.Sign) Sign(hand, s, px[0]);
        }
    }

    float Wide(float units, float leastPx, float mostPx) => Mathf.Clamp(units * _perUnit, leastPx, mostPx);

    void Area(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        var look = hand.Area(s.Class);
        if (look.Fill.A > 0) Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Wide(BuiltHand.Fine, BuiltHand.LeastPx, BuiltHand.FineMostPx), true);
    }

    void Filled(Vector2[] px, Color fill)
    {
        DrawColoredPolygon(px, fill);
        DrawPolyline(px.Append(px[0]).ToArray(), fill, 1, true);
    }

    void Line(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 2) return;
        var look = hand.Line(s.Class);
        float outer = Wide(look.Width, look.LeastPx, look.MostPx);
        if (look.Dash > 0)
        {
            float dash = Mathf.Max(look.Dash * _perUnit, outer * 2);
            for (int i = 0; i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], look.Ink, outer, dash, true, true);
            return;
        }
        DrawPolyline(px, look.Ink, outer, true);
        float casing = Wide(look.Casing, BuiltHand.LeastPx, BuiltHand.FineMostPx);
        if (look.Fill.A > 0 && outer > casing * 2 + 0.6f) DrawPolyline(px, look.Fill, outer - casing * 2, true);
    }

    void Block(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        var look = hand.Block(s.Class);
        float shade = Wide(BuiltHand.Heavy, BuiltHand.LeastPx, BuiltHand.HeavyMostPx);
        if (look.Shadow.A > 0) Filled(px.Select(p => p + new Vector2(shade, shade)).ToArray(), look.Shadow);
        Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Wide(BuiltHand.Fine, BuiltHand.LeastPx, BuiltHand.FineMostPx), true);
    }

    const float TreeCrown = 0.5f, TreeShade = 0.14f, RickSize = 0.36f;

    void Sign(BuiltHand hand, BuiltPainter.Shape s, Vector2 at)
    {
        float r = Wide(BuiltHand.SignUnits, BuiltHand.SignLeastPx, BuiltHand.SignMostPx);
        float line = Wide(BuiltHand.Medium, BuiltHand.LeastPx, BuiltHand.MediumMostPx);
        var across = Vector2.Right.Rotated(Mathf.DegToRad(s.Turn / 10f));
        var along = across.Orthogonal();
        switch (s.Class)
        {
            case "well":
                DrawArc(at, r * 0.5f, 0, Mathf.Tau, 16, hand.Ink, line, true);
                break;
            case "bridge":
                DrawLine(at - across * r + along * r * 0.6f, at + across * r + along * r * 0.6f, hand.Ink, line, true);
                DrawLine(at - across * r - along * r * 0.6f, at + across * r - along * r * 0.6f, hand.Ink, line, true);
                break;
            case "ford":
                DrawDashedLine(at - across * r, at + across * r, hand.Ink, line, line * 2, true, true);
                break;
            case "mill":
                DrawLine(at - new Vector2(r, r), at + new Vector2(r, r), hand.Ink, line, true);
                DrawLine(at - new Vector2(r, -r), at + new Vector2(r, -r), hand.Ink, line, true);
                DrawCircle(at, r * 0.3f, hand.Signal);
                break;
            case "cross":
                DrawLine(at - Vector2.Down * r, at + Vector2.Down * r * 0.6f, hand.Ink, line, true);
                DrawLine(at - Vector2.Right * r * 0.5f - Vector2.Down * r * 0.35f, at + Vector2.Right * r * 0.5f - Vector2.Down * r * 0.35f, hand.Ink, line, true);
                break;
            case "tower":
                DrawCircle(at, r * 0.55f, hand.Ink);
                break;
            case "tree":
                if (hand.Shaded) DrawCircle(at + new Vector2(r, r) * TreeShade, r * TreeCrown, hand.Ink with { A = 0.3f });
                DrawCircle(at, r * TreeCrown, hand.Tree);
                DrawArc(at, r * TreeCrown, 0, Mathf.Tau, 14, hand.Ink with { A = 0.75f }, line * 0.5f, true);
                break;
            case "rick":
                DrawCircle(at, r * RickSize, hand.Straw);
                DrawArc(at, r * RickSize, 0, Mathf.Tau, 12, hand.Ink with { A = 0.8f }, line * 0.5f, true);
                break;
            case "shrine":
                DrawLine(at - Vector2.Down * r * 0.55f, at + Vector2.Down * r * 0.35f, hand.Ink, line * 0.8f, true);
                DrawLine(at - Vector2.Right * r * 0.3f - Vector2.Down * r * 0.2f, at + Vector2.Right * r * 0.3f - Vector2.Down * r * 0.2f, hand.Ink, line * 0.8f, true);
                break;
            case "flag":
                DrawLine(at, at - Vector2.Down * r * 1.1f, hand.Ink, line * 0.8f, true);
                DrawColoredPolygon([at - Vector2.Down * r * 1.1f, at - Vector2.Down * r * 0.55f, at + Vector2.Right * r * 0.75f - Vector2.Down * r * 0.82f], hand.Signal);
                break;
            case "wheel":
                DrawArc(at, r * 0.5f, 0, Mathf.Tau, 14, hand.Wet, line, true);
                DrawLine(at - across * r * 0.5f, at + across * r * 0.5f, hand.Wet, line * 0.6f, true);
                DrawLine(at - along * r * 0.5f, at + along * r * 0.5f, hand.Wet, line * 0.6f, true);
                break;
            default:
                DrawCircle(at, r * 0.35f, hand.Ink);
                break;
        }
    }
}

public sealed class BuiltHand
{
    public const float Fine = 0.012f, Medium = 0.025f, Heavy = 0.05f, SignUnits = 0.18f, WidestUnits = 1.2f;
    const float GardenWash = 0.5f, TroddenWash = 0.45f, GlacisWash = 0.3f, PlotEdge = 0.28f;
    public const float LeastPx = 0.7f, FineMostPx = 1.5f, MediumMostPx = 2.2f, HeavyMostPx = 3.2f, SignLeastPx = 2.5f, SignMostPx = 9;

    public readonly record struct AreaLook(Color Fill, Color Edge);
    public readonly record struct BlockLook(Color Fill, Color Edge, Color Shadow);
    public readonly record struct LineLook(Color Ink, Color Fill, float Width, float LeastPx, float MostPx, float Casing, float Dash = 0);

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
    Color Timber { get; init; }
    public Color Tree { get; private init; }
    public Color Straw { get; private init; }
    public Color Wet { get; private init; }
    public bool Shaded => Shadows;
    bool Shadows { get; init; }
    bool RoadsAsLines { get; init; }

    static Color C(string html) => Color.FromHtml(html);

    static readonly Color None = new(0, 0, 0, 0);

    static readonly BuiltHand Survey = new()
    {
        Ink = C("#241F17"), Signal = C("#A33A2A"), Paper = C("#D6C8A6"), Masonry = C("#84423A"), Church = C("#9C3628"), Road = C("#D9C9A2"), MainRoad = C("#BDA078"),
        Water = C("#7F9AAB"), WaterLine = C("#2B4763"), Garden = C("#9FAA78"), Yard = C("#C6B48F"), Hedge = C("#4E593A"), Timber = C("#5F4E3C"), Tree = C("#55633F"), Straw = C("#BFA058"), Wet = C("#2B4763"), Shadows = true,
    };

    static readonly BuiltHand Modern = new()
    {
        Ink = C("#242220"), Signal = C("#A84025"), Paper = C("#DED7BF"), Masonry = C("#242220"), Church = C("#242220"), Road = C("#EAE4D0"), MainRoad = C("#A84025"),
        Water = C("#9DB7D6"), WaterLine = C("#364A75"), Garden = C("#C9D3B2"), Yard = C("#DED7BF"), Hedge = C("#242220"), Timber = C("#4A4744"), Tree = C("#7FA06A"), Straw = C("#B99A56"), Wet = C("#364A75"),
    };

    static readonly BuiltHand Ancient = new()
    {
        Ink = C("#1E1B18"), Signal = C("#8F4226"), Paper = C("#C4A878"), Masonry = C("#964E36"), Church = C("#EFE8D6"), Road = C("#8F4226"), MainRoad = C("#8F4226"),
        Water = C("#6E9E9B"), WaterLine = C("#3E6F6B"), Garden = C("#A8AC82"), Yard = C("#C4A878"), Hedge = C("#1E1B18"), Timber = C("#6E5136"), Tree = C("#62733F"), Straw = C("#B8923F"), Wet = C("#3E6F6B"), RoadsAsLines = true,
    };

    static readonly BuiltHand Staff = new()
    {
        Ink = C("#201E1C"), Signal = C("#201E1C"), Paper = C("#DAD7C3"), Masonry = C("#201E1C"), Church = C("#201E1C"), Road = C("#E6E3D0"), MainRoad = C("#E6E3D0"),
        Water = C("#C6C8C0"), WaterLine = C("#32302E"), Garden = C("#C9C7B4"), Yard = C("#DAD7C3"), Hedge = C("#201E1C"), Timber = C("#55534E"), Tree = C("#8E8D7F"), Straw = C("#A9A796"), Wet = C("#32302E"),
    };

    public static BuiltHand Of(int style) => style == 1 ? Ancient : style == 2 ? Modern : style == 3 ? Staff : Survey;

    static readonly string[] LineOrder =
        ["moat", "ditch", "stream", "river", "hedge", "fence", "palisade", "track", "lane", "street", "road", "chaussee", "glacis", "rampart", "wall"];

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
        "quarter" => new(None, None),
        "moat" or "pond" => new(Water, WaterLine),
        "glacis" => new(Yard with { A = GlacisWash }, None),
        "field" => new(None, Ink with { A = 0.55f }),
        _ => new(Yard with { A = TroddenWash }, None),
    };

    public BlockLook Block(string cls) => cls switch
    {
        "church" or "public" or "steeple" => new(Church, Ink, Shadows ? Ink : None),
        "shed" or "barn" => new(Timber, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
        "tower" or "bastion" => new(Ink, Ink, None),
        _ => new(Masonry, Shadows || RoadsAsLines ? Ink : None, Shadows ? Ink : None),
    };

    public LineLook Line(string cls) => cls switch
    {
        "track" => new(RoadsAsLines ? Road : Ink with { A = 0.8f }, None, Medium, 0.9f, MediumMostPx, 0, RoadsAsLines ? 0 : 0.14f),
        "lane" => RoadsAsLines ? new(Road, None, Fine * 1.5f, 0.9f, 1.8f, 0) : new(Ink, Paper, 0.07f, 1.6f, 4, Fine),
        "street" => RoadsAsLines ? new(Road, None, Medium, 1, 2.4f, 0) : new(Ink, Paper, 0.1f, 2, 5, Fine),
        "road" => RoadsAsLines ? new(Road, None, Medium * 1.4f, 1.2f, 3, 0) : new(Ink, Road, 0.12f, 2.4f, 6, Fine),
        "chaussee" => RoadsAsLines ? new(Road, None, Heavy, 1.6f, 4, 0) : new(Ink, MainRoad, 0.16f, 3.2f, 7.5f, Medium),
        "wall" => new(Ink, None, Heavy, 1.2f, HeavyMostPx, 0),
        "rampart" => new(Ink, Yard, 0.22f, 2.6f, 9, Medium),
        "palisade" => new(Ink, None, Medium, 1, MediumMostPx, 0, 0.05f),
        "glacis" => new(Ink with { A = 0.45f }, None, Fine, LeastPx, FineMostPx, 0),
        "hedge" => new(Hedge, None, Medium, 0.9f, MediumMostPx, 0, 0.04f),
        "fence" => new(Ink with { A = 0.7f }, None, Fine, LeastPx, FineMostPx, 0),
        "ditch" or "stream" => new(WaterLine, None, Medium, 0.9f, MediumMostPx, 0),
        "moat" or "river" => new(WaterLine, Water, 0.3f, 2.4f, 14, Fine),
        _ => new(Ink, None, Fine, LeastPx, FineMostPx, 0),
    };
}
