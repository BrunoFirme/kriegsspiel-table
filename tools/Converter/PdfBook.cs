using System.Globalization;
using System.Security.Cryptography;
using KTable.Core;
using KTable.Core.Model;
using PDFtoImage;
using SkiaSharp;

#pragma warning disable CA1416

namespace KTable.Converter;

public sealed partial class DefFactory
{
    public const int PageDpi = 150;
    public const int MostPages = 300;

    readonly Dictionary<string, PdfShape> _pdfs = new();

    public sealed record PdfShape(int Pages, double Aspect);

    public static PdfShape Shape(byte[] pdf)
    {
        var size = Conversion.GetPageSize(pdf, 0);
        double aspect = size.Height > 0 ? size.Width / (double)size.Height : 1;
        return new PdfShape(Conversion.GetPageCount(pdf), aspect > 0 ? aspect : 1);
    }

    public static IEnumerable<(int page, byte[] bytes, int w, int h)> Pages(byte[] pdf, int dpi, int quality)
    {
        int count = Math.Min(Conversion.GetPageCount(pdf), MostPages);
        for (int i = 0; i < count; i++)
        {
            using var drawn = Conversion.ToImage(pdf, page: i, options: new RenderOptions(Dpi: dpi));
            using var data = drawn.Encode(SKEncodedImageFormat.Webp, quality);
            yield return (i + 1, data.ToArray(), drawn.Width, drawn.Height);
        }
    }

    PdfShape? PdfOf(string? url)
    {
        var e = Entry(url);
        if (e is not { Ext: "pdf", Sha: { } sha } || _assets.FilePath(e) is not { } src) return null;
        if (_pdfs.TryGetValue(sha, out var known)) return known;
        PdfShape shape;
        try { shape = Shape(File.ReadAllBytes(src)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
        _pdfs[sha] = shape;
        return shape;
    }

    void FillBook(PieceDef def, ModuleObj o, PackOut pack, string where)
    {
        var e = Entry(o.PdfUrl);
        if (e is not { Ext: "pdf", Sha: { } sha } || _assets.FilePath(e) is not { } src || PdfOf(o.PdfUrl) is not { } shape)
        {
            _rep.Missing(o.PdfUrl ?? "a PDF", (o.PdfUrl == null ? null : _assets.Why(o.PdfUrl)) ?? "not a PDF", where);
            def.Image = Placeholder(pack);
            return;
        }
        var pages = BookPages(sha, src, pack, where);
        if (pages.Count == 0) { def.Image = Placeholder(pack); return; }
        def.Image = pages[0];
        def.States = pages.Select((p, i) => new PieceStateDef { Name = $"Page {i + 1}", Image = p }).ToList();
        if (shape.Pages > pages.Count) _rep.Note($"{where}: the PDF has {shape.Pages} pages; the first {pages.Count} came over");
    }

    List<string> BookPages(string sha, string src, PackOut pack, string where)
    {
        var dir = Path.Combine(_cfg.Cache, "pdf", $"{sha}-{PageDpi}");
        var index = Path.Combine(dir, "pages.txt");
        if (File.Exists(index))
        {
            var kept = new List<string>();
            foreach (var line in File.ReadAllLines(index))
            {
                var p = line.Split('\t');
                var file = Path.Combine(dir, p[0]);
                if (p.Length != 4 || !File.Exists(file)) { kept.Clear(); break; }
                Put(pack, Path.GetFileNameWithoutExtension(p[0]), "webp", file, "image", int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture));
                kept.Add(Path.GetFileNameWithoutExtension(p[0]));
            }
            if (kept.Count > 0) return kept;
        }
        Directory.CreateDirectory(dir);
        var shas = new List<string>();
        var lines = new List<string>();
        try
        {
            foreach (var (page, bytes, w, h) in Pages(File.ReadAllBytes(src), PageDpi, _cfg.Tiles.WebpQuality))
            {
                var pageSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var file = Path.Combine(dir, pageSha + ".webp");
                if (!File.Exists(file)) Json.WriteAtomic(file, bytes);
                Put(pack, pageSha, "webp", file, "image", w, h);
                shas.Add(pageSha);
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{pageSha}.webp\t{page}\t{w}\t{h}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _rep.Missing(src, "the PDF could not be read: " + ex.Message, where);
            return shas;
        }
        if (shas.Count > 0) Json.WriteAtomic(index, string.Join("\n", lines));
        return shas;
    }
}
