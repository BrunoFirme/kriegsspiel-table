using System.Numerics;
using System.Text;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class ModelFilesTests
{
    readonly string _dir = TestFolders.New();

    string Write(string name, string text)
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void An_obj_s_faces_become_clockwise_triangles_with_their_materials_and_pictures()
    {
        Write("tex/wood.png", "a picture");
        Write("a.mtl", "newmtl wood\nKd 0.8 0.6 0.3\nmap_Kd tex\\wood.png\nnewmtl lost\nmap_Kd C:\\elsewhere\\gone.png\n");
        var m = ModelFiles.Obj(Write("a.obj", "mtllib a.mtl\nv 0 0 0 1 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0.25\nusemtl wood\nf 1/1 2/1 3/1 4/1\nusemtl lost\nf -4 -3 -2\n"));
        Assert.Equal(3, m.Mat.Count);
        Assert.Equal(new[] { 0, 0, 1 }, m.Mat);
        Assert.Equal(new[] { new Vector3(0, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, m.V.Take(3));
        Assert.Equal(new Vector2(0, 0.75f), m.UV[0]);
        Assert.Equal(new Rgba(1, 0, 0), m.C[0]);
        Assert.Equal(Rgba.White, m.C[1]);
        Assert.Equal(new ModelMaterial(new Rgba(0.8f, 0.6f, 0.3f), Path.Combine(_dir, "tex", "wood.png")), m.Materials[0]);
        Assert.Equal(new ModelMaterial(Rgba.White, null), m.Materials[1]);
    }

    [Fact]
    public void A_corner_that_points_past_the_list_is_at_the_origin_rather_than_an_error()
    {
        var m = ModelFiles.Obj(Write("b.obj", "v 1 1 1\nv 2 2 2\nf 1 2 99\n"));
        Assert.Equal(Vector3.Zero, m.V[1]);
        Assert.Equal(new ModelMaterial(Rgba.White, null), Assert.Single(m.Materials));
    }

    [Fact]
    public void An_stl_is_stood_up_y_up_with_a_normal_per_face_in_either_form()
    {
        var text = ModelFiles.Stl(Encoding.ASCII.GetBytes("solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid\n"));
        var bin = new MemoryStream();
        using (var w = new BinaryWriter(bin, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(new byte[80]);
            w.Write(5u);
            foreach (var f in new float[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0 }) w.Write(f);
            w.Write((ushort)0);
        }
        var binary = ModelFiles.Stl(bin.ToArray());
        foreach (var m in new[] { text, binary })
        {
            Assert.Equal(3, m.V.Count);
            Assert.Equal(new Vector3(0, 0, -1), m.V[1]);
            Assert.All(m.N, n => Assert.Equal(new Vector3(0, 1, 0), n));
            Assert.Equal(new Rgba(0.75f, 0.74f, 0.70f), m.Materials[0].Color);
        }
    }

    [Fact]
    public void A_ply_reads_its_colours_and_texture_places_and_refuses_big_endian()
    {
        var m = ModelFiles.Ply(Encoding.ASCII.GetBytes(
            "ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\n" +
            "property float s\nproperty float t\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0 255 0 0 0 0\n1 0 0 0 255 0 1 0\n0 1 0 0 0 255 0 1\n3 0 1 2\n"));
        Assert.Equal(new[] { new Rgba(1, 0, 0), new Rgba(0, 0, 1), new Rgba(0, 1, 0) }, m.C);
        Assert.Equal(new Vector2(0, 0), m.UV[1]);
        Assert.Equal(Rgba.White, m.Materials[0].Color);
        Assert.Equal("big-endian PLY is not supported", Assert.Throws<NotSupportedException>(() => ModelFiles.Ply(Encoding.ASCII.GetBytes("ply\nformat binary_big_endian 1.0\nend_header\n"))).Message);
        Assert.Equal("not a PLY file", Assert.Throws<InvalidDataException>(() => ModelFiles.Ply(new byte[] { 1, 2, 3 })).Message);
    }

    [Fact]
    public void A_ply_that_claims_more_than_it_holds_is_refused_before_anything_is_made()
    {
        static byte[] Ply(string header, string data) => Encoding.ASCII.GetBytes("ply\nformat ascii 1.0\n" + header + "end_header\n" + data);
        Assert.Throws<InvalidDataException>(() => ModelFiles.Ply(Ply("element vertex 2000000000\nproperty float x\n", "0\n")));
        Assert.Throws<InvalidDataException>(() => ModelFiles.Ply(Ply("element vertex 2000000000\n", "")));
        Assert.Throws<InvalidDataException>(() => ModelFiles.Ply(Ply(
            "element vertex 1\nproperty float x\nelement face 1\nproperty list int int vertex_indices\n", "0\n2000000000 0\n")));
    }

    static byte[] BinaryStl()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(new byte[80]);
        w.Write(2u);
        for (int t = 0; t < 2; t++)
        {
            for (int k = 0; k < 12; k++) w.Write((float)(k % 3 == t ? 1 : 0));
            w.Write((ushort)0);
        }
        return ms.ToArray();
    }

    static byte[] BinaryPly()
    {
        var head = Encoding.ASCII.GetBytes("ply\nformat binary_little_endian 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n");
        var ms = new MemoryStream();
        ms.Write(head);
        var w = new BinaryWriter(ms);
        foreach (var f in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) w.Write(f);
        w.Write((byte)3);
        w.Write(0);
        w.Write(1);
        w.Write(2);
        return ms.ToArray();
    }

    [Fact]
    public void A_model_file_over_the_size_limit_is_refused_before_it_is_read()
    {
        var path = Write("huge.stl", "solid x\n");
        using (var f = new FileStream(path, FileMode.Open)) f.SetLength(KTable.Core.ContentLimits.MaxModelBytes + 1);
        Assert.Throws<KTable.Core.TooLargeException>(() => ModelFiles.Bytes(path));
        File.Delete(path);
    }

    [Fact]
    public void A_damaged_file_is_read_or_refused_never_hung_on()
    {
        var good = new[]
        {
            Encoding.ASCII.GetBytes("solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid\n"),
            Encoding.ASCII.GetBytes("ply\nformat ascii 1.0\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n0 0 0\n1 0 0\n0 1 0\n3 0 1 2\n"),
            Encoding.ASCII.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nf 1/1 2/1 3/1\n"),
            BinaryStl(),
            BinaryPly(),
        };
        var rng = new Random(1805);
        for (int round = 0; round < 3000; round++)
        {
            int which = round % good.Length;
            var bytes = (byte[])good[which].Clone();
            if (round % 7 == 0) Array.Resize(ref bytes, rng.Next(1, bytes.Length));
            bool binary = which >= 3;
            for (int k = rng.Next(1, 4); k > 0; k--) bytes[rng.Next(bytes.Length)] = binary ? (byte)rng.Next(256) : (byte)rng.Next(32, 127);
            ModelMesh? m = null;
            try
            {
                m = which switch
                {
                    0 or 3 => ModelFiles.Stl(bytes),
                    1 or 4 => ModelFiles.Ply(bytes),
                    _ => ModelFiles.Obj(Write($"f{round}.obj", Encoding.ASCII.GetString(bytes))),
                };
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException) { }
            if (m == null) continue;
            Assert.Equal(m.V.Count, m.N.Count);
            Assert.Equal(m.V.Count, m.UV.Count);
            Assert.Equal(m.V.Count, m.C.Count);
            Assert.Equal(m.V.Count, m.Mat.Count * 3);
            Assert.All(m.Mat, i => Assert.InRange(i, 0, m.Materials.Count - 1));
        }
    }
}
