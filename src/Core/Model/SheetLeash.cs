using System;
using System.Collections.Generic;

namespace KTable.Core.Model;

public readonly record struct SheetLeash(Vec2 Centre, double Reach)
{
    public const double ReachSlack = 4, ViewSlack = 2, ShortestReach = 10;
    const double SmallestArea = 1e-6;

    public static SheetLeash? Of(IReadOnlyCollection<SheetDef> sheets)
    {
        if (sheets.Count == 0) return null;
        double weight = 0, x = 0, z = 0;
        foreach (var s in sheets)
        {
            double area = Math.Max(SmallestArea, s.Size.W * s.Size.H);
            weight += area;
            x += s.Pos.X * area;
            z += s.Pos.Z * area;
        }
        var centre = new Vec2(x / weight, z / weight);
        double reach = ShortestReach;
        foreach (var s in sheets)
            foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                reach = Math.Max(reach, (s.Pos + SheetGeometry.Offset(dx * s.Size.W / 2, dz * s.Size.H / 2, s.RotDeg) - centre).Length);
        return new SheetLeash(centre, reach);
    }

    public double Allowed(double viewDistance) => Reach * ReachSlack + Math.Max(0, viewDistance) * ViewSlack;

    public Vec2 Hold(Vec2 from, Vec2 to, double viewDistance)
    {
        var away = to - Centre;
        double far = away.Length, limit = Math.Max(Allowed(viewDistance), (from - Centre).Length);
        if (far <= limit) return to;
        return Centre + new Vec2(away.X * limit / far, away.Z * limit / far);
    }
}
