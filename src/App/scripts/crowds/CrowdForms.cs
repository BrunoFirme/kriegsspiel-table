using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CrowdBatch
{
    public const int Stride = 20, Least = 32;

    public readonly MultiMeshInstance3D Node;
    float[] _data = new float[Least * Stride];
    public int Count { get; private set; }

    public CrowdBatch(Mesh mesh, Material material, string name)
    {
        Node = new MultiMeshInstance3D
        {
            Name = name, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = Least, VisibleInstanceCount = 0,
            },
            CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
        };
    }

    public void Begin() => Count = 0;

    public void Add(Vector3 at, float yaw, float scale, ReadOnlySpan<float> colours, float phase)
    {
        if ((Count + 1) * Stride > _data.Length) Array.Resize(ref _data, _data.Length * 2);
        float c = MathF.Cos(yaw) * scale, s = MathF.Sin(yaw) * scale;
        var row = _data.AsSpan(Count++ * Stride, Stride);
        (row[0], row[1], row[2], row[3]) = (c, 0, s, at.X);
        (row[4], row[5], row[6], row[7]) = (0, scale, 0, at.Y);
        (row[8], row[9], row[10], row[11]) = (-s, 0, c, at.Z);
        (row[12], row[13], row[14], row[15]) = (colours[0], colours[1], colours[2], colours[3]);
        (row[16], row[17], row[18], row[19]) = (colours[4], colours[5], colours[6], phase);
    }

    public void Push()
    {
        if (Count == 0 && _shown == 0) return;
        var many = Node.Multimesh;
        int room = _data.Length / Stride;
        if (many.InstanceCount != room) many.InstanceCount = room;
        if (_shown != Count) many.VisibleInstanceCount = Count;
        Node.Visible = Count > 0;
        _shown = Count;
        if (Count > 0) RenderingServer.MultimeshSetBuffer(many.GetRid(), _data);
    }

    int _shown;
}

static class CrowdMeshes
{
    static ArrayMesh? _square;

    public static ArrayMesh Square => _square ??= Of(Flat());

    static CrowdShape Flat()
    {
        var s = new CrowdShape();
        foreach (var (x, z) in new[] { (-0.5f, -0.5f), (0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f) })
        {
            s.Points.Add(new System.Numerics.Vector3(x, 0, z));
            s.Normals.Add(System.Numerics.Vector3.UnitY);
            s.Colour.Add(0);
            s.Bone.Add(0);
            s.Limb.Add(0);
            s.Pivot.Add(0);
        }
        s.Triangles.AddRange([0, 2, 1, 0, 3, 2]);
        return s;
    }

    public static ArrayMesh Of(CrowdShape shape)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = shape.Points.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = shape.Normals.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = shape.Colour.Select(c => new Vector2(c, 0)).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = shape.Limb.Select((l, i) => new Vector2(l, shape.Pivot[i])).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = shape.Triangles.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public static float Packed(string hex)
    {
        var c = Coords.ParseColor(hex, Colors.Gray);
        return c.R8 * 65536 + c.G8 * 256 + c.B8;
    }

    public static float[] Palette(IReadOnlyList<string> colours)
    {
        var packed = new float[CrowdShape.MostColours];
        for (int i = 0; i < packed.Length; i++) packed[i] = Packed(i < colours.Count ? colours[i] : colours.Count > 0 ? colours[0] : "#808080");
        return packed;
    }
}
