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
    const int HorizonRows = 8;

    readonly TableContext _c;
    readonly Godot.Environment _env;
    readonly ShaderMaterial _sky = new() { Shader = new Shader { Code = Godot.FileAccess.GetFileAsString("res://shaders/backdrop.gdshader") } };
    string _wanted = Backdrops.None;
    int _generation;

    public TableBackdrop(TableContext c, Godot.Environment env)
    {
        _c = c; _env = env;
        _env.Sky = new Sky { SkyMaterial = _sky };
        ShowColour(Plain);
    }

    public static string BuiltInPath(Backdrop b) => ProjectSettings.GlobalizePath("res://backdrops/" + b.File);

    public static Color ColourOf(string backdrop) => Backdrops.IsColour(backdrop) ? Color.FromHtml(backdrop) : Plain;

    public void Show(string backdrop, float ambient)
    {
        _sky.SetShaderParameter("energy", Mathf.Clamp(ambient / NeutralAmbient, DimmestSky, BrightestSky));
        if (backdrop == _wanted) return;
        _wanted = backdrop;
        int generation = ++_generation;
        if (Backdrops.Find(backdrop) is { } built) { Load(generation, BuiltInPath(built), built.SkyOnly, built.Mirrored); return; }
        if (Backdrops.IsPicture(backdrop) && _c.S?.Assets is { } assets)
        {
            assets.EnsureFile(UploadAsset.ReliefPackage, backdrop,
                () => Load(generation, assets.Abs(UploadAsset.ReliefPackage, backdrop), false, false),
                () => { if (generation == _generation) ShowColour(Plain); });
            return;
        }
        ShowColour(ColourOf(backdrop));
    }

    void ShowColour(Color colour)
    {
        _env.BackgroundMode = Godot.Environment.BGMode.Color;
        _env.BackgroundColor = colour;
        _sky.SetShaderParameter("panorama", default(Variant));
    }

    sealed record Packed(Image Picture, Color Below);

    void Load(int generation, string path, bool skyOnly, bool mirrored)
    {
        Task.Run(() =>
        {
            Packed? packed = null;
            try { packed = Pack(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { GD.PrintErr("[backdrop] could not read " + path + ": " + e.Message); }
            if (Assets.Quitting) { packed?.Picture.Dispose(); return; }
            Callable.From(() => Arrived(generation, packed, path, skyOnly, mirrored)).CallDeferred();
        });
    }

    static Packed? Pack(string path)
    {
        if (!File.Exists(path)) return null;
        using var img = Image.LoadFromFile(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        var below = BottomColour(img);
        img.GenerateMipmaps();
        return BlockCompress.Compress(img, false) is { } dxt ? new Packed(dxt, below) : null;
    }

    static Color BottomColour(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight(), step = Math.Max(1, w / 256);
        float r = 0, g = 0, b = 0; int n = 0;
        for (int y = Math.Max(0, h - HorizonRows); y < h; y++)
            for (int x = 0; x < w; x += step) { var c = img.GetPixel(x, y); r += c.R; g += c.G; b += c.B; n++; }
        return n == 0 ? Plain : new Color(r / n, g / n, b / n);
    }

    void Arrived(int generation, Packed? packed, string path, bool skyOnly, bool mirrored)
    {
        if (generation != _generation) { packed?.Picture.Dispose(); return; }
        if (packed == null) { GD.PrintErr("[backdrop] no picture in " + path); ShowColour(Plain); return; }
        _sky.SetShaderParameter("panorama", ImageTexture.CreateFromImage(packed.Picture));
        _sky.SetShaderParameter("sky_only", skyOnly);
        _sky.SetShaderParameter("mirrored", mirrored);
        _sky.SetShaderParameter("below", packed.Below);
        packed.Picture.Dispose();
        _env.BackgroundMode = Godot.Environment.BGMode.Sky;
    }
}
