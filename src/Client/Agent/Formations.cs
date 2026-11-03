using KTable.Core.Model;

namespace KTable.Client.Agent;

public static class Formations
{
    public static readonly string[] Names = { "line", "column", "two-lines", "grid", "wedge" };

    public static List<Vec2> Positions(int count, string formation, Vec2 centre, double facingDeg, double across, double deep)
    {
        var local = new List<(double x, double z)>();
        switch (formation)
        {
            case "column":
                for (int i = 0; i < count; i++) local.Add((0, (i - (count - 1) / 2.0) * deep));
                break;
            case "two-lines":
            {
                int front = (count + 1) / 2, back = count - front;
                for (int i = 0; i < front; i++) local.Add(((i - (front - 1) / 2.0) * across, -deep));
                for (int i = 0; i < back; i++) local.Add(((i - (back - 1) / 2.0) * across + across / 2 * (front > back ? 0 : 1), deep));
                break;
            }
            case "grid":
            {
                int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
                int rows = (count + cols - 1) / cols;
                for (int i = 0; i < count; i++) local.Add(((i % cols - (cols - 1) / 2.0) * across, (i / cols - (rows - 1) / 2.0) * deep));
                break;
            }
            case "wedge":
                for (int i = 0; i < count; i++)
                {
                    int rank = (i + 1) / 2, side = i == 0 ? 0 : (i % 2 == 1 ? -1 : 1);
                    local.Add((side * rank * across, rank * deep));
                }
                break;
            default:
                for (int i = 0; i < count; i++) local.Add(((i - (count - 1) / 2.0) * across, 0));
                break;
        }
        double a = facingDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return local.Select(p => new Vec2(centre.X + p.x * c - p.z * s, centre.Z + p.x * s + p.z * c)).ToList();
    }
}
