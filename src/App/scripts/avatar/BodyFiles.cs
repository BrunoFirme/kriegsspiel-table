using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Godot;
using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.App;

static class BodyFiles
{
    public const int MostTriangles = 60000;

    public static string? Keep(string path, string storeRoot, out List<string> files, out string? error)
    {
        files = new List<string>();
        var m = ModelImport.Load(path, out error);
        if (m == null || m.Tris == 0)
        {
            error ??= "no triangles in it";
            return null;
        }
        if (m.Tris > MostTriangles)
        {
            error = $"{m.Tris:#,0} triangles, a body takes {MostTriangles:#,0} at most";
            return null;
        }
        var b = m.Bounds();
        if (b.Size.Y <= 1e-6f)
        {
            error = "it has no height";
            return null;
        }
        float s = 1 / b.Size.Y;
        var foot = new Vector3(b.GetCenter().X, b.Position.Y, b.GetCenter().Z);
        m.Transform(new Transform3D(Basis.Identity.Scaled(Vector3.One * s), -foot * s));
        var picture = ModelImport.Unwrap(m, ModelStudio.TexSizeFor(m)).SavePngToBuffer();
        var mesh = Encoding.UTF8.GetBytes(ModelImport.ToObj(m));
        var folder = Path.Combine(storeRoot, UploadAsset.ReliefPackage);
        try
        {
            var meshRel = Write(folder, UploadAsset.WebModels, mesh, "obj");
            var pictureRel = Write(folder, UploadAsset.WebPictures, picture, "png");
            files.Add(meshRel);
            files.Add(pictureRel);
            return AvatarBody.OfModel(meshRel, pictureRel);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }
    }

    public static bool Present(string body, string storeRoot) =>
        AvatarBody.Model(body) is not var (mesh, picture)
        || File.Exists(Path.Combine(storeRoot, UploadAsset.ReliefPackage, mesh)) && (picture == null || File.Exists(Path.Combine(storeRoot, UploadAsset.ReliefPackage, picture)));

    public static Vector2 Shape(string body, string storeRoot)
    {
        if (AvatarBody.Model(body) is not var (mesh, _)) return Vector2.Zero;
        var file = Path.Combine(storeRoot, UploadAsset.ReliefPackage, mesh);
        if (!File.Exists(file)) return Vector2.Zero;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.StartsWith("v ", StringComparison.Ordinal)) continue;
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) continue;
                float x = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture);
                (minX, maxX, minZ, maxZ) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minZ, z), Math.Max(maxZ, z));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { return Vector2.Zero; }
        return maxX > minX && maxZ > minZ ? new Vector2(maxX - minX, maxZ - minZ) : Vector2.Zero;
    }

    public static IEnumerable<string> Of(string body)
    {
        if (AvatarBody.Model(body) is not var (mesh, picture)) yield break;
        yield return mesh;
        if (picture != null) yield return picture;
    }

    static string Write(string folder, string kind, byte[] bytes, string ext)
    {
        var rel = $"{kind}/{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}.{ext}";
        var file = Path.Combine(folder, rel);
        if (File.Exists(file)) return rel;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        KTable.Core.Json.WriteAtomic(file, bytes);
        return rel;
    }
}
