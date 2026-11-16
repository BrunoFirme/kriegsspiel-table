using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace KTable.Core.Model;

public static class RigFile
{
    public const string Kind = "rig";
    public const int MostBones = 1024, MostPoints = 1_000_000, MostCorners = 3_000_000, MostClips = 256, MostTracks = 8192, MostKeys = 4_000_000, MostName = 256;
    static readonly byte[] Magic = "KTRIG1\n"u8.ToArray();

    public static bool Is(ReadOnlySpan<byte> head) => head.Length >= Magic.Length && head[..Magic.Length].SequenceEqual(Magic);

    public static byte[] Write(Rig rig)
    {
        using var file = new MemoryStream();
        file.Write(Magic);
        using (var packed = new ZLibStream(file, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var w = new BinaryWriter(packed, Encoding.UTF8))
        {
            w.Write(rig.Bones.Count);
            foreach (var b in rig.Bones)
            {
                w.Write(b.Name);
                w.Write(b.Parent);
                Put(w, b.Position);
                Put(w, b.Rotation);
                w.Write(b.Scale);
            }
            Put(w, rig.Root.Offset);
            Put(w, rig.Root.Turn);
            w.Write(rig.Root.Scale);
            w.Write(rig.Points.Length);
            foreach (var p in rig.Points) Put(w, p);
            foreach (var n in rig.Normals) Put(w, n);
            foreach (var c in rig.Coords) { w.Write(c.X); w.Write(c.Y); }
            foreach (var j in rig.Joints) w.Write(j);
            foreach (var x in rig.Weights) w.Write(x);
            w.Write(rig.Triangles.Length);
            foreach (var t in rig.Triangles) w.Write(t);
            w.Write(rig.Humanoid.Count);
            foreach (var (role, bone) in rig.Humanoid.OrderBy(h => h.Key, StringComparer.Ordinal))
            {
                w.Write(role);
                w.Write(bone);
            }
            w.Write(rig.Clips.Count);
            foreach (var c in rig.Clips)
            {
                w.Write(c.Name);
                w.Write(c.Length);
                w.Write(c.Tracks.Count);
                foreach (var t in c.Tracks)
                {
                    w.Write(t.Bone);
                    w.Write((byte)t.Channel);
                    w.Write(t.Times.Length);
                    foreach (var x in t.Times) w.Write(x);
                    foreach (var x in t.Values) w.Write(x);
                }
            }
        }
        return file.ToArray();
    }

    public static Rig Read(byte[] file, long most = ContentLimits.MaxModelBytes)
    {
        if (!Is(file)) throw new FormatException("not a rig file");
        try
        {
            using var packed = new ZLibStream(new MemoryStream(file, Magic.Length, file.Length - Magic.Length), CompressionMode.Decompress);
            using var r = new BinaryReader(new Capped(packed, most), Encoding.UTF8);
            int boneCount = Count(r, MostBones);
            var bones = new List<RigBone>(boneCount);
            for (int i = 0; i < boneCount; i++)
            {
                var name = Name(r);
                int parent = r.ReadInt32();
                if (parent < -1 || parent >= i) throw new FormatException("a bone comes before its parent");
                bones.Add(new RigBone(name, parent, Vec3(r), Turn(r), Finite(r.ReadSingle())));
            }
            var root = new RigPlace(Vec3(r), Turn(r), Finite(r.ReadSingle()));
            if (root.Scale <= 0) throw new FormatException("the rig has no size");
            int n = Count(r, MostPoints);
            var points = new Vector3[n];
            var normals = new Vector3[n];
            var coords = new Vector2[n];
            var joints = new ushort[n * Rig.Influences];
            var weights = new float[n * Rig.Influences];
            for (int i = 0; i < n; i++) points[i] = Vec3(r);
            for (int i = 0; i < n; i++) normals[i] = Vec3(r);
            for (int i = 0; i < n; i++) coords[i] = new Vector2(Finite(r.ReadSingle()), Finite(r.ReadSingle()));
            for (int i = 0; i < joints.Length; i++)
                if ((joints[i] = r.ReadUInt16()) >= Math.Max(1, boneCount)) throw new FormatException("a point is tied to a bone the rig lacks");
            for (int i = 0; i < weights.Length; i++)
                if ((weights[i] = Finite(r.ReadSingle())) < 0) throw new FormatException("a point has a negative weight");
            int corners = Count(r, MostCorners);
            if (corners % 3 != 0) throw new FormatException("the triangles are cut short");
            var triangles = new int[corners];
            for (int i = 0; i < corners; i++)
                if ((uint)(triangles[i] = r.ReadInt32()) >= (uint)n) throw new FormatException("a triangle names a point the rig lacks");
            int roles = Count(r, MostBones);
            var humanoid = new Dictionary<string, int>();
            for (int i = 0; i < roles; i++)
            {
                var role = Name(r);
                int bone = r.ReadInt32();
                if ((uint)bone >= (uint)boneCount) throw new FormatException("a body part names a bone the rig lacks");
                humanoid[role] = bone;
            }
            int clipCount = Count(r, MostClips);
            var clips = new List<RigClip>(clipCount);
            long keys = 0;
            for (int c = 0; c < clipCount; c++)
            {
                var name = Name(r);
                float length = Finite(r.ReadSingle());
                int trackCount = Count(r, MostTracks);
                var tracks = new List<RigTrack>(trackCount);
                for (int t = 0; t < trackCount; t++)
                {
                    int bone = r.ReadInt32();
                    if ((uint)bone >= (uint)boneCount) throw new FormatException("a motion moves a bone the rig lacks");
                    var channel = (RigChannel)r.ReadByte();
                    if (channel is not (RigChannel.Position or RigChannel.Rotation)) throw new FormatException("a motion has an unknown kind of track");
                    int count = Count(r, MostKeys);
                    if ((keys += count) > MostKeys) throw new FormatException("the motions hold too many keys");
                    var times = new float[count];
                    for (int i = 0; i < count; i++) times[i] = Finite(r.ReadSingle());
                    var values = new float[count * (channel == RigChannel.Rotation ? 4 : 3)];
                    for (int i = 0; i < values.Length; i++) values[i] = Finite(r.ReadSingle());
                    tracks.Add(new RigTrack(bone, channel, times, values));
                }
                clips.Add(new RigClip(name, length, tracks));
            }
            return new Rig
            {
                Bones = bones, Root = root, Points = points, Normals = normals, Coords = coords, Joints = joints, Weights = weights,
                Triangles = triangles, Humanoid = humanoid, Clips = clips,
            };
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException || e is IOException and not TooLargeException)
        {
            throw new FormatException("the rig file is damaged: " + e.Message, e);
        }
    }

    static int Count(BinaryReader r, int most)
    {
        int n = r.ReadInt32();
        return n >= 0 && n <= most ? n : throw new FormatException("the rig file holds more than a rig may");
    }

    static string Name(BinaryReader r)
    {
        var s = r.ReadString();
        return s.Length <= MostName ? s : throw new FormatException("a name in the rig is too long");
    }

    static float Finite(float x) => float.IsFinite(x) ? x : throw new FormatException("the rig holds a number that is not one");

    static Vector3 Vec3(BinaryReader r) => new(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()));

    static Quaternion Turn(BinaryReader r)
    {
        var q = new Quaternion(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()));
        return q.LengthSquared() > 1e-12f ? Quaternion.Normalize(q) : Quaternion.Identity;
    }

    static void Put(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    static void Put(BinaryWriter w, Quaternion q)
    {
        w.Write(q.X);
        w.Write(q.Y);
        w.Write(q.Z);
        w.Write(q.W);
    }

    sealed class Capped(Stream inner, long most) : Stream
    {
        long _left = most;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int got = inner.Read(buffer, offset, (int)Math.Min(count, Math.Max(1, _left + 1)));
            if ((_left -= got) < 0) throw new TooLargeException("the rig unpacks to more than a model may be");
            return got;
        }
    }
}
