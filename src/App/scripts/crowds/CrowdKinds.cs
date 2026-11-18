using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CrowdKinds(TableContext c)
{
    public const int Stand = 0, StrideA = 1, StrideB = 2, ArmsUp = 3;
    static readonly string[] Raised = [RigMotion.Cheer, RigMotion.Yield, RigMotion.Throw, RigMotion.Carry];

    public sealed class Kind
    {
        public required Rig Rig;
        public required float Tall;
        public required ArrayMesh[] Frozen;
        public required ArrayMesh Skinned;
        public required Material Material;
    }

    readonly Dictionary<string, Kind?> _ready = new();
    readonly ConcurrentDictionary<string, Rig?> _read = new();
    readonly HashSet<string> _asked = new();

    public Kind? Of(string defId)
    {
        if (_ready.TryGetValue(defId, out var known)) return known;
        if (c.S.Store.Def(defId) is not { Rig: { } key } def || c.S.PackOfDef(defId) is not { } pack || !pack.Assets.TryGetValue(key, out var asset)) return _ready[defId] = null;
        if (_asked.Add(defId))
        {
            var abs = c.S.Assets.Abs(pack.Id, asset.File);
            c.S.Assets.EnsureFile(pack.Id, asset.File, () => _ = Task.Run(() => _read[defId] = Read(abs)), () => _read[defId] = null);
        }
        if (!_read.TryRemove(defId, out var rig)) return null;
        var texture = def.Texture is { } tex && pack.Assets.TryGetValue(tex, out var picture) ? c.S.Assets.Texture(pack.Id, picture.File, null) : null;
        return _ready[defId] = rig == null ? null : Build(rig, new StandardMaterial3D { AlbedoTexture = texture, Roughness = 1 });
    }

    static Rig? Read(string abs)
    {
        try { return RigFile.Read(File.ReadAllBytes(abs)); }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[crowds] a person's kind could not be read: {e.Message}");
            return null;
        }
    }

    public static int Pose(bool moving, float phase, string? act) =>
        moving ? phase < 0.5f ? StrideA : StrideB : act != null && Raised.Contains(act) ? ArmsUp : Stand;

    public static Kind Build(Rig rig, Material material)
    {
        float tall = Math.Max(1e-3f, rig.Points.Max(p => p.Y) - rig.Points.Min(p => p.Y));
        var motion = new RigMotion(rig, RigMotion.Natural);
        ArrayMesh Frozen(RigMove move)
        {
            motion.Pose(move);
            var (points, normals) = rig.Posed(motion.Points, motion.Turns);
            return Mesh(rig, points.Select(p => p / tall).ToArray(), normals, false);
        }
        var frozen = new[]
        {
            Frozen(new RigMove(0, 0, 0, 0, 1, 0)), Frozen(new RigMove(1, 0.25f, 0, 0, 0, 0)), Frozen(new RigMove(1, 0.75f, 0, 0, 0, 0)),
            Frozen(new RigMove(0, 0, 0, 0, 1, 0, Act: RigMotion.Cheer, ActSeconds: 0.5f)),
        };
        return new Kind { Rig = rig, Tall = tall, Frozen = frozen, Skinned = Mesh(rig, rig.Points, rig.Normals, true), Material = material };
    }

    static ArrayMesh Mesh(Rig rig, System.Numerics.Vector3[] points, System.Numerics.Vector3[] normals, bool skinned)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = points.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normals.Select(RigImport.ToGodot).ToArray();
        arrays[(int)Godot.Mesh.ArrayType.TexUV] = rig.Coords.Select(uv => new Vector2(uv.X, uv.Y)).ToArray();
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
