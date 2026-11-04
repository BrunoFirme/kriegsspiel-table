using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class TableBackdrop
{
    public static readonly Color Plain = new(0.16f, 0.17f, 0.18f);
    const float NeutralAmbient = 0.72f, DimmestSky = 0.2f, BrightestSky = 1.3f;

    readonly TableContext _c;
    readonly Godot.Environment _env;
    readonly PanoramaSkyMaterial _sky = new() { Filter = true };
    string _wanted = Backdrops.None;
    int _generation;

    public TableBackdrop(TableContext c, Godot.Environment env)
    {
        _c = c; _env = env;
        _env.Sky = new Sky { SkyMaterial = _sky };
        ShowPlain();
    }

    public static string BuiltInPath(string id) => ProjectSettings.GlobalizePath("res://backdrops/" + id + Backdrops.FileExtension);

    public void Show(string backdrop, float ambient)
    {
        _sky.EnergyMultiplier = Mathf.Clamp(ambient / NeutralAmbient, DimmestSky, BrightestSky);
        if (backdrop == _wanted) return;
        _wanted = backdrop;
        int generation = ++_generation;
        if (Backdrops.Find(backdrop) is { } built) { Load(generation, BuiltInPath(built.Id)); return; }
        if (Backdrops.IsPicture(backdrop) && _c.S?.Assets is { } assets)
        {
            assets.EnsureFile(UploadAsset.ReliefPackage, backdrop,
                () => Load(generation, assets.Abs(UploadAsset.ReliefPackage, backdrop)),
                () => { if (generation == _generation) ShowPlain(); });
            return;
        }
        ShowPlain();
    }

    void ShowPlain()
    {
        _env.BackgroundMode = Godot.Environment.BGMode.Color;
        _env.BackgroundColor = Plain;
        _sky.Panorama = null;
    }

    void Load(int generation, string path)
    {
        Task.Run(() =>
        {
            Image? packed = null;
            try { packed = Packed(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { GD.PrintErr("[backdrop] could not read " + path + ": " + e.Message); }
            if (Assets.Quitting) { packed?.Dispose(); return; }
            Callable.From(() => Arrived(generation, packed, path)).CallDeferred();
        });
    }

    static Image? Packed(string path)
    {
        if (!File.Exists(path)) return null;
        using var img = Image.LoadFromFile(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        img.GenerateMipmaps();
        return BlockCompress.Compress(img, false);
    }

    void Arrived(int generation, Image? packed, string path)
    {
        if (generation != _generation) { packed?.Dispose(); return; }
        if (packed == null) { GD.PrintErr("[backdrop] no picture in " + path); ShowPlain(); return; }
        _sky.Panorama = ImageTexture.CreateFromImage(packed);
        packed.Dispose();
        _env.BackgroundMode = Godot.Environment.BGMode.Sky;
    }
}
