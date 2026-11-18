using KTable.Core.Model;

namespace KTable.Client;

public sealed record SightLine(string Kind, List<Vec2> Points, double Blur = 0)
{
    public const string Block = "block", Blurs = "blur", Shows = "view";
    public static readonly string[] Kinds = [Block, Blurs, Shows];

    public static string KindNamed(string? name) => Array.IndexOf(Kinds, name) >= 0 ? name! : Block;
}

public readonly record struct SightRay(double Reach, double Open, List<(double From, double To)> Shown, List<(double At, double Blur)> Blurred);
