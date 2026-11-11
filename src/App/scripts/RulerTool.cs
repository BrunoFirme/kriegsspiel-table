using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RulerTool
{
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    public RulerTool(TableContext c, ToolOverlay overlay) { _c = c; _overlay = overlay; }

    public Vec2? A, B;
    const double MinRise = 0.01;

    public void Press(Vec2? g) { A = g; B = g; }
    public void Clear() => A = B = null;

    public double? Measured => A != null && B != null && (B.Value - A.Value).Length > 0.01 ? (B.Value - A.Value).Length : null;

    public static double NiceStep(double span)
    {
        if (span <= 0 || double.IsNaN(span)) return 1;
        double raw = span / 6;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag;
    }

    public void Draw()
    {
        if (A is { } ra && B is { } rb) Draw(ra, rb);
    }

    void Draw(Vec2 a, Vec2 b)
    {
        var overlay = _overlay;
        bool cal = _c.Tool == Tool.Calibrate;
        var colour = cal ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.42f, 0.28f);
        var mat = new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        double len = (b - a).Length;
        var dir = len > 1e-9 ? new Vec2((b.X - a.X) / len, (b.Z - a.Z) / len) : new Vec2(1, 0);
        var perp = new Vec2(-dir.Z, dir.X);
        const float y = 0.3f;
        void Seg(Vec2 p, Vec2 q) => overlay.SegPx(overlay.Mesh, mat, p, q, y);
        Vec2 At(Vec2 p, Vec2 along, double k) => new(p.X + along.X * k, p.Z + along.Z * k);
        Seg(a, b);
        float cap = overlay.Px(7);
        Seg(At(a, perp, cap), At(a, perp, -cap));
        Seg(At(b, perp, cap), At(b, perp, -cap));
        var c = _c.S.View.Calibration;
        double shown = cal ? len : c.FromTableUnits(len);
        double step = NiceStep(shown);
        double stepUnits = cal ? step : c.ToTableUnits(step);
        if (stepUnits > 1e-6 && len / stepUnits < 400)
            for (int i = 1; i * stepUnits < len; i++)
            {
                float t = overlay.Px(i % 5 == 0 ? 9 : 4.5f);
                var p = At(a, dir, i * stepUnits);
                Seg(At(p, perp, t), At(p, perp, -t));
            }
        var mid = new Vec2((a.X + b.X) / 2, (a.Z + b.Z) / 2);
        var ground = _c.S.Ground;
        _c.Labels.Set("ruler", Coords.ToWorld(mid, y + (float)ground.At(mid)), cal ? $"{len:0.00} u" : Fmt.Distance(len, c),
            cal ? new Color(0.6f, 0.95f, 1f) : new Color(1f, 0.95f, 0.85f), 14, new Vector2(0, -16), centered: true);
        if (!cal && len > 1e-6)
        {
            double bearing = c.CompassBearing(Mathf.RadToDeg((float)Math.Atan2(b.X - a.X, b.Z - a.Z)));
            double rise = ground.At(b) - ground.At(a);
            var heading = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:000}°", bearing);
            if (Math.Abs(rise) >= MinRise) heading += $" · {(rise > 0 ? "up" : "down")} {Fmt.Distance(Math.Abs(rise), c)}";
            _c.Labels.Set("ruler-bearing", Coords.ToWorld(b, y + (float)ground.At(b)), heading,
                new Color(1f, 0.82f, 0.6f), 12, new Vector2(14, -14), centered: false);
        }
    }
}
