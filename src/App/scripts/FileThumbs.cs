using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

public static class FileThumbs
{
    const int ThumbLongerSide = 128;
    const long LargestFileToRead = 60L << 20;
    const long MostPixels = 40_000_000;
    const int ThumbsKeptBetweenDialogs = 400;

    static readonly object Lock = new();
    static readonly Dictionary<string, ImageTexture> Made = new();
    static readonly SemaphoreSlim Readers = new(2);
    static readonly List<FileDialog> Open = new();
    static int _openCount;

    public static void Install() => FileDialog.SetGetThumbnailCallback(Callable.From<string, Texture2D?>(Get));

    public static void Uninstall() => FileDialog.SetGetThumbnailCallback(new Callable());

    public static void Show(FileDialog fd)
    {
        fd.DisplayMode = FileDialog.DisplayModeEnum.Thumbnails;
        Open.Add(fd);
        Interlocked.Increment(ref _openCount);
        void Closed()
        {
            if (!Open.Remove(fd)) return;
            Interlocked.Decrement(ref _openCount);
        }
        fd.VisibilityChanged += () => { if (!fd.Visible) Closed(); };
        fd.TreeExiting += Closed;
    }

    static Texture2D? Get(string path)
    {
        try
        {
            if (PictureFor(path) is not { } picture) return null;
            path = picture;
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0 || fi.Length > LargestFileToRead) return null;
            var key = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            ImageTexture tex;
            lock (Lock)
            {
                if (Made.TryGetValue(key, out var had)) return had;
                if (Made.Count >= ThumbsKeptBetweenDialogs) Made.Clear();
                tex = new ImageTexture();
                Made[key] = tex;
            }
            _ = Task.Run(() => Read(path, key, tex));
            return tex;
        }
        catch (Exception e)
        {
            GD.PushWarning($"thumbnail: {path}: {e.Message}");
            return null;
        }
    }

    static string? PictureFor(string path)
    {
        if (ImageFiles.IsImage(path)) return path;
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
        var beside = Path.ChangeExtension(path, ".png");
        return File.Exists(beside) ? beside : null;
    }

    static async Task Read(string path, string key, ImageTexture tex)
    {
        await Readers.WaitAsync();
        Image? img = null;
        bool skipped = false;
        try
        {
            if (Volatile.Read(ref _openCount) == 0) skipped = true;
            else if (Pixels(path) <= MostPixels) img = Shrunk(ImageFiles.Load(path, ThumbLongerSide * 2));
        }
        catch (Exception e) { GD.PushWarning($"thumbnail: {path}: {e.Message}"); }
        finally { Readers.Release(); }
        if (skipped) { lock (Lock) Made.Remove(key); return; }
        Callable.From(() => Fill(tex, img)).CallDeferred();
    }

    static Image? Shrunk(Image? img)
    {
        if (img == null || img.IsEmpty()) return null;
        while (img.GetWidth() >= ThumbLongerSide * 4 && img.GetHeight() >= ThumbLongerSide * 4) img.ShrinkX2();
        int w = img.GetWidth(), h = img.GetHeight();
        float k = ThumbLongerSide / (float)Math.Max(w, h);
        if (k < 1) img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), Image.Interpolation.Cubic);
        return img;
    }

    static void Fill(ImageTexture tex, Image? img)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(tex)) return;
            img ??= ThemeDB.GetDefaultTheme().GetIcon("file_thumbnail", "FileDialog")?.GetImage();
            if (img == null || img.IsEmpty()) return;
            tex.SetImage(img);
            foreach (var fd in Open) if (GodotObject.IsInstanceValid(fd)) Redraw(fd);
        }
        catch (Exception e) { GD.PushWarning($"thumbnail: {e.Message}"); }
    }

    static void Redraw(Node n)
    {
        if (n is ItemList list) { list.QueueRedraw(); return; }
        foreach (var c in n.GetChildren(includeInternal: true)) Redraw(c);
    }

    static long Pixels(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg")) return 0;
        using var f = File.OpenRead(path);
        var b = new byte[Math.Min(f.Length, 256 * 1024)];
        int got = 0, r;
        while (got < b.Length && (r = f.Read(b, got, b.Length - got)) > 0) got += r;
        int Be16(int i) => b[i] << 8 | b[i + 1];
        if (ext == ".png")
            return got >= 24 && b[0] == 0x89 && b[1] == 'P' ? (long)((uint)(Be16(16) << 16 | Be16(18))) * (uint)(Be16(20) << 16 | Be16(22)) : 0;
        if (got < 4 || b[0] != 0xFF || b[1] != 0xD8) return 0;
        int p = 2;
        while (p + 9 < got)
        {
            if (b[p] != 0xFF) { p++; continue; }
            int m = b[p + 1];
            if (m == 0xFF) { p++; continue; }
            if (m is >= 0xC0 and <= 0xCF && m is not (0xC4 or 0xC8 or 0xCC)) return (long)Be16(p + 5) * Be16(p + 7);
            if (m is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { p += 2; continue; }
            p += 2 + Be16(p + 2);
        }
        return 0;
    }
}
