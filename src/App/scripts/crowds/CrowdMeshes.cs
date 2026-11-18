using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CrowdMeshes
{
    const int Outer = 0, Under = 1, Skin = 2, Hair = 3, Trim = 4;
    const int Still = 0, LeftLeg = 1, RightLeg = 2, LeftArm = 3, RightArm = 4, Skirt = 5;
    const float HipY = 0.48f, ShoulderY = 0.79f;

    sealed class Builder
    {
        readonly List<Vector3> _at = new(), _normal = new();
        readonly List<Vector2> _slot = new(), _limb = new();
        readonly List<int> _index = new();

        public void Box(Vector3 low, Vector3 high, int slot, int limb = Still, float pivot = 0, float flare = 0)
        {
            Vector3 P(float x, float y, float z) => new(Mathf.Lerp(low.X, high.X, x) * (1 + flare * (1 - y)), Mathf.Lerp(low.Y, high.Y, y),
                Mathf.Lerp(low.Z, high.Z, z) * (1 + flare * (1 - y)));
            Face(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), Vector3.Back, slot, limb, pivot);
            Face(P(1, 0, 0), P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), Vector3.Forward, slot, limb, pivot);
            Face(P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), Vector3.Left, slot, limb, pivot);
            Face(P(1, 0, 1), P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), Vector3.Right, slot, limb, pivot);
            Face(P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), P(0, 1, 0), Vector3.Up, slot, limb, pivot);
            Face(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.Down, slot, limb, pivot);
        }

        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, int slot, int limb, float pivot)
        {
            int first = _at.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                _at.Add(p);
                _normal.Add(n);
                _slot.Add(new Vector2(slot, 0));
                _limb.Add(new Vector2(limb, pivot));
            }
            _index.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
        }

        public ArrayMesh Mesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = _at.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Normal] = _normal.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = _slot.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.TexUV2] = _limb.ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Index] = _index.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurface(Godot.Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }

    static ArrayMesh[]? _figures;
    static ArrayMesh? _square;

    public static ArrayMesh Figure(int outfit) => (_figures ??= Build())[outfit];

    public static ArrayMesh Square
    {
        get
        {
            if (_square != null) return _square;
            var b = new Builder();
            b.Box(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0.01f, 0.5f), Outer);
            return _square = b.Mesh();
        }
    }

    static ArrayMesh[] Build()
    {
        var all = new ArrayMesh[CrowdRoster.Outfits];
        for (int o = 0; o < all.Length; o++)
        {
            var b = new Builder();
            Body(b, o);
            all[o] = b.Mesh();
        }
        return all;
    }

    static void Body(Builder b, int outfit)
    {
        b.Box(new(-0.1f, 0, -0.05f), new(-0.02f, HipY, 0.05f), Under, LeftLeg, HipY);
        b.Box(new(0.02f, 0, -0.05f), new(0.1f, HipY, 0.05f), Under, RightLeg, HipY);
        b.Box(new(-0.15f, HipY - 0.04f, -0.085f), new(0.15f, ShoulderY + 0.02f, 0.085f), Outer);
        b.Box(new(-0.21f, 0.46f, -0.045f), new(-0.15f, ShoulderY, 0.045f), Outer, LeftArm, ShoulderY);
        b.Box(new(0.15f, 0.46f, -0.045f), new(0.21f, ShoulderY, 0.045f), Outer, RightArm, ShoulderY);
        b.Box(new(-0.205f, 0.41f, -0.035f), new(-0.155f, 0.46f, 0.035f), Skin, LeftArm, ShoulderY);
        b.Box(new(0.155f, 0.41f, -0.035f), new(0.205f, 0.46f, 0.035f), Skin, RightArm, ShoulderY);
        b.Box(new(-0.035f, ShoulderY, -0.035f), new(0.035f, 0.84f, 0.035f), Skin);
        b.Box(new(-0.065f, 0.83f, -0.07f), new(0.065f, 0.97f, 0.07f), Skin);
        switch (outfit)
        {
            case CrowdRoster.Coat:
                b.Box(new(-0.16f, 0.28f, -0.095f), new(0.16f, HipY, 0.095f), Outer, Skirt, HipY, 0.25f);
                b.Box(new(-0.12f, 0.95f, -0.12f), new(0.12f, 0.98f, 0.12f), Hair);
                b.Box(new(-0.07f, 0.97f, -0.075f), new(0.07f, 1.03f, 0.075f), Hair);
                break;
            case CrowdRoster.Soldier:
                b.Box(new(-0.16f, 0.3f, -0.095f), new(0.16f, HipY, 0.095f), Outer, Skirt, HipY, 0.2f);
                b.Box(new(-0.155f, 0.6f, 0.086f), new(0.155f, 0.66f, 0.09f), Trim);
                b.Box(new(-0.07f, 0.95f, -0.075f), new(0.07f, 1.06f, 0.075f), Hair);
                break;
            case CrowdRoster.Jacket:
                b.Box(new(-0.07f, 0.95f, -0.075f), new(0.07f, 1.0f, 0.075f), Hair);
                break;
            case CrowdRoster.Gown:
            case CrowdRoster.Apron:
                b.Box(new(-0.17f, 0.02f, -0.11f), new(0.17f, HipY, 0.11f), Outer, Skirt, HipY, 0.45f);
                if (outfit == CrowdRoster.Apron) b.Box(new(-0.11f, 0.08f, 0.11f), new(0.11f, 0.62f, 0.14f), Trim);
                b.Box(new(-0.075f, 0.9f, -0.08f), new(0.075f, 0.99f, 0.08f), Hair);
                break;
            default:
                b.Box(new(-0.15f, 0.12f, -0.09f), new(0.15f, HipY, 0.09f), Outer, Skirt, HipY, 0.3f);
                b.Box(new(-0.07f, 0.93f, -0.075f), new(0.07f, 0.99f, 0.075f), Hair);
                break;
        }
    }
}
