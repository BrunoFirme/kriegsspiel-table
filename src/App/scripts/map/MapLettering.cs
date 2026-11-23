using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

sealed class MapLettering
{
    public sealed record Hand(string Name, Font Font, int Pixels, Color Ink, Color Halo, bool Capitals = false, bool Interpuncts = false);

    public sealed class Lettered
    {
        public SubViewport? View;
        public ImageTexture? Texture;
        public Vector2 Size;
        public int FramesLeft = FramesToRender;
        public readonly List<Action<ImageTexture>> Waiting = new();
    }

    public readonly record struct Row(string Text, float Width, bool Fill);

    public sealed record Layout(Vector2 Size, List<Row> Rows, float Step, float Wide);

    public readonly record struct NameBox(Vec2 Centre, Vec2 Along, double HalfWide, double HalfTall)
    {
        public Vec2 Up => new(-Along.Z, Along.X);

        public Vec2 At(double across, double up) => new(Centre.X + Along.X * across + Up.X * up, Centre.Z + Along.Z * across + Up.Z * up);

        public List<Vec2> Corners(double pad) =>
            [At(-HalfWide - pad, -HalfTall - pad), At(HalfWide + pad, -HalfTall - pad), At(HalfWide + pad, HalfTall + pad), At(-HalfWide - pad, HalfTall + pad)];

        public (double Across, double Up) Local(Vec2 g)
        {
            var d = g - Centre;
            return (d.X * Along.X + d.Z * Along.Z, d.X * Up.X + d.Z * Up.Z);
        }

        public bool Holds(Vec2 g, double pad)
        {
            var (across, up) = Local(g);
            return Math.Abs(across) <= HalfWide + pad && Math.Abs(up) <= HalfTall + pad;
        }
    }

    public const int Pixels = 96, Margin = 12, HaloPixels = 10;
    const int FramesToRender = 2, BoldWeight = 700, PlainWeight = 400, MostWrapPixels = 8192;
    const float ReachPerLetter = 4f, Baseline = 0.42f;
    const double PlaceGap = 0.3, SpaceShare = 0.3;
    const string Interpunct = " · ";
    static readonly string[] Serifs = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
    static readonly Dictionary<string, string> Shipped = new()
    {
        [Stroke.FontDidone] = "res://fonts/ui/OldStandard-Regular.ttf",
        [Stroke.FontSans] = "res://fonts/ui/Barlow-Regular.ttf",
        [Stroke.FontFraktur] = "res://fonts/panels/UnifrakturCook-Bold.ttf",
        [Stroke.FontScript] = "res://fonts/panels/LoversQuarrel-Regular.ttf",
        [Stroke.FontCapitals] = "res://fonts/panels/OverlockSC-Regular.ttf",
    };
    static readonly Color[] Ink = { new(0.93f, 0.93f, 0.88f), new(0.3f, 0.2f, 0.12f), new(0.1f, 0.1f, 0.1f), new(0.15f, 0.14f, 0.13f), new(0.133f, 0.102f, 0.082f) };
    public static readonly Color[] Paper =
    {
        new(0.13f, 0.16f, 0.08f, 0.55f), new(0.690f, 0.573f, 0.384f, 0.55f), new(0.792f, 0.757f, 0.627f, 0.7f), new(0.773f, 0.757f, 0.671f, 0.6f), new(0.506f, 0.435f, 0.361f, 0.5f),
    };

    internal static int Styles => Math.Min(Ink.Length, Paper.Length);

    public static Color OwnInk(int style) => Ink[Math.Clamp(style, 0, Ink.Length - 1)];
    static readonly Color ModernBlue = new(0.2f, 0.33f, 0.44f);
    static readonly Color Rubric = new(0.529f, 0.169f, 0.153f);
    const float BackSlant = -0.35f, ForwardSlant = 0.22f, Embolden = 0.6f;
    const float AreaTracking = 0.35f, ModernAreaTracking = 0.5f, AncientTracking = 0.25f, ItalicTracking = 0.04f, Tracking = 0.02f;

    readonly TableContext _c;
    readonly Dictionary<string, Lettered> _baked = new();
    readonly Dictionary<(int, NameKind, string, int, string, bool, bool), Hand> _hands = new();
    readonly List<Node3D> _placed = new(), _preview = new();
    readonly List<Lettered> _used = new();
    List<Node3D> _into;
    Func<string, bool> _shown = _ => true;
    public readonly Node3D Root = new() { Name = "MapLettering" };

    public MapLettering(TableContext c)
    {
        _c = c;
        _into = _placed;
    }

    public readonly record struct Mark(Vec2 At, Vec2 Up, float Height);

    public string? Hidden { get; private set; }
    public Mark? Caret { get; private set; }
    public int Previewed => _preview.Count;

    public void Preview(Stroke? st, string? hidden)
    {
        if (hidden != Hidden)
        {
            Hidden = hidden;
            Sync(_shown);
        }
        var old = _preview.ToList();
        _preview.Clear();
        _used.Clear();
        Caret = null;
        if (st is { Text.Length: > 0, Points.Count: > 0 })
        {
            _into = _preview;
            Write(st, Style);
            _into = _placed;
        }
        var pending = _used.Where(l => l.Texture == null).Distinct().ToList();
        if (pending.Count == 0)
        {
            Free(old);
            return;
        }
        int left = pending.Count;
        foreach (var l in pending) l.Waiting.Add(_ => { if (--left == 0) Free(old); });
    }

    int Style => MapStyles.Index(_c.S.View.Lighting.MapStyle);

    static void Free(List<Node3D> nodes)
    {
        foreach (var n in nodes)
            if (GodotObject.IsInstanceValid(n)) n.QueueFree();
    }

    static Vector2 Measured(string words, Hand hand) => hand.Font.GetStringSize(words, HorizontalAlignment.Left, -1, hand.Pixels);

    static string Gap(Hand hand) => hand.Interpuncts ? Interpunct : " ";

    public static Layout LaidOut(string words, Hand hand, float? wrap)
    {
        var rows = new List<Row>();
        foreach (var paragraph in words.Split('\n'))
        {
            if (wrap is { } wide) rows.AddRange(Wrapped(paragraph, hand, wide));
            else rows.Add(new Row(paragraph, Measured(paragraph, hand).X, false));
        }
        float step = rows.Max(r => r.Text.Length == 0 ? hand.Font.GetHeight(hand.Pixels) : Measured(r.Text, hand).Y);
        float across = wrap ?? rows.Max(r => r.Width);
        return new Layout(new Vector2(across + Margin * 2, step * rows.Count + Margin * 2), rows, step, across);
    }

    static IEnumerable<Row> Wrapped(string paragraph, Hand hand, float wide)
    {
        string gap = Gap(hand), line = "";
        float Across(string s) => Measured(s, hand).X;
        foreach (var word in paragraph.Split(gap, StringSplitOptions.RemoveEmptyEntries))
        {
            var tried = line.Length == 0 ? word : line + gap + word;
            if (line.Length > 0 && Across(tried) > wide)
            {
                yield return new Row(line, Across(line), true);
                tried = word;
            }
            line = tried;
            while (line.Length > 1 && Across(line) > wide)
            {
                int fit = 1;
                while (fit < line.Length - 1 && Across(line[..(fit + 1)]) <= wide) fit++;
                yield return new Row(line[..fit], Across(line[..fit]), false);
                line = line[fit..];
            }
        }
        yield return new Row(line, Across(line), false);
    }

    static float RowFrom(Row row, float across, string align) => row.Fill && align == Stroke.AlignSpread ? 0 : align switch
    {
        Stroke.AlignLeft => 0,
        Stroke.AlignRight => across - row.Width,
        _ => (across - row.Width) / 2,
    };

    public Lettered Bake(string key, string words, Hand hand) => Bake(key, LaidOut(words, hand, null), hand, Stroke.AlignLeft);

    Lettered Bake(string key, Layout layout, Hand hand, string align)
    {
        if (_baked.TryGetValue(key, out var made)) return made;
        var size = layout.Size;
        var view = new SubViewport
        {
            Size = new Vector2I((int)Math.Ceiling(size.X), (int)Math.Ceiling(size.Y)),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        for (int i = 0; i < layout.Rows.Count; i++)
        {
            var row = layout.Rows[i];
            if (row.Text.Length == 0) continue;
            var label = new Label { Text = row.Text, Position = new Vector2(Margin + RowFrom(row, layout.Wide, align), Margin + i * layout.Step) };
            if (row.Fill && align == Stroke.AlignSpread)
            {
                label.HorizontalAlignment = HorizontalAlignment.Fill;
                label.JustificationFlags = TextServer.JustificationFlag.Kashida | TextServer.JustificationFlag.WordBound | TextServer.JustificationFlag.TrimEdgeSpaces;
                label.Size = new Vector2(layout.Wide, layout.Step);
            }
            label.AddThemeFontOverride("font", hand.Font);
            label.AddThemeFontSizeOverride("font_size", hand.Pixels);
            label.AddThemeColorOverride("font_color", hand.Ink);
            label.AddThemeColorOverride("font_outline_color", hand.Halo);
            label.AddThemeConstantOverride("outline_size", HaloPixels);
            view.AddChild(label);
        }
        Root.AddChild(view);
        return _baked[key] = new Lettered { View = view, Size = size };
    }

    static string Key(Hand hand, string words, Layout layout, float? wrap, string align) =>
        layout.Rows.Count == 1 && wrap == null ? $"{hand.Name}:{words}" : $"{hand.Name}:{wrap}:{align}:{words}";

    public void Develop()
    {
        foreach (var l in _baked.Values)
        {
            if (l.View == null || --l.FramesLeft > 0) continue;
            l.Texture = ImageTexture.CreateFromImage(l.View.GetTexture().GetImage());
            l.View.QueueFree();
            l.View = null;
            foreach (var give in l.Waiting) give(l.Texture);
            l.Waiting.Clear();
        }
    }

    public Decal Place(Lettered lettered, Vector3 at, Vector3 up, float height, float slope)
    {
        var right = new Vector3(-up.Z, 0, up.X);
        float width = height * lettered.Size.X / lettered.Size.Y;
        var decal = new Decal
        {
            Size = new Vector3(width, height * ReachPerLetter * (1 + slope), height),
            CullMask = ReliefSurface.GroundLayer,
            NormalFade = 0,
            Transform = new Transform3D(new Basis(right, Vector3.Up, -up), at),
        };
        Root.AddChild(decal);
        if (lettered.Texture != null) decal.TextureAlbedo = lettered.Texture;
        else lettered.Waiting.Add(t => { if (GodotObject.IsInstanceValid(decal)) decal.TextureAlbedo = t; });
        return decal;
    }

    public static Font Styled(bool bold, bool italic) => new SystemFont { FontNames = Serifs, FontWeight = bold ? BoldWeight : PlainWeight, FontItalic = italic };

    static string OwnShipped(int style, NameKind kind) =>
        style == MapStyles.Index(MapStyles.Ancient) ? Stroke.FontDidone
        : style != MapStyles.Index(MapStyles.Medieval) ? "" : kind == NameKind.Area ? Stroke.FontDidone : Stroke.FontFraktur;

    public static NameKind? MapHand(string? font) => font switch
    {
        null or Stroke.FontSerif => NameKind.Plain,
        Stroke.FontWater => NameKind.Water,
        Stroke.FontRegion => NameKind.Area,
        _ => null,
    };

    public static (Font Face, bool Capitals) OwnLettering(int style, NameKind kind)
    {
        var (face, capitals) = Letters(style, kind, "", 0, false, false);
        face.SpacingGlyph = 0;
        return (face, capitals);
    }

    public static Font Face(string? font) =>
        font != null && Shipped.TryGetValue(font, out var file) && ResourceLoader.Load<FontFile>(file) is { } shipped ? shipped : new SystemFont { FontNames = Serifs };

    public Hand HandFor(int style, NameKind kind, string ink = "", double spacing = 0, string? font = null, bool bold = false, bool italic = false)
    {
        int spaced = (int)Math.Round(spacing * Pixels);
        string chosen = font != null && Shipped.ContainsKey(font) ? font : "";
        var key = (style, kind, ink, spaced, chosen, bold, italic);
        if (_hands.TryGetValue(key, out var hand)) return hand;
        var (face, capitals) = Letters(style, kind, chosen, spaced, bold, italic);
        bool own = chosen.Length == 0, medieval = style == MapStyles.Index(MapStyles.Medieval);
        var inked = style == MapStyles.Index(MapStyles.Modern) && kind == NameKind.Water ? ModernBlue : own && medieval && kind == NameKind.Area ? Rubric : Ink[style];
        string name = $"{style}/{kind}/{ink}/{spaced}/{chosen}/{(bold ? "b" : "")}{(italic ? "i" : "")}";
        return _hands[key] = new Hand(name, face, Pixels, Coords.ParseColor(ink, inked), Paper[style], capitals, own && style == MapStyles.Index(MapStyles.Ancient));
    }

    static (FontVariation Face, bool Capitals) Letters(int style, NameKind kind, string chosen, int spaced, bool bold, bool italic)
    {
        bool ancient = style == MapStyles.Index(MapStyles.Ancient), modern = style == MapStyles.Index(MapStyles.Modern), medieval = style == MapStyles.Index(MapStyles.Medieval);
        bool own = chosen.Length == 0;
        string shipped = own ? OwnShipped(style, kind) : chosen;
        bool backslant = own && !ancient && !medieval && kind == NameKind.Water;
        bool capitals = own && (ancient || kind == NameKind.Area);
        float tracking = !own ? Tracking : ancient ? AncientTracking : kind == NameKind.Area ? modern ? ModernAreaTracking : AreaTracking : backslant ? ItalicTracking : Tracking;
        var face = new FontVariation
        {
            BaseFont = shipped.Length > 0 ? Face(shipped) : new SystemFont { FontNames = Serifs, FontItalic = backslant || italic, FontWeight = bold ? BoldWeight : PlainWeight },
            SpacingGlyph = (int)Math.Round(tracking * Pixels) + spaced,
            Fallbacks = new Godot.Collections.Array<Font> { new SystemFont { FontNames = Serifs } },
        };
        if (backslant && !modern) face.VariationTransform = Slanted(BackSlant);
        else if (italic && shipped.Length > 0) face.VariationTransform = Slanted(ForwardSlant);
        if (bold && shipped.Length > 0) face.VariationEmbolden = Embolden;
        return (face, capitals);
    }

    static Transform2D Slanted(float by) => new(new Vector2(1, 0), new Vector2(by, 1), Vector2.Zero);

    Hand HandOf(Stroke st, int style) => HandFor(style, MapHand(st.Font) ?? NameKind.Plain, st.Color, st.Spacing ?? 0, st.Font, st.Bold, st.Italic);

    public static string Words(string text, Hand hand)
    {
        var words = text.Replace("\r", "").Replace('\t', ' ').TrimStart().TrimEnd(' ');
        if (hand.Capitals) words = words.ToUpperInvariant();
        var lines = words.Split('\n').Select(l => l.Trim(' '));
        if (hand.Interpuncts) lines = lines.Select(l => string.Join(Interpunct, l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        return string.Join("\n", lines);
    }

    public void Sync(Func<string, bool> boardShown)
    {
        _shown = boardShown;
        foreach (var n in _placed) n.QueueFree();
        _placed.Clear();
        var s = _c.S;
        int style = Style;
        foreach (var (layer, strokes) in s.View.Drawings)
        {
            if (!_c.LayerShown(layer)) continue;
            foreach (var st in strokes)
                if (st.Text is { Length: > 0 } && st.Points.Count > 0 && st.Id != Hidden && boardShown(st.Board)) Write(st, style);
        }
        foreach (var thing in s.View.Built)
            if (thing.Name is { Length: > 0 } name && BuiltGeometry.NameSpot(thing) is { } spot && s.View.Sheets.FirstOrDefault(x => x.Id == thing.Sheet) is { } sheet)
                Sign(name, BuiltGeometry.ToWorld(sheet, spot), NameKind.Place, style);
    }

    readonly record struct Single(Vec2 Centre, Vec2 Along, Vec2 Up, float Height, float Inside, float Rise);

    static Vec2 Step(Vec2 at, Vec2 way, double by) => new(at.X + way.X * by, at.Z + way.Z * by);

    public static Vec2 AlongOf(double turn)
    {
        double rad = turn * Math.PI / 180;
        return new Vec2(Math.Cos(rad), -Math.Sin(rad));
    }

    public static double TurnOf(Vec2 along) => Stroke.Turned(Math.Atan2(-along.Z, along.X) * 180 / Math.PI);

    static Vec2 UpOf(Vec2 along) => new(-along.Z, along.X);

    static Single OnePoint(Vec2 at, Layout layout, float letter, double turn, string align, double extra = 0)
    {
        var along = AlongOf(turn);
        var size = layout.Size;
        float height = letter * size.Y / Pixels, width = height * size.X / size.Y, inside = width / 2 - Margin * letter / Pixels;
        float below = (layout.Rows.Count - 1) * layout.Step * letter / Pixels, rise = (height - below) * Baseline - below / 2;
        double shift = align switch { Stroke.AlignLeft => inside, Stroke.AlignRight => -inside, _ => 0 } + extra;
        return new Single(Step(at, along, shift), along, UpOf(along), height, inside, rise);
    }

    static string SignAlign(NameKind kind) => kind == NameKind.Place ? Stroke.AlignLeft : Stroke.AlignCentre;

    static Single SignPoint(Vec2 at, NameKind kind, Layout layout, float letter, double turn) =>
        OnePoint(at, layout, letter, turn, SignAlign(kind), kind == NameKind.Place ? Margin * letter / Pixels + PlaceGap * letter / GroundNames.PlaceLetter : 0);

    static Mark CaretOf(Single one, Layout layout, float letter, string align)
    {
        var last = layout.Rows[^1];
        float k = letter / Pixels, end = RowFrom(last, layout.Wide, align) + last.Width;
        var at = Step(Step(one.Centre, one.Along, -one.Inside + end * k), one.Up, -(layout.Rows.Count - 1) * layout.Step * k);
        return new Mark(at, one.Up, letter);
    }

    void Sign(string text, Vec2 at, NameKind kind, int style, double? letter = null, double turn = 0)
    {
        var hand = HandFor(style, kind);
        var words = Words(text, hand);
        if (words.Length == 0) return;
        var layout = LaidOut(words, hand, null);
        var baked = Bake(Key(hand, words, layout, null, SignAlign(kind)), layout, hand, SignAlign(kind));
        float size = (float)(letter ?? GroundNames.Letter(kind));
        var one = SignPoint(at, kind, layout, size, turn);
        Set(baked, one.Centre, one.Up, one.Height, one.Rise);
        if (_into == _preview) Caret = CaretOf(one, layout, size, SignAlign(kind));
        if (kind == NameKind.Place && letter == null && _into.Count > 0) _into[^1].AddChild(new NearSign { Height = one.Height });
    }

    NameKind KindOf(Stroke st) => GroundNames.KindOf(_c.S.Ground.KindAt(st.Points[st.Points.Count / 2]));

    void Write(Stroke st, int style)
    {
        if (st.Align is { } align) { Laid(st, style, align); return; }
        var kind = KindOf(st);
        if (st.Points.Count == 1) { Sign(st.Text!, st.Points[0], kind, style, st.Letter, st.TurnDeg); return; }
        var hand = HandFor(style, kind);
        var words = Words(st.Text!, hand);
        if (words.Length == 0) return;
        float letter = (float)(st.Letter ?? GroundNames.Letter(kind));
        Along(Rightward(st.Points), words, hand, letter, Stroke.AlignSpread);
    }

    static List<Vec2> Rightward(List<Vec2> path) =>
        path[^1].X < path[0].X || path[^1].X == path[0].X && path[^1].Z < path[0].Z ? Enumerable.Reverse(path).ToList() : path;

    void Laid(Stroke st, int style, string align)
    {
        var hand = HandOf(st, style);
        var words = Words(st.Text!, hand);
        if (words.Length == 0) return;
        float letter = (float)(st.Letter ?? GroundNames.PlaceLetter);
        if (st.Points.Count > 1) { Along(Rightward(st.Points), words, hand, letter, align); return; }
        var wrap = WrapOf(st, letter);
        var layout = LaidOut(words, hand, wrap);
        var baked = Bake(Key(hand, words, layout, wrap, align), layout, hand, align);
        var one = OnePoint(st.Points[0], layout, letter, st.TurnDeg, align);
        Set(baked, one.Centre, one.Up, one.Height, one.Rise);
        if (_into == _preview) Caret = CaretOf(one, layout, letter, align);
    }

    static float? WrapOf(Stroke st, float letter) =>
        st is { Align: not null, Points.Count: 1, Wrap: { } wrap } ? (float)Math.Min(wrap * Pixels / letter, MostWrapPixels) : null;

    readonly record struct Placed(string Letter, Vec2 At, Vec2 Tangent, double Width);

    static List<Placed> Placements(List<Vec2> path, string words, Hand hand, float letter, string align)
    {
        var letters = words.Select(ch => ch.ToString()).ToList();
        var widths = letters.Select(l => l == " " ? letter * SpaceShare : hand.Font.GetStringSize(l, HorizontalAlignment.Left, -1, hand.Pixels).X * letter / hand.Pixels).ToList();
        var marks = new List<double> { 0 };
        for (int i = 1; i < path.Count; i++) marks.Add(marks[i - 1] + (path[i] - path[i - 1]).Length);
        double length = marks[^1], natural = widths.Sum();
        double gap = align == Stroke.AlignSpread && letters.Count > 1 ? Math.Max(0, (length - natural) / (letters.Count - 1)) : 0;
        double s = align switch
        {
            Stroke.AlignLeft => 0,
            Stroke.AlignRight => length - natural,
            _ => (length - natural - gap * (letters.Count - 1)) / 2,
        };
        var placed = new List<Placed>(letters.Count);
        for (int i = 0; i < letters.Count; i++)
        {
            var (at, tangent) = AlongPath(path, marks, s + widths[i] / 2);
            placed.Add(new Placed(letters[i], at, tangent, widths[i]));
            s += widths[i] + gap;
        }
        return placed;
    }

    static IEnumerable<(List<Vec2> Path, List<Placed> Placed)> Rows(List<Vec2> path, string words, Hand hand, float letter, string align)
    {
        var lines = words.Split('\n');
        float step = letter * hand.Font.GetHeight(hand.Pixels) / hand.Pixels;
        for (int i = 0; i < lines.Length; i++)
        {
            var row = Offset(path, -i * step);
            yield return (row, Placements(row, lines[i], hand, letter, align));
        }
    }

    static List<Vec2> Offset(List<Vec2> path, double by)
    {
        if (by == 0) return path;
        var moved = new List<Vec2>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            var way = Unit(path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)]);
            moved.Add(Step(path[i], UpOf(way), by));
        }
        return moved;
    }

    static Vec2 Unit(Vec2 v) => v.Length > 1e-9 ? new Vec2(v.X / v.Length, v.Z / v.Length) : new Vec2(1, 0);

    void Along(List<Vec2> path, string words, Hand hand, float letter, string align)
    {
        Mark? caret = null;
        foreach (var (row, placed) in Rows(path, words, hand, letter, align))
        {
            foreach (var p in placed)
            {
                if (p.Letter == " ") continue;
                var baked = Bake($"{hand.Name}:{p.Letter}", p.Letter, hand);
                Set(baked, p.At, UpOf(p.Tangent), letter * baked.Size.Y / hand.Pixels);
            }
            caret = placed.Count > 0
                ? new Mark(Step(placed[^1].At, placed[^1].Tangent, placed[^1].Width / 2), UpOf(placed[^1].Tangent), letter)
                : RowStart(row, align, letter);
        }
        if (_into == _preview) Caret = caret;
    }

    static Mark RowStart(List<Vec2> row, string align, float letter)
    {
        var marks = new List<double> { 0 };
        for (int i = 1; i < row.Count; i++) marks.Add(marks[i - 1] + (row[i] - row[i - 1]).Length);
        double s = align switch { Stroke.AlignLeft => 0, Stroke.AlignRight => marks[^1], _ => marks[^1] / 2 };
        var (at, tangent) = AlongPath(row, marks, s);
        return new Mark(at, UpOf(tangent), letter);
    }

    static (Vec2 at, Vec2 tangent) AlongPath(List<Vec2> path, List<double> marks, double s)
    {
        int i = 1;
        while (i < path.Count - 1 && marks[i] < s) i++;
        var a = path[i - 1];
        var b = path[i];
        double len = Math.Max(1e-9, marks[i] - marks[i - 1]);
        var t = new Vec2((b.X - a.X) / len, (b.Z - a.Z) / len);
        double f = s - marks[i - 1];
        return (new Vec2(a.X + t.X * f, a.Z + t.Z * f), t);
    }

    (Hand Hand, float Letter, string Align, NameKind Kind)? Lettering(Stroke st)
    {
        if (st.Text is not { Length: > 0 } || st.Points.Count == 0) return null;
        if (st.Align is { } align) return (HandOf(st, Style), (float)(st.Letter ?? GroundNames.PlaceLetter), align, NameKind.Plain);
        var kind = KindOf(st);
        return (HandFor(Style, kind), (float)(st.Letter ?? GroundNames.Letter(kind)), Stroke.AlignSpread, kind);
    }

    sealed record Framed(int Style, NameBox? Box, List<NameBox> Letters);

    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Stroke, Framed> _framed = new();

    public double LetterOf(Stroke st) => Lettering(st)?.Letter ?? GroundNames.PlaceLetter;

    public NameBox? BoxOf(Stroke st) => FrameOf(st).Box;

    public bool Holds(Stroke st, Vec2 g, double pad)
    {
        var framed = FrameOf(st);
        if (st.Points.Count == 1) return framed.Box is { } box && box.Holds(g, pad);
        return framed.Letters.Any(b => b.Holds(g, pad)) || framed.Box != null && Polyline.Near(st.Points, g, pad);
    }

    Framed FrameOf(Stroke st)
    {
        int style = Style;
        if (_framed.TryGetValue(st, out var kept) && kept.Style == style) return kept;
        var framed = Frame(st, style);
        _framed.AddOrUpdate(st, framed);
        return framed;
    }

    Framed Frame(Stroke st, int style)
    {
        if (Lettering(st) is not { } lettering) return new Framed(style, null, []);
        var (hand, letter, align, kind) = lettering;
        var words = Words(st.Text!, hand);
        if (words.Length == 0) return new Framed(style, null, []);
        float margin = Margin * letter / Pixels;
        if (st.Points.Count == 1)
        {
            var layout = LaidOut(words, hand, st.Align != null ? WrapOf(st, letter) : null);
            var one = st.Align != null ? OnePoint(st.Points[0], layout, letter, st.TurnDeg, align) : SignPoint(st.Points[0], kind, layout, letter, st.TurnDeg);
            return new Framed(style, new NameBox(Step(one.Centre, one.Up, one.Rise), one.Along, one.Inside, one.Height / 2 - margin), []);
        }
        var path = Rightward(st.Points);
        float tall = letter * (hand.Font.GetHeight(hand.Pixels) + 2 * Margin) / hand.Pixels;
        var letters = Rows(path, words, hand, letter, align).SelectMany(r => r.Placed).Where(p => p.Letter != " ")
            .Select(p => new NameBox(Step(p.At, UpOf(p.Tangent), tall * Baseline), p.Tangent, p.Width / 2, tall / 2 - margin)).ToList();
        if (letters.Count == 0) return new Framed(style, null, []);
        var chord = path[^1] - path[0];
        var along = chord.Length > 1e-9 ? new Vec2(chord.X / chord.Length, chord.Z / chord.Length) : new Vec2(1, 0);
        var frame = new NameBox(path[0], along, 0, 0);
        var corners = letters.SelectMany(b => b.Corners(0)).Select(frame.Local).ToList();
        double left = corners.Min(c => c.Across), right = corners.Max(c => c.Across), low = corners.Min(c => c.Up), high = corners.Max(c => c.Up);
        return new Framed(style, new NameBox(frame.At((left + right) / 2, (low + high) / 2), along, (right - left) / 2, (high - low) / 2), letters);
    }

    void Set(Lettered baked, Vec2 at, Vec2 up, float height, float? rise = null)
    {
        if (_into == _preview) _used.Add(baked);
        float lift = rise ?? height * Baseline;
        var centre = new Vec2(at.X + up.X * lift, at.Z + up.Z * lift);
        var worldUp = (Coords.ToWorld(new Vec2(centre.X + up.X, centre.Z + up.Z)) - Coords.ToWorld(centre)).Normalized();
        var ground = _c.S.Ground;
        if (ground.ReliefAt(centre) != null)
        {
            _into.Add(Place(baked, Coords.ToWorld(centre, (float)ground.At(centre)), worldUp, height, 1));
            return;
        }
        float width = height * baked.Size.X / baked.Size.Y;
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, height) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(new Vector3(-worldUp.Z, 0, worldUp.X), worldUp, Vector3.Up), Coords.ToWorld(centre, _c.SheetsTop + DrawingLayer.StrokeLift)),
        };
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        if (baked.Texture != null) mat.AlbedoTexture = baked.Texture;
        else baked.Waiting.Add(t => mat.AlbedoTexture = t);
        quad.MaterialOverride = mat;
        Root.AddChild(quad);
        _into.Add(quad);
    }
}

sealed partial class NearSign : Node
{
    const float MostShare = 0.045f;

    public float Height { get; init; }

    public override void _Process(double delta)
    {
        if (GetParent() is not Node3D sign || GetViewport()?.GetCamera3D() is not { } eye || eye.IsPositionBehind(sign.GlobalPosition)) return;
        float tall = (eye.UnprojectPosition(sign.GlobalPosition + eye.GlobalBasis.Y * Height) - eye.UnprojectPosition(sign.GlobalPosition)).Length();
        float most = GetViewport().GetVisibleRect().Size.Y * MostShare, fit = tall > most ? most / tall : 1;
        if (!Mathf.IsEqualApprox(sign.Scale.X, fit)) sign.Scale = Vector3.One * fit;
    }
}
