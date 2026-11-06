using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace KTable.Client.Relief;

public static class HeightFiles
{
    public static readonly string[] Extensions = { ".asc", ".hgt", ".xyz" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const double MetresPerDegree = 111_320;
    const short HgtVoid = -32768;
    const int SrtmFine = 3601, SrtmCoarse = 1201;
    const double ArcSecondsPerDegree = 3600;

    public static bool IsHeightFile(string path) => Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    public static ReliefSource Read(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".asc" => Asc(path),
        ".hgt" => Hgt(path),
        ".xyz" => Xyz(path),
        _ => throw new InvalidDataException("not a height file"),
    };

    public static ReliefSource FromSamples(string name, int cols, int rows, float[] raw, double cellX, double cellY, bool realUnits, float? noData = null)
    {
        var filled = new bool[raw.Length];
        for (int i = 0; i < raw.Length; i++) filled[i] = float.IsFinite(raw[i]) && (noData == null || Math.Abs(raw[i] - noData.Value) > 1e-3f);
        ReliefBuilder.FillHoles(raw, filled, cols, rows);
        double width = Math.Max(1e-9, (cols - 1) * cellX), depth = Math.Max(1e-9, (rows - 1) * cellY);
        if (!realUnits)
        {
            float lo = raw.Min(), hi = raw.Max(), span = Math.Max(1e-9f, hi - lo);
            for (int i = 0; i < raw.Length; i++) raw[i] = (float)((raw[i] - lo) / span * ReliefSource.UnitlessRise * width);
        }
        return new ReliefSource { Name = name, Cols = cols, Rows = rows, Raw = raw, Width = width, Depth = depth, RealUnits = realUnits };
    }

    static ReliefSource Asc(string path)
    {
        var head = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var values = new List<float>();
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p.Length == 2 && char.IsLetter(p[0][0])) { head[p[0]] = double.Parse(p[1], NumberStyles.Float, Inv); continue; }
            foreach (var s in p) values.Add(float.Parse(s, NumberStyles.Float, Inv));
        }
        int cols = (int)Need(head, "ncols"), rows = (int)Need(head, "nrows");
        if (cols < 2 || rows < 2 || values.Count < cols * rows) throw new InvalidDataException("the grid is shorter than its header says");
        double cell = head.TryGetValue("cellsize", out var cs) ? cs : head.TryGetValue("dx", out var dx) ? dx : 1;
        double cellY = head.TryGetValue("dy", out var dy) ? dy : cell;
        float? noData = head.TryGetValue("nodata_value", out var nd) ? (float)nd : null;
        bool degrees = cell < 0.01 && head.TryGetValue("xllcorner", out var xll) && Math.Abs(xll) <= 180;
        double k = degrees ? MetresPerDegree : 1;
        return FromSamples(Path.GetFileNameWithoutExtension(path), cols, rows, values.Take(cols * rows).ToArray(), cell * k, cellY * k, true, noData);
    }

    static double Need(Dictionary<string, double> head, string key) => head.TryGetValue(key, out var v) ? v : throw new InvalidDataException($"the header has no {key}");

    static ReliefSource Hgt(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int side = bytes.Length == SrtmFine * SrtmFine * 2 ? SrtmFine : bytes.Length == SrtmCoarse * SrtmCoarse * 2 ? SrtmCoarse : 0;
        if (side == 0) throw new InvalidDataException("not an SRTM tile (1201² or 3601² samples)");
        var raw = new float[side * side];
        for (int i = 0; i < raw.Length; i++)
        {
            short h = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(i * 2));
            raw[i] = h == HgtVoid ? float.NaN : h;
        }
        double arc = (side == SrtmFine ? 1 : 3) / ArcSecondsPerDegree * MetresPerDegree;
        return FromSamples(Path.GetFileNameWithoutExtension(path), side, side, raw, arc, arc, true);
    }

    static ReliefSource Xyz(string path)
    {
        var pts = new List<(double x, double y, double z)>();
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) continue;
            if (!double.TryParse(p[0], NumberStyles.Float, Inv, out var x) || !double.TryParse(p[1], NumberStyles.Float, Inv, out var y) || !double.TryParse(p[2], NumberStyles.Float, Inv,
                out var z)) continue;
            pts.Add((x, y, z));
        }
        if (pts.Count < 4) throw new InvalidDataException("fewer than four points");
        double x0 = pts.Min(p => p.x), x1 = pts.Max(p => p.x), y0 = pts.Min(p => p.y), y1 = pts.Max(p => p.y);
        if (x1 <= x0 || y1 <= y0) throw new InvalidDataException("the points lie on a line");
        var xs = pts.Select(p => p.x).Distinct().Count();
        var ys = pts.Select(p => p.y).Distinct().Count();
        bool regular = (long)xs * ys == pts.Count;
        int cols = regular ? xs : (int)Math.Clamp(Math.Sqrt(pts.Count * (x1 - x0) / (y1 - y0)), 2, KTable.Core.Model.ReliefGrid.MaxSide);
        int rows = regular ? ys : (int)Math.Clamp(pts.Count / (double)cols, 2, KTable.Core.Model.ReliefGrid.MaxSide);
        cols = Math.Min(cols, KTable.Core.Model.ReliefGrid.MaxSide);
        rows = Math.Min(rows, KTable.Core.Model.ReliefGrid.MaxSide);
        var raw = new float[cols * rows];
        Array.Fill(raw, float.NaN);
        foreach (var (x, y, z) in pts)
        {
            int gx = (int)Math.Round((x - x0) / (x1 - x0) * (cols - 1)), gy = (int)Math.Round((y1 - y) / (y1 - y0) * (rows - 1));
            int i = gy * cols + gx;
            raw[i] = float.IsNaN(raw[i]) ? (float)z : Math.Max(raw[i], (float)z);
        }
        bool degrees = x1 - x0 < 5 && Math.Abs(x0) <= 180 && Math.Abs(y0) <= 90;
        double k = degrees ? MetresPerDegree : 1;
        return FromSamples(Path.GetFileNameWithoutExtension(path), cols, rows, raw, (x1 - x0) / (cols - 1) * k, (y1 - y0) / (rows - 1) * k, true);
    }

    static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    const int GreyType = 0, RgbType = 2, GreyAlphaType = 4, RgbaType = 6;

    public static bool IsDeepPng(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[26];
            if (f.Read(head, 0, head.Length) < head.Length || !head.AsSpan(0, 8).SequenceEqual(PngSignature)) return false;
            return head[24] == 16;
        }
        catch (IOException) { return false; }
    }

    public static ReliefSource Png(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature)) throw new InvalidDataException("not a PNG");
        int w = 0, h = 0, depth = 0, type = 0, interlace = 0;
        using var idat = new MemoryStream();
        for (int at = 8; at + 8 <= bytes.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at));
            var kind = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
            if (len < 0 || at + 12 + len > bytes.Length) throw new InvalidDataException("the PNG is cut short");
            var data = bytes.AsSpan(at + 8, len);
            if (kind == "IHDR")
            {
                w = BinaryPrimitives.ReadInt32BigEndian(data);
                h = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                depth = data[8];
                type = data[9];
                interlace = data[12];
            }
            else if (kind == "IDAT") idat.Write(data);
            else if (kind == "IEND") break;
            at += 12 + len;
        }
        if (interlace != 0) throw new InvalidDataException("interlaced PNG heightmaps are not read; save it without interlacing");
        if (depth is not (8 or 16)) throw new InvalidDataException("PNG heightmaps need 8 or 16 bits a sample");
        int channels = type switch { GreyType => 1, RgbType => 3, GreyAlphaType => 2, RgbaType => 4, _ => throw new InvalidDataException("palette PNGs are not heightmaps") };
        if (w < 2 || h < 2 || w > 16384 || h > 16384) throw new InvalidDataException("the PNG's size is out of range");
        int bpp = channels * depth / 8, stride = w * bpp;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var rows = new byte[(stride + 1) * h];
        int read = 0;
        while (read < rows.Length)
        {
            int n = z.Read(rows, read, rows.Length - read);
            if (n == 0) break;
            read += n;
        }
        if (read < rows.Length) throw new InvalidDataException("the PNG's pixels are cut short");
        var prev = new byte[stride];
        var cur = new byte[stride];
        var raw = new float[w * h];
        float most = depth == 16 ? ushort.MaxValue : byte.MaxValue;
        for (int y = 0; y < h; y++)
        {
            int f = rows[y * (stride + 1)];
            Buffer.BlockCopy(rows, y * (stride + 1) + 1, cur, 0, stride);
            Unfilter(f, cur, prev, bpp);
            for (int x = 0; x < w; x++)
            {
                int o = x * bpp;
                float v = depth == 16 ? (cur[o] << 8 | cur[o + 1]) : cur[o];
                raw[y * w + x] = v / most;
            }
            (prev, cur) = (cur, prev);
        }
        return FromSamples(Path.GetFileNameWithoutExtension(path), w, h, raw, 1, 1, false);
    }

    static void Unfilter(int f, byte[] cur, byte[] prev, int bpp)
    {
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
            cur[i] = (byte)(f switch
            {
                0 => cur[i],
                1 => cur[i] + a,
                2 => cur[i] + b,
                3 => cur[i] + (a + b) / 2,
                4 => cur[i] + Paeth(a, b, c),
                _ => throw new InvalidDataException("the PNG uses an unknown filter"),
            });
        }
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
