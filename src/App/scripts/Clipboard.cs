using System;
using System.Runtime.InteropServices;
using Godot;

namespace KTable.App;

public static class AppClipboard
{
    static bool _keptInApp;
    static Image? _image;
    static string? _file;

    public static void KeepInApp() => _keptInApp = true;

    public static string? KeptFile => _keptInApp ? _file : null;

    public static void SetText(string text)
    {
        if (_keptInApp) Forget();
        else DisplayServer.ClipboardSet(text);
    }

    public static bool HasImage() => _keptInApp ? _image != null : DisplayServer.ClipboardHasImage();

    public static Image GetImage() => _keptInApp ? (Image?)_image?.Duplicate() ?? new Image() : DisplayServer.ClipboardGetImage();

    public static string? SetImage(Image img)
    {
        if (!_keptInApp) return ImageClipboard.Copy(img);
        Forget();
        _image = (Image)img.Duplicate();
        return null;
    }

    public static string? SetFile(string path)
    {
        if (!_keptInApp) return ImageClipboard.CopyFile(path);
        Forget();
        _file = path;
        return null;
    }

    static void Forget()
    {
        _image = null;
        _file = null;
    }
}

public static class ImageClipboard
{
    const uint CF_DIB = 8;
    const uint CF_HDROP = 15;
    const int DropFilesSize = 20, DropFilesWideAt = 16, DropEffectCopy = 1, OpenAttempts = 10, OpenRetryMs = 20;
    const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalUnlock(IntPtr hMem);

    public static string? Copy(Image img)
    {
        if (OS.GetName() != "Windows")
            return CopyUnix(img);
        img = (Image)img.Duplicate();
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        var rgba = img.GetData();
        var png = img.SavePngToBuffer();

        var dib = new byte[40 + w * h * 4];
        const int BitmapInfoHeaderSize = 40, UncompressedRgb = 0;
        BitConverter.GetBytes(BitmapInfoHeaderSize).CopyTo(dib, 0);
        BitConverter.GetBytes(w).CopyTo(dib, 4);
        BitConverter.GetBytes(h).CopyTo(dib, 8);
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);
        BitConverter.GetBytes(UncompressedRgb).CopyTo(dib, 16);
        BitConverter.GetBytes(w * h * 4).CopyTo(dib, 20);
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * w * 4, dst = 40 + y * w * 4;
            for (int x = 0; x < w; x++, src += 4, dst += 4)
            {
                dib[dst] = rgba[src + 2];
                dib[dst + 1] = rgba[src + 1];
                dib[dst + 2] = rgba[src];
                dib[dst + 3] = rgba[src + 3];
            }
        }

        var hwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        for (int attempt = 0; attempt < 10 && !OpenClipboard(hwnd); attempt++) System.Threading.Thread.Sleep(20);
        try
        {
            EmptyClipboard();
            SetClipboardData(CF_DIB, ToGlobal(dib));
            var pngFmt = RegisterClipboardFormat("PNG");
            if (pngFmt != 0) SetClipboardData(pngFmt, ToGlobal(png));
        }
        finally { CloseClipboard(); }
        return null;
    }

    public static string? CopyFile(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (OS.GetName() != "Windows") return CopyFileUnix(path);
        var name = System.Text.Encoding.Unicode.GetBytes(path + "\0\0");
        var drop = new byte[DropFilesSize + name.Length];
        BitConverter.GetBytes(DropFilesSize).CopyTo(drop, 0);
        BitConverter.GetBytes(1).CopyTo(drop, DropFilesWideAt);
        name.CopyTo(drop, DropFilesSize);
        var hwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        bool open = false;
        for (int attempt = 0; attempt < OpenAttempts && !(open = OpenClipboard(hwnd)); attempt++) System.Threading.Thread.Sleep(OpenRetryMs);
        if (!open) return "another program is holding the clipboard";
        try
        {
            EmptyClipboard();
            SetClipboardData(CF_HDROP, ToGlobal(drop));
            var effect = RegisterClipboardFormat("Preferred DropEffect");
            if (effect != 0) SetClipboardData(effect, ToGlobal(BitConverter.GetBytes(DropEffectCopy)));
        }
        finally { CloseClipboard(); }
        return null;
    }

    static string? CopyFileUnix(string path)
    {
        var output = new Godot.Collections.Array();
        if (OS.GetName() == "macOS")
        {
            var quoted = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return OS.Execute("osascript", new[] { "-e", $"set the clipboard to (POSIX file \"{quoted}\")" }, output) == 0 ? null : "osascript refused the file";
        }
        var uri = new Uri(path).AbsoluteUri;
        const string Copy = "printf '%s\r\n' \"$1\" | (wl-copy -t text/uri-list || xclip -selection clipboard -t text/uri-list -i)";
        return OS.Execute("sh", new[] { "-c", Copy, "sh", uri }, output) == 0 ? null : "no wl-copy/xclip";
    }

    static IntPtr ToGlobal(byte[] data)
    {
        var h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
        var p = GlobalLock(h);
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(h);
        return h;
    }

    static string? CopyUnix(Image img)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ktable_capture.png");
        img.SavePng(tmp);
        var output = new Godot.Collections.Array();
        if (OS.GetName() == "macOS")
            OS.Execute("osascript", new[] { "-e", $"set the clipboard to (read (POSIX file \"{tmp}\") as «class PNGf»)" }, output);
        else if (OS.Execute("sh", new[] { "-c", $"wl-copy -t image/png < '{tmp}' || xclip -selection clipboard -t image/png -i '{tmp}'" }, output) != 0)
            return "no wl-copy/xclip";
        return null;
    }
}
