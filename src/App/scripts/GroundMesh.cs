using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class GroundMesh
{
    public const int MostCellsPerTile = 48;

    static float Slope(float rise, float step) => Math.Abs(step) < 1e-6f ? 0 : rise / (2 * step);

    public static Func<float, float, float> Heights(Ground ground, Transform3D local) =>
        (x, z) => (float)ground.At(Coords.FromWorld(local * new Vector3(x, 0, z)));

    public static ArrayMesh Grid(Func<float, float, float> h, float x0, float z0, float x1, float z1, int nx, int nz, Vector2 uv0, Vector2 uv1, float lift = 0, float skirt = 0)
    {
        nx = Math.Max(1, nx); nz = Math.Max(1, nz);
        int vx = nx + 1, vz = nz + 1;
        int rim = skirt > 0 ? 2 * (vx + vz) : 0;
        var verts = new Vector3[vx * vz + rim];
        var normals = new Vector3[vx * vz + rim];
        var uvs = new Vector2[vx * vz + rim];
        float ex = (x1 - x0) / nx, ez = (z1 - z0) / nz;
        int wx = vx + 2;
        var hs = new float[wx * (vz + 2)];
        for (int j = -1; j <= vz; j++)
            for (int i = -1; i <= vx; i++)
                hs[(j + 1) * wx + i + 1] = h(x0 + ex * i, z0 + ez * j);
        for (int j = 0; j < vz; j++)
            for (int i = 0; i < vx; i++)
            {
                float x = x0 + ex * i, z = z0 + ez * j;
                int k = j * vx + i, at = (j + 1) * wx + i + 1;
                verts[k] = new Vector3(x, hs[at] + lift, z);
                float sx = Slope(hs[at + 1] - hs[at - 1], ex), sz = Slope(hs[at + wx] - hs[at - wx], ez);
                normals[k] = new Vector3(-sx, 1, -sz).Normalized();
                uvs[k] = new Vector2(Mathf.Lerp(uv0.X, uv1.X, i / (float)nx), Mathf.Lerp(uv0.Y, uv1.Y, j / (float)nz));
            }
        var idx = new int[nx * nz * 6 + (skirt > 0 ? 2 * (nx + nz) * 6 : 0)];
        int t = 0;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * vx + i, b = a + 1, c = a + vx, d = c + 1;
                idx[t++] = a; idx[t++] = b; idx[t++] = c;
                idx[t++] = b; idx[t++] = d; idx[t++] = c;
            }
        if (skirt > 0)
        {
            int next = vx * vz;
            float lean = skirt * SkirtLean;
            void Hang(Func<int, int> top, int n, Vector3 outward)
            {
                int first = next;
                for (int k = 0; k <= n; k++)
                {
                    int above = top(k);
                    verts[next] = verts[above] + outward * lean + Vector3.Down * skirt;
                    normals[next] = normals[above];
                    uvs[next] = uvs[above];
                    next++;
                }
                for (int k = 0; k < n; k++)
                {
                    int p = top(k), q = top(k + 1), pb = first + k, qb = first + k + 1;
                    Face(p, q, pb, outward);
                    Face(q, qb, pb, outward);
                }
            }
            void Face(int a, int b, int c, Vector3 outward)
            {
                bool facesOut = (verts[b] - verts[a]).Cross(verts[c] - verts[a]).Dot(outward) < 0;
                idx[t++] = a; idx[t++] = facesOut ? b : c; idx[t++] = facesOut ? c : b;
            }
            Hang(k => k, nx, ez > 0 ? Vector3.Forward : Vector3.Back);
            Hang(k => (vz - 1) * vx + k, nx, ez > 0 ? Vector3.Back : Vector3.Forward);
            Hang(k => k * vx, nz, ex > 0 ? Vector3.Left : Vector3.Right);
            Hang(k => k * vx + vx - 1, nz, ex > 0 ? Vector3.Right : Vector3.Left);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    const float SkirtLean = 0.05f;

    public static ArrayMesh Skirt(Func<float, float, float> h, float x0, float z0, float x1, float z1, int nx, int nz, float floor, Func<float, float, bool>? open = null, float probe = 0)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Edge(Vector2 a, Vector2 b, int n, Vector3 outward)
        {
            for (int i = 0; i < n; i++)
            {
                var p = a.Lerp(b, i / (float)n); var q = a.Lerp(b, (i + 1) / (float)n);
                var pt = new Vector3(p.X, h(p.X, p.Y), p.Y); var qt = new Vector3(q.X, h(q.X, q.Y), q.Y);
                var pb = new Vector3(p.X, floor, p.Y); var qb = new Vector3(q.X, floor, q.Y);
                if (pt.Y <= floor + 1e-4f && qt.Y <= floor + 1e-4f) continue;
                var mid = (p + q) / 2 + new Vector2(outward.X, outward.Z) * probe;
                if (open != null && !open(mid.X, mid.Y)) continue;
                foreach (var v in new[] { pt, pb, qt, qt, pb, qb }) { st.SetNormal(outward); st.AddVertex(v); }
            }
        }
        Edge(new Vector2(x0, z0), new Vector2(x1, z0), nx, Vector3.Forward);
        Edge(new Vector2(x1, z0), new Vector2(x1, z1), nz, Vector3.Right);
        Edge(new Vector2(x1, z1), new Vector2(x0, z1), nx, Vector3.Back);
        Edge(new Vector2(x0, z1), new Vector2(x0, z0), nz, Vector3.Left);
        return st.Commit();
    }
}
