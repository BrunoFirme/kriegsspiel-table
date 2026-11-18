using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class Rigged(PieceNode body, TableContext c) : IFigureMotion
{
    const float NearHeights = 25, FarHeights = 70, ShadowHeights = 35;
    const int FarEvery = 3;

    sealed record Built(Rig Rig, ArrayMesh Mesh, Skin Skin, Transform3D[] Rest);

    sealed record Source(string Pack, string Rel, string Abs);

    static readonly Dictionary<string, Built?> Ready = new();
    static readonly ConcurrentDictionary<string, Rig?> Read = new();
    static readonly HashSet<string> Asked = new();

    readonly Source? _source = SourceOf(body.Def, c);
    readonly CrowdLook? _look = CrowdPiece.LookOf(body.Piece, body.Def);
    Built? _looked;
    GunGrip? _grip;
    int _hold;
    readonly StrideClock _clock = new();
    Node3D? _holder;
    Skeleton3D? _skeleton;
    MeshInstance3D? _shown;
    RigMotion? _motion;
    string? _gait;

    public string? Style { get; set; }
    public string? Act { get; set; }
    public bool Aloft { get; set; }

    public void Pulse() => _pulse = 1;

    const float FlyEase = 5f, GlideEase = 4f, GlideHeights = 4f, PulseSeconds = 0.55f, StillShare = 0.5f;
    float _fly, _glide, _pulse, _actSeconds;
    string? _actWas;
    Vector3? _was;
    int _frame;
    bool _posed;

    public static bool Wants(PieceDef? def) => def?.Rig != null;

    static Source? SourceOf(PieceDef? def, TableContext c) =>
        def?.Rig is { } key && c.S.PackOfDef(def.Id) is { } pack && pack.Assets.TryGetValue(key, out var asset)
            ? new Source(pack.Id, asset.File, c.S.Assets.Abs(pack.Id, asset.File)) : null;

    public static void Fetch(PieceDef def, TableContext c)
    {
        if (SourceOf(def, c) is not { } src || !Asked.Add(src.Abs)) return;
        c.S.Assets.EnsureFile(src.Pack, src.Rel, () => _ = Task.Run(() => Parse(src.Abs)));
    }

    public static bool Settled(PieceDef def, TableContext c) => SourceOf(def, c) is not { } src || Ready.ContainsKey(src.Abs);

    static void Parse(string abs)
    {
        try { Read[abs] = RigFile.Read(File.ReadAllBytes(abs)); }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[avatar] the body's rig could not be read: {e.Message}");
            Read[abs] = null;
        }
    }

    static Built? Take(Source src, TableContext c)
    {
        if (Ready.TryGetValue(src.Abs, out var done)) return done;
        if (Read.TryRemove(src.Abs, out var rig)) return Ready[src.Abs] = rig == null ? null : Make(rig);
        if (!File.Exists(src.Abs) && c.S.Assets.Waits.KnownMissing(src.Abs)) Ready[src.Abs] = null;
        return null;
    }

    Built Looked(CrowdLook look)
    {
        (_grip, _hold) = (CrowdFigure.GripOf(look), CrowdFigure.Hold(look));
        var (rig, shape) = CrowdFigure.Whole(look);
        var made = Make(rig);
        return made with { Mesh = CrowdFigures.Mesh(rig, shape, look, skinned: true) };
    }

    static Built Make(Rig rig)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = rig.Points.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = rig.Normals.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = rig.Coords.Select(uv => new Vector2(uv.X, uv.Y)).ToArray();
        arrays[(int)Mesh.ArrayType.Bones] = rig.Joints.Select(j => (int)j).ToArray();
        arrays[(int)Mesh.ArrayType.Weights] = rig.Weights;
        arrays[(int)Mesh.ArrayType.Index] = rig.Triangles;
        var mesh = new ArrayMesh();
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
        var (points, turns, scales) = rig.Rest();
        var skin = new Skin();
        for (int i = 0; i < rig.Bones.Count; i++)
            skin.AddBind(i, new Transform3D(new Basis(RigImport.ToGodot(turns[i])).Scaled(Vector3.One * scales[i]), RigImport.ToGodot(points[i])).AffineInverse());
        var rest = rig.Bones.Select(b => new Transform3D(new Basis(RigImport.ToGodot(b.Rotation)).Scaled(Vector3.One * b.Scale), RigImport.ToGodot(b.Position))).ToArray();
        return new Built(rig, mesh, skin, rest);
    }

    public static Transform3D PlaceOf(RigPlace p) => new(new Basis(RigImport.ToGodot(p.Turn)).Scaled(Vector3.One * p.Scale), RigImport.ToGodot(p.Offset));

    public void Step(Vector3 feet, float height, float delta)
    {
        if (_source == null && _look == null || !GodotObject.IsInstanceValid(body)) return;
        if ((_look != null ? _looked ??= Looked(_look) : Take(_source!, c)) is not { } built)
        {
            Fetch(body.Def!, c);
            return;
        }
        _clock.Step(feet, height, delta, true);
        _actSeconds = Act == _actWas ? _actSeconds + delta : 0;
        _actWas = Act;
        if (!Show(built)) return;
        if (!Due(height)) return;
        float run = _clock.Moving * _clock.Running, walk = _clock.Moving - run;
        if (delta > 0)
        {
            float pace = _was is { } was ? new Vector2(feet.X - was.X, feet.Z - was.Z).Length() / delta / Mathf.Max(height, 1e-6f) : 0;
            _was = feet;
            _fly = Mathf.Lerp(_fly, Aloft ? 1 : 0, 1 - Mathf.Exp(-delta * FlyEase));
            _glide = Mathf.Lerp(_glide, Mathf.Clamp(pace / GlideHeights, 0, 1), 1 - Mathf.Exp(-delta * GlideEase));
            _pulse = Mathf.MoveToward(_pulse, 0, delta / PulseSeconds);
        }
        float ground = 1 - _fly, tuck = Mathf.Sin(_pulse * Mathf.Pi);
        string? act = _clock.Moving > StillShare || _fly > StillShare ? RigMotion.Parts(Act).Arm : Act;
        _motion!.Pose(new RigMove(walk * ground, _clock.WalkPhase, run * ground, _clock.RunPhase, (1 - _clock.Moving) * ground, _clock.IdlePhase, _fly, _glide, tuck, act, _actSeconds,
            _hold));
        var points = _motion.Points;
        var turns = _motion.Turns;
        if (_look != null) CrowdGrip.Hold(built.Rig, points, turns, _grip, act, (float)_look.HeightM);
        for (int i = 0; i < _motion.Bones; i++)
        {
            _skeleton!.SetBonePosePosition(i, RigImport.ToGodot(points[i]));
            _skeleton.SetBonePoseRotation(i, RigImport.ToGodot(turns[i]));
        }
        _posed = true;
    }

    bool Due(float height)
    {
        ++_frame;
        if (!_posed) return true;
        if (_shown == null || c.Scene.GetViewport()?.GetCamera3D() is not { } eye) return true;
        var middle = _shown.GlobalPosition + Vector3.Up * (height / 2);
        float away = eye.GlobalPosition.DistanceTo(middle) / Mathf.Max(height, 1e-6f);
        _shown.CastShadow = away < ShadowHeights && body.Model is { } model ? model.CastShadow : GeometryInstance3D.ShadowCastingSetting.Off;
        if (away > FarHeights || !eye.IsPositionInFrustum(middle)) return false;
        return away < NearHeights || _frame % FarEvery == 0;
    }

    bool Show(Built built)
    {
        if (body.Model is not { } model || !GodotObject.IsInstanceValid(model) || model.Mesh == null) return false;
        if (_holder == null || !GodotObject.IsInstanceValid(_holder) || _holder.GetParent() != model.GetParent())
        {
            _holder?.QueueFree();
            _holder = new Node3D();
            _skeleton = new Skeleton3D { Transform = PlaceOf(built.Rig.Root) };
            for (int i = 0; i < built.Rig.Bones.Count; i++)
            {
                _skeleton.AddBone($"b{i}");
                _skeleton.SetBoneParent(i, built.Rig.Bones[i].Parent);
                _skeleton.SetBoneRest(i, built.Rest[i]);
            }
            _skeleton.ResetBonePoses();
            _shown = new MeshInstance3D { Mesh = built.Mesh, Skin = built.Skin, CastShadow = model.CastShadow };
            _skeleton.AddChild(_shown);
            _shown.Skeleton = "..";
            _holder.AddChild(_skeleton);
            model.AddSibling(_holder);
            _motion = null;
            _posed = false;
        }
        if (_motion == null || _gait != (Style ?? body.Def?.Gait))
        {
            _gait = Style ?? body.Def?.Gait;
            _motion = new RigMotion(built.Rig, _gait);
        }
        _shown!.MaterialOverride = model.MaterialOverride;
        if (model.GetSurfaceOverrideMaterialCount() > 0) _shown.SetSurfaceOverrideMaterial(0, model.GetSurfaceOverrideMaterial(0));
        _holder.Transform = model.Transform;
        _holder.Visible = true;
        model.Visible = false;
        return true;
    }

    public void Stop()
    {
        if (_holder != null && GodotObject.IsInstanceValid(_holder)) _holder.QueueFree();
        (_holder, _skeleton, _shown, _motion) = (null, null, null, null);
        if (GodotObject.IsInstanceValid(body) && body.Model is { } model && GodotObject.IsInstanceValid(model)) model.Visible = true;
    }
}
