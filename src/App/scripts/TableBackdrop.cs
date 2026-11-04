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
    readonly ShaderMaterial _sky = new() { Shader = new Shader { Code = Godot.FileAccess.GetFileAsString("res://shaders/backdrop.gdshader") } };
    string _wanted = Backdrops.None;
    int _generation;

    public TableBackdrop(TableContext c, Godot.Environment env)
    {
        _c = c; _env = env;
        _env.Sky = new Sky { SkyMaterial = _sky };
        ShowColour(Plain);
    }

    public static string BuiltInPath(string file) => ProjectSettings.GlobalizePath("res://backdrops/" + file);

    public static Color ColourOf(string backdrop) => Backdrops.IsColour(backdrop) ? Color.FromHtml(backdrop) : Plain;

    public void Show(string backdrop, float ambient)
    {
        _sky.SetShaderParameter("energy", Mathf.Clamp(ambient / NeutralAmbient, DimmestSky, BrightestSky));
        if (backdrop == _wanted) return;
        _wanted = backdrop;
        int generation = ++_generation;
        if (Backdrops.Find(backdrop) is { } built)
        {
            Load(generation, BuiltInPath(built.File), built.Ground is { } ground ? BuiltInPath(ground) : null);
            return;
        }
        if (Backdrops.IsPicture(backdrop) && _c.S?.Assets is { } assets)
        {
            assets.EnsureFile(UploadAsset.ReliefPackage, backdrop,
                () => Load(generation, assets.Abs(UploadAsset.ReliefPackage, backdrop), null),
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
        _sky.SetShaderParameter("ground", default(Variant));
    }

    sealed record Packed(Image Sky, Image? Ground);

    void Load(int generation, string path, string? groundPath)
    {
        Task.Run(() =>
        {
            Packed? packed = null;
            try
            {
                var sky = Pack(path);
                var ground = sky != null && groundPath != null ? Pack(groundPath) : null;
                if (sky != null && (groundPath == null || ground != null)) packed = new Packed(sky, ground);
                else { sky?.Dispose(); ground?.Dispose(); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { GD.PrintErr("[backdrop] could not read " + path + ": " + e.Message); }
            if (Assets.Quitting) { packed?.Sky.Dispose(); packed?.Ground?.Dispose(); return; }
            Callable.From(() => Arrived(generation, packed, path)).CallDeferred();
        });
    }

    static Image? Pack(string path)
    {
        if (!File.Exists(path)) return null;
        using var img = Image.LoadFromFile(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        img.GenerateMipmaps();
        return BlockCompress.Compress(img, false);
    }

    void Arrived(int generation, Packed? packed, string path)
    {
        if (generation != _generation) { packed?.Sky.Dispose(); packed?.Ground?.Dispose(); return; }
        if (packed == null) { GD.PrintErr("[backdrop] no picture in " + path); ShowColour(Plain); return; }
        _sky.SetShaderParameter("panorama", ImageTexture.CreateFromImage(packed.Sky));
        _sky.SetShaderParameter("ground", packed.Ground == null ? default(Variant) : ImageTexture.CreateFromImage(packed.Ground));
        _sky.SetShaderParameter("halves", packed.Ground != null);
        packed.Sky.Dispose();
        packed.Ground?.Dispose();
        _env.BackgroundMode = Godot.Environment.BGMode.Sky;
    }
}
