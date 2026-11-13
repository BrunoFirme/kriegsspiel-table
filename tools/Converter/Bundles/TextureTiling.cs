using AssetsTools.NET;

namespace KTable.Converter.Bundles;

public enum Wrap { Repeat, Clamp, Mirror }

public sealed record TextureTiling(double ScaleU, double ScaleV, double OffsetU, double OffsetV, Wrap WrapU, Wrap WrapV)
{
    public static readonly TextureTiling Plain = new(1, 1, 0, 0, Wrap.Repeat, Wrap.Repeat);

    const int UnityRepeat = 0, UnityClamp = 1, UnityMirror = 2, UnityMirrorOnce = 3;

    public static TextureTiling Of(AssetTypeValueField materialEntry, AssetTypeValueField texture)
    {
        var scale = materialEntry["m_Scale"];
        var offset = materialEntry["m_Offset"];
        var settings = texture["m_TextureSettings"];
        var both = settings["m_WrapMode"];
        Wrap Axis(string name) => WrapOf(settings[name] is { IsDummy: false } own ? own.AsInt : both.IsDummy ? UnityRepeat : both.AsInt);
        return new TextureTiling(
            scale.IsDummy ? 1 : scale["x"].AsDouble, scale.IsDummy ? 1 : scale["y"].AsDouble,
            offset.IsDummy ? 0 : offset["x"].AsDouble, offset.IsDummy ? 0 : offset["y"].AsDouble,
            Axis("m_WrapU"), Axis("m_WrapV"));
    }

    static Wrap WrapOf(int unity) => unity switch
    {
        UnityClamp or UnityMirrorOnce => Wrap.Clamp,
        UnityMirror => Wrap.Mirror,
        _ => Wrap.Repeat,
    };

    public double U(double raw) => raw * ScaleU + OffsetU;

    public double V(double raw) => raw * ScaleV + OffsetV;
}
