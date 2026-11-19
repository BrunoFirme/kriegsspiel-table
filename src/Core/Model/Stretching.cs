namespace KTable.Core.Model;

public static class Stretching
{
    const string Model = "model", Die = "die";

    static bool ShowsModel(PieceInstance p, PieceDef? def, PieceDef? worn)
    {
        if (worn is { Kind: Model }) return true;
        var state = def != null && p.State >= 0 && p.State < def.States.Count ? def.States[p.State] : null;
        return (state?.Kind ?? def?.Kind) == Model;
    }

    public static (double Wide, double Deep, double Tall) By(PieceInstance p, PieceDef? def, PieceDef? worn, double across, double up, double deep)
    {
        double wide = p.WideBy * across, far = p.DeepBy * deep;
        double tall = !ShowsModel(p, def, worn) ? p.TallBy * up
            : def?.HeightScale != null && !def.Tags.Contains(Die) ? p.TallBy * up / across
            : p.TallBy * up * (p.WideBy + p.DeepBy) / (wide + far);
        return (Fit(wide), Fit(far), Fit(tall));
    }

    static double Fit(double by) => Math.Clamp(by, PieceInstance.LeastStretch, PieceInstance.MostStretch);
}
