using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;
using N = System.Numerics;

namespace KTable.App;

static class RigImport
{
    public const int MostTriangles = 150_000;
    const float SmallestMetres = 0.4f, LargestMetres = 3.2f;

    public sealed record Result(MeshData Shape, Rig Rig, Image Picture, float Metres);

    const int Skinned = -1, Loose = -2;

    sealed record Part(Node Node, Mesh Mesh, Func<int, Material?> Look, Skin? Skin, Skeleton3D? Skeleton, int Bone, Transform3D At);

    sealed class Soup
    {
        public readonly MeshData Mesh = new();
        public readonly List<int> Source = new();
        public readonly List<N.Vector3> Points = new(), Normals = new();
        public readonly List<ushort> Joints = new();
        public readonly List<float> Weights = new();
    }

    public static Result? Read(string path, out string? error)
    {
        error = null;
        if (!ModelImport.IsScene(path)) return null;
        Node? root = null;
        try
        {
            root = ModelImport.Scene(path);
            var parts = Parts(root, root, Transform3D.Identity).ToList();
            var skeleton = parts.Where(p => p.Skeleton != null && p.Bone == Skinned).GroupBy(p => p.Skeleton!).MaxBy(g => g.Sum(p => Corners(p.Mesh)))?.Key;
            if (skeleton == null) return null;
            if (skeleton.GetBoneCount() > RigFile.MostBones) { error = $"{skeleton.GetBoneCount():#,0} bones, a body takes {RigFile.MostBones:#,0} at most"; return null; }
            var at = SceneAt(skeleton, root);
            var order = Ordered(skeleton);
            var index = new int[order.Count];
            for (int i = 0; i < order.Count; i++) index[order[i]] = i;
            var bones = order.Select(b => Bone(skeleton, b, index)).ToList();
            var soup = new Soup();
            int source = 0;
            foreach (var part in parts.Where(p => Belongs(p, skeleton)))
                source = Add(soup, part, skeleton, at, index, source);
            if (soup.Mesh.Tris == 0) return null;
            if (soup.Mesh.Tris > MostTriangles) { error = $"{soup.Mesh.Tris:#,0} triangles, a body takes {MostTriangles:#,0} at most"; return null; }
            var rig = Provisional(soup, bones, Place(at));
            rig.Clips.AddRange(Clips(root, skeleton, index, rig));
            Settle(rig, out float metres);
            var (shapePoints, shapeNormals) = new RigMotion(rig).RelaxedShape();
            var corner = Corners(soup);
            for (int k = 0; k < corner.Length; k++)
            {
                soup.Mesh.V[k] = ToGodot(shapePoints[corner[k]]);
                soup.Mesh.N[k] = ToGodot(shapeNormals[corner[k]]);
            }
            var picture = ModelImport.Unwrap(soup.Mesh, ModelStudio.TexSizeFor(soup.Mesh));
            return new Result(soup.Mesh, Final(rig, soup, corner), picture, metres);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or FormatException or ArgumentException or InvalidOperationException)
        {
            error = e.Message;
            return null;
        }
        finally
        {
            root?.Free();
        }
    }

    static bool Belongs(Part p, Skeleton3D skeleton) => p.Skeleton == null || p.Skeleton == skeleton;

    static int Corners(Mesh m) => Enumerable.Range(0, m.GetSurfaceCount()).Sum(s => m.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length);

    static IEnumerable<Part> Parts(Node n, Node root, Transform3D parent)
    {
        var t = n is Node3D n3 ? parent * n3.Transform : parent;
        Mesh? mesh = null;
        Func<int, Material?> look = _ => null;
        Skin? skin = null;
        NodePath? skeletonPath = null;
        if (n is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
        {
            (mesh, skin, skeletonPath) = (mi.Mesh, mi.Skin, mi.Skeleton);
            look = s => mi.GetActiveMaterial(s);
        }
        else if (n is ImporterMeshInstance3D imi && imi.Mesh != null && imi.Visible)
        {
            var am = imi.Mesh.GetMesh();
            (mesh, skin, skeletonPath) = (am, imi.Skin, imi.SkeletonPath);
            look = s => am.SurfaceGetMaterial(s);
        }
        if (mesh != null)
        {
            var skeleton = skeletonPath is { IsEmpty: false } && n.GetNodeOrNull(skeletonPath) is Skeleton3D sk ? sk : null;
            bool skinned = skeleton != null && Enumerable.Range(0, mesh.GetSurfaceCount()).Any(i => mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Bones].VariantType != Variant.Type.Nil);
            var (held, bone) = skinned ? (skeleton, Skinned) : Holder(n);
            yield return new Part(n, mesh, i => look(i) ?? mesh.SurfaceGetMaterial(i), skin, held, bone, t);
        }
        foreach (var c in n.GetChildren())
            foreach (var p in Parts(c, root, t)) yield return p;
    }

    static (Skeleton3D?, int) Holder(Node n)
    {
        for (var up = n.GetParent(); up != null; up = up.GetParent())
        {
            if (up is BoneAttachment3D ba && ba.GetParent() is Skeleton3D s) return (s, ba.BoneIdx >= 0 ? ba.BoneIdx : s.FindBone(ba.BoneName));
            if (up is Skeleton3D sk) return (sk, Loose);
        }
        return (null, Loose);
    }

    static Transform3D SceneAt(Node n, Node root)
    {
        var t = Transform3D.Identity;
        for (var x = n; x != null; x = x.GetParent())
        {
            if (x is Node3D n3) t = n3.Transform * t;
            if (x == root) break;
        }
        return t;
    }

    static List<int> Ordered(Skeleton3D s)
    {
        int n = s.GetBoneCount();
        var order = new List<int>(n);
        var queue = new Queue<int>(Enumerable.Range(0, n).Where(b => s.GetBoneParent(b) < 0));
        var children = Enumerable.Range(0, n).ToLookup(s.GetBoneParent);
        while (queue.Count > 0)
        {
            int b = queue.Dequeue();
            order.Add(b);
            foreach (var c in children[b]) queue.Enqueue(c);
        }
        if (order.Count != n) throw new InvalidDataException("its skeleton loops back on itself");
        return order;
    }

    static RigBone Bone(Skeleton3D s, int b, int[] index)
    {
        var rest = s.GetBoneRest(b);
        var scale = rest.Basis.Scale;
        int parent = s.GetBoneParent(b);
        return new RigBone(s.GetBoneName(b), parent < 0 ? -1 : index[parent], ToNumerics(rest.Origin), ToNumerics(rest.Basis.Orthonormalized().GetRotationQuaternion()),
            (scale.X + scale.Y + scale.Z) / 3);
    }

    static RigPlace Place(Transform3D t)
    {
        var scale = t.Basis.Scale;
        return new RigPlace(ToNumerics(t.Origin), ToNumerics(t.Basis.Orthonormalized().GetRotationQuaternion()), Math.Max(1e-6f, (scale.X + scale.Y + scale.Z) / 3));
    }

    static int Add(Soup soup, Part part, Skeleton3D skeleton, Transform3D skeletonAt, int[] index, int source)
    {
        var toSkeleton = skeletonAt.AffineInverse() * part.At;
        var binds = Binds(part, skeleton);
        int rootBone = part.Bone >= 0 ? index[part.Bone] : 0;
        for (int s = 0; s < part.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = part.Mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (verts.Length == 0) continue;
            bool Has(Mesh.ArrayType kind) => arrays[(int)kind].VariantType != Variant.Type.Nil;
            var norms = Has(Mesh.ArrayType.Normal) ? arrays[(int)Mesh.ArrayType.Normal].AsVector3Array() : [];
            var uvs = Has(Mesh.ArrayType.TexUV) ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array() : [];
            var cols = Has(Mesh.ArrayType.Color) ? arrays[(int)Mesh.ArrayType.Color].AsColorArray() : [];
            var idx = Has(Mesh.ArrayType.Index) ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : Enumerable.Range(0, verts.Length).ToArray();
            var boneIds = Has(Mesh.ArrayType.Bones) ? arrays[(int)Mesh.ArrayType.Bones].AsInt32Array() : [];
            var boneWeights = Has(Mesh.ArrayType.Weights) ? arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array() : [];
            int per = binds != null && boneIds.Length > 0 && boneIds.Length == boneWeights.Length ? boneIds.Length / verts.Length : 0;
            var skinned = new N.Vector3[verts.Length];
            var turned = new N.Vector3[verts.Length];
            var joints = new (ushort, float)[verts.Length][];
            for (int v = 0; v < verts.Length; v++)
            {
                var n = norms.Length > v ? norms[v] : Vector3.Zero;
                if (per > 0)
                {
                    var (p, nn, j) = Weigh(verts[v], n, boneIds, boneWeights, v, per, binds!, index);
                    (skinned[v], turned[v], joints[v]) = (ToNumerics(p), ToNumerics(nn), j);
                }
                else
                {
                    skinned[v] = ToNumerics(toSkeleton * verts[v]);
                    turned[v] = ToNumerics((toSkeleton.Basis.Inverse().Transposed() * n).Normalized());
                    joints[v] = [((ushort)rootBone, 1f)];
                }
            }
            var mat = part.Look(s);
            Image? tex = null;
            var col = Colors.White;
            if (mat is BaseMaterial3D bm) (col, tex) = (bm.AlbedoColor, ModelImport.ImageOf(bm.AlbedoTexture));
            int matIndex = soup.Mesh.Tex.Count;
            soup.Mesh.Tex.Add(tex);
            soup.Mesh.MatColor.Add(col);
            bool flip = per == 0 && toSkeleton.Basis.Determinant() < 0;
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                var tri = flip ? new[] { idx[i], idx[i + 2], idx[i + 1] } : new[] { idx[i], idx[i + 1], idx[i + 2] };
                foreach (var k in tri)
                {
                    soup.Mesh.Add(ToGodot(skinned[k]), ToGodot(turned[k]), uvs.Length > k ? uvs[k] : Vector2.Zero, cols.Length > k ? cols[k] : Colors.White);
                    soup.Source.Add(source + k);
                    soup.Points.Add(skinned[k]);
                    soup.Normals.Add(turned[k]);
                    for (int w = 0; w < Rig.Influences; w++)
                    {
                        var (bone, weight) = w < joints[k].Length ? joints[k][w] : ((ushort)0, 0f);
                        soup.Joints.Add(bone);
                        soup.Weights.Add(weight);
                    }
                }
                soup.Mesh.Mat.Add(matIndex);
            }
            source += verts.Length;
        }
        return source;
    }

    static (int Bone, Transform3D Move)[]? Binds(Part part, Skeleton3D skeleton)
    {
        if (part.Skeleton != skeleton || part.Bone != Skinned) return null;
        if (part.Skin == null)
            return Enumerable.Range(0, skeleton.GetBoneCount()).Select(b => (b, Transform3D.Identity)).ToArray();
        return Enumerable.Range(0, part.Skin.GetBindCount()).Select(i =>
        {
            int b = part.Skin.GetBindBone(i);
            if (b < 0) b = skeleton.FindBone(part.Skin.GetBindName(i));
            return b < 0 ? (0, Transform3D.Identity) : (b, skeleton.GetBoneGlobalRest(b) * part.Skin.GetBindPose(i));
        }).ToArray();
    }

    static (Vector3, Vector3, (ushort, float)[]) Weigh(Vector3 v, Vector3 n, int[] ids, float[] weights, int at, int per, (int Bone, Transform3D Move)[] binds, int[] index)
    {
        var p = Vector3.Zero;
        var nn = Vector3.Zero;
        var held = new Dictionary<int, float>();
        float total = 0;
        for (int k = 0; k < per; k++)
        {
            int bind = ids[at * per + k];
            float w = weights[at * per + k];
            if (w <= 0 || bind < 0 || bind >= binds.Length) continue;
            var (bone, move) = binds[bind];
            p += move * v * w;
            nn += move.Basis * n * w;
            held[index[bone]] = held.GetValueOrDefault(index[bone]) + w;
            total += w;
        }
        if (total <= 0) return (v, n, [((ushort)0, 1f)]);
        var top = held.OrderByDescending(h => h.Value).Take(Rig.Influences).ToList();
        float kept = top.Sum(h => h.Value);
        return (p / total, nn.LengthSquared() > 0 ? nn.Normalized() : n, top.Select(h => ((ushort)h.Key, h.Value / kept)).ToArray());
    }

    static Rig Provisional(Soup soup, List<RigBone> bones, RigPlace root)
    {
        var first = new Dictionary<int, int>();
        var corner = new int[soup.Source.Count];
        var points = new List<N.Vector3>();
        var normals = new List<N.Vector3>();
        var joints = new List<ushort>();
        var weights = new List<float>();
        for (int k = 0; k < corner.Length; k++)
        {
            if (!first.TryGetValue(soup.Source[k], out int u))
            {
                first[soup.Source[k]] = u = points.Count;
                points.Add(soup.Points[k]);
                normals.Add(soup.Normals[k]);
                for (int w = 0; w < Rig.Influences; w++)
                {
                    joints.Add(soup.Joints[k * Rig.Influences + w]);
                    weights.Add(soup.Weights[k * Rig.Influences + w]);
                }
            }
            corner[k] = u;
        }
        var rig = new Rig
        {
            Bones = bones, Root = root, Points = points.ToArray(), Normals = normals.ToArray(), Coords = new N.Vector2[points.Count],
            Joints = joints.ToArray(), Weights = weights.ToArray(), Triangles = corner,
        };
        foreach (var (role, bone) in RigHumanoid.Map(bones)) rig.Humanoid[role] = bone;
        return rig;
    }

    static int[] Corners(Soup soup)
    {
        var first = new Dictionary<int, int>();
        var corner = new int[soup.Source.Count];
        for (int k = 0; k < corner.Length; k++)
        {
            if (!first.TryGetValue(soup.Source[k], out int u)) first[soup.Source[k]] = u = first.Count;
            corner[k] = u;
        }
        return corner;
    }

    static void Settle(Rig rig, out float metres)
    {
        var facing = new RigMotion(rig).FacingInModel();
        rig.Root = rig.Root.Then(new RigPlace(N.Vector3.Zero, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.Atan2(facing.X, facing.Z)), 1));
        var (points, _) = new RigMotion(rig).RelaxedShape();
        var lo = new N.Vector3(float.MaxValue);
        var hi = new N.Vector3(float.MinValue);
        foreach (var p in points) (lo, hi) = (N.Vector3.Min(lo, p), N.Vector3.Max(hi, p));
        float tall = hi.Y - lo.Y;
        if (tall <= 1e-6f) throw new InvalidDataException("it has no height");
        float scale = tall is >= SmallestMetres and <= LargestMetres ? 1 : (float)AvatarPose.PersonMetres / tall;
        var foot = new N.Vector3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2);
        rig.Root = rig.Root.Then(new RigPlace(-foot * scale, N.Quaternion.Identity, scale));
        metres = tall * scale;
    }

    static Rig Final(Rig rig, Soup soup, int[] corner)
    {
        var welded = new Dictionary<(int, Vector2), int>();
        var points = new List<N.Vector3>();
        var normals = new List<N.Vector3>();
        var coords = new List<N.Vector2>();
        var joints = new List<ushort>();
        var weights = new List<float>();
        var triangles = new int[corner.Length];
        for (int k = 0; k < corner.Length; k++)
        {
            var uv = soup.Mesh.UV[k];
            if (!welded.TryGetValue((corner[k], uv), out int at))
            {
                welded[(corner[k], uv)] = at = points.Count;
                points.Add(rig.Points[corner[k]]);
                normals.Add(rig.Normals[corner[k]]);
                coords.Add(new N.Vector2(uv.X, uv.Y));
                for (int w = 0; w < Rig.Influences; w++)
                {
                    joints.Add(rig.Joints[corner[k] * Rig.Influences + w]);
                    weights.Add(rig.Weights[corner[k] * Rig.Influences + w]);
                }
            }
            triangles[k] = at;
        }
        return new Rig
        {
            Bones = rig.Bones, Root = rig.Root, Points = points.ToArray(), Normals = normals.ToArray(), Coords = coords.ToArray(),
            Joints = joints.ToArray(), Weights = weights.ToArray(), Triangles = triangles, Humanoid = rig.Humanoid, Clips = rig.Clips,
        };
    }

    static IEnumerable<RigClip> Clips(Node root, Skeleton3D skeleton, int[] index, Rig rig)
    {
        long keys = 0;
        var (_, restTurns, _) = rig.Rest();
        int hips = rig.Humanoid.GetValueOrDefault(RigHumanoid.Hips, -1);
        var up = rig.Root.Back(N.Vector3.UnitY);
        foreach (var player in Nodes<AnimationPlayer>(root))
        {
            var from = player.GetNodeOrNull(player.RootNode) ?? player.GetParent();
            if (from == null) continue;
            foreach (var name in player.GetAnimationList())
            {
                var anim = player.GetAnimation(name);
                if (anim == null || anim.Length <= 0) continue;
                var tracks = new List<RigTrack>();
                for (int t = 0; t < anim.GetTrackCount(); t++)
                {
                    var kind = anim.TrackGetType(t);
                    if (kind is not (Animation.TrackType.Position3D or Animation.TrackType.Rotation3D)) continue;
                    var path = anim.TrackGetPath(t);
                    if (from.GetNodeOrNull(new NodePath(path.GetConcatenatedNames())) != skeleton) continue;
                    int bone = skeleton.FindBone(path.GetConcatenatedSubNames());
                    int count = anim.TrackGetKeyCount(t);
                    if (bone < 0 || count == 0 || (keys += count) > RigFile.MostKeys) continue;
                    int b = index[bone];
                    var times = new float[count];
                    bool turn = kind == Animation.TrackType.Rotation3D;
                    var values = new float[count * (turn ? 4 : 3)];
                    for (int k = 0; k < count; k++)
                    {
                        times[k] = (float)anim.TrackGetKeyTime(t, k);
                        var value = anim.TrackGetKeyValue(t, k);
                        if (turn)
                        {
                            var q = value.AsQuaternion();
                            (values[k * 4], values[k * 4 + 1], values[k * 4 + 2], values[k * 4 + 3]) = (q.X, q.Y, q.Z, q.W);
                        }
                        else
                        {
                            var p = value.AsVector3();
                            (values[k * 3], values[k * 3 + 1], values[k * 3 + 2]) = (p.X, p.Y, p.Z);
                        }
                    }
                    if (!turn && b == hips) InPlace(values, rig.Bones[b].Parent is >= 0 and var parent ? N.Vector3.Transform(up, N.Quaternion.Inverse(restTurns[parent])) : up);
                    tracks.Add(new RigTrack(b, turn ? RigChannel.Rotation : RigChannel.Position, times, values));
                }
                if (tracks.Count > 0 && rig.Clips.Count < RigFile.MostClips) yield return new RigClip(name.Length > RigFile.MostName ? name[..RigFile.MostName] : name, (float)anim.Length, tracks);
            }
        }
    }

    static void InPlace(float[] values, N.Vector3 up)
    {
        if (values.Length < 3) return;
        up = N.Vector3.Normalize(up);
        var first = new N.Vector3(values[0], values[1], values[2]);
        for (int k = 0; k < values.Length / 3; k++)
        {
            var p = new N.Vector3(values[k * 3], values[k * 3 + 1], values[k * 3 + 2]);
            var drift = p - first;
            var kept = first + N.Vector3.Dot(drift, up) * up;
            (values[k * 3], values[k * 3 + 1], values[k * 3 + 2]) = (kept.X, kept.Y, kept.Z);
        }
    }

    static IEnumerable<T> Nodes<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var x in Nodes<T>(c)) yield return x;
    }

    public static N.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);

    public static N.Quaternion ToNumerics(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public static Vector3 ToGodot(N.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Quaternion ToGodot(N.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
}
