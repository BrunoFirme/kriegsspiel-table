using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class SheetPicture
{
    const int LeastAssemblers = 2, MostAssemblers = 4, CoresPerAssembler = 3;
    const double MipmapsShare = 4.0 / 3;
    static readonly SemaphoreSlim Slots = new(Math.Clamp(System.Environment.ProcessorCount / CoresPerAssembler, LeastAssemblers, MostAssemblers));
    public static double ComposeMs;
    public static int Composed;
    public static bool Packs { get; private set; } = true;
    const double PackedBytesPerPixel = 0.5, PackedAlphaBytesPerPixel = 1, LooseBytesPerPixel = 4;
    public const int MostPartSide = 8192, PartAlign = 64, PartGutter = PartAlign / 2;
    double _bytesPerPixel;

    readonly Assets _assets;
    readonly string _package, _tilesRel;
    public TileIndex Index { get; }
    public int Level { get; private set; } = -1;
    public int Target { get; private set; }
    public Texture2DArray? Texture { get; private set; }
    public Parts Shape { get; private set; }
    public bool HasAlpha { get; private set; }
    public int TilesLoaded { get; private set; }
    public int TilesTotal { get; private set; }
    public event Action? Changed;
    int _building = -1;
    volatile bool _dropped;

    public readonly record struct Parts(int Across, int Down, int W, int H, int Gutter)
    {
        public int Count => Across * Down;
        public int TextureW => W + 2 * Gutter;
        public int TextureH => H + 2 * Gutter;
        public Vector2 Span(TileLevel lv) => new((float)lv.W / W, (float)lv.H / H);
        public Vector2 Share => new((float)W / TextureW, (float)H / TextureH);
        public Vector2 Inset => new((float)Gutter / TextureW, (float)Gutter / TextureH);
    }

    public SheetPicture(Assets assets, string package, string tilesRel, TileIndex index)
    {
        _assets = assets;
        _package = package;
        _tilesRel = tilesRel;
        Index = index;
        Target = Coarsest;
    }

    public int Coarsest => Index.Levels.Count - 1;
    public TileLevel Shown => Index.Levels[Math.Max(0, Level)];

    public static Parts PartsOf(TileLevel lv)
    {
        int across = (lv.W + MostPartSide - 1) / MostPartSide, down = (lv.H + MostPartSide - 1) / MostPartSide;
        return new Parts(across, down, Side(lv.W, across), Side(lv.H, down), across * down > 1 ? PartGutter : 0);
        static int Side(int whole, int count) => count == 1 ? whole : PartAlign * ((whole + count * PartAlign - 1) / (count * PartAlign));
    }

    public bool Done => Level == Target;
    public long Bytes => Level < 0 ? 0 : Size(Index.Levels[Level], _bytesPerPixel);

    public static long BytesAt(TileIndex index, int level, bool alpha) =>
        Size(index.Levels[Math.Clamp(level, 0, index.Levels.Count - 1)], !Packs ? LooseBytesPerPixel : alpha ? PackedAlphaBytesPerPixel : PackedBytesPerPixel);

    static long Size(TileLevel lv, double bytesPerPixel)
    {
        var parts = PartsOf(lv);
        return (long)((double)parts.TextureW * parts.TextureH * parts.Count * bytesPerPixel * MipmapsShare);
    }

    public void Want(int level)
    {
        Target = Math.Clamp(level, 0, Coarsest);
        Next();
    }

    public void Drop() => _dropped = true;

    void Next()
    {
        if (_building >= 0 || _dropped) return;
        int next = Level < 0 ? Coarsest : Level != Target ? Target : -1;
        if (next >= 0) Fetch(next);
    }

    string TileRel(int level, int x, int y) => Path.Combine(_tilesRel, level.ToString(), $"{x}_{y}.{Index.Ext}");

    void Fetch(int level)
    {
        _building = level;
        var lv = Index.Levels[level];
        int waiting = lv.Cols * lv.Rows, missing = 0;
        TilesTotal = waiting;
        TilesLoaded = 0;
        void One(bool there)
        {
            if (there) TilesLoaded++;
            else missing++;
            if (--waiting > 0) return;
            if (missing > 0)
            {
                _building = -1;
                Target = Math.Max(Level, 0);
                return;
            }
            Compose(level);
        }
        for (int y = 0; y < lv.Rows; y++)
            for (int x = 0; x < lv.Cols; x++)
                _assets.EnsureFile(_package, TileRel(level, x, y), () => One(true), () => One(false));
    }

    void Compose(int level)
    {
        var lv = Index.Levels[level];
        int ts = Index.TileSize;
        var paths = new string[lv.Cols * lv.Rows];
        for (int y = 0; y < lv.Rows; y++)
            for (int x = 0; x < lv.Cols; x++)
                paths[y * lv.Cols + x] = _assets.Abs(_package, TileRel(level, x, y));
        Task.Run(async () =>
        {
            await Slots.WaitAsync();
            Stitched? made = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { made = Stitch(lv, ts, paths, () => Assets.Quitting || _dropped); }
            catch (Exception e) when (Unreadable(e)) { made = null; }
            finally { Slots.Release(); }
            double ms = clock.Elapsed.TotalMilliseconds;
            if (Assets.Quitting) made?.Dispose();
            else Callable.From(() => Arrived(level, made, ms)).CallDeferred();
        });
    }

    static bool Unreadable(Exception e) =>
        e is IOException or InvalidOperationException or UnauthorizedAccessException || e is AggregateException all && all.InnerExceptions.All(Unreadable);

    internal sealed record Stitched(Image[] Imgs, Parts Parts, bool Alpha, bool Packed)
    {
        public void Dispose() { foreach (var img in Imgs) img.Dispose(); }
    }

    internal readonly record struct Made(Image Img, bool Alpha, bool Packed);

    internal static Stitched? Stitch(TileLevel lv, int ts, string[] paths, Func<bool> stopped)
    {
        var parts = PartsOf(lv);
        var imgs = new Image[parts.Count];
        bool alpha = false, loose = false;
        for (int i = 0; i < imgs.Length; i++)
        {
            if (stopped() || Part(lv, ts, paths, parts, i, alpha, loose, stopped) is not { } got)
            {
                foreach (var img in imgs.AsSpan(0, i)) img.Dispose();
                return null;
            }
            if (got.Alpha == alpha && got.Packed != loose)
            {
                imgs[i] = got.Img;
                continue;
            }
            got.Img.Dispose();
            foreach (var img in imgs.AsSpan(0, i)) img.Dispose();
            alpha |= got.Alpha;
            loose |= !got.Packed;
            i = -1;
        }
        return new Stitched(imgs, parts, alpha, !loose);
    }

    internal static Made? Part(TileLevel lv, int ts, string[] paths, Parts parts, int index, bool alpha, bool loose, Func<bool> stopped)
    {
        int originX = index % parts.Across * parts.W - parts.Gutter, originY = index / parts.Across * parts.H - parts.Gutter;
        int x0 = Math.Max(0, originX), y0 = Math.Max(0, originY);
        int x1 = Math.Min(lv.W, originX + parts.TextureW), y1 = Math.Min(lv.H, originY + parts.TextureH);
        var img = Image.CreateEmpty(parts.TextureW, parts.TextureH, false, Image.Format.Rgba8);
        int firstX = x0 / ts, lastX = (x1 - 1) / ts;
        var row = new Image?[lastX - firstX + 1];
        for (int ty = y0 / ts; ty <= (y1 - 1) / ts; ty++)
        {
            if (stopped()) { img.Dispose(); return null; }
            int rowAt = ty;
            Parallel.For(0, row.Length, i => row[i] = Tile(paths[rowAt * lv.Cols + firstX + i]));
            bool whole = Array.TrueForAll(row, t => t != null);
            for (int i = 0; i < row.Length; i++)
            {
                using var tile = row[i];
                row[i] = null;
                if (!whole || tile == null) continue;
                int tx = firstX + i;
                int left = Math.Max(x0, tx * ts), top = Math.Max(y0, ty * ts);
                int right = Math.Min(x1, tx * ts + tile.GetWidth()), bottom = Math.Min(y1, ty * ts + tile.GetHeight());
                if (right > left && bottom > top)
                    img.BlitRect(tile, new Rect2I(left - tx * ts, top - ty * ts, right - left, bottom - top), new Vector2I(left - originX, top - originY));
            }
            if (!whole) { img.Dispose(); return null; }
        }
        StretchEdges(img, new Rect2I(x0 - originX, y0 - originY, x1 - x0, y1 - y0));
        img.GenerateMipmaps();
        if (stopped()) { img.Dispose(); return null; }
        bool hasAlpha = alpha || img.DetectAlpha() != Image.AlphaMode.None;
        if (loose) return new Made(img, hasAlpha, false);
        if (BlockCompress.Compress(img, hasAlpha) is not { } packed) return new Made(img, hasAlpha, false);
        img.Dispose();
        return new Made(packed, hasAlpha, true);
    }

    static Image? Tile(string path)
    {
        var tile = Image.LoadFromFile(path);
        if (tile == null || tile.IsEmpty()) { tile?.Dispose(); return null; }
        if (tile.IsCompressed()) tile.Decompress();
        if (tile.GetFormat() != Image.Format.Rgba8) tile.Convert(Image.Format.Rgba8);
        return tile;
    }

    static void StretchEdges(Image img, Rect2I filled)
    {
        int fullW = img.GetWidth(), fullH = img.GetHeight();
        Repeat(img, new Rect2I(filled.Position.X, filled.Position.Y, 1, filled.Size.Y), 0, filled.Position.X, true);
        Repeat(img, new Rect2I(filled.End.X - 1, filled.Position.Y, 1, filled.Size.Y), filled.End.X, fullW, true);
        Repeat(img, new Rect2I(0, filled.Position.Y, fullW, 1), 0, filled.Position.Y, false);
        Repeat(img, new Rect2I(0, filled.End.Y - 1, fullW, 1), filled.End.Y, fullH, false);
    }

    static void Repeat(Image img, Rect2I line, int from, int to, bool across)
    {
        if (from >= to) return;
        using var copy = img.GetRegion(line);
        var whole = new Rect2I(Vector2I.Zero, line.Size);
        for (int at = from; at < to; at++) img.BlitRect(copy, whole, across ? new Vector2I(at, line.Position.Y) : new Vector2I(line.Position.X, at));
    }

    void Arrived(int level, Stitched? made, double ms)
    {
        _building = -1;
        if (_dropped || made == null)
        {
            made?.Dispose();
            if (!_dropped) Target = Math.Max(Level, 0);
            return;
        }
        var texture = new Texture2DArray();
        var error = texture.CreateFromImages(new Godot.Collections.Array<Image>(made.Imgs));
        made.Dispose();
        if (error != Error.Ok)
        {
            GD.PrintErr($"[sheet] uploading a {made.Parts.Count}-part map picture: {error}");
            Target = Math.Max(Level, 0);
            return;
        }
        ComposeMs += ms;
        Composed++;
        if (!made.Packed) Packs = false;
        HasAlpha = made.Alpha;
        _bytesPerPixel = !made.Packed ? LooseBytesPerPixel : made.Alpha ? PackedAlphaBytesPerPixel : PackedBytesPerPixel;
        Texture = texture;
        Shape = made.Parts;
        Level = level;
        Changed?.Invoke();
        Next();
    }
}
