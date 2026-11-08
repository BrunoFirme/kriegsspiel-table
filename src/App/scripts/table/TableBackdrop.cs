using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class TableBackdrop
{
    public static readonly Color Plain = new(0.16f, 0.17f, 0.18f);
    static readonly float NeutralAmbient = (float)new Lighting().Ambient;
    const float DimmestSky = 0.2f, BrightestSky = 1.3f;
    const int ToneAcross = 16, ToneDown = 8;
    const float GroundShare = 1f / 3;

    readonly TableContext _c;
    readonly Godot.Environment _env;
    readonly ShaderMaterial _sky = new() { Shader = new Shader { Code = Godot.FileAccess.GetFileAsString("res://shaders/backdrop.gdshader") } };
    string _wanted = Backdrops.None;
    int _generation;
    Color _colour = Plain;
    Color? _tone;
    float _energy = 1;
    bool _flat;

    public TableBackdrop(TableContext c, Godot.Environment env)
    {
        _c = c;
        _env = env;
        _env.Sky = new Sky { SkyMaterial = _sky };
        ShowColour(Plain);
    }

    public static string BuiltInPath(string file) => ProjectSettings.GlobalizePath("res://backdrops/" + file);

    public static Color ColourOf(string backdrop) => Backdrops.IsColour(backdrop) ? Color.FromHtml(backdrop) : Plain;

    public void ShowFlat(bool flat)
    {
        if (flat == _flat) return;
        _flat = flat;
        Paint();
    }

    public void Show(string backdrop, float ambient)
    {
        _energy = Mathf.Clamp(ambient / NeutralAmbient, DimmestSky, BrightestSky);
        _sky.SetShaderParameter("energy", _energy);
        if (backdrop == _wanted) { Paint(); return; }
        _wanted = backdrop;
        int generation = ++_generation;
        if (Backdrops.Find(backdrop) is { } built) { Load(generation, BuiltInPath(built.File)); return; }
        if (Backdrops.IsPicture(backdrop) && _c.S?.Assets is { } assets)
        {
            assets.EnsureFile(UploadAsset.ReliefPackage, backdrop,
                () => Load(generation, assets.Abs(UploadAsset.ReliefPackage, backdrop)),
                () => { if (generation == _generation) ShowColour(Plain); });
            return;
        }
        ShowColour(ColourOf(backdrop));
    }

    void ShowColour(Color colour)
    {
        _colour = colour;
        _tone = null;
        _sky.SetShaderParameter("panorama", default(Variant));
        Paint();
    }

    void Paint()
    {
        if (_tone is { } tone && _flat)
        {
            _env.BackgroundMode = Godot.Environment.BGMode.Color;
            _env.BackgroundColor = new Color(tone.R * _energy, tone.G * _energy, tone.B * _energy);
            return;
        }
        if (_tone != null) { _env.BackgroundMode = Godot.Environment.BGMode.Sky; return; }
        _env.BackgroundMode = Godot.Environment.BGMode.Color;
        _env.BackgroundColor = _colour;
    }

    void Load(int generation, string path)
    {
        Task.Run(() =>
        {
            Packed? packed = null;
            try { packed = Pack(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { GD.PrintErr("[backdrop] could not read " + path + ": " + e.Message); }
            if (Assets.Quitting) { packed?.Img.Dispose(); return; }
            Callable.From(() => Arrived(generation, packed, path)).CallDeferred();
        });
    }

    readonly record struct Packed(Image Img, Color Tone);

    static Packed? Pack(string path)
    {
        if (!File.Exists(path)) return null;
        using var img = Image.LoadFromFile(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        var tone = GroundTone(img);
        img.GenerateMipmaps();
        return BlockCompress.Compress(img, false) is { } packed ? new Packed(packed, tone) : null;
    }

    static Color GroundTone(Image panorama)
    {
        using var small = (Image)panorama.Duplicate();
        small.Resize(ToneAcross, ToneDown, Image.Interpolation.Lanczos);
        int from = Math.Clamp((int)(ToneDown * (1 - GroundShare)), 0, ToneDown - 1);
        float r = 0, g = 0, b = 0;
        int taken = 0;
        for (int y = from; y < ToneDown; y++)
            for (int x = 0; x < ToneAcross; x++)
            {
                var lit = small.GetPixel(x, y).SrgbToLinear();
                r += lit.R;
                g += lit.G;
                b += lit.B;
                taken++;
            }
        return new Color(r / taken, g / taken, b / taken).LinearToSrgb();
    }

    void Arrived(int generation, Packed? made, string path)
    {
        if (generation != _generation) { made?.Img.Dispose(); return; }
        if (made is not { } got)
        {
            GD.PrintErr("[backdrop] no picture in " + path);
            ShowColour(Plain);
            return;
        }
        _sky.SetShaderParameter("panorama", ImageTexture.CreateFromImage(got.Img));
        got.Img.Dispose();
        _tone = got.Tone;
        Paint();
    }
}
