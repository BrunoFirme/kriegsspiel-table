namespace KTable.Client.Gif;

public sealed class GifWriter
{
    const int Centis = 100, LeastDelay = 2;
    const byte Extension = 0x21, GraphicControl = 0xF9, ApplicationLabel = 0xFF, ImageStart = 0x2C, Trailer = 0x3B;
    const byte FullColourTable = 0xF7, KeepUnderneath = 1 << 2, HasClear = 1;
    const int LoopForever = 0;
    const byte GraphicControlLength = 4, NetscapeIdLength = 11, NetscapeDataLength = 3, NetscapeLoopBlock = 1;
    const int FrameHeadBytes = 18, TrailerBytes = 1;

    readonly Stream _out;
    readonly int _width, _height;
    readonly long _most;
    readonly byte[] _canvas, _next;
    GifPalette? _palette;
    (int x, int y, int w, int h, byte[] lzw, bool clear, double at)? _pending;
    long _sent;
    bool _finished;

    public GifWriter(Stream output, int width, int height, long mostBytes = long.MaxValue)
    {
        _out = output;
        _width = width;
        _height = height;
        _most = mostBytes;
        _canvas = new byte[width * height];
        _next = new byte[width * height];
    }

    public int Frames { get; private set; }

    public long Bytes => _sent + Held + (_palette == null || _finished ? 0 : TrailerBytes);

    long Held => _pending is { } p ? FrameHeadBytes + p.lzw.Length : 0;

    public bool Add(ReadOnlySpan<byte> rgba, double at)
    {
        if (rgba.Length < _width * _height * 4) throw new ArgumentException($"a {_width}×{_height} frame needs {_width * _height * 4} bytes, not {rgba.Length}");
        if (_palette == null)
        {
            _palette = GifPalette.From(rgba);
            WriteHead(_palette);
        }
        _palette.Index(rgba, _width, _height, _next);
        if (Frames == 0) return Hold((0, 0, _width, _height), _next, false, at);
        if (Changed() is not { } box) return true;
        var (x, y, w, h) = box;
        var pixels = new byte[w * h];
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
            {
                int i = (y + row) * _width + x + col;
                pixels[row * w + col] = _next[i] == _canvas[i] ? GifPalette.Clear : _next[i];
            }
        return Hold(box, pixels, true, at);
    }

    bool Hold((int x, int y, int w, int h) box, byte[] pixels, bool clear, double at)
    {
        using var packed = new MemoryStream();
        GifLzw.Write(packed, pixels);
        var lzw = packed.ToArray();
        if (_sent + Held + FrameHeadBytes + lzw.Length + TrailerBytes > _most) return false;
        WritePending(at);
        _pending = (box.x, box.y, box.w, box.h, lzw, clear, at);
        Array.Copy(_next, _canvas, _next.Length);
        Frames++;
        return true;
    }

    public void Finish(double at)
    {
        WritePending(at);
        if (_palette != null) Put(Trailer);
        _finished = true;
        _out.Flush();
    }

    (int x, int y, int w, int h)? Changed()
    {
        int left = _width, top = _height, right = -1, bottom = -1;
        for (int y = 0; y < _height; y++)
        {
            int row = y * _width;
            for (int x = 0; x < _width; x++)
            {
                if (_next[row + x] == _canvas[row + x]) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = y;
            }
        }
        return right < 0 ? null : (left, top, right - left + 1, bottom - top + 1);
    }

    void WriteHead(GifPalette palette)
    {
        Put("GIF89a"u8);
        Short(_width);
        Short(_height);
        Put(FullColourTable);
        Put(0);
        Put(0);
        Put(palette.Rgb);
        Put(Extension);
        Put(ApplicationLabel);
        Put(NetscapeIdLength);
        Put("NETSCAPE2.0"u8);
        Put(NetscapeDataLength);
        Put(NetscapeLoopBlock);
        Short(LoopForever);
        Put(0);
    }

    void WritePending(double until)
    {
        if (_pending is not { } pending) return;
        var (x, y, w, h, lzw, clear, at) = pending;
        _pending = null;
        int delay = Math.Max(LeastDelay, (int)Math.Round(until * Centis) - (int)Math.Round(at * Centis));
        Put(Extension);
        Put(GraphicControl);
        Put(GraphicControlLength);
        Put((byte)(KeepUnderneath | (clear ? HasClear : 0)));
        Short(delay);
        Put(GifPalette.Clear);
        Put(0);
        Put(ImageStart);
        Short(x);
        Short(y);
        Short(w);
        Short(h);
        Put(0);
        Put(lzw);
    }

    void Put(byte b)
    {
        _out.WriteByte(b);
        _sent++;
    }

    void Put(ReadOnlySpan<byte> bytes)
    {
        _out.Write(bytes);
        _sent += bytes.Length;
    }

    void Short(int v)
    {
        Put((byte)v);
        Put((byte)(v >> 8));
    }
}
