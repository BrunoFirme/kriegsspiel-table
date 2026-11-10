using KTable.Core.Model;

namespace KTable.Client;

public static class MarkerPlace
{
    static Vec2 Turn(Vec2 v, double deg)
    {
        double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return new Vec2(v.X * c + v.Z * s, -v.X * s + v.Z * c);
    }
    public static Vec2 World(Vec2 basePos, double baseRot, Vec2 offset) => basePos + Turn(offset, baseRot - 180);
    public static Vec2 Offset(Vec2 basePos, double baseRot, Vec2 markerPos) => Turn(markerPos - basePos, -(baseRot - 180));
}
