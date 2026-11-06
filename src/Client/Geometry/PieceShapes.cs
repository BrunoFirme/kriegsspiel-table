using System.Numerics;

namespace KTable.Client.Geometry;

public static class PieceShapes
{
    public static List<Vector2> Outline(string shape, float w, float h)
    {
        var pts = new List<Vector2>();
        switch (shape)
        {
            case "circle":
                for (int i = 0; i < 48; i++) { var a = Mathf.Tau * i / 48; pts.Add(new Vector2(MathF.Cos(a) * w / 2, MathF.Sin(a) * h / 2)); }
                break;
            case "hex":
                for (int i = 0; i < 6; i++) { var a = Mathf.Tau * i / 6; pts.Add(new Vector2(MathF.Cos(a) * w / 2, MathF.Sin(a) * h / 2)); }
                break;
            case "rounded":
                float r = MathF.Min(w, h) * 0.14f;
                var corners = new[] { (w / 2 - r, h / 2 - r, 0f), (-w / 2 + r, h / 2 - r, 90f), (-w / 2 + r, -h / 2 + r, 180f), (w / 2 - r, -h / 2 + r, 270f) };
                foreach (var (cx, cz, start) in corners)
                    for (int i = 0; i <= 6; i++) { var a = (start + 90f * i / 6) * (MathF.PI / 180f); pts.Add(new Vector2(cx + MathF.Cos(a) * r, cz + MathF.Sin(a) * r)); }
                break;
            default:
                pts.AddRange(new[] { new Vector2(w / 2, h / 2), new Vector2(-w / 2, h / 2), new Vector2(-w / 2, -h / 2), new Vector2(w / 2, -h / 2) });
                break;
        }
        return pts;
    }

    public static bool Inside(string shape, float w, float h, Vector2 local)
    {
        float hw = w / 2, hh = h / 2;
        if (MathF.Abs(local.X) > hw || MathF.Abs(local.Y) > hh) return false;
        switch (shape)
        {
            case "circle":
                return local.X * local.X / (hw * hw) + local.Y * local.Y / (hh * hh) <= 1;
            case "hex":
            {
                var o = Outline("hex", w, h);
                bool inside = false;
                for (int i = 0, j = o.Count - 1; i < o.Count; j = i++)
                    if ((o[i].Y > local.Y) != (o[j].Y > local.Y) && local.X < (o[j].X - o[i].X) * (local.Y - o[i].Y) / (o[j].Y - o[i].Y) + o[i].X) inside = !inside;
                return inside;
            }
            default:
                return true;
        }
    }
}
