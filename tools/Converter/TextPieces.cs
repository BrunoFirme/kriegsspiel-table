using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    const double TextUnitsPerFontSize = 1.0 / 80;
    const string PdfName = "PDF";

    public static bool IsTextObject(TtsObj o) => o.Name is "Notecard" or "3DText" or "Custom_PDF";

    public DefResult? TextDef(TtsObj o, PackOut pack, double? refScale)
    {
        double bs = Math.Abs(o.ScaleX) < 1e-6 ? 1 : Math.Abs(o.ScaleX);
        double r = refScale ?? bs;
        double instScale = Math.Round(bs / r, 4);
        if (o.Name == "Notecard") return new DefResult(NoteDef(pack, o.Nickname, o.Description, r), pack, instScale, 0, o, ScriptInfo.Parse("", ""));
        if (o.Name == "Custom_PDF")
        {
            _rep.Note($"{Context} Custom_PDF '{o.Nickname}' {o.Guid}: the PDF's pages are not shown here; a note carries its link.");
            var text = (o.Description.Trim().Length > 0 ? o.Description.Trim() + "\n" : "") + (o.PdfUrl ?? "");
            return new DefResult(NoteDef(pack, o.Nickname.Length > 0 ? o.Nickname : PdfName, text, r), pack, instScale, 0, o, ScriptInfo.Parse("", ""));
        }
        string body = o.J["Text"]?["Text"]?.GetValue<string>() ?? "";
        double fontSize = o.J["Text"]?["fontSize"]?.GetValue<double>() ?? 64;
        var colour = ColourOf(o.J["Text"]?["colorstate"] as JsonObject);
        if (body.Trim().Length == 0) return null;

        var content = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{o.Name}||{body}|{fontSize}|{colour}|upright|v4");
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var path = Path.Combine(_cfg.Cache, "text", sha + ".png");
        var lines = body.Replace("\r", "").Split('\n');
        using var font = Font((float)fontSize, bold: true);
        float lineH = (float)(fontSize * 1.1);
        float wide = Math.Max(1, lines.Max(l => font.MeasureText(l.Length > 0 ? l : " ")));
        int w = (int)Math.Ceiling(wide) + 8, h = (int)Math.Ceiling(lineH * lines.Length) + 4;
        var baseSize = new Size2(Math.Round(w * TextUnitsPerFontSize, 4), Math.Round(h * TextUnitsPerFontSize, 4));
        if (!File.Exists(path)) BakeText(path, w, h, lines, font, colour, lineH);
        var image = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        Put(pack, image, "png", path, "image", w, h);

        var key = $"text|{sha}";
        if (pack.ByKey.TryGetValue(key, out var existing)) return new DefResult(existing, pack, instScale, 0, o, ScriptInfo.Parse("", ""));
        var name = body.Length > 24 ? body[..24].Trim() + "…" : body.Trim();
        var def = new PieceDef
        {
            Name = name.Length > 0 ? name : o.Name, Kind = "flat", Image = image, Size = Mul(baseSize, r), Thickness = 0.02, Shape = "box", Tags = new() { "text" },
        };
        def.Id = $"{pack.Short}.{Slug(def.Name)}-{Hash8(key)}";
        pack.ByKey[key] = def;
        pack.Richness[def.Id] = 0;
        pack.M.Pieces.Add(def);
        return new DefResult(def, pack, instScale, 0, o, ScriptInfo.Parse("", ""));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[(/?(b|i|u|s|sub|sup)|[0-9A-Fa-f]{6}|-)\]")] private static partial System.Text.RegularExpressions.Regex TtsMarkup();

    public static string Plain(string ttsText) => TtsMarkup().Replace(ttsText, "");

    public PieceDef NoteDef(PackOut pack, string title, string body, double scale)
    {
        title = Plain(title).Trim();
        body = Plain(body).Replace("\r", "").Trim('\n');
        var image = NotePicture(pack);
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"note|{title}|{body}|{scale:0.####}");
        if (pack.ByKey.TryGetValue(key, out var existing)) return existing;
        var def = new PieceDef
        {
            Name = title.Length > 0 ? title : NoteCard.DefaultName, Kind = "flat", Image = image, Size = Mul(new Size2(NoteCard.Width, NoteCard.Height), scale),
            Thickness = NoteCard.Thickness, Shape = "rounded", Tags = new() { NoteCard.Tag }, TextLines = NoteCard.TextLines(title, body),
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
        byte V(string k) => (byte)Math.Clamp(Math.Round((c[k]?.GetValue<double>() ?? 1) * 255), 0, 255);
        return new SKColor(V("r"), V("g"), V("b"));
    }

    static SKFont Font(float size, bool bold) =>
        new(SKTypeface.FromFamilyName("Arial", bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default, size) { Subpixel = true, Edging = SKFontEdging.Antialias };

    static void TurnToReadUprightAtTtsRotationZero(SKCanvas canvas, int w, int h) => canvas.RotateDegrees(180, w / 2f, h / 2f);

    static void BakeText(string path, int w, int h, string[] lines, SKFont font, SKColor colour, float lineH)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            using var paint = new SKPaint { Color = colour, IsAntialias = true };
            canvas.Clear(SKColors.Transparent);
            TurnToReadUprightAtTtsRotationZero(canvas, w, h);
            float y = -font.Metrics.Ascent + 2;
            foreach (var line in lines) { canvas.DrawText(line, 4, y, SKTextAlign.Left, font, paint); y += lineH; }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SavePng(bmp, path);
    }
}
