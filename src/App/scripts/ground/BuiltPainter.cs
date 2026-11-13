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
        var roads = _shapes.Where(BuiltHand.IsRoad).ToList();
        bool paved = false;
        foreach (var s in _shapes)
        {
            var px = Px(s.Uv);
            if (s.Kind == PartKinds.Area) Area(hand, s, px);
            else if (BuiltHand.IsRoad(s))
            {
                if (paved) continue;
                paved = true;
                foreach (var road in roads) Line(hand, road, Px(road.Uv), true, false);
                foreach (var road in roads) Line(hand, road, Px(road.Uv), false, true);
            }
            else if (s.Kind == PartKinds.Line) Line(hand, s, px, true, true);
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
        if (s.Class == "garden" && px.Length == 4) Beds(hand, px);
    }

    void Beds(BuiltHand hand, Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2 a = px[0], along = longFirst ? px[1] - px[0] : px[3] - px[0], across = longFirst ? px[3] - px[0] : px[1] - px[0];
        if (across.Length() < BedsFromPx) return;
        int beds = Mathf.Clamp((int)(across.Length() / BedPx), 2, MostBeds);
        for (int i = 1; i < beds; i++) DrawLine(a + across * (i / (float)beds), a + across * (i / (float)beds) + along, hand.Ink with { A = BedInk }, BuiltHand.LeastPx, true);
    }

    void Ridge(BuiltHand hand, Vector2[] px)
    {
        bool longFirst = (px[1] - px[0]).Length() >= (px[2] - px[1]).Length();
        Vector2 from = longFirst ? (px[0] + px[3]) / 2 : (px[0] + px[1]) / 2, to = longFirst ? (px[1] + px[2]) / 2 : (px[2] + px[3]) / 2;
        if ((to - from).Length() < RidgeFromPx) return;
        DrawLine(from.Lerp(to, RidgeInset), to.Lerp(from, RidgeInset), hand.Ink with { A = RidgeInk }, BuiltHand.LeastPx, true);
    }

    void Avenue(BuiltHand hand, Vector2[] px, float outer)
    {
        float r = Wide(BuiltHand.SignUnits * AvenueTree, 0, BuiltHand.SignMostPx), apart = AvenueApart * _perUnit;
        if (r < AvenueFromPx) return;
        float carried = apart / 2;
        for (int i = 0; i + 1 < px.Length; i++)
        {
            var leg = px[i + 1] - px[i];
            float length = leg.Length();
            if (length < 0.001f) continue;
            var side = leg.Orthogonal() / length * (outer / 2 + r * 1.6f);
            for (; carried < length; carried += apart)
                foreach (int way in new[] { 1, -1 })
                {
                    var at = px[i] + leg * (carried / length) + side * way;
                    DrawCircle(at, r, hand.Tree);
                    DrawArc(at, r, 0, Mathf.Tau, 10, hand.Ink with { A = 0.6f }, BuiltHand.LeastPx * 0.7f, true);
                }
            carried -= length;
        }
    }

    void Filled(Vector2[] px, Color fill)
    {
        DrawColoredPolygon(px, fill);
        DrawPolyline(px.Append(px[0]).ToArray(), fill, 1, true);
    }

    void Line(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px, bool cased, bool filled)
    {
        if (px.Length < 2) return;
        var look = hand.Line(s.Class);
        float outer = Wide(look.Width, look.LeastPx, look.MostPx);
        if (look.Dash > 0)
        {
            float dash = Mathf.Max(look.Dash * _perUnit, outer * 2);
            for (int i = 0; cased && i + 1 < px.Length; i++) DrawDashedLine(px[i], px[i + 1], look.Ink, outer, dash, true, true);
            return;
        }
        if (cased) DrawPolyline(px, look.Ink, outer, true);
        if (!filled) return;
        float casing = Wide(look.Casing, BuiltHand.LeastPx, BuiltHand.FineMostPx);
        if (look.Fill.A > 0 && outer > casing * 2 + 0.6f) DrawPolyline(px, look.Fill, outer - casing * 2, true);
        if (s.Class == "chaussee") Avenue(hand, px, outer);
    }

    void Block(BuiltHand hand, BuiltPainter.Shape s, Vector2[] px)
    {
        if (px.Length < 3) return;
        var look = hand.Block(s.Class);
        float shade = Wide(BuiltHand.Heavy, BuiltHand.LeastPx, BuiltHand.HeavyMostPx);
        if (look.Shadow.A > 0) Filled(px.Select(p => p + new Vector2(shade, shade)).ToArray(), look.Shadow);
        Filled(px, look.Fill);
        if (look.Edge.A > 0) DrawPolyline(px.Append(px[0]).ToArray(), look.Edge, Wide(BuiltHand.Fine, BuiltHand.LeastPx, BuiltHand.FineMostPx), true);
        if (px.Length == 4 && hand.Shaded && s.Class is "house" or "barn" or "farm" or "church" or "public" or "barracks") Ridge(hand, px);
    }

    const float TreeCrown = 0.5f, TreeShade = 0.14f, RickSize = 0.36f, BedsFromPx = 9, BedPx = 4.5f, BedInk = 0.2f, RidgeFromPx = 9, RidgeInset = 0.12f, RidgeInk = 0.5f;
    const float AvenueTree = 0.3f, AvenueApart = 0.5f, AvenueFromPx = 1.1f;
    const int MostBeds = 7;

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
        Ink = C("#1F1A12"), Signal = C("#A3301F"), Paper = C("#D0C098"), Masonry = C("#8A3B2C"), Church = C("#A52D1E"), Road = C("#E0D0A4"), MainRoad = C("#C9A66E"),
        Water = C("#6088A8"), WaterLine = C("#20405F"), Garden = C("#8FA062"), Yard = C("#BFA97E"), Hedge = C("#3F5230"), Timber = C("#4E3E2E"), Tree = C("#445A30"),
        Straw = C("#C9A040"), Wet = C("#20405F"), Shadows = true,
    };

    static readonly BuiltHand Modern = new()
    {
        Ink = C("#1E1C1A"), Signal = C("#A63A1E"), Paper = C("#DAD2B6"), Masonry = C("#1E1C1A"), Church = C("#1E1C1A"), Road = C("#EDE6CE"), MainRoad = C("#A63A1E"),
        Water = C("#86A9D6"), WaterLine = C("#2C447A"), Garden = C("#BFD0A0"), Yard = C("#DAD2B6"), Hedge = C("#1E1C1A"), Timber = C("#403D3A"), Tree = C("#6C9A58"),
        Straw = C("#B8974C"), Wet = C("#2C447A"),
    };

    static readonly BuiltHand Ancient = new()
    {
        Ink = C("#1B1815"), Signal = C("#8A3A20"), Paper = C("#BE9F6C"), Masonry = C("#8E4630"), Church = C("#EFE6CF"), Road = C("#8A3A20"), MainRoad = C("#8A3A20"),
        Water = C("#4F8F96"), WaterLine = C("#2F6268"), Garden = C("#9CA070"), Yard = C("#BE9F6C"), Hedge = C("#1B1815"), Timber = C("#654A30"), Tree = C("#55703A"),
        Straw = C("#B88C38"), Wet = C("#2F6268"), RoadsAsLines = true,
    };

    static readonly BuiltHand Staff = new()
    {
        Ink = C("#1A1816"), Signal = C("#1A1816"), Paper = C("#D4D0BA"), Masonry = C("#1A1816"), Church = C("#1A1816"), Road = C("#E4E0CC"), MainRoad = C("#E4E0CC"),
        Water = C("#B8BCB4"), WaterLine = C("#2A2826"), Garden = C("#C0BEAA"), Yard = C("#D4D0BA"), Hedge = C("#1A1816"), Timber = C("#4A4844"), Tree = C("#84837A"),
        Straw = C("#A09E8E"), Wet = C("#2A2826"),
    };

    public static BuiltHand Of(int style) => style == 1 ? Ancient : style == 2 ? Modern : style == 3 ? Staff : Survey;

    static readonly string[] RoadClasses = ["track", "lane", "street", "road", "chaussee"];

    public static bool IsRoad(BuiltPainter.Shape s) => s.Kind == PartKinds.Line && Array.IndexOf(RoadClasses, s.Class) >= 0;

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
