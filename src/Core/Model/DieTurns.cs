namespace KTable.Core.Model;

public static class DieTurns
{
    const double Level = 1e-9;

    static double Rad(double deg) => deg * Math.PI / 180;
    static double Deg(double rad) => rad * 180 / Math.PI;

    public static double[] UpOf(double rotX, double rotZ)
    {
        double cx = Math.Cos(Rad(rotX)), sx = Math.Sin(Rad(rotX)), cz = Math.Cos(Rad(rotZ)), sz = Math.Sin(Rad(rotZ));
        return [-cx * sz, cx * cz, -sx];
    }

    public static (double X, double Y, double Z) RotationOf(double[] up)
    {
        double len = Math.Sqrt(up[0] * up[0] + up[1] * up[1] + up[2] * up[2]);
        if (len < Level) return (0, 0, 0);
        double a = up[0] / len, b = up[1] / len, c = up[2] / len;
        double rx = Math.Asin(Math.Clamp(-c, -1, 1));
        double rz = Math.Abs(Math.Cos(rx)) < Level ? 0 : Math.Atan2(-a, b);
        return (Math.Round(Deg(rx), 4), 0, Math.Round(Deg(rz), 4));
    }

    public static int Nearest(IReadOnlyList<DieFace> faces, double[] up)
    {
        int best = 0;
        double most = double.NegativeInfinity;
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i].Up;
            double d = f[0] * up[0] + f[1] * up[1] + f[2] * up[2];
            if (d > most) { most = d; best = i; }
        }
        return best;
    }
}
