using System.Collections.Generic;
using Godot;

namespace KTable.App;

enum TreeKind { Broad, Conifer, Palm, Cypress, Olive, Fruit, Birch, Bare }

static class RaisedTrees
{
    const int TrunkSides = 6, Lumps = 4;
    const float TrunkShare = 0.11f;

    static readonly Color Bark = new(0.3f, 0.24f, 0.18f);
    static readonly Color Leaf = new(0.3f, 0.38f, 0.2f);
    static readonly Color Needle = new(0.2f, 0.29f, 0.19f);
    static readonly Color Frond = new(0.36f, 0.42f, 0.22f);
    static readonly Color Silver = new(0.4f, 0.45f, 0.33f);
    static readonly Color Orchard = new(0.34f, 0.43f, 0.2f);
    static readonly Color Birchleaf = new(0.42f, 0.5f, 0.22f);
    static readonly Color Paled = new(0.8f, 0.78f, 0.72f);
    static readonly Color Twig = new(0.24f, 0.2f, 0.17f);

    static readonly Dictionary<(TreeKind, bool), ArrayMesh> Made = new();

    public static ArrayMesh Mesh(TreeKind kind, bool near)
    {
        if (Made.TryGetValue((kind, near), out var had)) return had;
        var at = new List<Vector3>();
        var tints = new List<Color>();
        if (kind == TreeKind.Palm) Palm(at, tints, near);
        else if (kind == TreeKind.Conifer) Conifer(at, tints, near, 4.2f);
        else if (kind == TreeKind.Cypress) Conifer(at, tints, near, 6);
        else if (kind == TreeKind.Olive) Rounded(at, tints, near, 1.1f, 0.15f, new Vector3(1.05f, 0.55f, 1.05f), Silver, Bark);
        else if (kind == TreeKind.Fruit) Rounded(at, tints, near, 1.4f, 0.1f, new Vector3(0.78f, 0.68f, 0.78f), Orchard, Bark);
        else if (kind == TreeKind.Birch) Rounded(at, tints, near, 2.6f, 0.08f, new Vector3(0.55f, 1.05f, 0.55f), Birchleaf, Paled);
        else if (kind == TreeKind.Bare) Bare(at, tints, near);
        else Broad(at, tints, near);
        var normals = new Vector3[at.Count];
        for (int i = 0; i + 2 < at.Count; i += 3)
        {
            var n = (at[i + 2] - at[i]).Cross(at[i + 1] - at[i]).Normalized();
            normals[i] = normals[i + 1] = normals[i + 2] = n;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = at.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
        arrays[(int)Godot.Mesh.ArrayType.Color] = tints.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurface(Godot.Mesh.PrimitiveType.Triangles, arrays);
        Made[(kind, near)] = mesh;
        return mesh;
    }

    static void Tri(List<Vector3> at, List<Color> tints, Vector3 a, Vector3 b, Vector3 c, Color tint)
    {
        at.Add(a);
        at.Add(b);
        at.Add(c);
        tints.Add(tint);
        tints.Add(tint);
        tints.Add(tint);
    }

    static void Trunk(List<Vector3> at, List<Color> tints, float radius, float high) => Trunk(at, tints, radius, high, Bark);

    static void Trunk(List<Vector3> at, List<Color> tints, float radius, float high, Color bark)
    {
        for (int i = 0; i < TrunkSides; i++)
        {
            float a0 = i * Mathf.Tau / TrunkSides, a1 = (i + 1) * Mathf.Tau / TrunkSides;
            Vector3 b0 = new(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius), b1 = new(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
            Vector3 t0 = b0 * 0.6f + Vector3.Up * high, t1 = b1 * 0.6f + Vector3.Up * high;
            Tri(at, tints, b0, t0, b1, bark);
            Tri(at, tints, b1, t0, t1, bark);
        }
    }

    static void Blob(List<Vector3> at, List<Color> tints, Vector3 centre, Vector3 size, int rings, int sides, float salt, Color tint)
    {
        Vector3 Point(int ring, int side)
        {
            float lat = Mathf.Pi * ring / rings - Mathf.Pi / 2, lon = Mathf.Tau * side / sides;
            float bump = 1 + 0.18f * (BuiltCanvas.Chanced(new Vector2(ring * 7 + salt, side * 3), salt) - 0.5f) * (ring > 0 && ring < rings ? 1 : 0);
            return centre + new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * size.X, Mathf.Sin(lat) * size.Y, Mathf.Cos(lat) * Mathf.Sin(lon) * size.Z) * bump;
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                Vector3 a = Point(r, s), b = Point(r, s + 1), c = Point(r + 1, s + 1), d = Point(r + 1, s);
                float shade = 0.82f + 0.3f * r / rings;
                var t = new Color(tint.R * shade, tint.G * shade, tint.B * shade);
                if (r > 0) Tri(at, tints, a, d, b, t);
                if (r < rings - 1) Tri(at, tints, b, d, c, t);
            }
    }

    static void Broad(List<Vector3> at, List<Color> tints, bool near)
    {
        if (!near)
        {
            Blob(at, tints, new Vector3(0, 2.05f, 0), new Vector3(1.05f, 0.95f, 1.05f), 3, 5, 1, Leaf);
            return;
        }
        Trunk(at, tints, TrunkShare, 1.9f);
        Blob(at, tints, new Vector3(0, 2.15f, 0), new Vector3(0.88f, 0.8f, 0.88f), 4, 7, 1, Leaf);
        for (int i = 0; i < Lumps; i++)
        {
            float a = i * Mathf.Tau / Lumps + 0.4f;
            Blob(at, tints, new Vector3(Mathf.Cos(a) * 0.48f, 1.85f + 0.32f * (i % 2), Mathf.Sin(a) * 0.48f), new Vector3(0.6f, 0.52f, 0.6f), 3, 6, i + 2, Leaf);
        }
    }

    static void Rounded(List<Vector3> at, List<Color> tints, bool near, float trunk, float radius, Vector3 crown, Color leaf, Color bark)
    {
        var middle = new Vector3(0, trunk + crown.Y * 0.8f, 0);
        if (!near)
        {
            Blob(at, tints, middle, crown * 1.1f, 3, 5, 1, leaf);
            return;
        }
        Trunk(at, tints, radius, trunk + crown.Y * 0.5f, bark);
        Blob(at, tints, middle, crown, 4, 7, 2, leaf);
        for (int i = 0; i < Lumps - 1; i++)
        {
            float a = i * Mathf.Tau / (Lumps - 1) + 0.7f;
            Blob(at, tints, middle + new Vector3(Mathf.Cos(a) * crown.X * 0.5f, -crown.Y * 0.2f, Mathf.Sin(a) * crown.Z * 0.5f), crown * 0.62f, 3, 6, i + 5, leaf);
        }
    }

    static void Bare(List<Vector3> at, List<Color> tints, bool near)
    {
        Trunk(at, tints, TrunkShare, 2.2f, Twig);
        if (!near) return;
        const int Boughs = 6;
        for (int i = 0; i < Boughs; i++)
        {
            float a = i * Mathf.Tau / Boughs + 0.3f * (i % 2);
            var way = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var foot = Vector3.Up * (1.5f + 0.25f * (i % 3));
            var tip = foot + way * 0.9f + Vector3.Up * (1.1f + 0.2f * (i % 2));
            var side = new Vector3(-way.Z, 0, way.X) * 0.05f;
            Tri(at, tints, foot - side, foot + side, tip, Twig);
            Tri(at, tints, foot + side, foot - side, tip, Twig);
        }
    }

    static void Conifer(List<Vector3> at, List<Color> tints, bool near, float tall)
    {
        int tiers = near ? 4 : 1, sides = near ? 8 : 5;
        if (near) Trunk(at, tints, TrunkShare * 0.8f, tall * 0.3f);
        for (int t = 0; t < tiers; t++)
        {
            float bottom = tall * (0.18f + 0.18f * t), top = tall * (0.48f + 0.18f * t), radius = 1.0f - 0.2f * t;
            if (!near)
            {
                bottom = tall * 0.15f;
                top = tall;
            }
            var apex = new Vector3(0, top, 0);
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.Tau / sides, a1 = (s + 1) * Mathf.Tau / sides;
                Vector3 b0 = new(Mathf.Cos(a0) * radius, bottom, Mathf.Sin(a0) * radius), b1 = new(Mathf.Cos(a1) * radius, bottom, Mathf.Sin(a1) * radius);
                float shade = 0.85f + 0.12f * t;
                Tri(at, tints, b0, apex, b1, new Color(Needle.R * shade, Needle.G * shade, Needle.B * shade));
                Tri(at, tints, b1, new Vector3(0, bottom, 0), b0, Needle.Darkened(0.4f));
            }
        }
    }

    static void Palm(List<Vector3> at, List<Color> tints, bool near)
    {
        const float High = 4.5f;
        const int Fronds = 7;
        Trunk(at, tints, TrunkShare * 0.8f, High);
        var top = new Vector3(0, High, 0);
        for (int i = 0; i < (near ? Fronds : 4); i++)
        {
            float a = i * Mathf.Tau / Fronds;
            var way = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-way.Z, 0, way.X) * 0.22f;
            var tip = top + way * 1.6f - Vector3.Up * 0.6f;
            var mid = top + way * 0.9f + Vector3.Up * 0.15f;
            Tri(at, tints, top, mid + side, mid - side, Frond);
            Tri(at, tints, mid - side, mid + side, tip, Frond.Darkened(0.1f));
        }
    }
}
