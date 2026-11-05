namespace KTable.Converter.Bundles;

public sealed class Mat4
{
    const int Side = 4;

    readonly double[] _v;

    Mat4(double[] v) => _v = v;

    public static Mat4 Rows(double[] sixteen) => new(sixteen);

    public static Mat4 Identity() => Rows(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 });

    public double At(int row, int col) => _v[row * Side + col];

    public (double X, double Y, double Z) Translation => (At(0, 3), At(1, 3), At(2, 3));

    public static Mat4 Trs((double X, double Y, double Z, double W) q, (double X, double Y, double Z) pos, (double X, double Y, double Z) scale)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        double[] r =
        {
            1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 0,
            2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), 0,
            2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), 0,
            0, 0, 0, 1,
        };
        var s = new[] { scale.X, scale.Y, scale.Z };
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                r[row * Side + col] *= s[col];
        r[3] = pos.X;
        r[Side + 3] = pos.Y;
        r[2 * Side + 3] = pos.Z;
        return Rows(r);
    }

    public Mat4 Times(Mat4 b)
    {
        var r = new double[Side * Side];
        for (int row = 0; row < Side; row++)
            for (int col = 0; col < Side; col++)
            {
                double sum = 0;
                for (int k = 0; k < Side; k++) sum += At(row, k) * b.At(k, col);
                r[row * Side + col] = sum;
            }
        return Rows(r);
    }

    public (double X, double Y, double Z) Apply(double x, double y, double z) =>
        (At(0, 0) * x + At(0, 1) * y + At(0, 2) * z + At(0, 3),
         At(1, 0) * x + At(1, 1) * y + At(1, 2) * z + At(1, 3),
         At(2, 0) * x + At(2, 1) * y + At(2, 2) * z + At(2, 3));
}
