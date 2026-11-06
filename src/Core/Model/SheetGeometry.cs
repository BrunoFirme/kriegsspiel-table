using System;

namespace KTable.Core.Model;

public static class SheetGeometry
{
    public static readonly FaceBox Whole = new() { X = 0, Y = 0, W = 1, H = 1 };

    public static Vec2 Offset(double right, double down, double rotDeg)
    {
        double a = (180 - rotDeg) * Math.PI / 180;
        double wx = right * Math.Cos(a) + down * Math.Sin(a);
        double wz = -right * Math.Sin(a) + down * Math.Cos(a);
        return new Vec2(wx, -wz);
    }

    public static (Vec2 centre, double w, double h) Full(SheetDef s)
    {
        var c = s.Crop ?? Whole;
        double w = s.Size.W / Math.Max(1e-6, c.W), h = s.Size.H / Math.Max(1e-6, c.H);
        var off = Offset((c.X + c.W / 2 - 0.5) * w, (c.Y + c.H / 2 - 0.5) * h, s.RotDeg);
        return (s.Pos - off, w, h);
    }

    public static (Vec2 pos, Size2 size) Cropped(Vec2 fullCentre, double w, double h, double rotDeg, FaceBox? crop)
    {
        var c = crop ?? Whole;
        var off = Offset((c.X + c.W / 2 - 0.5) * w, (c.Y + c.H / 2 - 0.5) * h, rotDeg);
        return (fullCentre + off, new Size2(w * c.W, h * c.H));
    }

    public static bool Valid(FaceBox c) => c.W > 0.01 && c.H > 0.01 && c.X >= 0 && c.Y >= 0 && c.X + c.W <= 1.0001 && c.Y + c.H <= 1.0001;
}
