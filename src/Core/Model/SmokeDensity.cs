namespace KTable.Core.Model;

public static class SmokeDensity
{
    public const double VeryFine = -2, Fine = -1, Standard = 0, Dense = 1, VeryDense = 2;
    public static readonly double[] Notches = { VeryFine, Fine, Standard, Dense, VeryDense };
    public const double DenseWind = 1.4, DensePowder = 1.5, DenseCannon = 1.4;
    public const string Range = "from -2 (very fine) through -1 (fine), 0 (standard) and 1 (dense) to 2 (very dense)";

    public static double DenseAmount(string kind) => kind == "cannon" ? DenseCannon : DensePowder;

    public static bool Known(double? value) => value is null or >= VeryFine and <= VeryDense;

    const string StandardEffects = "std.effects.", RetiredDense = "-dense-smoke";

    public static (string Def, double? Smoke) Retired(string def) =>
        def.StartsWith(StandardEffects, StringComparison.Ordinal) && def.EndsWith(RetiredDense, StringComparison.Ordinal) ? (def[..^RetiredDense.Length], Dense) : (def, null);
}
