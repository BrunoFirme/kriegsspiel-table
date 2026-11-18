using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CrowdFigures
{
    public static readonly StandardMaterial3D Painted = new() { VertexColorUseAsAlbedo = true, Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };

    public static MeshInstance3D Model(CrowdLook look, float scale, bool shadow)
    {
        var (rig, shape) = CrowdFigure.Whole(look);
        return new MeshInstance3D
        {
            Mesh = Mesh(rig, shape, look), RotationDegrees = new Vector3(0, 180, 0), MaterialOverride = Painted, Scale = Vector3.One * scale,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public static ArrayMesh Mesh(Rig rig, CrowdShape shape, CrowdLook look, bool skinned = false)
    {
        var colours = CrowdFigure.Colours(shape, look).Select(h => Coords.ParseColor(h, Colors.Gray)).ToArray();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = rig.Points.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Normal] = rig.Normals.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Color] = shape.Colour.Select(c => colours[c]).ToArray();
        if (skinned)
        {
            arrays[(int)Godot.Mesh.ArrayType.Bones] = rig.Joints.Select(j => (int)j).ToArray();
            arrays[(int)Godot.Mesh.ArrayType.Weights] = rig.Weights;
        }
        arrays[(int)Godot.Mesh.ArrayType.Index] = rig.Triangles;
        var mesh = new ArrayMesh();
        mesh.AddSurface(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
