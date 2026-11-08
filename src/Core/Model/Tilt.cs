namespace KTable.Core.Model;

public static class Tilt
{
    const double HalfTurn = 180, Turn = 360, Digits = 3, Noise = 0.05;

    public static double Signed(double deg)
    {
        double a = deg % Turn;
        if (a > HalfTurn) a -= Turn;
        if (a <= -HalfTurn) a += Turn;
        a = Math.Round(a, (int)Digits);
        return Math.Abs(a) < Noise ? 0 : a;
    }
}
