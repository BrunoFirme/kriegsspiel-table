using KTable.Core.Model;

namespace KTable.Client;

public sealed record SightLine(string Kind, List<Vec2> Points, double Blur = 0)
{
    public const string Block = Stroke.SightBlock, Blurs = Stroke.SightBlur, Shows = Stroke.SightView;
    public static readonly string[] Kinds = Stroke.SightKinds;

    public static string KindNamed(string? name) => Array.IndexOf(Kinds, name) >= 0 ? name! : Block;

    public static string InkOf(string kind) => kind switch { Blurs => "#59D166", Shows => "#599EFF", _ => "#F24D33" };
}

public readonly record struct SightRay(double Reach, double Open, List<(double From, double To)> Shown, List<(double At, double Blur)> Blurred);
