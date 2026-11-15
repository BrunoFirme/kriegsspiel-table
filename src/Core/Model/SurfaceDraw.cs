using System.Globalization;

namespace KTable.Core.Model;

public enum SurfaceAlpha { Opaque, Cut, Blend }

public sealed record SurfaceDraw(bool OneSided, SurfaceAlpha Alpha, double Cutoff, bool Metal = false)
{
    public const double HalfCut = 0.5;
    const string Cull = "cull", Back = "back", BothSides = "none", AlphaWord = "alpha", Opaque = "opaque", Cut = "cut", Blend = "blend", FinishWord = "finish";

    public static readonly SurfaceDraw Plain = new(false, SurfaceAlpha.Cut, HalfCut);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string MetalStatement => $"{FinishWord} {Finishes.Metal}\n";

    public string Statements() => $"{Cull} {(OneSided ? Back : BothSides)}\n{AlphaWord} {Words()}\n" + (Metal ? MetalStatement : "");

    string Words() => Alpha switch
    {
        SurfaceAlpha.Opaque => Opaque,
        SurfaceAlpha.Blend => Blend,
        _ => $"{Cut} {Cutoff.ToString("0.####", Inv)}",
    };

    public string Name => $"{(OneSided ? Back : BothSides)} {Words()}" + (Metal ? " " + Finishes.Metal : "");

    public static bool Says(string[] p) => p.Length >= 2 && p[0] is Cull or AlphaWord or FinishWord;

    public SurfaceDraw After(string[] p) => p[0] switch
    {
        Cull => this with { OneSided = p[1] == Back },
        AlphaWord => p[1] switch
        {
            Opaque => this with { Alpha = SurfaceAlpha.Opaque },
            Blend => this with { Alpha = SurfaceAlpha.Blend },
            _ => this with { Alpha = SurfaceAlpha.Cut, Cutoff = p.Length > 2 && double.TryParse(p[2], NumberStyles.Float, Inv, out var c) ? c : HalfCut },
        },
        FinishWord => this with { Metal = p[1] == Finishes.Metal },
        _ => this,
    };

    public static SurfaceDraw Named(string name)
    {
        var p = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool metal = p.Length > 0 && p[^1] == Finishes.Metal;
        if (metal) p = p[..^1];
        if (p.Length < 2) return Plain with { Metal = metal };
        return Plain.After([Cull, p[0]]).After(new[] { AlphaWord }.Concat(p.Skip(1)).ToArray()) with { Metal = metal };
    }
}
