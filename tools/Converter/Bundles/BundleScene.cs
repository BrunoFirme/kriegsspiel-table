using System.Security.Cryptography;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using KTable.Core;

namespace KTable.Converter.Bundles;

public sealed record ModelPart(double[] Verts, double[] Uvs, int[] Triangles, string? Texture);

public sealed record BundleModel(string Sha, string Name, List<ModelPart> Parts, double[] Low, double[] High, int Triangles, List<(string Sha, Picture Picture)> Textures, int Lost);

public sealed class BundleScene
{
    const string MainTexture = "_MainTex";
    const string ArchiveScheme = "archive:/";
    const int SameFile = 0;
    const int Axes = 3;
    const int UvParts = 2;
    const int Corners = 4;

    public static bool OnlyThisBundle(Exception e) =>
        e is not (OutOfMemoryException or StackOverflowException or OperationCanceledException);

    public static (BundleModel? Model, int Lost, string? Failed) Read(string path, string sha, IReadOnlyDictionary<string, string>? shared = null)
    {
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(path, true);
            var names = bundle.file.GetAllFileNames();
            var loaded = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            int lost = 0;
            for (int index = 0; index < names.Count; index++)
            {
                if (!bundle.file.IsAssetsFile(index)) continue;
                var file = manager.LoadAssetsFileFromBundle(bundle, index, false);
                if (shared != null) LoadNeeded(manager, file, shared, loaded);
                var found = Scene(manager, bundle, file, sha);
                lost += found.Lost;
                if (found.Model != null) return (found.Model, lost, null);
            }
            return (null, lost, null);
        }
        catch (Exception e) when (OnlyThisBundle(e))
        {
            return (null, 0, $"{e.GetType().Name}: {e.Message}");
        }
        finally { BestEffort.Try(() => manager.UnloadAll(true)); }
    }

    public static List<string> FilesIn(string path)
    {
        var manager = new AssetsManager();
        try { return manager.LoadBundleFile(path, false).file.GetAllFileNames(); }
        catch (Exception e) when (OnlyThisBundle(e)) { return new List<string>(); }
        finally { BestEffort.Try(() => manager.UnloadAll(true)); }
    }

    static void LoadNeeded(AssetsManager manager, AssetsFileInstance file, IReadOnlyDictionary<string, string> shared, HashSet<string> loaded)
    {
        foreach (var external in file.file.Metadata.Externals)
        {
            var name = external.PathName;
            if (!name.StartsWith(ArchiveScheme, StringComparison.Ordinal)) continue;
            name = name[(name.LastIndexOf('/') + 1)..];
            if (loaded.Contains(name) || !shared.TryGetValue(name, out var path)) continue;
            var bundle = manager.LoadBundleFile(path, true);
            var names = bundle.file.GetAllFileNames();
            loaded.UnionWith(names);
            for (int index = 0; index < names.Count; index++)
                if (bundle.file.IsAssetsFile(index)) LoadNeeded(manager, manager.LoadAssetsFileFromBundle(bundle, index, false), shared, loaded);
        }
    }

    static (AssetsFileInstance File, AssetTypeValueField Field)? Follow(AssetsManager manager, AssetsFileInstance from, AssetTypeValueField? pointer)
    {
        if (pointer == null || pointer.IsDummy || pointer["m_PathID"].AsLong == 0) return null;
        int fileId = pointer["m_FileID"].AsInt;
        if (fileId != SameFile && (fileId > from.file.Metadata.Externals.Count || from.GetDependency(manager, fileId - 1) == null)) return null;
        var found = manager.GetExtAsset(from, fileId, pointer["m_PathID"].AsLong);
        return found.file == null || found.baseField == null ? null : (found.file, found.baseField);
    }

    static int Major(string version) =>
        int.TryParse(new string(version.TakeWhile(char.IsDigit).ToArray()), out var major) ? major : 0;

    static byte[]? SideFile(BundleFileInstance bundle, AssetTypeValueField streamData)
    {
        if (streamData.IsDummy) return null;
        var name = streamData["path"].AsString;
        if (string.IsNullOrEmpty(name)) return null;
        if (name.StartsWith(ArchiveScheme, StringComparison.Ordinal)) name = name[(name.LastIndexOf('/') + 1)..];
        int at = bundle.file.GetFileIndex(name);
        if (at < 0) return null;
        bundle.file.GetFileRange(at, out long start, out long _);
        long offset = streamData["offset"].AsLong;
        int size = streamData["size"].AsInt;
        if (size <= 0) return null;
        var reader = bundle.file.DataReader;
        reader.Position = start + offset;
        return reader.ReadBytes(size);
    }

    static (BundleModel? Model, int Lost) Scene(AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance file, string sha)
    {
        int major = Major(file.file.Metadata.UnityVersion);
        var transforms = new Dictionary<long, AssetTypeValueField>();
        var objects = new Dictionary<long, AssetTypeValueField>();
        var transformOf = new Dictionary<long, long>();
        var meshOf = new Dictionary<long, AssetTypeValueField>();
        var order = new List<long>();
        var renderers = new List<(AssetFileInfo Info, bool Skinned)>();

        foreach (var info in file.file.AssetInfos)
            switch ((AssetClassID)info.TypeId)
            {
                case AssetClassID.Transform:
                    if (manager.GetBaseField(file, info) is { } transform)
                    {
                        transforms[info.PathId] = transform;
                        order.Add(info.PathId);
                        if (Points(transform["m_GameObject"]) is { } owner) transformOf[owner] = info.PathId;
                    }
                    break;
                case AssetClassID.GameObject:
                    if (manager.GetBaseField(file, info) is { } go) objects[info.PathId] = go;
                    break;
                case AssetClassID.MeshFilter:
                    if (manager.GetBaseField(file, info) is { } filter && Points(filter["m_GameObject"]) is { } holder)
                        meshOf[holder] = filter["m_Mesh"];
                    break;
                case AssetClassID.MeshRenderer:
                    renderers.Add((info, false));
                    break;
                case AssetClassID.SkinnedMeshRenderer:
                    renderers.Add((info, true));
                    break;
            }
        if (transforms.Count == 0) return (null, 0);

        var world = new Dictionary<long, Mat4>();

        long? Father(long id) => Points(transforms[id]["m_Father"]) is { } up && transforms.ContainsKey(up) ? up : null;

        Mat4 WorldOf(long id)
        {
            if (world.TryGetValue(id, out var found)) return found;
            var t = transforms[id];
            var local = Mat4.Trs(Quat(t["m_LocalRotation"]), Vec(t["m_LocalPosition"]), Vec(t["m_LocalScale"]));
            var up = Father(id);
            world[id] = up == null ? local : WorldOf(up.Value).Times(local);
            return world[id];
        }

        bool Shown(long id)
        {
            var t = transforms[id];
            if (Points(t["m_GameObject"]) is { } owner && objects.TryGetValue(owner, out var go) && !go["m_IsActive"].AsBool) return false;
            var up = Father(id);
            return up == null || Shown(up.Value);
        }

        var roots = order.Where(id => Father(id) == null).ToList();
        var origin = roots.Count > 0 ? WorldOf(roots[0]).Translation : (0d, 0d, 0d);
        string name = Named(roots, transforms, objects) ?? sha;

        var parts = new List<ModelPart>();
        var textures = new List<(string Sha, Picture Picture)>();
        int lost = 0;
        foreach (var (info, skinned) in renderers)
        {
            try
            {
                var renderer = manager.GetBaseField(file, info);
                if (renderer == null || !renderer["m_Enabled"].AsBool) continue;
                if (Points(renderer["m_GameObject"]) is not { } owner) continue;
                if (!transformOf.TryGetValue(owner, out long here) || !Shown(here)) continue;
                var pointer = skinned ? renderer["m_Mesh"] : meshOf.GetValueOrDefault(owner);
                if (pointer == null || Points(pointer) is not { } meshId) continue;
                var meshInfo = file.file.GetAssetInfo(meshId);
                if (meshInfo == null) continue;
                var meshField = manager.GetBaseField(file, meshInfo);
                if (meshField == null) continue;
                var mesh = UnityMesh.Read(meshField, major, s => SideFile(bundle, s));
                if (mesh == null) { lost++; continue; }
                var verts = Posed(renderer, skinned, mesh, WorldOf, here, transforms, origin);
                var materials = renderer["m_Materials.Array"].Children;
                for (int sub = 0; sub < mesh.SubMeshes.Count; sub++)
                {
                    if (mesh.SubMeshes[sub].Length == 0) continue;
                    var material = sub < materials.Count ? materials[sub] : materials.FirstOrDefault();
                    parts.Add(new ModelPart(verts, mesh.Uvs, mesh.SubMeshes[sub], Texture(manager, file, material, textures)));
                }
            }
            catch (Exception e) when (OnlyThisBundle(e))
            { lost++; }
        }

        if (parts.Count == 0) return (null, lost);
        var (low, high) = Bounds(parts);
        return (new BundleModel(sha, name, parts, low, high, parts.Sum(p => p.Triangles.Length / Axes), textures, lost), lost);
    }

    static string? Named(List<long> roots, Dictionary<long, AssetTypeValueField> transforms, Dictionary<long, AssetTypeValueField> objects)
    {
        foreach (var root in roots)
        {
            if (Points(transforms[root]["m_GameObject"]) is not { } owner || !objects.TryGetValue(owner, out var go)) continue;
            if (go["m_Name"].AsString is { Length: > 0 } found) return found;
        }
        return null;
    }

    static (double[] Low, double[] High) Bounds(List<ModelPart> parts)
    {
        var low = new double[Axes];
        var high = new double[Axes];
        for (int axis = 0; axis < Axes; axis++)
        {
            low[axis] = double.MaxValue;
            high[axis] = double.MinValue;
            foreach (var part in parts)
                foreach (int vertex in part.Triangles.Distinct())
                {
                    double v = part.Verts[vertex * Axes + axis];
                    low[axis] = Math.Min(low[axis], v);
                    high[axis] = Math.Max(high[axis], v);
                }
        }
        (low[0], high[0]) = (-high[0], -low[0]);
        return (low, high);
    }

    static double[] Posed(AssetTypeValueField renderer, bool skinnedRenderer, UnityMesh mesh, Func<long, Mat4> worldOf, long here,
                          Dictionary<long, AssetTypeValueField> transforms, (double X, double Y, double Z) origin)
    {
        int count = mesh.VertexCount;
        var output = new double[count * Axes];
        var bones = skinnedRenderer
            ? renderer["m_Bones.Array"].Children.Select(Points).ToList()
            : new List<long?>();
        bool skinned = bones.Count > 0 && mesh.Skin.Length > 0 && mesh.BindPose.Length > 0;
        var skin = new List<Mat4>();
        if (skinned)
            for (int b = 0; b < Math.Min(bones.Count, mesh.BindPose.Length); b++)
                skin.Add((bones[b] is { } id && transforms.ContainsKey(id) ? worldOf(id) : Mat4.Identity()).Times(mesh.BindPose[b]));

        var place = worldOf(here);
        for (int vertex = 0; vertex < count; vertex++)
        {
            double x = mesh.Positions[vertex * Axes], y = mesh.Positions[vertex * Axes + 1], z = mesh.Positions[vertex * Axes + 2];
            (double X, double Y, double Z) moved;
            if (!skinned || vertex >= mesh.Skin.Length)
            {
                moved = place.Apply(x, y, z);
            }
            else
            {
                double ax = 0, ay = 0, az = 0, total = 0;
                var influence = mesh.Skin[vertex];
                for (int corner = 0; corner < Corners; corner++)
                {
                    double weight = influence.Weights[corner];
                    int bone = influence.Bones[corner];
                    if (weight <= 0 || bone < 0 || bone >= skin.Count) continue;
                    var bent = skin[bone].Apply(x, y, z);
                    ax += weight * bent.X;
                    ay += weight * bent.Y;
                    az += weight * bent.Z;
                    total += weight;
                }
                moved = total > 0 ? (ax / total, ay / total, az / total) : place.Apply(x, y, z);
            }
            output[vertex * Axes] = moved.X - origin.X;
            output[vertex * Axes + 1] = moved.Y - origin.Y;
            output[vertex * Axes + 2] = moved.Z - origin.Z;
        }
        return output;
    }

    static string? Texture(AssetsManager manager, AssetsFileInstance file, AssetTypeValueField? material, List<(string Sha, Picture Picture)> into)
    {
        if (Follow(manager, file, material) is not { } found) return null;
        foreach (var entry in found.Field["m_SavedProperties"]["m_TexEnvs.Array"].Children)
        {
            if (entry["first"].AsString != MainTexture) continue;
            if (Follow(manager, found.File, entry["second"]["m_Texture"]) is not { } texture) return null;
            var picture = UnityTexture.Read(texture.Field, s => SideFile(texture.File.parentBundle, s));
            if (picture == null) return null;
            var sha = Convert.ToHexString(SHA256.HashData(
                picture.Rgba.Concat(Encoding.UTF8.GetBytes($"{picture.Width}x{picture.Height}")).ToArray())).ToLowerInvariant();
            if (!into.Any(t => t.Sha == sha)) into.Add((sha, picture));
            return sha;
        }
        return null;
    }

    static long? Points(AssetTypeValueField pointer) =>
        pointer.IsDummy || pointer["m_FileID"].AsInt != SameFile || pointer["m_PathID"].AsLong == 0 ? null : pointer["m_PathID"].AsLong;

    static (double X, double Y, double Z, double W) Quat(AssetTypeValueField f) =>
        (f["x"].AsDouble, f["y"].AsDouble, f["z"].AsDouble, f["w"].AsDouble);

    static (double X, double Y, double Z) Vec(AssetTypeValueField f) =>
        (f["x"].AsDouble, f["y"].AsDouble, f["z"].AsDouble);
}
