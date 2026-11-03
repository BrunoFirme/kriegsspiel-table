using System.Buffers.Binary;
using System.IO.Compression;

namespace KTable.Core;

public sealed class XcfImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Rgba { get; init; } = Array.Empty<byte>();
    public int Version { get; init; }
    public int LayersUsed { get; init; }
    public List<string> Warnings { get; init; } = new();
}

public static class Xcf
{
    public static bool IsXcf(string path) => Path.GetExtension(path).Equals(".xcf", StringComparison.OrdinalIgnoreCase);

    public static XcfImage Load(string path)
    {
        using var f = XcfFile.Open(path);
        return f.Flatten();
    }

    public static XcfImage Load(Stream stream)
    {
        using var f = XcfFile.Open(stream);
        return f.Flatten();
    }
}

public sealed class XcfFile : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int Version { get; }
    public int LayersUsed { get; }
    public IReadOnlyList<string> Warnings => _warnings;

    readonly Reader _rd;
    readonly Stream _stream;
    readonly bool _ownsStream;
    readonly Doc _img;
    readonly Layer _root;
    readonly List<string> _warnings = new();
    readonly HashSet<string> _warned = new();
    readonly HashSet<int> _warnedModes = new();
    readonly Stack<byte[]> _spareBands = new();
    byte[] _raw = new byte[64 * 64 * 32], _packed = new byte[64 * 64 * 32 * 2];
    readonly float[] _pix = new float[64 * 64 * 4], _maskPix = new float[64 * 64];

    public static XcfFile Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16), true);

    public static XcfFile Open(Stream stream, bool ownsStream = false) => new(stream, ownsStream);

    public void Dispose() { if (_ownsStream) _stream.Dispose(); }

    XcfFile(Stream stream, bool ownsStream)
    {
        _stream = stream; _ownsStream = ownsStream;
        var rd = _rd = new Reader(stream);
        var magic = rd.Bytes(14);
        if (!magic.AsSpan(0, 9).SequenceEqual("gimp xcf "u8)) throw new InvalidDataException("not a GIMP .xcf file");
        int version;
        if (magic.AsSpan(9, 5).SequenceEqual("file\0"u8)) version = 0;
        else if (magic[9] == (byte)'v' && magic[13] == 0 && int.TryParse(System.Text.Encoding.ASCII.GetString(magic, 10, 3), out var v)) version = v;
        else throw new InvalidDataException("not a GIMP .xcf file (unknown version mark)");
        rd.Wide = version >= 11;
        Version = version;

        var img = _img = new Doc { Version = version };
        img.Width = (int)rd.U32(); img.Height = (int)rd.U32(); img.BaseType = (int)rd.U32();
        if (img.Width <= 0 || img.Height <= 0 || img.Width > 1 << 20 || img.Height > 1 << 20) throw new InvalidDataException($"unreasonable picture size {img.Width}×{img.Height}");
        Width = img.Width; Height = img.Height;
        img.Format = version >= 4 ? PixelFormat.FromPrecision((int)rd.U32(), version) : PixelFormat.U8Gamma;

        while (true)
        {
            uint type = rd.U32(), len = rd.U32();
            long next = rd.Position + len;
            if (type == 0) break;
            switch (type)
            {
                case XcfProperty.Compression: img.Compression = len >= 1 ? rd.Bytes(1)[0] : 0; break;
                case 1 when version > 0:
                {
                    int n = (int)rd.U32();
                    if (n < 0 || n > 256 || 4 + 3L * n > len) throw new InvalidDataException("bad colormap");
                    img.Colormap = rd.Bytes(3 * n);
                    break;
                }
            }
            rd.Seek(next);
        }
        if (img.Compression > 2) throw new InvalidDataException(img.Compression == 3 ? "fractal-compressed .xcf files are not supported" : $"unknown .xcf compression {img.Compression}");
        if (img.BaseType == 2 && img.Colormap == null) throw new InvalidDataException("indexed picture without a colormap");

        var layerPtrs = new List<long>();
        for (long p = rd.Ptr(); p != 0; p = rd.Ptr()) layerPtrs.Add(p);

        _root = new Layer { IsGroup = true, Visible = true, Opacity = 1 };
        var openGroups = new List<Layer> { _root };
        foreach (var lp in layerPtrs)
        {
            rd.Seek(lp);
            var l = ReadLayer(rd);
            int depth = l.Path == null ? 0 : Math.Max(0, l.Path.Length - 1);
            if (depth >= openGroups.Count) depth = openGroups.Count - 1;
            openGroups.RemoveRange(depth + 1, openGroups.Count - depth - 1);
            openGroups[depth].Children.Add(l);
            if (l.IsGroup) openGroups.Add(l);
        }
        LayersUsed = CountUsed(_root);
    }

    static int CountUsed(Layer group)
    {
        int n = 0;
        foreach (var l in group.Children)
        {
            if (!l.Visible || l.Opacity <= 0) continue;
            if (l.IsGroup) { if (l.Children.Count == 0) continue; n += 1 + CountUsed(l); }
            else n++;
        }
        return n;
    }

    public XcfImage Flatten()
    {
        if ((long)Width * Height * 4 > int.MaxValue - 64) throw new InvalidDataException($"picture too large to flatten in memory ({Width}×{Height}); render it in bands");
        var canvas = new byte[Width * Height * 4];
        Render(0, Height, canvas);
        return new XcfImage { Width = Width, Height = Height, Rgba = canvas, Version = Version, LayersUsed = LayersUsed, Warnings = new List<string>(_warnings) };
    }

    public void Render(int y0, int rows, byte[] rgba)
    {
        if (y0 < 0 || rows <= 0 || y0 + rows > Height) throw new ArgumentOutOfRangeException(nameof(rows), "rows outside the picture");
        int need = Width * rows * 4;
        if (rgba.Length < need) throw new ArgumentException("buffer too small", nameof(rgba));
        Array.Clear(rgba, 0, need);
        CompositeChildren(_root, rgba, y0, rows, 1f);
    }

    sealed class Doc
    {
        public int Version, Width, Height, BaseType, Compression;
        public PixelFormat Format = PixelFormat.U8Gamma;
        public byte[]? Colormap;
    }

    readonly record struct PixelFormat(int Bpc, Kind Kind, bool Linear)
    {
        public static readonly PixelFormat U8Gamma = new(1, Kind.U8, false);

        public static PixelFormat FromPrecision(int p, int version)
        {
            if (version == 4)
                return p switch { 0 => U8Gamma, 1 => new(2, Kind.U16, false), 2 => new(4, Kind.U32, true), 3 => new(2, Kind.Half, true), 4 => new(4, Kind.Float, true), _ => throw new InvalidDataException($"unknown precision {p}") };
            if (version is 5 or 6)
                return p switch
                {
                    100 => new(1, Kind.U8, true), 150 => U8Gamma, 200 => new(2, Kind.U16, true), 250 => new(2, Kind.U16, false), 300 => new(4, Kind.U32, true), 350 => new(4, Kind.U32, false),
                    400 => new(2, Kind.Half, true), 450 => new(2, Kind.Half, false), 500 => new(4, Kind.Float, true), 550 => new(4, Kind.Float, false), _ => throw new InvalidDataException($"unknown precision {p}"),
                };
            const int LinearTransferCurve = 0;
            int storage = p / 100, transferCurve = p % 100;
            bool linear = transferCurve == LinearTransferCurve;
            return storage switch
            {
                1 => new(1, Kind.U8, linear), 2 => new(2, Kind.U16, linear), 3 => new(4, Kind.U32, linear),
                5 => new(2, Kind.Half, linear), 6 => new(4, Kind.Float, linear), 7 => new(8, Kind.Double, linear),
                _ => throw new InvalidDataException($"unknown precision {p}"),
            };
        }
    }

    enum Kind { U8, U16, U32, Half, Float, Double }

    sealed class Layer
    {
        public int W, H, Type, OffX, OffY, Mode = 28, BlendSpace, CompositeSpace, CompositeMode;
        public string Name = "";
        public float Opacity = 1;
        public bool Visible = true, IsGroup, ApplyMask;
        public int[]? Path;
        public long Hierarchy, Mask;
        public List<Layer> Children = new();
        public TileTable? Tiles, MaskTiles;
        public bool MaskChecked;
        public int Channels => Type switch { 0 => 3, 1 => 4, 2 => 1, 3 => 2, 4 => 1, 5 => 2, _ => throw new InvalidDataException($"unknown layer type {Type}") };
        public bool HasAlpha => Type is 1 or 3 or 5;
        public bool Gray => Type is 2 or 3;
        public bool Indexed => Type is 4 or 5;
    }

    static Layer ReadLayer(Reader rd)
    {
        var l = new Layer { W = (int)rd.U32(), H = (int)rd.U32(), Type = (int)rd.U32() };
        l.Name = rd.Str();
        bool floatOpacity = false;
        while (true)
        {
            uint type = rd.U32(), len = rd.U32();
            long next = rd.Position + len;
            if (type == 0) break;
            switch (type)
            {
                case XcfProperty.Opacity: if (!floatOpacity) l.Opacity = Math.Clamp(rd.U32() / 255f, 0, 1); break;
                case XcfProperty.FloatOpacity: l.Opacity = Math.Clamp(rd.F32(), 0, 1); floatOpacity = true; break;
                case XcfProperty.Visible: l.Visible = rd.U32() != 0; break;
                case XcfProperty.Offsets: l.OffX = rd.I32(); l.OffY = rd.I32(); break;
                case XcfProperty.Mode: l.Mode = (int)rd.U32(); break;
                case XcfProperty.ApplyMask: l.ApplyMask = rd.U32() != 0; break;
                case XcfProperty.GroupItem: l.IsGroup = true; break;
                case XcfProperty.ItemPath:
                {
                    var path = new int[len / 4];
                    for (int i = 0; i < path.Length; i++) path[i] = (int)rd.U32();
                    l.Path = path;
                    break;
                }
                case XcfProperty.CompositeMode: l.CompositeMode = rd.I32(); break;
                case XcfProperty.CompositeSpace: l.CompositeSpace = rd.I32(); break;
                case XcfProperty.BlendSpace: l.BlendSpace = rd.I32(); break;
            }
            rd.Seek(next);
        }
        l.Hierarchy = rd.Ptr();
        l.Mask = rd.Ptr();
        if (l.W < 0 || l.H < 0 || l.W > 1 << 20 || l.H > 1 << 20) throw new InvalidDataException($"layer \"{l.Name}\" has an unreasonable size");
        return l;
    }

    sealed class TileTable
    {
        public int W, H, Bpp, Cols, Rows;
        public long[] Offsets = Array.Empty<long>();
    }

    TileTable ReadHierarchy(long ptr, string what)
    {
        var rd = _rd;
        rd.Seek(ptr);
        var t = new TileTable { W = (int)rd.U32(), H = (int)rd.U32(), Bpp = (int)rd.U32() };
        long level0 = rd.Ptr();
        if (level0 == 0) throw new InvalidDataException($"{what}: no pixel data");
        rd.Seek(level0);
        int lw = (int)rd.U32(), lh = (int)rd.U32();
        if (lw != t.W || lh != t.H) throw new InvalidDataException($"{what}: level size {lw}×{lh} differs from the drawable's {t.W}×{t.H}");
        t.Cols = (t.W + 63) / 64; t.Rows = (t.H + 63) / 64;
        int n = t.Cols * t.Rows;
        var offs = new long[n];
        int got = 0;
        for (long p = rd.Ptr(); p != 0 && got < n; p = rd.Ptr()) offs[got++] = p;
        if (got < n) throw new InvalidDataException($"{what}: {got} tiles stored, {n} needed");
        t.Offsets = offs;
        return t;
    }

    TileTable ReadChannel(long ptr, string what)
    {
        var rd = _rd;
        rd.Seek(ptr);
        rd.U32(); rd.U32();
        rd.Str();
        while (true)
        {
            uint type = rd.U32(), len = rd.U32();
            long next = rd.Position + len;
            if (type == 0) break;
            rd.Seek(next);
        }
        return ReadHierarchy(rd.Ptr(), what);
    }

    TileTable TilesOf(Layer l)
    {
        if (l.Tiles != null) return l.Tiles;
        var tiles = ReadHierarchy(l.Hierarchy, $"layer \"{l.Name}\"");
        if (tiles.W != l.W || tiles.H != l.H) throw new InvalidDataException($"layer \"{l.Name}\": pixel data is {tiles.W}×{tiles.H}, the layer {l.W}×{l.H}");
        int bpc = l.Indexed ? 1 : _img.Format.Bpc;
        if (tiles.Bpp != l.Channels * bpc) throw new InvalidDataException($"layer \"{l.Name}\": {tiles.Bpp} bytes a pixel, expected {l.Channels * bpc}");
        return l.Tiles = tiles;
    }

    TileTable? MaskOf(Layer l)
    {
        if (l.MaskChecked) return l.MaskTiles;
        l.MaskChecked = true;
        if (!l.ApplyMask || l.Mask == 0) return null;
        var mask = ReadChannel(l.Mask, $"mask of \"{l.Name}\"");
        if (mask.W != l.W || mask.H != l.H || mask.Bpp != _img.Format.Bpc) { Warn($"layer \"{l.Name}\": its mask does not match the layer and is ignored"); return null; }
        return l.MaskTiles = mask;
    }

    void Warn(string line) { if (_warned.Add(line)) _warnings.Add(line); }

    void CompositeChildren(Layer group, byte[] canvas, int y0, int rows, float opacityScale)
    {
        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            var l = group.Children[i];
            if (!l.Visible || l.Opacity <= 0) continue;
            if (l.IsGroup)
            {
                if (l.Children.Count == 0) continue;
                if (l.Mode == GimpLayerMode.PassThrough)
                {
                    if (l.Mask != 0 && l.ApplyMask) Warn($"group \"{l.Name}\": a mask on a pass-through group is ignored");
                    CompositeChildren(l, canvas, y0, rows, opacityScale * l.Opacity);
                    continue;
                }
                if (l.OffY >= y0 + rows || l.OffY + l.H <= y0) continue;
                int need = Width * rows * 4;
                var inner = _spareBands.Count > 0 ? _spareBands.Pop() : new byte[need];
                if (inner.Length < need) inner = new byte[need];
                Array.Clear(inner, 0, need);
                CompositeChildren(l, inner, y0, rows, 1f);
                CompositeBuffer(l, inner, canvas, y0, rows, opacityScale);
                _spareBands.Push(inner);
            }
            else CompositeLayer(l, canvas, y0, rows, opacityScale);
        }
    }

    readonly record struct Mix(Blend Blend, bool CompositeLinear, bool BlendLinear, Composite Composite, bool Legacy);

    enum Composite { Union, ClipToBackdrop, ClipToLayer, Intersection }

    Mix Spaces(Layer l)
    {
        var blend = BlendOf(l.Mode, out bool legacy, out bool known);
        if (!known && _warnedModes.Add(l.Mode)) Warn($"layer \"{l.Name}\": blend mode {ModeName(l.Mode)} is not supported; shown as Normal");
        int cs = Math.Abs(l.CompositeSpace), bs = Math.Abs(l.BlendSpace), cm = Math.Abs(l.CompositeMode);
        bool compositeLinear = cs switch { 1 => true, 2 or 3 or 4 => false, _ => !legacy };
        bool blendLinear = bs == 1;
        var composite = cm switch
        {
            1 => Composite.Union, 2 => Composite.ClipToBackdrop, 3 => Composite.ClipToLayer, 4 => Composite.Intersection,
            _ => blend == Blend.Normal || l.Mode == GimpLayerMode.PassThrough ? Composite.Union : Composite.ClipToBackdrop,
        };
        return new Mix(blend, compositeLinear, blendLinear, composite, legacy);
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    void CompositeLayer(Layer l, byte[] canvas, int y0, int rows, float opacityScale)
    {
        if (l.OffY >= y0 + rows || l.OffY + l.H <= y0 || l.OffX >= Width || l.OffX + l.W <= 0) return;
        var tiles = TilesOf(l);
        var mask = MaskOf(l);
        var mix = Spaces(l);
        float opacity = l.Opacity * opacityScale;
        int cw = Width, channels = l.Channels;
        int yEnd = y0 + rows;
        int tyFirst = Math.Max(0, FloorDiv(y0 - l.OffY, 64)), tyLast = Math.Min(tiles.Rows - 1, FloorDiv(yEnd - 1 - l.OffY, 64));

        for (int ty = tyFirst; ty <= tyLast; ty++)
            for (int tx = 0; tx < tiles.Cols; tx++)
            {
                int tw = Math.Min(64, tiles.W - tx * 64), th = Math.Min(64, tiles.H - ty * 64);
                int x0 = l.OffX + tx * 64, yTop = l.OffY + ty * 64;
                if (x0 >= cw || x0 + tw <= 0) continue;
                int ti = ty * tiles.Cols + tx;
                DecodeTile(tiles, ti, tw, th, channels, l, _pix);
                if (mask != null) DecodeMaskTile(mask, ti, tw, th, _maskPix);
                for (int j = 0; j < th; j++)
                {
                    int y = yTop + j;
                    if (y < y0 || y >= yEnd) continue;
                    int rowBase = (y - y0) * cw;
                    for (int i = 0; i < tw; i++)
                    {
                        int x = x0 + i;
                        if (x < 0 || x >= cw) continue;
                        int p = (j * tw + i) * 4;
                        float a = _pix[p + 3] * opacity;
                        if (mask != null) a *= _maskPix[j * tw + i];
                        Over(canvas, (rowBase + x) * 4, _pix[p], _pix[p + 1], _pix[p + 2], a, mix);
                    }
                }
            }
    }

    void CompositeBuffer(Layer group, byte[] src, byte[] canvas, int y0, int rows, float opacityScale)
    {
        var mix = Spaces(group);
        float opacity = group.Opacity * opacityScale;
        int cw = Width;
        float[]? maskBand = null;
        if (group.ApplyMask && group.Mask != 0)
        {
            var mask = MaskOf(group);
            if (mask != null)
            {
                maskBand = new float[cw * rows];
                int yEnd = y0 + rows;
                int tyFirst = Math.Max(0, FloorDiv(y0 - group.OffY, 64)), tyLast = Math.Min(mask.Rows - 1, FloorDiv(yEnd - 1 - group.OffY, 64));
                for (int ty = tyFirst; ty <= tyLast; ty++)
                    for (int tx = 0; tx < mask.Cols; tx++)
                    {
                        int tw = Math.Min(64, mask.W - tx * 64), th = Math.Min(64, mask.H - ty * 64);
                        DecodeMaskTile(mask, ty * mask.Cols + tx, tw, th, _maskPix);
                        for (int j = 0; j < th; j++)
                        {
                            int y = group.OffY + ty * 64 + j;
                            if (y < y0 || y >= yEnd) continue;
                            for (int i = 0; i < tw; i++)
                            {
                                int x = group.OffX + tx * 64 + i;
                                if (x >= 0 && x < cw) maskBand[(y - y0) * cw + x] = _maskPix[j * tw + i];
                            }
                        }
                    }
            }
        }
        int n = cw * rows;
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            float a = src[p + 3] / 255f * opacity;
            if (maskBand != null) a *= maskBand[i];
            if (a <= 0) continue;
            Over(canvas, p, src[p] / 255f, src[p + 1] / 255f, src[p + 2] / 255f, a, mix);
        }
    }

    void DecodeTile(TileTable t, int index, int tw, int th, int channels, Layer l, float[] outPix)
    {
        int n = tw * th;
        var fmt = l.Indexed ? PixelFormat.U8Gamma : _img.Format;
        int bpc = fmt.Bpc;
        var raw = ReadTileBytes(t, index, n * channels * bpc);
        var cmap = _img.Colormap;
        if (!l.Indexed && !l.Gray && fmt.Kind == Kind.U8 && !fmt.Linear)
        {
            for (int i = 0, src = 0, dst = 0; i < n; i++, src += channels, dst += 4)
            {
                outPix[dst] = raw[src] * (1f / 255); outPix[dst + 1] = raw[src + 1] * (1f / 255); outPix[dst + 2] = raw[src + 2] * (1f / 255);
                outPix[dst + 3] = channels == 4 ? raw[src + 3] * (1f / 255) : 1f;
            }
            return;
        }
        for (int i = 0; i < n; i++)
        {
            int src = i * channels * bpc, dst = i * 4;
            if (l.Indexed)
            {
                int idx = raw[src] * 3;
                if (cmap == null || idx + 2 >= cmap.Length) { outPix[dst] = outPix[dst + 1] = outPix[dst + 2] = 0; }
                else { outPix[dst] = cmap[idx] / 255f; outPix[dst + 1] = cmap[idx + 1] / 255f; outPix[dst + 2] = cmap[idx + 2] / 255f; }
                outPix[dst + 3] = channels == 2 ? raw[src + 1] / 255f : 1f;
                continue;
            }
            float r = Component(raw, src, fmt), g, b;
            if (l.Gray) { g = b = r; }
            else { g = Component(raw, src + bpc, fmt); b = Component(raw, src + 2 * bpc, fmt); }
            if (fmt.Linear) { r = LinearToSrgb(r); g = LinearToSrgb(g); b = LinearToSrgb(b); }
            outPix[dst] = r; outPix[dst + 1] = g; outPix[dst + 2] = b;
            outPix[dst + 3] = l.HasAlpha ? Component(raw, src + (channels - 1) * bpc, fmt) : 1f;
        }
    }

    void DecodeMaskTile(TileTable t, int index, int tw, int th, float[] outMask)
    {
        int n = tw * th;
        var fmt = _img.Format;
        var raw = ReadTileBytes(t, index, n * fmt.Bpc);
        for (int i = 0; i < n; i++) outMask[i] = Component(raw, i * fmt.Bpc, fmt);
    }

    static float Component(byte[] raw, int at, PixelFormat fmt) => fmt.Kind switch
    {
        Kind.U8 => raw[at] / 255f,
        Kind.U16 => BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(at)) / 65535f,
        Kind.U32 => (float)(BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(at)) / 4294967295.0),
        Kind.Half => Math.Clamp((float)BitConverter.Int16BitsToHalf(BinaryPrimitives.ReadInt16BigEndian(raw.AsSpan(at))), 0, 1),
        Kind.Float => Math.Clamp(BinaryPrimitives.ReadSingleBigEndian(raw.AsSpan(at)), 0, 1),
        _ => Math.Clamp((float)BinaryPrimitives.ReadDoubleBigEndian(raw.AsSpan(at)), 0, 1),
    };

    byte[] ReadTileBytes(TileTable t, int index, int size)
    {
        if (_raw.Length < size) _raw = new byte[size];
        var raw = _raw;
        long at = t.Offsets[index];
        long next = index + 1 < t.Offsets.Length ? t.Offsets[index + 1] : 0;
        long limit = next > at ? next - at : (long)size * 3 / 2 + 64;
        if (limit > int.MaxValue) throw new InvalidDataException("tile too large");
        _rd.Seek(at);
        switch (_img.Compression)
        {
            case 0:
                _rd.ReadExactly(raw, size);
                break;
            case 1:
            {
                int packed = _rd.ReadUpTo((int)limit, ref _packed);
                DecodeRle(_packed, packed, raw, size / t.Bpp, t.Bpp);
                break;
            }
            default:
            {
                int packed = _rd.ReadUpTo((int)limit, ref _packed);
                using var z = new ZLibStream(new MemoryStream(_packed, 0, packed, false), CompressionMode.Decompress);
                int got = 0;
                while (got < size)
                {
                    int k = z.Read(raw, got, size - got);
                    if (k <= 0) throw new InvalidDataException("zlib tile ends early");
                    got += k;
                }
                break;
            }
        }
        return raw;
    }

    static void DecodeRle(byte[] src, int packedLen, byte[] raw, int pixels, int bpp)
    {
        int p = 0;
        for (int plane = 0; plane < bpp; plane++)
        {
            int n = 0;
            while (n < pixels)
            {
                if (p >= packedLen) throw new InvalidDataException("RLE tile ends early");
                int op = src[p++];
                if (op <= 127)
                {
                    int count;
                    if (op == 127) { if (p + 2 > packedLen) throw new InvalidDataException("RLE tile ends early"); count = (src[p] << 8) | src[p + 1]; p += 2; }
                    else count = op + 1;
                    if (p >= packedLen || n + count > pixels) throw new InvalidDataException("RLE run overruns the tile");
                    byte v = src[p++];
                    for (int k = 0; k < count; k++, n++) raw[n * bpp + plane] = v;
                }
                else
                {
                    int count;
                    if (op == 128) { if (p + 2 > packedLen) throw new InvalidDataException("RLE tile ends early"); count = (src[p] << 8) | src[p + 1]; p += 2; }
                    else count = 256 - op;
                    if (p + count > packedLen || n + count > pixels) throw new InvalidDataException("RLE literal overruns the tile");
                    for (int k = 0; k < count; k++, n++) raw[n * bpp + plane] = src[p++];
                }
            }
        }
    }

    enum Blend { Normal, Multiply, Screen, Overlay, Darken, Lighten, Addition, Subtract, Difference, HardLight, SoftLight, GrainExtract, GrainMerge, Dodge, Burn, Divide }

    static Blend BlendOf(int mode, out bool legacy, out bool known)
    {
        legacy = mode <= GimpLayerMode.LastLegacy;
        known = true;
        switch (mode)
        {
            case 0 or 28: return Blend.Normal;
            case 3 or 30: return Blend.Multiply;
            case 4 or 31: return Blend.Screen;
            case GimpLayerMode.LegacyOverlay: return Blend.SoftLight;
            case GimpLayerMode.Overlay: return Blend.Overlay;
            case 6 or 32: return Blend.Difference;
            case 7 or 33: return Blend.Addition;
            case 8 or 34: return Blend.Subtract;
            case 9 or 35: return Blend.Darken;
            case 10 or 36: return Blend.Lighten;
            case 15 or 41: return Blend.Divide;
            case 16 or 42: return Blend.Dodge;
            case 17 or 43: return Blend.Burn;
            case 18 or 44: return Blend.HardLight;
            case 19 or 45: return Blend.SoftLight;
            case 20 or 46: return Blend.GrainExtract;
            case 21 or 47: return Blend.GrainMerge;
            default: known = false; return Blend.Normal;
        }
    }

    static string ModeName(int mode) => mode switch
    {
        1 => "Dissolve", 2 or 29 => "Behind", 11 or 37 => "HSV Hue", 12 or 38 => "HSV Saturation", 13 or 39 => "HSL Color", 14 or 40 => "HSV Value",
        22 or 57 => "Color erase", 24 => "LCh Hue", 25 => "LCh Chroma", 26 => "LCh Color", 27 => "LCh Lightness", 48 => "Vivid light", 49 => "Pin light",
        50 => "Linear light", 51 => "Hard mix", 52 => "Exclusion", 53 => "Linear burn", 54 => "Luma darken only", 55 => "Luma lighten only",
        56 => "Luminance", 58 => "Erase", 59 => "Merge", 60 => "Split", 61 => "Pass through", _ => $"#{mode}",
    };

    static float BlendChannel(Blend b, float cb, float cs, bool legacy)
    {
        float v = b switch
        {
            Blend.Multiply => cb * cs,
            Blend.Screen => 1 - (1 - cb) * (1 - cs),
            Blend.Overlay => cb <= 0.5f ? 2 * cb * cs : 1 - 2 * (1 - cb) * (1 - cs),
            Blend.HardLight => cs <= 0.5f ? 2 * cs * cb : 1 - 2 * (1 - cs) * (1 - cb),
            Blend.SoftLight => (1 - cb) * cb * cs + cb * (1 - (1 - cb) * (1 - cs)),
            Blend.Darken => Math.Min(cb, cs),
            Blend.Lighten => Math.Max(cb, cs),
            Blend.Addition => cb + cs,
            Blend.Subtract => cb - cs,
            Blend.Difference => Math.Abs(cb - cs),
            Blend.GrainExtract => cb - cs + 0.5f,
            Blend.GrainMerge => cb + cs - 0.5f,
            Blend.Dodge => 1 - cs <= 1e-6f ? (cb <= 0 ? 0 : Huge) : cb / (1 - cs),
            Blend.Burn => cs <= 1e-6f ? (cb >= 1 ? 1 : -Huge) : 1 - (1 - cb) / cs,
            Blend.Divide => cs <= 1e-6f ? (cb <= 0 ? 0 : Huge) : cb / cs,
            _ => cs,
        };
        return legacy ? Math.Clamp(v, 0, 1) : v;
    }

    const float Huge = 1e6f;

    static void Over(byte[] canvas, int ci, float lr, float lg, float lb, float la, Mix mix)
    {
        if (la <= 0) return;
        if (mix.Blend == Blend.Normal && la >= 1 && mix.Composite is Composite.Union or Composite.ClipToLayer)
        {
            canvas[ci] = ToByte(lr); canvas[ci + 1] = ToByte(lg); canvas[ci + 2] = ToByte(lb); canvas[ci + 3] = 255;
            return;
        }
        float ba = canvas[ci + 3] / 255f;
        if (ba <= 0)
        {
            if (mix.Composite is Composite.ClipToBackdrop or Composite.Intersection) return;
            canvas[ci] = ToByte(lr); canvas[ci + 1] = ToByte(lg); canvas[ci + 2] = ToByte(lb); canvas[ci + 3] = ToByte(la);
            return;
        }
        float br = canvas[ci] / 255f, bg = canvas[ci + 1] / 255f, bb = canvas[ci + 2] / 255f;
        float sr = lr, sg = lg, sb = lb;
        bool linear = mix.CompositeLinear;
        if (mix.Blend != Blend.Normal)
        {
            float xr = br, xg = bg, xb = bb, yr = lr, yg = lg, yb = lb;
            if (mix.BlendLinear) { xr = SrgbToLinear(xr); xg = SrgbToLinear(xg); xb = SrgbToLinear(xb); yr = SrgbToLinear(yr); yg = SrgbToLinear(yg); yb = SrgbToLinear(yb); }
            float zr = BlendChannel(mix.Blend, xr, yr, mix.Legacy), zg = BlendChannel(mix.Blend, xg, yg, mix.Legacy), zb = BlendChannel(mix.Blend, xb, yb, mix.Legacy);
            if (mix.BlendLinear) { zr = LinearToSrgb(zr); zg = LinearToSrgb(zg); zb = LinearToSrgb(zb); }
            if (linear) { zr = SrgbToLinear(zr); zg = SrgbToLinear(zg); zb = SrgbToLinear(zb); lr = SrgbToLinear(lr); lg = SrgbToLinear(lg); lb = SrgbToLinear(lb); }
            sr = ba * zr + (1 - ba) * lr; sg = ba * zg + (1 - ba) * lg; sb = ba * zb + (1 - ba) * lb;
        }
        else if (linear)
        {
            sr = SrgbToLinear(sr); sg = SrgbToLinear(sg); sb = SrgbToLinear(sb);
        }
        if (linear) { br = _toLinear[canvas[ci]]; bg = _toLinear[canvas[ci + 1]]; bb = _toLinear[canvas[ci + 2]]; }
        float oa, ratio;
        switch (mix.Composite)
        {
            case Composite.ClipToBackdrop: oa = ba; ratio = la; break;
            case Composite.ClipToLayer: oa = la; ratio = ba; break;
            case Composite.Intersection: oa = la * ba; ratio = 1; break;
            default: oa = la + ba * (1 - la); ratio = la / oa; break;
        }
        float orr = ratio * sr + (1 - ratio) * br, og = ratio * sg + (1 - ratio) * bg, ob = ratio * sb + (1 - ratio) * bb;
        if (linear) { canvas[ci] = LinearToByte(orr); canvas[ci + 1] = LinearToByte(og); canvas[ci + 2] = LinearToByte(ob); }
        else { canvas[ci] = ToByte(orr); canvas[ci + 1] = ToByte(og); canvas[ci + 2] = ToByte(ob); }
        canvas[ci + 3] = ToByte(oa);
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    static readonly float[] _toLinear = BuildToLinear();
    static readonly byte[] _toSrgb = BuildToSrgb();
    const int SrgbTable = 1 << 15;

    static float[] BuildToLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++) { double c = i / 255.0; t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)); }
        return t;
    }

    static byte[] BuildToSrgb()
    {
        var t = new byte[SrgbTable + 1];
        for (int i = 0; i <= SrgbTable; i++) { double l = (double)i / SrgbTable; double c = l <= 0.0031308 ? 12.92 * l : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055; t[i] = (byte)Math.Clamp((int)Math.Round(c * 255), 0, 255); }
        return t;
    }

    static byte LinearToByte(float l) => _toSrgb[Math.Clamp((int)MathF.Round(l * SrgbTable), 0, SrgbTable)];

    static float SrgbToLinear(float c)
    {
        if (c < 0) return -SrgbToLinear(-c);
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    static float LinearToSrgb(float l)
    {
        if (l < 0) return -LinearToSrgb(-l);
        return l <= 0.0031308f ? 12.92f * l : 1.055f * MathF.Pow(l, 1 / 2.4f) - 0.055f;
    }

    sealed class Reader
    {
        readonly Stream _s;
        readonly byte[] _b = new byte[8];
        public bool Wide;
        public Reader(Stream s) { _s = s; if (!s.CanSeek) throw new ArgumentException("the .xcf stream must be seekable"); }
        public long Position => _s.Position;
        public void Seek(long at) { if (at < 0 || at > _s.Length) throw new InvalidDataException("offset outside the file"); _s.Position = at; }
        public uint U32() { ReadExactly(_b, 4); return BinaryPrimitives.ReadUInt32BigEndian(_b); }
        public int I32() { ReadExactly(_b, 4); return BinaryPrimitives.ReadInt32BigEndian(_b); }
        public float F32() { ReadExactly(_b, 4); return BinaryPrimitives.ReadSingleBigEndian(_b); }
        public long Ptr()
        {
            if (!Wide) return U32();
            ReadExactly(_b, 8);
            ulong v = BinaryPrimitives.ReadUInt64BigEndian(_b);
            if (v > long.MaxValue) throw new InvalidDataException("offset outside the file");
            return (long)v;
        }
        public string Str()
        {
            uint n = U32();
            if (n == 0) return "";
            if (n > 1 << 20) throw new InvalidDataException("string too long");
            var bytes = Bytes((int)n);
            int len = (int)n - 1;
            while (len > 0 && bytes[len - 1] == 0) len--;
            return System.Text.Encoding.UTF8.GetString(bytes, 0, len);
        }
        public byte[] Bytes(int n) { var a = new byte[n]; ReadExactly(a, n); return a; }
        public void ReadExactly(byte[] into, int n)
        {
            int got = 0;
            while (got < n)
            {
                int k = _s.Read(into, got, n - got);
                if (k <= 0) throw new InvalidDataException("file ends early");
                got += k;
            }
        }
        public int ReadUpTo(int max, ref byte[] buf)
        {
            if (buf.Length < max) buf = new byte[Math.Max(max, buf.Length * 2)];
            int got = 0;
            while (got < max)
            {
                int k = _s.Read(buf, got, max - got);
                if (k <= 0) break;
                got += k;
            }
            return got;
        }
    }
}

static class XcfProperty
{
    public const int Opacity = 6, Mode = 7, Visible = 8, ApplyMask = 11, Offsets = 15, Compression = 17, GroupItem = 29, ItemPath = 30,
        FloatOpacity = 33, CompositeMode = 35, CompositeSpace = 36, BlendSpace = 37;
}

static class GimpLayerMode
{
    public const int LegacyOverlay = 5, LastLegacy = 22, Overlay = 23, PassThrough = 61;
}
