using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class RaisedSmoke
{
    public const float ReachM = 500;
    const int Puffs = 6;
    const float CullM = 30;

    static ArrayMesh? _mesh;
    static ShaderMaterial? _material;

    static ArrayMesh Puffed()
    {
        if (_mesh != null) return _mesh;
        var at = new List<Vector3>();
        var uv = new List<Vector2>();
        var order = new List<Vector2>();
        var index = new List<int>();
        for (int i = 0; i < Puffs; i++)
        {
            int first = at.Count;
            foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) })
            {
                at.Add(Vector3.Zero);
                uv.Add(corner);
                order.Add(new Vector2(i / (float)Puffs, 0));
            }
            index.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = at.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = order.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = index.ToArray();
        _mesh = new ArrayMesh();
        _mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        return _mesh;
    }

    static Vector2 _wind = Vector2.Right;

    public static void Blow(Vector2 wind)
    {
        _wind = wind;
        _material?.SetShaderParameter("wind", wind);
    }

    static ShaderMaterial Material(float upm)
    {
        _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/raised_smoke.gdshader") };
        _material.SetShaderParameter("metre", upm);
        _material.SetShaderParameter("wind", _wind);
        return _material;
    }

    public static GeometryInstance3D Node(IReadOnlyList<Vector3> stacks, Vector3 origin, float upm)
    {
        var many = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Puffed(), InstanceCount = stacks.Count };
        for (int i = 0; i < stacks.Count; i++) many.SetInstanceTransform(i, new Transform3D(Basis.Identity, stacks[i] - origin));
        return new MultiMeshInstance3D
        {
            Multimesh = many, Position = origin, MaterialOverride = Material(upm), Name = "Smoke", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = CullM * upm,
        };
    }
}
