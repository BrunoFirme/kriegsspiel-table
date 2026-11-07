using System.Globalization;
using KTable.Core.Panels;

namespace KTable.Client.Panels;

public sealed class PanelScene
{
    public const float ScreenHeight = 1080, PieceCanvas = 10000;
    public PanelBox Root { get; }
    public List<PanelBox> Elements { get; } = new();

    PanelScene(PanelBox root) => Root = root;

    public static string Key(IEnumerable<int> path) => string.Join(".", path);

    public (float X, float Y, float W, float H) Screen(PanelBox b)
    {
        var at = b.RectMin;
        for (var p = b; p.Parent != null; p = p.Parent) at += p.PivotAt;
        float x = at.X - Root.RectMin.X, up = at.Y - Root.RectMin.Y;
        return (x, Root.RectSize.Y - up - b.RectSize.Y, b.RectSize.X, b.RectSize.Y);
    }

    public PanelBox? At(IReadOnlyList<int> path) => Elements.FirstOrDefault(e => e.Path != null && e.Path.SequenceEqual(path));

    public PanelBox? ById(string id) => Elements.FirstOrDefault(e => e.Element?.Get("id") is { } v && v.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static PanelScene Build(UiLayout layout, V2 canvas, PanelContext ctx, IPanelMeter meter, IReadOnlyDictionary<string, string>? local = null, ISet<string>? keep = null)
    {
        var top = new PanelBox { Name = "Canvas", AnchorMin = V2.Zero, AnchorMax = V2.Zero, SizeDelta = canvas, Pivot = V2.Half };
        top.RectSize = canvas;
        top.RectMin = new V2(-canvas.X / 2, -canvas.Y / 2);
        var root = top.Add(new PanelBox { Name = "XmlLayout", Tag = "XmlLayout", RectMask = true, Image = new ImageProps { Color = new Paint(1, 1, 1, 0), Type = PanelPrefabs.Sliced } });
        var scene = new PanelScene(top);
        var made = new List<(PanelBox Box, UiElement Element)>();
        for (int i = 0; i < layout.Elements.Count; i++) scene.Make(root, layout.Elements[i], new List<int> { i }, made);
        foreach (var (box, element) in made)
        {
            Apply(box, element, ctx, local);
            if (keep != null && box.Path != null && keep.Contains(Key(box.Path))) box.Active = true;
        }
        PanelLayout.Run(top, meter);
        return scene;
    }

    void Make(PanelBox parent, UiElement e, List<int> path, List<(PanelBox, UiElement)> made)
    {
        var box = PanelPrefabs.Create(e.Tag);
        box.Element = e;
        box.Path = path;
        PanelPrefabs.ContentOf(parent).Add(box);
        if (box.Text != null && e.Tag == "Text") box.Text.Text = e.Inner ?? "";
        Elements.Add(box);
        for (int i = 0; i < e.Children.Count; i++) Make(box, e.Children[i], path.Append(i).ToList(), made);
        Adopt(box);
        made.Add((box, e));
    }

    static void Adopt(PanelBox box)
    {
        var first = box.Children.FirstOrDefault(c => c.Element != null);
        if (first?.Tag != "Text") return;
        if (box.Tag is "Button" or "ToggleButton" && box.Part("Text Cell") is { } cell)
        {
            box.Children.Remove(first);
            first.Parent = cell;
            cell.Children.Insert(0, first);
            first.Scale = V3.One;
        }
    }

    static void Apply(PanelBox box, UiElement e, PanelContext ctx, IReadOnlyDictionary<string, string>? local)
    {
        PanelApply.Element(box, e, e.Attributes, ctx);
        if (e.Tag == "Text" && e.Shown != null && box.Text != null) box.Text.Text = e.Shown;
        var mine = local != null && box.Path != null && local.TryGetValue(Key(box.Path), out var v) ? v : null;
        Values(box, e, mine);
    }

    static string? Now(PanelBox b, string key, string? local) => local ?? b.Extra.GetValueOrDefault(key);

    static void Values(PanelBox b, UiElement e, string? local)
    {
        switch (b.Tag)
        {
            case "Toggle":
                if (b.Part("Checkmark") is { } check) check.Active = PanelValues.Bool(Now(b, "ison", local));
                break;
            case "ToggleButton":
                ToggleButton(b, PanelValues.Bool(Now(b, "ison", local)));
                break;
            case "Slider":
                SliderAt(b, local);
                break;
            case "ProgressBar":
                Progress(b);
                break;
            case "Dropdown":
                DropdownAt(b, e, local);
                break;
            case "InputField":
                InputAt(b, local);
                break;
        }
    }

    static void ToggleButton(PanelBox b, bool on)
    {
        if (b.Image == null) return;
        var c = b.Extra.GetValueOrDefault(on ? "selectedBackgroundColor" : "deselectedBackgroundColor");
        if (c != null) b.Image.Color = PanelValues.Color(c, null);
    }

    public static float SliderValue(PanelBox b, string? local)
    {
        float min = PanelValues.Float(b.Extra.GetValueOrDefault("minvalue") ?? b.Extra.GetValueOrDefault("minValue")), max = PanelValues.Float(b.Extra.GetValueOrDefault("maxvalue") ?? "1");
        float v = PanelValues.Float(Now(b, "value", local));
        if (PanelValues.Bool(b.Extra.GetValueOrDefault("wholenumbers"))) v = (float)Math.Round(v);
        return Math.Clamp(v, Math.Min(min, max), Math.Max(min, max));
    }

    static void SliderAt(PanelBox b, string? local)
    {
        float min = PanelValues.Float(b.Extra.GetValueOrDefault("minvalue")), max = PanelValues.Float(b.Extra.GetValueOrDefault("maxvalue") ?? "1");
        float v = SliderValue(b, local);
        float t = max == min ? 0 : (v - min) / (max - min);
        int dir = PanelValues.Enum<Directions>(b.Extra.GetValueOrDefault("direction")) is { } d ? (int)d : 0;
        bool reverse = dir is 1 or 3;
        int axis = dir >= 2 ? 1 : 0;
        if (reverse) t = 1 - t;
        if (b.Part("Fill") is { } fill)
        {
            fill.AnchorMin = reverse ? V2.Zero.With(axis, t).With(1 - axis, 0) : V2.Zero;
            fill.AnchorMax = reverse ? V2.One : V2.One.With(axis, t);
        }
        if (b.Part("Handle") is { } handle)
        {
            handle.AnchorMin = V2.Zero.With(axis, t);
            handle.AnchorMax = V2.One.With(axis, t);
        }
    }

    enum Directions { LeftToRight, RightToLeft, BottomToTop, TopToBottom }

    static void Progress(PanelBox b)
    {
        float pct = Math.Clamp(PanelValues.Float(b.Extra.GetValueOrDefault("percentage")), 0, 100);
        bool horizontal = !b.Extra.TryGetValue("horizontal", out var h) || PanelValues.Bool(h);
        bool reverse = PanelValues.Bool(b.Extra.GetValueOrDefault("reverse"));
        if (b.Part("Fill") is { } fill)
        {
            int axis = horizontal ? 0 : 1;
            fill.AnchorMin = reverse ? V2.Zero.With(axis, 1 - pct / 100) : V2.Zero;
            fill.AnchorMax = reverse ? V2.One : V2.One.With(axis, pct / 100);
            fill.SizeDelta = V2.Zero;
            fill.Position = V2.Zero;
        }
        if (b.Part("PercentageText") is { } text)
        {
            text.Active = !b.Extra.TryGetValue("showPercentageText", out var show) || PanelValues.Bool(show);
            var format = b.Extra.GetValueOrDefault("percentageTextFormat") ?? "0.00";
            string shown;
            try { shown = pct.ToString(format, CultureInfo.InvariantCulture); }
            catch (FormatException) { shown = pct.ToString(CultureInfo.InvariantCulture); }
            text.Text!.Text = shown + "%";
        }
    }

    public static int DropdownIndex(PanelBox b, UiElement e, string? local)
    {
        var options = e.Options ?? new();
        if (local != null && int.TryParse(local, out var picked)) return Math.Clamp(picked, 0, Math.Max(0, options.Count - 1));
        int sel = options.FindIndex(o => PanelValues.Bool(o.Attributes.GetValueOrDefault("selected")));
        if (sel < 0 && b.Extra.GetValueOrDefault("value") is { } v && int.TryParse(v, out var vi)) sel = vi;
        return Math.Clamp(sel < 0 ? 0 : sel, 0, Math.Max(0, options.Count - 1));
    }

    static void DropdownAt(PanelBox b, UiElement e, string? local)
    {
        var options = e.Options ?? new();
        if (b.Part("Label")?.Text is { } label) label.Text = options.Count == 0 ? "" : options[DropdownIndex(b, e, local)].Value ?? "";
    }

    static void InputAt(PanelBox b, string? local)
    {
        var text = local ?? b.Extra.GetValueOrDefault("text") ?? "";
        if (b.Part("Text")?.Text is { } t) t.Text = text;
        if (b.Part("Placeholder") is { } ph) ph.Active = text.Length == 0;
        b.Extra["typed"] = text;
    }
}
