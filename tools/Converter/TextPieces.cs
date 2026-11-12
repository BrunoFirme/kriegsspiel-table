using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    public const double TextUnitsPerFontSize = 0.015;

    const double DefaultFontSize = 64, Degree = Math.PI / 180;
    const string TextFace = "table/Amaranth-Regular.ttf";
    const char GlyphTheGameCentresInALine = ')';
    const float TextOutline = 1;

    static readonly Lazy<SKTypeface> TextTypeface = new(() =>
    {
        using var face = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(TextFace) ?? throw new InvalidDataException("the table lettering face is missing from the converter");
        return SKTypeface.FromStream(face);
    });

    public static bool IsTextObject(ModuleObj o) => o.Name is "Notecard" or "3DText";

    static string TextOf(ModuleObj o) => ModuleValues.Text(o.J["Text"]?["Text"]) ?? "";

    static double FontSizeOf(ModuleObj o) => ModuleValues.Finite(o.J["Text"]?["fontSize"], DefaultFontSize);

    static string[] TextLines(string body) => Markup.Plain(body).Replace("\r", "").Split('\n');

    public static double TextHeight(ModuleObj o) => TextLines(TextOf(o)).Length * FontSizeOf(o) * TextUnitsPerFontSize;

    public static (double X, double Y, double Z) TextMiddle(ModuleObj o)
    {
        double half = TextHeight(o) / 2, a = o.RotX * Degree, b = o.RotY * Degree, c = o.RotZ * Degree;
        double x = half * Math.Sin(c), y = -half * Math.Cos(c), z = y * Math.Sin(a);
        return (o.PosX + x * Math.Cos(b) + z * Math.Sin(b), o.PosY + y * Math.Cos(a), o.PosZ - x * Math.Sin(b) + z * Math.Cos(b));
    }

    public DefResult? TextDef(ModuleObj o, PackOut pack, double? refScale)
    {
        double bs = o.Name == "3DText" || Math.Abs(o.ScaleX) < 1e-6 ? 1 : Math.Abs(o.ScaleX);
        double r = o.Name == "3DText" ? 1 : refScale ?? bs;
        double instScale = Math.Round(bs / r, 4);
        if (o.Name == "Notecard") return new DefResult(NoteDef(pack, o.Nickname, o.Description, r, ModuleNotecard), pack, instScale, 0, o);
        string body = TextOf(o);
        double fontSize = FontSizeOf(o);
        var colour = ColourOf(o.J["Text"]?["colorstate"] as JsonObject);
        if (body.Trim().Length == 0) return null;

        var content = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{o.Name}||{body}|{fontSize}|{colour}|upright|v5");
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var path = Path.Combine(_cfg.Cache, "text", sha + ".png");
        var lines = TextLines(body);
        using var font = new SKFont(TextTypeface.Value, (float)fontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        float lineH = (float)fontSize;
        float wide = Math.Max(1, lines.Max(l => font.MeasureText(l.Length > 0 ? l : " ")));
        int w = (int)Math.Ceiling(wide + 2 * TextOutline), h = (int)Math.Ceiling(lineH * lines.Length);
        var baseSize = new Size2(Math.Round(w * TextUnitsPerFontSize, 4), Math.Round(h * TextUnitsPerFontSize, 4));
        if (!File.Exists(path)) BakeText(path, w, h, lines, font, colour, lineH);
        var image = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Put(pack, image, "png", path, "image", w, h);

        var key = $"text|{sha}";
        var live = new Lettering { Text = body, Size = fontSize, Color = $"#{colour.Red:X2}{colour.Green:X2}{colour.Blue:X2}{colour.Alpha:X2}", UnitsPerPixel = TextUnitsPerFontSize * r };
        if (pack.ByKey.TryGetValue(key, out var existing)) return new DefResult(existing, pack, instScale, 0, o) { Lettering = live };
        var said = string.Join(" ", lines).Trim();
        var name = said.Length > 24 ? said[..24].Trim() + "…" : said;
        var def = new PieceDef
        {
            Name = name.Length > 0 ? name : o.Name, Kind = "flat", Image = image, Size = Mul(baseSize, r), Thickness = 0.02, Shape = "box", Tags = new() { "text" },
        };
        def.Id = $"{pack.Short}.{Slug(def.Name)}-{Hash8(key)}";
        pack.ByKey[key] = def;
        pack.Richness[def.Id] = 0;
        pack.M.Pieces.Add(def);
        return new DefResult(def, pack, instScale, 0, o) { Lettering = live };
    }

    public static string Plain(string moduleText) => Markup.Plain(moduleText);

    public static readonly (Size2 Size, double Thickness) ModuleNotecard = (new Size2(6.9031, 4.1303), 0.06);

    public PieceDef NoteDef(PackOut pack, string title, string body, double scale, (Size2 Size, double Thickness)? card = null)
    {
        var (size, thick) = card ?? (new Size2(NoteCard.Width, NoteCard.Height), NoteCard.Thickness);
        title = title.Trim();
        body = body.Replace("\r", "").Trim('\n');
        string named = Plain(title).Trim();
        var image = NotePicture(pack);
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"note|{title}|{body}|{scale:0.####}") + (card == null ? "" : "|module");
        if (pack.ByKey.TryGetValue(key, out var existing)) return existing;
        var def = new PieceDef
        {
            Name = named.Length > 0 ? named : NoteCard.DefaultName, Kind = "flat", Image = image, Size = Mul(size, scale),
            Thickness = thick, Shape = "box", Tags = new() { NoteCard.Tag }, TextLines = NoteCard.TextLines(title, body),
        };
        def.Id = $"{pack.Short}.{Slug(def.Name)}-{Hash8(key)}";
        pack.ByKey[key] = def;
        pack.Richness[def.Id] = 0;
        pack.M.Pieces.Add(def);
        return def;
    }

    CacheEntry? _notePicture;

    string NotePicture(PackOut pack)
    {
        if (_notePicture == null)
        {
            var (rgba, w, h) = NoteCard.Paint();
            using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bmp.GetPixels(), rgba.Length);
            using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
            _notePicture = _assets.Keep(data.ToArray());
            _notePicture.W = w;
            _notePicture.H = h;
        }
        Put(pack, _notePicture.Sha!, _notePicture.Ext!, _assets.FilePath(_notePicture)!, "image", _notePicture.W, _notePicture.H);
        return _notePicture.Sha!;
    }

    static SKColor ColourOf(JsonObject? c)
    {
        if (c == null) return SKColors.White;
        byte V(string k) => (byte)Math.Clamp(Math.Round(ModuleValues.Finite(c[k], 1) * 255), 0, 255);
        return new SKColor(V("r"), V("g"), V("b"), V("a"));
    }

    static void TurnToReadUprightAtModuleRotationZero(SKCanvas canvas, int w, int h) => canvas.RotateDegrees(180, w / 2f, h / 2f);

    static float BaselineBelowLineTop(SKFont font)
    {
        font.MeasureText(GlyphTheGameCentresInALine.ToString(), out var glyph);
        return MathF.Round((font.Size - glyph.Top - glyph.Bottom) / 2);
    }

    static void BakeText(string path, int w, int h, string[] lines, SKFont font, SKColor colour, float lineH)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            using var ink = new SKPaint { Color = colour, IsAntialias = true };
            using var edge = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(colour.Alpha), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * TextOutline, StrokeJoin = SKStrokeJoin.Round,
            };
            canvas.Clear(SKColors.Transparent);
            TurnToReadUprightAtModuleRotationZero(canvas, w, h);
            float y = BaselineBelowLineTop(font);
            foreach (var line in lines)
            {
                canvas.DrawText(line, w / 2f, y, SKTextAlign.Center, font, edge);
                canvas.DrawText(line, w / 2f, y, SKTextAlign.Center, font, ink);
                y += lineH;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(bmp, path);
    }
}
