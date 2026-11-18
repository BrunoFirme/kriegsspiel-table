using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CrowdNear(Node3D root, bool background = false)
{
    sealed record Prepared(CrowdLook Look, Rig Rig, CrowdShape Shape, RigMotion Motion, GunGrip? Gun, int Hold);

    const int MostMaking = 4;
    readonly Dictionary<(int, int), (CrowdLook Look, Task<Prepared> Task)> _making = new();
    public const int Most = 60;
    const float StrideHeights = 1.3f;
    const int EveryFrame = 15, Every = 3;
    const float BuildsPerSecond = 6, BuildBurst = 4, KeepSeconds = 4;
    float _builds = BuildBurst;
    double _clock;

    sealed class Body
    {
        public required Node3D Holder;
        public required Skeleton3D Skeleton;
        public required RigMotion Motion;
        public required Rig Rig;
        public required float HeightM;
        public object? Of;
        public GunGrip? Gun;
        public int Hold;
        public float Factor = 1;
        public bool Seen;
        public double SeenAt;
        public float Idle;
    }

    readonly Dictionary<(int, int), Body> _bodies = new();
    readonly List<(int, int)> _gone = new();
    int _frame;

    public int Count { get; private set; }

    public void Begin(float delta = 1)
    {
        foreach (var b in _bodies.Values) b.Seen = false;
        _frame++;
        _clock += delta;
        _builds = Mathf.Min(BuildBurst, _builds + delta * BuildsPerSecond);
        Count = 0;
    }

    public bool Show((int Town, int Person) who, int rank, CrowdLook look, Vector3 at, float yaw, float metre, bool moving, float strideUnits, float delta, string? act = null,
        CrowdKinds.Kind? kind = null)
    {
        object of = (object?)kind ?? look;
        bool fresh = !_bodies.TryGetValue(who, out var had) || !ReferenceEquals(had.Of, of);
        var body = had!;
        if (fresh)
        {
            Body? made;
            if (kind == null && background)
            {
                if (Ready(who, look) is not { } prepared) return false;
                made = Assemble(prepared);
            }
            else
            {
                if (_builds < 1) return false;
                _builds -= 1;
                made = kind != null ? Made(kind.Rig, kind.Skinned, kind.Material, (float)look.HeightM, (float)look.HeightM / kind.Tall) : Make(look);
            }
            had?.Holder.QueueFree();
            _bodies[who] = body = made;
            body.Of = of;
        }
        body.Seen = true;
        body.SeenAt = _clock;
        Count++;
        body.Holder.Visible = true;
        body.Holder.Position = at;
        body.Holder.Rotation = new Vector3(0, yaw, 0);
        body.Holder.Scale = Vector3.One * metre * body.Factor;
        float tall = body.HeightM * metre, phase = Mathf.PosMod(strideUnits / Mathf.Max(tall * StrideHeights, 1e-6f), 1);
        body.Idle += delta * 0.2f;
        if (!fresh && rank >= EveryFrame && (_frame + rank) % Every != 0) return true;
        bool acting = moving && RigMotion.Parts(act).Arm != null;
        body.Motion.Pose(new RigMove(moving && !acting ? 1 : 0, phase, 0, 0, moving && !acting ? 0 : 1, body.Idle % 1, Act: act, ActSeconds: body.Idle * 5, Hold: body.Hold));
        var points = body.Motion.Points;
        var turns = body.Motion.Turns;
        CrowdGrip.Hold(body.Rig, points, turns, body.Gun, act, body.HeightM);
        for (int i = 0; i < body.Motion.Bones; i++)
        {
            body.Skeleton.SetBonePosePosition(i, RigImport.ToGodot(points[i]));
            body.Skeleton.SetBonePoseRotation(i, RigImport.ToGodot(turns[i]));
        }
        return true;
    }

    public void End()
    {
        foreach (var (key, body) in _bodies)
        {
            if (body.Seen) continue;
            body.Holder.Visible = false;
            if (_clock - body.SeenAt > KeepSeconds) _gone.Add(key);
        }
        foreach (var key in _gone)
        {
            _bodies[key].Holder.QueueFree();
            _bodies.Remove(key);
        }
        _gone.Clear();
        foreach (var (key, (_, task)) in _making)
            if (task.IsCompleted && !(_bodies.TryGetValue(key, out var b) && b.Seen)) _gone.Add(key);
        foreach (var key in _gone) _making.Remove(key);
        _gone.Clear();
    }

    public void Clear()
    {
        foreach (var body in _bodies.Values) body.Holder.QueueFree();
        _bodies.Clear();
    }

    Prepared? Ready((int, int) who, CrowdLook look)
    {
        if (_making.TryGetValue(who, out var making) && ReferenceEquals(making.Look, look))
        {
            if (!making.Task.IsCompleted) return null;
            _making.Remove(who);
            return making.Task.IsCompletedSuccessfully ? making.Task.Result : null;
        }
        if (_making.Count >= MostMaking || _builds < 1) return null;
        _builds -= 1;
        _making[who] = (look, Task.Run(() => Prepare(look)));
        return null;
    }

    static Prepared Prepare(CrowdLook look)
    {
        var (rig, shape) = CrowdFigure.Whole(look);
        return new Prepared(look, rig, shape, new RigMotion(rig, RigMotion.Natural), CrowdFigure.GripOf(look), CrowdFigure.Hold(look));
    }

    Body Assemble(Prepared p)
    {
        var body = Made(p.Rig, CrowdFigures.Mesh(p.Rig, p.Shape, p.Look, skinned: true), CrowdFigures.Painted, (float)p.Look.HeightM, 1, p.Motion);
        (body.Gun, body.Hold) = (p.Gun, p.Hold);
        return body;
    }

    Body Make(CrowdLook look)
    {
        var (rig, shape) = CrowdFigure.Whole(look);
        var body = Made(rig, CrowdFigures.Mesh(rig, shape, look, skinned: true), CrowdFigures.Painted, (float)look.HeightM, 1);
        (body.Gun, body.Hold) = (CrowdFigure.GripOf(look), CrowdFigure.Hold(look));
        return body;
    }

    Body Made(Rig rig, ArrayMesh mesh, Material material, float heightM, float factor, RigMotion? motion = null)
    {
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
        var shown = new MeshInstance3D { Mesh = mesh, Skin = skin, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        skeleton.AddChild(shown);
        shown.Skeleton = "..";
        var holder = new Node3D { Name = "Near" };
        holder.AddChild(skeleton);
        root.AddChild(holder);
        return new Body { Holder = holder, Skeleton = skeleton, Motion = motion ?? new RigMotion(rig, RigMotion.Natural), Rig = rig, HeightM = heightM, Factor = factor };
    }
}
