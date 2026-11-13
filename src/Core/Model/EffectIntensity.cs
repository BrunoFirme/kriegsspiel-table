namespace KTable.Core.Model;

public static class EffectIntensity
{
    public const double Least = 0.1, AsMade = 1, Most = 4;
    public const string Range = "from 0.1 through 1 (as its kind is made) to 4";

    public static bool Known(double? value) => value is null or >= Least and <= Most;
}
