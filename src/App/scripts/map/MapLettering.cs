using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
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

    public const int Pixels = 96, Margin = 12, HaloPixels = 10;
    const int FramesToRender = 2;
    const float ReachPerLetter = 4f, Baseline = 0.42f;
    const double PlaceGap = 0.3, SpaceShare = 0.3;
    static readonly string[] Serifs = { "Georgia", "Times New Roman", "Liberation Serif", "DejaVu Serif" };
    static readonly string[] Titling = { "Perpetua Titling MT", "Cambria", "Georgia", "Liberation Serif" };
    static readonly Color[] Ink = { new(0.2f, 0.17f, 0.13f), new(0.3f, 0.2f, 0.12f), new(0.1f, 0.1f, 0.1f), new(0.15f, 0.14f, 0.13f) };
    public static readonly Color[] Paper =
    {
        new(0.765f, 0.714f, 0.580f, 0.55f), new(0.690f, 0.573f, 0.384f, 0.55f), new(0.792f, 0.757f, 0.627f, 0.7f), new(0.773f, 0.757f, 0.671f, 0.6f),
    };

    internal static int Styles => Math.Min(Ink.Length, Paper.Length);
    static readonly Color ModernBlue = new(0.2f, 0.33f, 0.44f);
    const float BackSlant = -0.35f, AreaTracking = 0.35f, ModernAreaTracking = 0.5f, AncientTracking = 0.25f, ItalicTracking = 0.04f, Tracking = 0.02f;

    readonly TableContext _c;
    readonly Dictionary<string, Lettered> _baked = new();
    readonly Dictionary<(int, NameKind), Hand> _hands = new();
    readonly List<Node3D> _placed = new();
    public readonly Node3D Root = new() { Name = "MapLettering" };

    public MapLettering(TableContext c) { _c = c; }

    public Lettered Bake(string key, string words, Hand hand)
    {
        if (_baked.TryGetValue(key, out var made)) return made;
        var size = hand.Font.GetStringSize(words, HorizontalAlignment.Left, -1, hand.Pixels) + new Vector2(Margin * 2, Margin * 2);
        var view = new SubViewport
        {
            Size = new Vector2I((int)Math.Ceiling(size.X), (int)Math.Ceiling(size.Y)),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        var label = new Label { Text = words, Position = new Vector2(Margin, Margin) };
        label.AddThemeFontOverride("font", hand.Font);
        label.AddThemeFontSizeOverride("font_size", hand.Pixels);
        label.AddThemeColorOverride("font_color", hand.Ink);
        label.AddThemeColorOverride("font_outline_color", hand.Halo);
        label.AddThemeConstantOverride("outline_size", HaloPixels);
        view.AddChild(label);
        Root.AddChild(view);
        return _baked[key] = new Lettered { View = view, Size = size };
    }

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

    public Hand HandFor(int style, NameKind kind)
    {
        if (_hands.TryGetValue((style, kind), out var hand)) return hand;
        bool ancient = style == MapStyles.Index(MapStyles.Ancient), modern = style == MapStyles.Index(MapStyles.Modern);
        bool italic = !ancient && kind == NameKind.Water;
        bool capitals = ancient || kind == NameKind.Area;
        float tracking = ancient ? AncientTracking : kind == NameKind.Area ? modern ? ModernAreaTracking : AreaTracking : italic ? ItalicTracking : Tracking;
        var face = new FontVariation
        {
            BaseFont = new SystemFont { FontNames = ancient ? Titling : Serifs, FontItalic = italic },
            SpacingGlyph = (int)Math.Round(tracking * Pixels),
        };
        if (italic && !modern) face.VariationTransform = new Transform2D(new Vector2(1, 0), new Vector2(BackSlant, 1), Vector2.Zero);
        var ink = modern && kind == NameKind.Water ? ModernBlue : Ink[style];
        return _hands[(style, kind)] = new Hand($"{style}/{kind}", face, Pixels, ink, Paper[style], capitals, ancient);
    }

    public static string Words(string text, Hand hand)
    {
        var words = text.Trim();
        if (hand.Capitals) words = words.ToUpperInvariant();
        return hand.Interpuncts ? string.Join(" · ", words.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : words;
    }

    public void Sync(Func<string, bool> boardShown)
    {
        foreach (var n in _placed) n.QueueFree();
        _placed.Clear();
        var s = _c.S;
        int style = MapStyles.Index(s.View.Lighting.MapStyle);
        foreach (var (layer, strokes) in s.View.Drawings)
        {
            if (!_c.LayerShown(layer)) continue;
            foreach (var st in strokes)
                if (st.Text is { Length: > 0 } && st.Points.Count > 0 && boardShown(st.Board)) Write(st, style);
        }
        foreach (var thing in s.View.Built)
            if (thing.Name is { Length: > 0 } name && BuiltGeometry.NameSpot(thing) is { } spot && s.View.Sheets.FirstOrDefault(x => x.Id == thing.Sheet) is { } sheet)
                Sign(name, BuiltGeometry.ToWorld(sheet, spot), NameKind.Place, style);
    }

    void Sign(string text, Vec2 at, NameKind kind, int style)
    {
        var hand = HandFor(style, kind);
        var words = Words(text, hand);
        if (words.Length == 0) return;
        var baked = Bake($"{hand.Name}:{words}", words, hand);
        float height = (float)GroundNames.Letter(kind) * baked.Size.Y / hand.Pixels, width = height * baked.Size.X / baked.Size.Y;
        if (kind == NameKind.Place) at = new Vec2(at.X + width / 2 + PlaceGap, at.Z);
        Set(baked, at, new Vec2(0, 1), height);
        if (kind == NameKind.Place && _placed.Count > 0) _placed[^1].AddChild(new NearSign { Height = height });
    }

    void Write(Stroke st, int style)
    {
        var mid = st.Points[st.Points.Count / 2];
        var kind = GroundNames.KindOf(_c.S.Ground.KindAt(mid));
        if (st.Points.Count == 1) { Sign(st.Text!, st.Points[0], kind, style); return; }
        var hand = HandFor(style, kind);
        var words = Words(st.Text!, hand);
        if (words.Length == 0) return;
        float letter = (float)GroundNames.Letter(kind);
        var path = st.Points;
        if (path[^1].X < path[0].X || path[^1].X == path[0].X && path[^1].Z < path[0].Z) path = Enumerable.Reverse(path).ToList();
        Along(path, words, hand, letter);
    }

    void Along(List<Vec2> path, string words, Hand hand, float letter)
    {
        var letters = words.Select(ch => ch.ToString()).ToList();
        var widths = letters.Select(l => l == " " ? letter * SpaceShare : (Bake($"{hand.Name}:{l}", l, hand).Size.X - 2 * Margin) * letter / hand.Pixels).ToList();
        var marks = new List<double> { 0 };
        for (int i = 1; i < path.Count; i++) marks.Add(marks[i - 1] + (path[i] - path[i - 1]).Length);
        double length = marks[^1], natural = widths.Sum();
        double gap = letters.Count > 1 ? Math.Max(0, (length - natural) / (letters.Count - 1)) : 0;
        double s = (length - natural - gap * (letters.Count - 1)) / 2;
        for (int i = 0; i < letters.Count; i++)
        {
            var (at, tangent) = AlongPath(path, marks, s + widths[i] / 2);
            if (letters[i] != " ")
            {
                var baked = Bake($"{hand.Name}:{letters[i]}", letters[i], hand);
                Set(baked, at, new Vec2(-tangent.Z, tangent.X), letter * baked.Size.Y / hand.Pixels);
            }
            s += widths[i] + gap;
        }
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

    void Set(Lettered baked, Vec2 at, Vec2 up, float height)
    {
        var centre = new Vec2(at.X + up.X * height * Baseline, at.Z + up.Z * height * Baseline);
        var worldUp = (Coords.ToWorld(new Vec2(centre.X + up.X, centre.Z + up.Z)) - Coords.ToWorld(centre)).Normalized();
        var ground = _c.S.Ground;
        if (ground.ReliefAt(centre) != null)
        {
            _placed.Add(Place(baked, Coords.ToWorld(centre, (float)ground.At(centre)), worldUp, height, 1));
            return;
        }
        float width = height * baked.Size.X / baked.Size.Y;
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, height) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(new Vector3(-worldUp.Z, 0, worldUp.X), -worldUp, Vector3.Up), Coords.ToWorld(centre, _c.SheetsTop + DrawingLayer.StrokeLift)),
        };
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        if (baked.Texture != null) mat.AlbedoTexture = baked.Texture;
        else baked.Waiting.Add(t => mat.AlbedoTexture = t);
        quad.MaterialOverride = mat;
        Root.AddChild(quad);
        _placed.Add(quad);
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
