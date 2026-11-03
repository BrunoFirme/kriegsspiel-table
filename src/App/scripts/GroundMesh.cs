using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class GroundMesh
{
    public const int MostCellsPerTile = 48;

    public static Func<float, float, float> Heights(Ground ground, Transform3D local) =>
        (x, z) => (float)ground.At(Coords.FromWorld(local * new Vector3(x, 0, z)));

    public static ArrayMesh Grid(Func<float, float, float> h, float x0, float z0, float x1, float z1, int nx, int nz, Vector2 uv0, Vector2 uv1, float lift = 0)
    {
        nx = Math.Max(1, nx); nz = Math.Max(1, nz);
        int vx = nx + 1, vz = nz + 1;
        var verts = new Vector3[vx * vz];
        var normals = new Vector3[vx * vz];
        var uvs = new Vector2[vx * vz];
        float ex = (x1 - x0) / nx, ez = (z1 - z0) / nz;
        for (int j = 0; j < vz; j++)
            for (int i = 0; i < vx; i++)
            {
                float x = x0 + ex * i, z = z0 + ez * j;
                int k = j * vx + i;
                verts[k] = new Vector3(x, h(x, z) + lift, z);
                normals[k] = Normal(h, x, z, Math.Abs(ex), Math.Abs(ez));
                uvs[k] = new Vector2(Mathf.Lerp(uv0.X, uv1.X, i / (float)nx), Mathf.Lerp(uv0.Y, uv1.Y, j / (float)nz));
            }
        var idx = new int[nx * nz * 6];
        int t = 0;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * vx + i, b = a + 1, c = a + vx, d = c + 1;
                idx[t++] = a; idx[t++] = b; idx[t++] = c;
                idx[t++] = b; idx[t++] = d; idx[t++] = c;
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

    static Vector3 Normal(Func<float, float, float> h, float x, float z, float ex, float ez)
    {
        float dx = Math.Max(1e-3f, ex), dz = Math.Max(1e-3f, ez);
        float sx = (h(x + dx, z) - h(x - dx, z)) / (2 * dx);
        float sz = (h(x, z + dz) - h(x, z - dz)) / (2 * dz);
        return new Vector3(-sx, 1, -sz).Normalized();
    }

    public static ArrayMesh Skirt(Func<float, float, float> h, float x0, float z0, float x1, float z1, int nx, int nz, float floor)
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
