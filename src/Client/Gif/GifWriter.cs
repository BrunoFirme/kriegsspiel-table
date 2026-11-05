namespace KTable.Client.Gif;

public sealed class GifWriter
{
    const int Centis = 100, LeastDelay = 2;
    const byte Extension = 0x21, GraphicControl = 0xF9, ApplicationLabel = 0xFF, ImageStart = 0x2C, Trailer = 0x3B;
    const byte FullColourTable = 0xF7, KeepUnderneath = 1 << 2, HasClear = 1;
    const int LoopForever = 0;
    const byte GraphicControlLength = 4, NetscapeIdLength = 11, NetscapeDataLength = 3, NetscapeLoopBlock = 1;

    readonly Stream _out;
    readonly int _width, _height;
    readonly byte[] _canvas, _next;
    GifPalette? _palette;
    (int x, int y, int w, int h, byte[] pixels, bool clear, double at)? _pending;

    public GifWriter(Stream output, int width, int height)
    {
        _out = output;
        _width = width;
        _height = height;
        _canvas = new byte[width * height];
        _next = new byte[width * height];
    }

    public int Frames { get; private set; }

    public void Add(ReadOnlySpan<byte> rgba, double at)
    {
        if (rgba.Length < _width * _height * 4) throw new ArgumentException($"a {_width}×{_height} frame needs {_width * _height * 4} bytes, not {rgba.Length}");
        if (_palette == null)
        {
            _palette = GifPalette.From(rgba);
            WriteHead(_palette);
        }
        _palette.Index(rgba, _width, _height, _next);
        if (Frames == 0)
        {
            _pending = (0, 0, _width, _height, (byte[])_next.Clone(), false, at);
            Array.Copy(_next, _canvas, _next.Length);
            Frames = 1;
            return;
        }
        if (Changed() is not { } box) return;
        var (x, y, w, h) = box;
        WritePending(at);
        var pixels = new byte[w * h];
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
            {
                int i = (y + row) * _width + x + col;
                pixels[row * w + col] = _next[i] == _canvas[i] ? GifPalette.Clear : _next[i];
            }
        _pending = (x, y, w, h, pixels, true, at);
        Array.Copy(_next, _canvas, _next.Length);
        Frames++;
    }

    public void Finish(double at)
    {
        WritePending(at);
        if (_palette != null) _out.WriteByte(Trailer);
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
        _out.Write("GIF89a"u8);
        Short(_width);
        Short(_height);
        _out.WriteByte(FullColourTable);
        _out.WriteByte(0);
        _out.WriteByte(0);
        _out.Write(palette.Rgb);
        _out.WriteByte(Extension);
        _out.WriteByte(ApplicationLabel);
        _out.WriteByte(NetscapeIdLength);
        _out.Write("NETSCAPE2.0"u8);
        _out.WriteByte(NetscapeDataLength);
        _out.WriteByte(NetscapeLoopBlock);
        Short(LoopForever);
        _out.WriteByte(0);
    }

    void WritePending(double until)
    {
        if (_pending is not { } pending) return;
        var (x, y, w, h, pixels, clear, at) = pending;
        _pending = null;
        int delay = Math.Max(LeastDelay, (int)Math.Round(until * Centis) - (int)Math.Round(at * Centis));
        _out.WriteByte(Extension);
        _out.WriteByte(GraphicControl);
        _out.WriteByte(GraphicControlLength);
        _out.WriteByte((byte)(KeepUnderneath | (clear ? HasClear : 0)));
        Short(delay);
        _out.WriteByte(GifPalette.Clear);
        _out.WriteByte(0);
        _out.WriteByte(ImageStart);
        Short(x);
        Short(y);
        Short(w);
        Short(h);
        _out.WriteByte(0);
        GifLzw.Write(_out, pixels);
    }

    void Short(int v)
    {
        _out.WriteByte((byte)v);
        _out.WriteByte((byte)(v >> 8));
    }
}
