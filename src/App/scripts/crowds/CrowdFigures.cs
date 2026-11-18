using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CrowdFigures
{
    public static readonly StandardMaterial3D Painted = new()
    {
        VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static MeshInstance3D Model(CrowdLook look, float scale, bool shadow)
    {
        var (rig, shape) = CrowdFigure.Whole(look);
        var motion = new RigMotion(rig);
        motion.Pose(new RigMove(0, 0, 0, 0, 1, 0, Hold: CrowdFigure.Hold(look)));
        var (points, turns) = (motion.Points.ToArray(), motion.Turns.ToArray());
        CrowdGrip.Hold(rig, points, turns, CrowdFigure.GripOf(look), null, (float)look.HeightM);
        var (posed, normals) = rig.Posed(points, turns);
        rig = new Rig { Bones = rig.Bones, Points = posed, Normals = normals, Coords = rig.Coords, Joints = rig.Joints, Weights = rig.Weights, Triangles = rig.Triangles };
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
        arrays[(int)Godot.Mesh.ArrayType.Color] = shape.Colour.Select((c, k) => Shaded(colours[c], shape.Shade[k])).ToArray();
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

    static Color Shaded(Color c, float shade) => new(c.R * shade, c.G * shade, c.B * shade, c.A);
}
