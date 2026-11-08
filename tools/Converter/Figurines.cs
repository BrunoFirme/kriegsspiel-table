using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    public const string Figurine = "Figurine_Custom";
    public const double FigurineBase = 1.789, FigurineBaseRise = 0.316, FigurineCard = 1.7926, FigurineCardFoot = 0.2495, FigurineBackBehind = 0.004;
    const int FigurineSegments = 24, BaseStripPx = 8;
    const string StandColour = "#000000";

    public static double FigurineAspect(int w, int h) => w > 0 && h > 0 ? w / (double)h : 1;

    public static double FigurineTop => FigurineCardFoot + FigurineCard;

    public static double FigurineTopOf(ModuleObj o) => FigurineTop * o.ImageScalar;

    double FigurineAspectOf(ModuleObj o) => Entry(o.ImageUrl) is { W: > 0, H: > 0 } e ? FigurineAspect(e.W, e.H) : 1;

    double FigurineBackAspectOf(ModuleObj o) => Entry(o.BackUrl) is { W: > 0, H: > 0 } e && AssetStore.IsPicture(e.Ext) ? FigurineAspect(e.W, e.H) : FigurineAspectOf(o);

    public Size2 FigurineSize(ModuleObj o, double ratio) =>
        new(Math.Max(FigurineBase, FigurineCard * o.ImageScalar * Math.Max(FigurineAspectOf(o), FigurineBackAspectOf(o))), FigurineBase * ratio);

    void FillFigurine(PieceDef def, ModuleObj b, PackOut pack, string where)
    {
        var front = Entry(b.ImageUrl);
        if (front == null || _assets.FilePath(front) is not { } frontPath || !AssetStore.IsPicture(front.Ext))
        {
            _rep.Missing(b.ImageUrl ?? "a picture", (b.ImageUrl == null ? null : _assets.Why(b.ImageUrl)) ?? "not a picture", where);
            def.Image = Placeholder(pack);
            return;
        }
        var back = Entry(b.BackUrl) is { } be && AssetStore.IsPicture(be.Ext) ? _assets.FilePath(be) : null;
        var stand = SKColor.Parse(b.Color is { Length: >= 7 } c ? c[..7] : StandColour);
        using var atlas = FigurineAtlas(frontPath, back ?? frontPath, b.MirroredBack, stand, out double frontShare, out double pictureShare);
        if (atlas == null)
        {
            def.Image = Placeholder(pack);
            return;
        }
        var png = atlas.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        var card = new FigurineCardShape(FigurineAspectOf(b), FigurineBackAspectOf(b), b.ImageScalar);
        var obj = Encoding.UTF8.GetBytes(FigurineObj(card, frontShare, pictureShare));
        def.Kind = "model";
        def.Shape = "box";
        def.Image = null;
        def.Tint = "#FFFFFF";
        def.Texture = Generated(pack, png, "png", "image", atlas.Width, atlas.Height);
        def.Mesh = Generated(pack, obj, "obj", "mesh", 0, 0);
        if (def.Name == "Piece") def.Name = "Figurine";
    }

    string Generated(PackOut pack, byte[] bytes, string ext, string kind, int w, int h)
    {
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = Path.Combine(_cfg.Cache, "made", $"{sha}.{ext}");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            KTable.Core.Json.WriteAtomic(path, bytes);
        }
        Put(pack, sha, ext, path, kind, w, h);
        return sha;
    }

    static SKBitmap? FigurineAtlas(string frontPath, string backPath, bool mirrored, SKColor stand, out double frontShare, out double pictureShare)
    {
        frontShare = pictureShare = 1;
        using var front = SKBitmap.Decode(frontPath);
        using var back = SKBitmap.Decode(backPath);
        if (front == null || back == null) return null;
        int h = Math.Max(front.Height, back.Height), w = front.Width + back.Width;
        var atlas = new SKBitmap(w, h + BaseStripPx);
        using (var canvas = new SKCanvas(atlas))
        {
            canvas.Clear(SKColors.Transparent);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
            using (var image = SKImage.FromBitmap(front)) canvas.DrawImage(image, new SKRect(0, 0, front.Width, h), sampling);
            using (var image = SKImage.FromBitmap(back))
            {
                canvas.Save();
                if (mirrored) canvas.Scale(-1, 1, front.Width + back.Width / 2f, 0);
                canvas.DrawImage(image, new SKRect(front.Width, 0, w, h), sampling);
                canvas.Restore();
            }
            using var paint = new SKPaint { Color = stand };
            canvas.DrawRect(0, h, w, BaseStripPx, paint);
        }
        frontShare = front.Width / (double)w;
        pictureShare = h / (double)(h + BaseStripPx);
        return atlas;
    }

    sealed record FigurineCardShape(double Front, double Back, double Scalar);

    static string FigurineObj(FigurineCardShape card, double frontShare, double pictureShare)
    {
        var inv = CultureInfo.InvariantCulture;
        var v = new List<string>();
        var vt = new List<string>();
        var f = new List<string>();
        void Face(params (double x, double y, double z, double u, double t)[] corners)
        {
            int at = v.Count;
            foreach (var c in corners)
            {
                v.Add(string.Format(inv, "v {0:0.######} {1:0.######} {2:0.######}", c.x, c.y, c.z));
                vt.Add(string.Format(inv, "vt {0:0.######} {1:0.######}", c.u, c.t));
            }
            f.Add("f " + string.Join(' ', Enumerable.Range(at + 1, corners.Length).Select(i => $"{i}/{i}")));
        }
        double hw = FigurineCard * card.Scalar * card.Front / 2, hb = FigurineCard * card.Scalar * card.Back / 2;
        double y0 = FigurineCardFoot * card.Scalar, y1 = FigurineTop * card.Scalar, low = 1 - pictureShare;
        double r = FigurineBase / 2, strip = low / 2;
        f.Add("usemtl base");
        for (int i = 0; i < FigurineSegments; i++)
        {
            double a0 = Math.Tau * i / FigurineSegments, a1 = Math.Tau * (i + 1) / FigurineSegments;
            double x0 = Math.Cos(a0) * r, z0 = Math.Sin(a0) * r, x1 = Math.Cos(a1) * r, z1 = Math.Sin(a1) * r;
            Face((x0, 0, z0, 0.5, strip), (x1, 0, z1, 0.5, strip), (x1, FigurineBaseRise, z1, 0.5, strip), (x0, FigurineBaseRise, z0, 0.5, strip));
            Face((0, FigurineBaseRise, 0, 0.5, strip), (x1, FigurineBaseRise, z1, 0.5, strip), (x0, FigurineBaseRise, z0, 0.5, strip));
            Face((0, 0, 0, 0.5, strip), (x0, 0, z0, 0.5, strip), (x1, 0, z1, 0.5, strip));
        }
        f.Add("usemtl card");
        Face((-hw, y0, 0, 0, low), (hw, y0, 0, frontShare, low), (hw, y1, 0, frontShare, 1), (-hw, y1, 0, 0, 1));
        double behind = -FigurineBackBehind * card.Scalar;
        Face((hb, y0, behind, frontShare, low), (-hb, y0, behind, 1, low), (-hb, y1, behind, 1, 1), (hb, y1, behind, frontShare, 1));
        return string.Join('\n', v.Concat(vt).Concat(f)) + "\n";
    }
}
