using System;
using System.IO;
using Godot;
using KTable.Core;

namespace KTable.App;

public static class ImageFiles
{
    public static readonly string[] Extensions = { "png", "jpg", "jpeg", "webp", "svg", "bmp", "tga", "tif", "tiff", "ktx" };
    public static string Filter => "*." + string.Join(", *.", Extensions) + " ; Pictures";

    public static bool IsImage(string path) => Array.IndexOf(Extensions, Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) >= 0;

    public static Image? Load(string path, int svgLongSide = 1024)
    {
        try
        {
            var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (ext is "tif" or "tiff") return Finish(TiffImages.Load(path));
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes.Length > 200_000_000 || !ContentLimits.PictureAllowed(bytes, ext)) return null;
            var img = new Image();
            Error err;
            switch (ext)
            {
                case "png": err = img.LoadPngFromBuffer(bytes); break;
                case "jpg": case "jpeg": err = img.LoadJpgFromBuffer(bytes); break;
                case "webp": err = img.LoadWebpFromBuffer(bytes); break;
                case "bmp": err = img.LoadBmpFromBuffer(bytes); break;
                case "tga": err = img.LoadTgaFromBuffer(bytes); break;
                case "ktx": err = img.LoadKtxFromBuffer(bytes); break;
                case "svg":
                    err = img.LoadSvgFromBuffer(bytes, 1);
                    if (err == Error.Ok && !img.IsEmpty())
                    {
                        float k = svgLongSide / (float)Math.Max(1, Math.Max(img.GetWidth(), img.GetHeight()));
                        if (Math.Abs(k - 1) > 0.01f) { img = new Image(); err = img.LoadSvgFromBuffer(bytes, k); }
                    }
                    break;
                default: return null;
            }
            return err == Error.Ok ? Finish(img) : null;
        }
        catch (Exception e)
        {
            GD.PushWarning($"studio: could not read {path}: {e.Message}");
            return null;
        }
    }

    static Image? Finish(Image? img)
    {
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight(), m = Math.Max(w, h);
        if (m > 4096) img.Resize(Math.Max(1, w * 4096 / m), Math.Max(1, h * 4096 / m), Image.Interpolation.Lanczos);
        return img;
    }

    public static Image? FromClipboard()
    {
        if (!AppClipboard.HasImage()) return null;
        return Finish(AppClipboard.GetImage());
    }

    public static void Unpremultiply(Image img)
    {
        var d = img.GetData();
        for (int i = 0; i < d.Length; i += 4)
        {
            int a = d[i + 3];
            if (a == 0) { d[i] = d[i + 1] = d[i + 2] = 0; continue; }
            if (a == 255) continue;
            d[i] = (byte)Math.Min(255, d[i] * 255 / a);
            d[i + 1] = (byte)Math.Min(255, d[i + 1] * 255 / a);
            d[i + 2] = (byte)Math.Min(255, d[i + 2] * 255 / a);
        }
        img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, d);
    }
}
