using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CrowdNear(Node3D root)
{
    public const int Most = 60;
    const float StrideHeights = 1.3f;

    sealed class Body
    {
        public required Node3D Holder;
        public required Skeleton3D Skeleton;
        public required RigMotion Motion;
        public required float HeightM;
        public bool Seen;
        public float Idle;
    }

    static readonly StandardMaterial3D Painted = new() { VertexColorUseAsAlbedo = true, Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };

    readonly Dictionary<(int, int), Body> _bodies = new();

    public int Count => _bodies.Count;

    public void Begin()
    {
        foreach (var b in _bodies.Values) b.Seen = false;
    }

    public void Show((int Town, int Person) who, CrowdLook look, Vector3 at, float yaw, float metre, bool moving, float strideUnits, float delta)
    {
        if (!_bodies.TryGetValue(who, out var body)) _bodies[who] = body = Make(look);
        body.Seen = true;
        body.Holder.Visible = true;
        body.Holder.Position = at;
        body.Holder.Rotation = new Vector3(0, yaw, 0);
        body.Holder.Scale = Vector3.One * metre;
        float tall = body.HeightM * metre, phase = Mathf.PosMod(strideUnits / Mathf.Max(tall * StrideHeights, 1e-6f), 1);
        body.Idle += delta * 0.2f;
        body.Motion.Pose(new RigMove(moving ? 1 : 0, phase, 0, 0, moving ? 0 : 1, body.Idle % 1));
        var points = body.Motion.Points;
        var turns = body.Motion.Turns;
        for (int i = 0; i < body.Motion.Bones; i++)
        {
            body.Skeleton.SetBonePosePosition(i, RigImport.ToGodot(points[i]));
            body.Skeleton.SetBonePoseRotation(i, RigImport.ToGodot(turns[i]));
        }
    }

    public void End()
    {
        foreach (var (key, body) in _bodies.Where(b => !b.Value.Seen).ToList())
        {
            body.Holder.QueueFree();
            _bodies.Remove(key);
        }
    }

    public void Clear()
    {
        foreach (var body in _bodies.Values) body.Holder.QueueFree();
        _bodies.Clear();
    }

    Body Make(CrowdLook look)
    {
        var (rig, shape) = CrowdFigure.Whole(look);
        var colours = CrowdFigure.Colours(shape, look).Select(h => Coords.ParseColor(h, Colors.Gray)).ToArray();
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = rig.Points.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = rig.Normals.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = shape.Colour.Select(c => colours[c]).ToArray();
        arrays[(int)Mesh.ArrayType.Bones] = rig.Joints.Select(j => (int)j).ToArray();
        arrays[(int)Mesh.ArrayType.Weights] = rig.Weights;
        arrays[(int)Mesh.ArrayType.Index] = rig.Triangles;
        var mesh = new ArrayMesh();
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        var (rest, turns, scales) = rig.Rest();
        var skin = new Skin();
        for (int i = 0; i < rig.Bones.Count; i++)
            skin.AddBind(i, new Transform3D(new Basis(RigImport.ToGodot(turns[i])).Scaled(Vector3.One * scales[i]), RigImport.ToGodot(rest[i])).AffineInverse());
        var skeleton = new Skeleton3D();
        for (int i = 0; i < rig.Bones.Count; i++)
        {
            var b = rig.Bones[i];
            skeleton.AddBone(b.Name);
            skeleton.SetBoneParent(i, b.Parent);
            skeleton.SetBoneRest(i, new Transform3D(new Basis(RigImport.ToGodot(b.Rotation)), RigImport.ToGodot(b.Position)));
        }
        skeleton.ResetBonePoses();
        var shown = new MeshInstance3D { Mesh = mesh, Skin = skin, MaterialOverride = Painted, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        skeleton.AddChild(shown);
        shown.Skeleton = "..";
        var holder = new Node3D { Name = "Near" };
        holder.AddChild(skeleton);
        root.AddChild(holder);
        return new Body { Holder = holder, Skeleton = skeleton, Motion = new RigMotion(rig, RigMotion.Natural), HeightM = (float)look.HeightM };
    }
}
