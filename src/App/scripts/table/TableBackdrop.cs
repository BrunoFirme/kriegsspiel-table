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
    static readonly StringName Energy = "energy", Pictured = "pictured", Reflected = "reflected", Kind = "kind", Drifting = "drift";
    static readonly StringName SunAt = "sun_at", MoonAt = "moon_at", Pole = "pole", Dark = "dark", Glow = "glow", Turn = "turn", Moonlight = "moonlight";
    static readonly Color DayTone = new(0.47f, 0.6f, 0.7f), DuskTone = new(0.5f, 0.32f, 0.36f), NightTone = new(0.07f, 0.11f, 0.22f);
    const float HalfTurn = 180;

    readonly TableContext _c;
    readonly Godot.Environment _env;
    readonly ShaderMaterial _sky = new() { Shader = new Shader { Code = Godot.FileAccess.GetFileAsString("res://shaders/backdrop.gdshader") } };
    readonly ShaderMaterial _painted = new() { Shader = new Shader { Code = Godot.FileAccess.GetFileAsString("res://shaders/painted_sky.gdshader") } };
    readonly Sky _dome;
    string _wanted = Backdrops.None;
    int _generation;
    Color _colour = Plain;
    Color? _tone;
    float _energy = 1, _reflected = 1;
    Backdrop? _paintedNow;
    bool _flat;

    public TableBackdrop(TableContext c, Godot.Environment env)
    {
        _c = c;
        _env = env;
        _env.Sky = _dome = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size64 };
        _env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        var seen = PieceFinish.Surroundings;
        _sky.SetShaderParameter("above", seen.SkyTopColor);
        _sky.SetShaderParameter("around", seen.SkyHorizonColor);
        _sky.SetShaderParameter("beside", seen.GroundHorizonColor);
        _sky.SetShaderParameter("below", seen.GroundBottomColor);
        _sky.SetShaderParameter("above_curve", seen.SkyCurve);
        _sky.SetShaderParameter("below_curve", seen.GroundCurve);
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

    public void Show(string backdrop, float ambient, SkyTurn turn, double north)
    {
        float energy = Mathf.Clamp(ambient / NeutralAmbient, DimmestSky, BrightestSky);
        if (!Mathf.IsEqualApprox(energy, _energy)) _sky.SetShaderParameter(Energy, _energy = energy);
        if (Backdrops.Find(backdrop) is { Painted: > 0 } painted)
        {
            if (backdrop != _wanted) { _wanted = backdrop; _generation++; }
            ShowPainted(painted, turn, north);
            return;
        }
        if (backdrop == _wanted) { Paint(); return; }
        _wanted = backdrop;
        int generation = ++_generation;
        _dome.SkyMaterial = _sky;
        _paintedNow = null;
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

    public void Drift(Vector2 drift) => _painted.SetShaderParameter(Drifting, drift);

    public void Reflect(float share)
    {
        if (Mathf.IsEqualApprox(share, _reflected)) return;
        _sky.SetShaderParameter(Reflected, _reflected = share);
        _painted.SetShaderParameter(Reflected, share);
    }

    static Vector3 Toward(double from, double height, double north) =>
        Basis.FromEuler(new Vector3(Mathf.DegToRad((float)-height), Mathf.DegToRad((float)(HalfTurn - from - north)), 0)).Z;

    void ShowPainted(Backdrop sky, SkyTurn turn, double north)
    {
        _painted.SetShaderParameter(Kind, sky.Painted);
        _painted.SetShaderParameter(SunAt, Toward(turn.SunFrom, turn.SunHeight, north));
        _painted.SetShaderParameter(MoonAt, Toward(turn.MoonFrom, turn.MoonHeight, north));
        _painted.SetShaderParameter(Pole, Toward(0, turn.Pole, north));
        _painted.SetShaderParameter(Dark, (float)turn.Dark);
        _painted.SetShaderParameter(Glow, (float)turn.Glow);
        _painted.SetShaderParameter(Moonlight, (float)turn.Moonlight);
        _painted.SetShaderParameter(Turn, Mathf.DegToRad((float)turn.Turn));
        _painted.SetShaderParameter(Reflected, _reflected);
        if (_paintedNow == null) _dome.SkyMaterial = _painted;
        _paintedNow = sky;
        _tone = DayTone.Lerp(NightTone, (float)turn.Dark).Lerp(DuskTone, (float)turn.Glow);
        Paint();
    }

    void ShowColour(Color colour)
    {
        _colour = colour;
        _tone = null;
        _sky.SetShaderParameter("panorama", default(Variant));
        _sky.SetShaderParameter(Pictured, false);
        Paint();
    }

    void Paint()
    {
        if (_tone is { } tone && _flat)
        {
            _env.BackgroundMode = Godot.Environment.BGMode.Color;
            float lit = _paintedNow != null ? 1 : _energy;
            _env.BackgroundColor = new Color(tone.R * lit, tone.G * lit, tone.B * lit);
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
        _sky.SetShaderParameter(Pictured, true);
        got.Img.Dispose();
        _tone = got.Tone;
        Paint();
    }
}
