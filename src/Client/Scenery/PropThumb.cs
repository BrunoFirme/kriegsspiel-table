using System.Numerics;
using KTable.Client.Geometry;
using static KTable.Client.Scenery.PropAtlas;

namespace KTable.Client.Scenery;

public static class PropThumb
{
    public const int Out = 128;
    const int S = Out * 2;

    readonly record struct Rgb(float R, float G, float B);

    public static byte[] Render(PropMesh mesh, byte[] tex)
    {
        var view = new Basis(Axis.Right, Mathf.DegToRad(36)) * new Basis(Axis.Up, Mathf.DegToRad(-28));
        var pts = mesh.V.Select(v => view * v.p).ToArray();
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        float scale = (S * 0.92f) / Mathf.Max(maxX - minX, maxY - minY);
        float ox = S / 2f - (minX + maxX) / 2 * scale, oy = S / 2f + (minY + maxY) / 2 * scale;
        var depth = new float[S * S];
        Array.Fill(depth, float.MinValue);
        var col = new Rgb[S * S];
        var light = new Vector3(-0.3f, 1, 0.4f).Normalized();
        for (int i = 0; i < pts.Length; i += 3)
        {
            Vector2 Sc(Vector3 p) => new(ox + p.X * scale, oy - p.Y * scale);
            var a = Sc(pts[i]);
            var b = Sc(pts[i + 1]);
            var c = Sc(pts[i + 2]);
            float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Mathf.Abs(area) < 1e-6f) continue;
            int x0 = Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.X, Mathf.Min(b.X, c.X)))), x1 = Math.Min(S - 1, (int)Mathf.Ceil(Mathf.Max(a.X, Mathf.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)Mathf.Floor(Mathf.Min(a.Y, Mathf.Min(b.Y, c.Y)))), y1 = Math.Min(S - 1, (int)Mathf.Ceil(Mathf.Max(a.Y, Mathf.Max(b.Y, c.Y))));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((b.X - p.X) * (c.Y - p.Y) - (b.Y - p.Y) * (c.X - p.X)) / area;
                    float w1 = ((c.X - p.X) * (a.Y - p.Y) - (c.Y - p.Y) * (a.X - p.X)) / area;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float z = pts[i].Z * w0 + pts[i + 1].Z * w1 + pts[i + 2].Z * w2;
                    if (z <= depth[y * S + x]) continue;
                    var uv = mesh.V[i].t * w0 + mesh.V[i + 1].t * w1 + mesh.V[i + 2].t * w2;
                    int k = (Math.Clamp((int)(uv.Y * AtlasH), 0, AtlasH - 1) * AtlasW + Math.Clamp((int)(uv.X * AtlasW), 0, AtlasW - 1)) * 4;
                    if (tex[k + 3] < 128) continue;
                    var n = (mesh.V[i].n * w0 + mesh.V[i + 1].n * w1 + mesh.V[i + 2].n * w2).Normalized();
                    float sh = (mesh.Shade[i] * w0 + mesh.Shade[i + 1] * w1 + mesh.Shade[i + 2] * w2) * (0.72f + 0.35f * Mathf.Max(0, n.Dot(light)));
                    depth[y * S + x] = z;
                    col[y * S + x] = new Rgb(tex[k] / 255f * sh, tex[k + 1] / 255f * sh, tex[k + 2] / 255f * sh);
                }
        }
        var bytes = new byte[Out * Out * 4];
        for (int y = 0; y < Out; y++)
            for (int x = 0; x < Out; x++)
            {
                float r = 0, g = 0, bb = 0, al = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int q = (y * 2 + dy) * S + x * 2 + dx;
                        if (depth[q] == float.MinValue) continue;
                        r += col[q].R;
                        g += col[q].G;
                        bb += col[q].B;
                        al += 1;
                    }
                int o = (y * Out + x) * 4;
                if (al == 0) continue;
                bytes[o] = (byte)Mathf.Clamp(r / al * 255, 0, 255);
                bytes[o + 1] = (byte)Mathf.Clamp(g / al * 255, 0, 255);
                bytes[o + 2] = (byte)Mathf.Clamp(bb / al * 255, 0, 255);
                bytes[o + 3] = (byte)(al / 4 * 255);
            }
        return bytes;
    }
}
