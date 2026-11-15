using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace KTable.App;

public interface IRescaled
{
    void Rescaled();
}

static class UiRescale
{
    const string At = "ui_at", Own = "ui_own_scale";
    const string Constants = "theme_override_constants/", FontSizes = "theme_override_font_sizes/", Styles = "theme_override_styles/";

    static readonly HashSet<string> NotPixels = new()
    {
        "autohide", "align_to_largest_stylebox", "center_slider_grabbers", "completion_lines", "completion_max_width", "draw_guides", "draw_relationship_lines",
        "dragging_unfold_wait_msec", "gutter_compact", "hover_switch_wait_msec", "minimum_character_width", "scroll_speed", "scrollbar_margin_bottom",
        "scrollbar_margin_left", "scrollbar_margin_right", "scrollbar_margin_top", "set_min_buttons_width_from_icons", "strikethrough_alpha", "underline_alpha",
        "max_lines_visible", "lines_skipped",
    };

    static readonly ConditionalWeakTable<GodotObject, Dictionary<string, (float Design, float Shown)>> Kept = new();

    public static bool IsPixels(string constant) => !NotPixels.Contains(constant);

    public static int Constant(string name, int value, float from, float to) =>
        IsPixels(name) ? (int)Size(value, from, to, value != 0) : value;

    static float Size(float value, float from, float to, bool keepVisible = false)
    {
        float next = MathF.Round(value / from * to, MidpointRounding.AwayFromZero);
        return keepVisible && next == 0 && value != 0 ? MathF.Sign(value) : next;
    }

    static float Next(GodotObject o, string key, float now, float from, float to, bool keepVisible = false)
    {
        var kept = Kept.GetOrCreateValue(o);
        float design = kept.TryGetValue(key, out var k) && k.Shown == now ? k.Design : now / from;
        float next = MathF.Round(design * to, MidpointRounding.AwayFromZero);
        if (keepVisible && next == 0 && design != 0) next = MathF.Sign(design);
        kept[key] = (design, next);
        return next;
    }

    public static void Box(StyleBox b, float from, float to)
    {
        float M(string key, float v) => v < 0 ? v : Next(b, key, v, from, to);
        int W(string key, int v) => (int)Next(b, key, v, from, to, true);
        b.ContentMarginLeft = M("ml", b.ContentMarginLeft);
        b.ContentMarginTop = M("mt", b.ContentMarginTop);
        b.ContentMarginRight = M("mr", b.ContentMarginRight);
        b.ContentMarginBottom = M("mb", b.ContentMarginBottom);
        if (b is StyleBoxFlat f)
        {
            f.CornerRadiusTopLeft = W("rtl", f.CornerRadiusTopLeft);
            f.CornerRadiusTopRight = W("rtr", f.CornerRadiusTopRight);
            f.CornerRadiusBottomLeft = W("rbl", f.CornerRadiusBottomLeft);
            f.CornerRadiusBottomRight = W("rbr", f.CornerRadiusBottomRight);
            f.BorderWidthLeft = W("bl", f.BorderWidthLeft);
            f.BorderWidthTop = W("bt", f.BorderWidthTop);
            f.BorderWidthRight = W("br", f.BorderWidthRight);
            f.BorderWidthBottom = W("bb", f.BorderWidthBottom);
            f.ExpandMarginLeft = Next(f, "el", f.ExpandMarginLeft, from, to);
            f.ExpandMarginTop = Next(f, "et", f.ExpandMarginTop, from, to);
            f.ExpandMarginRight = Next(f, "er", f.ExpandMarginRight, from, to);
            f.ExpandMarginBottom = Next(f, "eb", f.ExpandMarginBottom, from, to);
            f.ShadowSize = W("ss", f.ShadowSize);
            f.ShadowOffset = new Vector2(Next(f, "sx", f.ShadowOffset.X, from, to), Next(f, "sy", f.ShadowOffset.Y, from, to));
        }
        else if (b is StyleBoxLine l)
        {
            l.Thickness = W("th", l.Thickness);
            l.GrowBegin = Next(l, "gb", l.GrowBegin, from, to);
            l.GrowEnd = Next(l, "ge", l.GrowEnd, from, to);
        }
    }

    public static void Arrived(Node n)
    {
        if (n is not (Control or Window) || n.GetViewport() is SubViewport) return;
        if (!n.HasMeta(At))
        {
            n.SetMeta(At, Ui.Scale);
            return;
        }
        float at = n.GetMeta(At).AsSingle();
        if (Mathf.IsEqualApprox(at, Ui.Scale)) return;
        var parent = n.GetParent();
        One(n, at, Ui.Scale, new HashSet<ulong>(), parent is Container, parent != null && !parent.GetChildren().Contains(n));
    }

    public static void OwnScale(Node n) => n.SetMeta(Own, true);

    public static void Walk(Node root, float from, float to) => Down(root, from, to, new HashSet<ulong>(), false, false);

    static void Down(Node n, float from, float to, HashSet<ulong> boxes, bool inContainer, bool engineMade)
    {
        if (n is SubViewport || n.HasMeta(Own)) return;
        if (n is Control or Window)
        {
            float at = n.HasMeta(At) ? n.GetMeta(At).AsSingle() : from;
            if (!Mathf.IsEqualApprox(at, to)) One(n, at, to, boxes, inContainer, engineMade);
        }
        if (n is CanvasItem ci) ci.QueueRedraw();
        if (n is IRescaled kept and not (Control or Window)) kept.Rescaled();
        var own = n.GetChildCount(true) == n.GetChildCount() ? null : new HashSet<Node>(n.GetChildren());
        foreach (var child in n.GetChildren(true)) Down(child, from, to, boxes, n is Container, engineMade || own?.Contains(child) == false);
    }

    static void One(Node n, float from, float to, HashSet<ulong> boxes, bool inContainer, bool engineMade)
    {
        n.SetMeta(At, to);
        Overrides(n, from, to, boxes);
        var own = n switch { Control c0 => c0.Theme, Window w0 => w0.Theme, _ => null };
        if (own != null && own != Ui.Theme() && boxes.Add(own.GetInstanceId())) Themed(own, from, to, boxes);
        if (!engineMade && n is Control c)
        {
            var min = c.CustomMinimumSize;
            if (min != Vector2.Zero) c.CustomMinimumSize = new Vector2(Next(c, "min.x", min.X, from, to), Next(c, "min.y", min.Y, from, to));
            if (!inContainer || c.TopLevel)
            {
                c.OffsetLeft = Next(c, "ol", c.OffsetLeft, from, to);
                c.OffsetTop = Next(c, "ot", c.OffsetTop, from, to);
                c.OffsetRight = Next(c, "or", c.OffsetRight, from, to);
                c.OffsetBottom = Next(c, "ob", c.OffsetBottom, from, to);
            }
            Special(c, from, to);
        }
        else if (!engineMade && n is Window w && w != w.GetTree()?.Root)
        {
            if (w.MinSize != Vector2I.Zero) w.MinSize = new Vector2I((int)Next(w, "min.x", w.MinSize.X, from, to), (int)Next(w, "min.y", w.MinSize.Y, from, to));
            if (w.MaxSize != Vector2I.Zero) w.MaxSize = new Vector2I((int)Next(w, "max.x", w.MaxSize.X, from, to), (int)Next(w, "max.y", w.MaxSize.Y, from, to));
            if (w.IsEmbedded())
            {
                w.Size = new Vector2I((int)Next(w, "size.x", w.Size.X, from, to), (int)Next(w, "size.y", w.Size.Y, from, to));
                w.Position = new Vector2I((int)Next(w, "pos.x", w.Position.X, from, to), (int)Next(w, "pos.y", w.Position.Y, from, to));
            }
        }
        if (n is IRescaled r) r.Rescaled();
    }

    static void Special(Control c, float from, float to)
    {
        switch (c)
        {
            case ItemList list:
                if (list.FixedIconSize != Vector2I.Zero)
                    list.FixedIconSize = new Vector2I((int)Next(list, "icon.x", list.FixedIconSize.X, from, to), (int)Next(list, "icon.y", list.FixedIconSize.Y, from, to));
                if (list.FixedColumnWidth > 0) list.FixedColumnWidth = (int)Next(list, "column", list.FixedColumnWidth, from, to);
                break;
            case SplitContainer split:
                var offsets = split.SplitOffsets;
                for (int i = 0; i < offsets.Length; i++) offsets[i] = (int)Next(split, "split" + i, offsets[i], from, to);
                split.SplitOffsets = offsets;
                break;
            case Tree tree when tree.GetRoot() is { } top:
                Items(tree, top, from, to);
                break;
        }
    }

    static void Items(Tree tree, TreeItem item, float from, float to)
    {
        for (int col = 0; col < tree.Columns; col++)
        {
            int size = item.GetCustomFontSize(col);
            if (size > 0) item.SetCustomFontSize(col, (int)Size(size, from, to, true));
        }
        for (var child = item.GetFirstChild(); child != null; child = child.GetNext()) Items(tree, child, from, to);
    }

    static void Themed(Theme t, float from, float to, HashSet<ulong> boxes)
    {
        if (t.HasDefaultFontSize()) t.DefaultFontSize = (int)Next(t, "default", t.DefaultFontSize, from, to, true);
        foreach (var type in t.GetFontSizeTypeList())
            foreach (var name in t.GetFontSizeList(type))
                if (t.HasFontSize(name, type)) t.SetFontSize(name, type, (int)Next(t, $"f/{type}/{name}", t.GetFontSize(name, type), from, to, true));
        foreach (var type in t.GetConstantTypeList())
            foreach (var name in t.GetConstantList(type))
                if (IsPixels(name)) t.SetConstant(name, type, (int)Next(t, $"c/{type}/{name}", t.GetConstant(name, type), from, to, t.GetConstant(name, type) != 0));
        foreach (var type in t.GetStyleboxTypeList())
            foreach (var name in t.GetStyleboxList(type))
                if (t.GetStylebox(name, type) is { } box && boxes.Add(box.GetInstanceId())) Box(box, from, to);
    }

    static void Overrides(Node n, float from, float to, HashSet<ulong> boxes)
    {
        foreach (var prop in n.GetPropertyList())
        {
            var name = prop["name"].AsString();
            if (!name.StartsWith("theme_override_", StringComparison.Ordinal)) continue;
            if (name.StartsWith(Constants, StringComparison.Ordinal))
            {
                var key = name[Constants.Length..];
                if (!IsPixels(key)) continue;
                var v = n.Get(name);
                if (v.VariantType != Variant.Type.Int) continue;
                int now = v.AsInt32();
                n.Set(name, (int)Next(n, name, now, from, to, now != 0));
            }
            else if (name.StartsWith(FontSizes, StringComparison.Ordinal))
            {
                var v = n.Get(name);
                if (v.VariantType != Variant.Type.Int || v.AsInt32() <= 0) continue;
                n.Set(name, (int)Next(n, name, v.AsInt32(), from, to, true));
            }
            else if (name.StartsWith(Styles, StringComparison.Ordinal) && n.Get(name).AsGodotObject() is StyleBox box && boxes.Add(box.GetInstanceId()))
                Box(box, from, to);
        }
    }
}
