using System.Collections.Concurrent;

namespace KTable.Core.Model;

public static class SheetExport
{
    public const int BandRows = 256;
    const int MostBandPixels = 1 << 24, MostTilesHeld = 1024;
    const double Aligned = 1e-4, Flat = 1e-12, Far = 1e9;

    public sealed record Source(SheetDef Sheet, TileIndex Index)
    {
        public TileLevel Full => Index.Levels[0];
    }

    public readonly record struct Frame(double Left, double Top, int W, int H, double StepX, double StepZ);

    public delegate byte[] TileRead(Source source, int tx, int ty);

    public static Frame FrameOf(IReadOnlyList<Source> sources)
    {
        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue, density = 0;
        foreach (var s in sources)
        {
            var sh = s.Sheet;
            var crop = sh.Crop ?? SheetGeometry.Whole;
            density = Math.Max(density, Math.Max(crop.W * s.Full.W / sh.Size.W, crop.H * s.Full.H / sh.Size.H));
            foreach (var (right, down) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var corner = sh.Pos + SheetGeometry.Offset(right * sh.Size.W / 2, down * sh.Size.H / 2, sh.RotDeg);
                (x0, x1, z0, z1) = (Math.Min(x0, corner.X), Math.Max(x1, corner.X), Math.Min(z0, corner.Z), Math.Max(z1, corner.Z));
            }
        }
        if (sources.Count == 0) return default;
        int w = Math.Max(1, (int)Math.Round((x1 - x0) * density)), h = Math.Max(1, (int)Math.Round((z1 - z0) * density));
        return new Frame(x0, z1, w, h, (x1 - x0) / w, (z1 - z0) / h);
    }

    public static void Render(IReadOnlyList<Source> sources, Frame frame, TileRead read, Action<byte[], int> rows, Func<bool> stopped, Action<double>? progress = null)
    {
        var order = sources.Select(s => s.Sheet).ToList();
        var laid = SheetStack.TopDown(order).AsEnumerable().Reverse().Select(sh => new Laid(sources[order.IndexOf(sh)], frame)).ToArray();
        var held = new ConcurrentDictionary<(Laid, int, int), Lazy<byte[]>>();
        int band = Math.Clamp(MostBandPixels / frame.W, 1, BandRows);
        var rgba = new byte[frame.W * band * 4];
        for (int top = 0; top < frame.H; top += band)
        {
            if (stopped()) throw new OperationCanceledException();
            if (held.Count > MostTilesHeld) held.Clear();
            int count = Math.Min(band, frame.H - top), first = top;
            Array.Clear(rgba, 0, count * frame.W * 4);
            Parallel.For(0, count, r =>
            {
                foreach (var sheet in laid) sheet.Row(first + r, rgba, r * frame.W * 4, new Reader(sheet, held, read));
            });
            rows(rgba, count);
            progress?.Invoke((double)(top + count) / frame.H);
        }
    }

    public static void Over(byte[] tile, int w, int h, byte[] premultiplied, int overW)
    {
        if (premultiplied.Length < overW * h * 4 || overW < w) throw new InvalidDataException("the drawing laid over a map tile is smaller than the tile");
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int t = (y * w + x) * 4, o = (y * overW + x) * 4;
                double over = premultiplied[o + 3] / 255.0;
                if (over <= 0) continue;
                double under = tile[t + 3] / 255.0 * (1 - over), whole = over + under;
                for (int c = 0; c < 3; c++) tile[t + c] = Byte((premultiplied[o + c] + tile[t + c] * under) / whole);
                tile[t + 3] = Byte(whole * 255);
            }
    }

    static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    sealed class Laid
    {
        public readonly Source Source;
        readonly int _w, _fullW, _fullH;
        readonly double _u0, _v0, _uAcross, _vAcross, _uDown, _vDown, _cropX, _cropY, _cropW, _cropH;

        public Laid(Source source, Frame f)
        {
            Source = source;
            var sh = source.Sheet;
            var crop = sh.Crop ?? SheetGeometry.Whole;
            (_cropX, _cropY, _cropW, _cropH) = (crop.X, crop.Y, crop.W, crop.H);
            (_w, _fullW, _fullH) = (f.W, source.Full.W, source.Full.H);
            double a = (180 - sh.RotDeg) * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            double dx = f.Left + f.StepX / 2 - sh.Pos.X, dz = f.Top - f.StepZ / 2 - sh.Pos.Z;
            _u0 = (dx * cos + dz * sin) / sh.Size.W + 0.5;
            _v0 = (dx * sin - dz * cos) / sh.Size.H + 0.5;
            (_uAcross, _vAcross) = (f.StepX * cos / sh.Size.W, f.StepX * sin / sh.Size.H);
            (_uDown, _vDown) = (-f.StepZ * sin / sh.Size.W, f.StepZ * cos / sh.Size.H);
        }

        public void Row(int y, byte[] rgba, int at, Reader reader)
        {
            double u = _u0 + y * _uDown, v = _v0 + y * _vDown;
            int from = 0, to = _w - 1;
            Clip(u, _uAcross, ref from, ref to);
            Clip(v, _vAcross, ref from, ref to);
            for (int i = from; i <= to; i++)
            {
                double pu = u + i * _uAcross, pv = v + i * _vAcross;
                if (pu < 0 || pu > 1 || pv < 0 || pv > 1) continue;
                reader.Over(rgba, at + i * 4, (_cropX + pu * _cropW) * _fullW - 0.5, (_cropY + pv * _cropH) * _fullH - 0.5);
            }
        }

        static void Clip(double start, double step, ref int from, ref int to)
        {
            if (Math.Abs(step) < Flat)
            {
                if (start < 0 || start > 1) to = from - 1;
                return;
            }
            double a = -start / step, b = (1 - start) / step;
            from = Math.Max(from, (int)Math.Clamp(Math.Floor(Math.Min(a, b)), -1, Far));
            to = Math.Min(to, (int)Math.Clamp(Math.Ceiling(Math.Max(a, b)), -1, Far));
        }
    }

    struct Reader(Laid sheet, ConcurrentDictionary<(Laid, int, int), Lazy<byte[]>> held, TileRead read)
    {
        readonly int _size = sheet.Source.Index.TileSize, _fullW = sheet.Source.Full.W, _fullH = sheet.Source.Full.H;
        int _tx = -1, _ty = -1, _tileW;
        byte[] _tile = [];

        public void Over(byte[] rgba, int at, double x, double y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            if (fx > 1 - Aligned) (x0, fx) = (x0 + 1, 0);
            if (fy > 1 - Aligned) (y0, fy) = (y0 + 1, 0);
            if (fx < Aligned) fx = 0;
            if (fy < Aligned) fy = 0;
            double r, g, b, alpha;
            if (fx == 0 && fy == 0)
            {
                int p = Texel(x0, y0);
                (r, g, b, alpha) = (_tile[p], _tile[p + 1], _tile[p + 2], _tile[p + 3]);
            }
            else
            {
                (r, g, b, alpha) = (0, 0, 0, 0);
                Add(x0, y0, (1 - fx) * (1 - fy), ref r, ref g, ref b, ref alpha);
                Add(x0 + 1, y0, fx * (1 - fy), ref r, ref g, ref b, ref alpha);
                Add(x0, y0 + 1, (1 - fx) * fy, ref r, ref g, ref b, ref alpha);
                Add(x0 + 1, y0 + 1, fx * fy, ref r, ref g, ref b, ref alpha);
                if (alpha > 0) (r, g, b) = (r / alpha, g / alpha, b / alpha);
            }
            double over = alpha / 255, under = rgba[at + 3] / 255.0 * (1 - over), whole = over + under;
            if (whole <= 0) return;
            rgba[at] = Byte((r * over + rgba[at] * under) / whole);
            rgba[at + 1] = Byte((g * over + rgba[at + 1] * under) / whole);
            rgba[at + 2] = Byte((b * over + rgba[at + 2] * under) / whole);
            rgba[at + 3] = Byte(whole * 255);
        }

        void Add(int x, int y, double weight, ref double r, ref double g, ref double b, ref double alpha)
        {
            if (weight <= 0) return;
            int p = Texel(x, y);
            double a = _tile[p + 3] * weight;
            (r, g, b, alpha) = (r + _tile[p] * a, g + _tile[p + 1] * a, b + _tile[p + 2] * a, alpha + a);
        }

        int Texel(int x, int y)
        {
            x = Math.Clamp(x, 0, _fullW - 1);
            y = Math.Clamp(y, 0, _fullH - 1);
            int tx = x / _size, ty = y / _size;
            if (tx != _tx || ty != _ty) Take(tx, ty);
            return ((y - ty * _size) * _tileW + x - tx * _size) * 4;
        }

        void Take(int tx, int ty)
        {
            var from = sheet;
            var reader = read;
            _tile = held.GetOrAdd((sheet, tx, ty), _ => new Lazy<byte[]>(() => reader(from.Source, tx, ty))).Value;
            (_tx, _ty, _tileW) = (tx, ty, Math.Min(_size, _fullW - tx * _size));
            int tileH = Math.Min(_size, _fullH - ty * _size);
            if (_tile.Length != _tileW * tileH * 4) throw new InvalidDataException($"map tile {tx},{ty} of {sheet.Source.Sheet.Name} is not {_tileW} × {tileH}");
        }
    }
}
