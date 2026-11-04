using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    public const double NotecardW = 7.0, NotecardH = 4.3;
    const int PixelsPerUnit = 200;
    const double TextUnitsPerFontSize = 1.0 / 80;

    public static bool IsTextObject(TtsObj o) => o.Name is "Notecard" or "3DText";

    public DefResult? TextDef(TtsObj o, PackOut pack, double? refScale)
    {
        double bs = Math.Abs(o.ScaleX) < 1e-6 ? 1 : Math.Abs(o.ScaleX);
        double r = refScale ?? bs;
        double instScale = Math.Round(bs / r, 4);
        bool card = o.Name == "Notecard";
        string title = card ? o.Nickname : "";
        string body = card ? o.Description : (o.J["Text"]?["Text"]?.GetValue<string>() ?? "");
        double fontSize = card ? 0 : (o.J["Text"]?["fontSize"]?.GetValue<double>() ?? 64);
        var colour = card ? SKColors.Black : ColourOf(o.J["Text"]?["colorstate"] as JsonObject);
        if (!card && body.Trim().Length == 0) return null;

        var content = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{o.Name}|{title}|{body}|{fontSize}|{colour}|upright|v4");
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var path = Path.Combine(_cfg.Cache, "text", sha + ".png");
        Size2 baseSize;
        int w, h;
        if (card)
        {
            w = (int)(NotecardW * PixelsPerUnit);
            h = (int)(NotecardH * PixelsPerUnit);
            baseSize = new Size2(NotecardW, NotecardH);
            if (!File.Exists(path)) BakeCard(path, w, h, title, body);
        }
        else
        {
            var lines = body.Replace("\r", "").Split('\n');
            using var font = Font((float)fontSize, bold: true);
            float lineH = (float)(fontSize * 1.1);
            float wide = Math.Max(1, lines.Max(l => font.MeasureText(l.Length > 0 ? l : " ")));
            w = (int)Math.Ceiling(wide) + 8;
            h = (int)Math.Ceiling(lineH * lines.Length) + 4;
            baseSize = new Size2(Math.Round(w * TextUnitsPerFontSize, 4), Math.Round(h * TextUnitsPerFontSize, 4));
            if (!File.Exists(path)) BakeText(path, w, h, lines, font, colour, lineH);
        }
        var image = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Put(pack, image, "png", path, "image", w, h);

        var key = $"text|{sha}";
        if (pack.ByKey.TryGetValue(key, out var existing)) return new DefResult(existing, pack, instScale, 0, o, ScriptInfo.Parse("", ""));
        var name = card ? (title.Length > 0 ? title : "Notecard") : (body.Length > 24 ? body[..24].Trim() + "…" : body.Trim());
        var def = new PieceDef
        {
            Name = name.Length > 0 ? name : o.Name, Kind = "flat", Image = image, Size = Mul(baseSize, r),
            Thickness = card ? 0.05 : 0.02, Shape = card ? "rounded" : "box", Tags = new() { card ? "notecard" : "text" },
        };
        def.Id = $"{pack.Short}.{Slug(def.Name)}-{Hash8(key)}";
        pack.ByKey[key] = def;
        pack.Richness[def.Id] = 0;
        pack.M.Pieces.Add(def);
        return new DefResult(def, pack, instScale, 0, o, ScriptInfo.Parse("", ""));
    }

    static SKColor ColourOf(JsonObject? c)
    {
        if (c == null) return SKColors.White;
        byte V(string k) => (byte)Math.Clamp(Math.Round((c[k]?.GetValue<double>() ?? 1) * 255), 0, 255);
        return new SKColor(V("r"), V("g"), V("b"));
    }

    static SKFont Font(float size, bool bold) =>
        new(SKTypeface.FromFamilyName("Arial", bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default, size) { Subpixel = true, Edging = SKFontEdging.Antialias };

    static SKFont Hand(float size, bool bold, bool italic)
    {
        var style = new SKFontStyle(bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var face = SKTypeface.FromFamilyName("Segoe Print", style);
        if (face == null || !string.Equals(face.FamilyName, "Segoe Print", StringComparison.OrdinalIgnoreCase)) face = SKTypeface.FromFamilyName("Arial", style) ?? SKTypeface.Default;
        return new SKFont(face, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
    }

    static void TurnToReadUprightAtTtsRotationZero(SKCanvas canvas, int w, int h) => canvas.RotateDegrees(180, w / 2f, h / 2f);

    static void BakeText(string path, int w, int h, string[] lines, SKFont font, SKColor colour, float lineH)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = colour, IsAntialias = true };
        canvas.Clear(SKColors.Transparent);
        TurnToReadUprightAtTtsRotationZero(canvas, w, h);
        float y = -font.Metrics.Ascent + 2;
        foreach (var line in lines) { canvas.DrawText(line, 4, y, SKTextAlign.Left, font, paint); y += lineH; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(bmp, path);
    }

    static void BakeCard(string path, int w, int h, string title, string body)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        TurnToReadUprightAtTtsRotationZero(canvas, w, h);
        using var fill = new SKPaint { Color = new SKColor(251, 250, 244), IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(176, 176, 168), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
        var rect = new SKRoundRect(new SKRect(2, 2, w - 2, h - 2), 18);
        canvas.DrawRoundRect(rect, fill);
        canvas.DrawRoundRect(rect, edge);
        float margin = 40, y = 30;
        using var tf = Hand(60, bold: false, italic: true);
        using var bf = Hand(50, bold: false, italic: false);
        const float ruleSpacing = 64;
        using var blue = new SKPaint { Color = new SKColor(120, 150, 215), StrokeWidth = 2 };
        for (float ry = 30 + ruleSpacing + ruleSpacing; ry < h - 20; ry += ruleSpacing) canvas.DrawLine(margin * 0.3f, ry, w - margin * 0.3f, ry, blue);
        using var tp = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var bp = new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
        if (title.Trim().Length > 0)
        {
            y += -tf.Metrics.Ascent;
            foreach (var line in Wrap(title.Trim(), tf, w - 2 * margin).Take(1)) { canvas.DrawText(line, w / 2f, y, SKTextAlign.Center, tf, tp); y += ruleSpacing; }
            using var rule = new SKPaint { Color = new SKColor(215, 90, 90), StrokeWidth = 3 };
            canvas.DrawLine(margin * 0.3f, y - tf.Size * 0.55f, w - margin * 0.3f, y - tf.Size * 0.55f, rule);
        }
        y += -bf.Metrics.Ascent;
        foreach (var para in body.Replace("\r", "").Split('\n'))
        {
            foreach (var line in Wrap(para, bf, w - 2 * margin))
            {
                if (y > h - margin * 0.5f) break;
                canvas.DrawText(line, margin, y, SKTextAlign.Left, bf, bp);
                y += ruleSpacing;
            }
            if (para.Length == 0) y += ruleSpacing / 2;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(bmp, path);
    }

    static IEnumerable<string> Wrap(string text, SKFont font, float width)
    {
        if (text.Length == 0) yield break;
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureText(trial) > width)
            {
                yield return line.ToString();
                line.Clear();
                line.Append(word);
            }
            else { line.Clear(); line.Append(trial); }
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
