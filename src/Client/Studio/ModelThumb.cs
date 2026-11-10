using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Studio;

public sealed record ModelPicture(byte[] Rgba, int W, int H);

public static class ModelThumb
{
    public const int Out = 160;
    const int S = Out * 2;
    const float Fill = 0.9f, Ambient = 0.62f, Diffuse = 0.45f;
    const byte CutOff = 128;

    public static byte[]? Render(ModelMesh mesh, ModelPicture? picture)
    {
        if (mesh.V.Count < 3) return null;
        var view = new Basis(Axis.Right, Mathf.DegToRad(30)) * new Basis(Axis.Up, Mathf.DegToRad(140));
        var pts = mesh.V.Select(v => view * v).ToArray();
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        float span = Mathf.Max(maxX - minX, maxY - minY);
        if (!(span > 0)) return null;
        float scale = S * Fill / span;
        float ox = S / 2f - (minX + maxX) / 2 * scale, oy = S / 2f + (minY + maxY) / 2 * scale;
        var depth = new float[S * S];
        Array.Fill(depth, float.MinValue);
        var col = new Vector3[S * S];
        var light = (view * new Vector3(-0.4f, 1, -0.5f)).Normalized();
        for (int i = 0; i + 2 < pts.Length; i += 3)
        {
            Vector2 Sc(Vector3 p) => new(ox + p.X * scale, oy - p.Y * scale);
            Vector2 a = Sc(pts[i]), b = Sc(pts[i + 1]), c = Sc(pts[i + 2]);
            float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Mathf.Abs(area) < 1e-6f) continue;
            var faceN = (pts[i + 1] - pts[i]).Cross(pts[i + 2] - pts[i]).Normalized();
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
                    int at = y * S + x;
                    if (z <= depth[at]) continue;
                    var tint = Tint(mesh, i, w0, w1, w2);
                    if (picture != null)
                    {
                        var uv = mesh.UV[i] * w0 + mesh.UV[i + 1] * w1 + mesh.UV[i + 2] * w2;
                        int k = Texel(picture, uv);
                        if (picture.Rgba[k + 3] < CutOff) continue;
                        tint *= new Vector3(picture.Rgba[k], picture.Rgba[k + 1], picture.Rgba[k + 2]) / 255f;
                    }
                    depth[at] = z;
                    col[at] = tint * (Ambient + Diffuse * Mathf.Abs(faceN.Dot(light)));
                }
        }
        return Shrink(depth, col);
    }

    static Vector3 Tint(ModelMesh mesh, int i, float w0, float w1, float w2)
    {
        Vector3 C(int k) => new(mesh.C[k].R, mesh.C[k].G, mesh.C[k].B);
        var vertex = C(i) * w0 + C(i + 1) * w1 + C(i + 2) * w2;
        int m = i / 3 < mesh.Mat.Count ? mesh.Mat[i / 3] : -1;
        var mat = m >= 0 && m < mesh.Materials.Count ? mesh.Materials[m].Color : Rgba.White;
        return vertex * new Vector3(mat.R, mat.G, mat.B);
    }

    static int Texel(ModelPicture t, Vector2 uv)
    {
        float u = uv.X - Mathf.Floor(uv.X), v = uv.Y - Mathf.Floor(uv.Y);
        int tx = Math.Clamp((int)(u * t.W), 0, t.W - 1), ty = Math.Clamp((int)(v * t.H), 0, t.H - 1);
        return (ty * t.W + tx) * 4;
    }

    static byte[] Shrink(float[] depth, Vector3[] col)
    {
        var bytes = new byte[Out * Out * 4];
        for (int y = 0; y < Out; y++)
            for (int x = 0; x < Out; x++)
            {
                var sum = Vector3.Zero;
                int hits = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int q = (y * 2 + dy) * S + x * 2 + dx;
                        if (depth[q] == float.MinValue) continue;
                        sum += col[q];
                        hits++;
                    }
                if (hits == 0) continue;
                int o = (y * Out + x) * 4;
                bytes[o] = (byte)Mathf.Clamp(sum.X / hits * 255, 0, 255);
                bytes[o + 1] = (byte)Mathf.Clamp(sum.Y / hits * 255, 0, 255);
                bytes[o + 2] = (byte)Mathf.Clamp(sum.Z / hits * 255, 0, 255);
                bytes[o + 3] = (byte)(hits * 255 / 4);
            }
        return bytes;
    }
}
