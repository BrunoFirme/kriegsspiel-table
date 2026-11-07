using System.Collections.Generic;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed class PanelMeter(System.Func<string, Vector2?> pictureSize) : IPanelMeter
{
    public const float LineHeight = 1.15f;
    static readonly Dictionary<string, Font> Faces = new();

    public static Font FontOf(TextProps t)
    {
        var key = $"{t.Font}|{t.Style}";
        if (Faces.TryGetValue(key, out var f)) return f;
        Font face = Fonts.Face;
        if (t.Font is { Length: > 0 } name)
        {
            var family = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            var dash = family.IndexOf('-');
            face = PanelFonts.Find(name) ?? new SystemFont { FontNames = new[] { dash > 0 ? family[..dash] : family, family }, Fallbacks = new Godot.Collections.Array<Font> { Fonts.Face } };
        }
        if (t.Style != KTable.Client.Panels.FontStyle.Normal)
        {
            const float Embolden = 0.6f, Slant = 0.2f;
            bool bold = t.Style is KTable.Client.Panels.FontStyle.Bold or KTable.Client.Panels.FontStyle.BoldAndItalic;
            bool italic = t.Style is KTable.Client.Panels.FontStyle.Italic or KTable.Client.Panels.FontStyle.BoldAndItalic;
            var v = new FontVariation { BaseFont = face, VariationEmbolden = bold ? Embolden : 0 };
            if (italic) v.VariationTransform = new Transform2D(new Vector2(1, 0), new Vector2(Slant, 1), Vector2.Zero);
            face = v;
        }
        Faces[key] = face;
        return face;
    }

    public V2 Measure(TextProps text, string words, float wrapWidth)
    {
        var plain = PanelRich.Plain(words, text.Rich);
        var font = FontOf(text);
        int size = System.Math.Max(1, text.Size);
        var mode = wrapWidth > 0 && !text.Overflow ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        var s = font.GetMultilineStringSize(plain, HorizontalAlignment.Left, mode == TextServer.AutowrapMode.Off ? -1 : wrapWidth, size, -1,
            mode == TextServer.AutowrapMode.Off ? TextServer.LineBreakFlag.Mandatory : TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound);
        int lines = System.Math.Max(1, Mathf.RoundToInt(s.Y / System.Math.Max(1f, font.GetHeight(size))));
        return new V2(s.X, lines * size * LineHeight * text.LineSpacing);
    }

    public V2? PictureSize(string sprite) => pictureSize(sprite) is { } v ? new V2(v.X, v.Y) : null;
}
