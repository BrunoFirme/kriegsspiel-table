using System;
using System.IO;
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
    double _bytesPerPixel;

    readonly Assets _assets;
    readonly string _package, _tilesRel;
    public TileIndex Index { get; }
    public int Level { get; private set; } = -1;
    public int Target { get; private set; }
    public Texture2D? Texture { get; private set; }
    public bool HasAlpha { get; private set; }
    public int TilesLoaded { get; private set; }
    public int TilesTotal { get; private set; }
    public event Action? Changed;
    int _building = -1;
    bool _dropped;

    public SheetPicture(Assets assets, string package, string tilesRel, TileIndex index)
    {
        _assets = assets; _package = package; _tilesRel = tilesRel; Index = index;
        Target = Coarsest;
    }

    public int Coarsest => Index.Levels.Count - 1;
    public bool Done => Level == Target;
    public long Bytes => Level < 0 ? 0 : Size(Index.Levels[Level], _bytesPerPixel);

    public static long BytesAt(TileIndex index, int level, bool alpha) =>
        Size(index.Levels[Math.Clamp(level, 0, index.Levels.Count - 1)], !Packs ? LooseBytesPerPixel : alpha ? PackedAlphaBytesPerPixel : PackedBytesPerPixel);

    static long Size(TileLevel lv, double bytesPerPixel) => (long)((double)lv.W * lv.H * bytesPerPixel * MipmapsShare);

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
        TilesTotal = waiting; TilesLoaded = 0;
        void One(bool there)
        {
            if (there) TilesLoaded++; else missing++;
            if (--waiting > 0) return;
            if (missing > 0) { _building = -1; Target = Math.Max(Level, 0); return; }
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
            try { if (!Assets.Quitting && !_dropped) made = Stitch(lv, ts, paths); }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { made = null; }
            finally { Slots.Release(); }
            double ms = clock.Elapsed.TotalMilliseconds;
            if (Assets.Quitting) made?.Img.Dispose();
            else Callable.From(() => Arrived(level, made, ms)).CallDeferred();
        });
    }

    readonly record struct Stitched(Image Img, bool Alpha, bool Packed);

    static Stitched? Stitch(TileLevel lv, int ts, string[] paths)
    {
        var img = Image.CreateEmpty(lv.W, lv.H, false, Image.Format.Rgba8);
        for (int y = 0; y < lv.Rows; y++)
            for (int x = 0; x < lv.Cols; x++)
            {
                using var tile = Image.LoadFromFile(paths[y * lv.Cols + x]);
                if (tile == null || tile.IsEmpty()) return null;
                if (tile.IsCompressed()) tile.Decompress();
                if (tile.GetFormat() != Image.Format.Rgba8) tile.Convert(Image.Format.Rgba8);
                img.BlitRect(tile, new Rect2I(0, 0, tile.GetWidth(), tile.GetHeight()), new Vector2I(x * ts, y * ts));
            }
        img.GenerateMipmaps();
        bool alpha = img.DetectAlpha() != Image.AlphaMode.None;
        var packed = BlockCompress.Compress(img, alpha);
        if (packed == null) return new Stitched(img, alpha, false);
        img.Dispose();
        return new Stitched(packed, alpha, true);
    }

    void Arrived(int level, Stitched? made, double ms)
    {
        _building = -1;
        if (_dropped || made is not { } got)
        {
            made?.Img.Dispose();
            if (!_dropped) Target = Math.Max(Level, 0);
            return;
        }
        ComposeMs += ms; Composed++;
        if (!got.Packed) Packs = false;
        HasAlpha = got.Alpha;
        _bytesPerPixel = !got.Packed ? LooseBytesPerPixel : got.Alpha ? PackedAlphaBytesPerPixel : PackedBytesPerPixel;
        Texture = ImageTexture.CreateFromImage(got.Img);
        got.Img.Dispose();
        Level = level;
        Changed?.Invoke();
        Next();
    }
}
