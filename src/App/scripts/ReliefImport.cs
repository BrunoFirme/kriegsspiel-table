using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BitMiracle.LibTiff.Classic;
using Godot;
using KTable.Client.Relief;
using KTable.Client.Studio;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public sealed record ReliefJob(string Relief, string? Picture, bool OwnPicture);

public sealed record ReliefMade(string Name, string Relief, ReliefSource Source, string? Image, int PictureW, int PictureH);

public static class ReliefImport
{
    public static readonly string[] ModelExtensions = { ".glb", ".gltf", ".fbx", ".obj", ".stl", ".ply", ".dae", ".blend" };
    const int BlenderSeconds = 180;
    static string WorkDir => Path.Combine(DataPaths.Local, "cache", "relief");

    public static string Filter =>
        string.Join(", ", SheetImporter.Extensions.Concat(ModelExtensions).Concat(HeightFiles.Extensions).Select(e => "*" + e)) + " ; Map pictures, 3D terrain and heightmaps";

    public static bool IsModel(string path) => Array.IndexOf(ModelExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    public static bool NeedsEngine(string path) => Path.GetExtension(path).ToLowerInvariant() is ".glb" or ".gltf" or ".fbx" or ".blend";

    public static bool IsRelief(string path) => IsModel(path) || HeightFiles.IsHeightFile(path) || IsDem(path) || HeightFiles.IsDeepPng(path);

    public static bool IsDem(string path)
    {
        if (!TiffImages.IsTiff(path)) return false;
        try
        {
            using var tiff = Tiff.Open(path, "r");
            if (tiff == null) return false;
            int samples = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1, bits = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
            return samples == 1 && bits >= 16;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    public static string? Blender(Settings cfg)
    {
        if (cfg.BlenderPath is { Length: > 0 } set && File.Exists(set)) return set;
        var pf = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
        var foundation = Path.Combine(pf, "Blender Foundation");
        if (Directory.Exists(foundation))
            foreach (var d in Directory.GetDirectories(foundation).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                if (File.Exists(Path.Combine(d, "blender.exe"))) return Path.Combine(d, "blender.exe");
        var steam = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Blender", "blender.exe");
        return File.Exists(steam) ? steam : null;
    }

    public static string FromBlend(string path, Settings cfg)
    {
        var blender = Blender(cfg) ?? throw new InvalidDataException("Blender is not on this PC: in Blender, File → Export → glTF 2.0, and add the .glb");
        Directory.CreateDirectory(WorkDir);
        var glb = Path.Combine(WorkDir, Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N")[..8] + ".glb");
        var psi = new ProcessStartInfo(blender) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-b", path, "--python-expr", $"import bpy; bpy.ops.export_scene.gltf(filepath=r'{glb}', export_format='GLB', export_apply=True)" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidDataException("Blender did not start");
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        if (!p.WaitForExit(BlenderSeconds * 1000)) { BestEffort.Try(() => p.Kill(true)); throw new InvalidDataException($"Blender took more than {BlenderSeconds} s"); }
        if (!File.Exists(glb)) throw new InvalidDataException("Blender wrote no model");
        return glb;
    }

    public static void Forget(string path)
    {
        if (path.StartsWith(WorkDir, StringComparison.OrdinalIgnoreCase)) BestEffort.DeleteFile(path);
    }

    public static ReliefSource FromEngine(string path, string name)
    {
        var m = ModelImport.Load(path, out var error) ?? throw new InvalidDataException(error ?? "no triangles in it");
        var mesh = new ModelMesh();
        for (int i = 0; i < m.V.Count; i++)
            mesh.Add(new System.Numerics.Vector3(m.V[i].X, m.V[i].Y, m.V[i].Z), System.Numerics.Vector3.Zero, new System.Numerics.Vector2(m.UV[i].X, m.UV[i].Y), KTable.Client.Rgba.White);
        mesh.Mat.AddRange(m.Mat);
        Directory.CreateDirectory(WorkDir);
        for (int i = 0; i < m.Tex.Count; i++)
        {
            string? pic = null;
            if (m.Tex[i] is { } img) { pic = Path.Combine(WorkDir, $"{Guid.NewGuid():N}.png"); img.SavePng(pic); }
            mesh.Materials.Add(new ModelMaterial(KTable.Client.Rgba.White, pic));
        }
        return ReliefBuilder.FromMesh(mesh, name);
    }

    public static ReliefSource Read(string path)
    {
        var name = MapPage.SheetName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (HeightFiles.IsHeightFile(path)) return HeightFiles.Read(path);
        if (ext == ".png") return HeightFiles.Png(path);
        if (TiffImages.IsTiff(path)) return Dem(path, name);
        if (ext == ".dae") return ReliefBuilder.FromMesh(ColladaFile.Read(path), name);
        if (ext == ".obj") return ReliefBuilder.FromMesh(ModelFiles.Obj(path), name);
        if (ext == ".stl") return ReliefBuilder.FromMesh(ModelFiles.Stl(ModelFiles.Bytes(path)), name);
        if (ext == ".ply") return ReliefBuilder.FromMesh(ModelFiles.Ply(ModelFiles.Bytes(path)), name);
        throw new InvalidDataException("not a relief file");
    }

    const int ModelPixelScaleTag = 33550;
    const int NoDataTag = 42113;
    const double MetresPerDegree = 111_320;

    static ReliefSource Dem(string path, string name)
    {
        using var tiff = Tiff.Open(path, "r") ?? throw new InvalidDataException("cannot open the TIFF");
        int w = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), h = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        int bits = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 16;
        var format = (SampleFormat)(tiff.GetField(TiffTag.SAMPLEFORMAT)?[0].ToInt() ?? (int)SampleFormat.UINT);
        if (w < 2 || h < 2 || (long)w * h > 400_000_000L) throw new InvalidDataException("the elevation file's size is out of range");
        int step = Math.Max(1, (int)Math.Ceiling(Math.Max(w, h) / (double)ReliefGrid.MaxSide));
        int cols = (w + step - 1) / step, rows = (h + step - 1) / step;
        var raw = new float[cols * rows];
        float Sample(byte[] buf, int i) => bits switch
        {
            16 when format == SampleFormat.INT => BitConverter.ToInt16(buf, i * 2),
            16 => BitConverter.ToUInt16(buf, i * 2),
            32 when format == SampleFormat.IEEEFP => BitConverter.ToSingle(buf, i * 4),
            32 when format == SampleFormat.INT => BitConverter.ToInt32(buf, i * 4),
            32 => BitConverter.ToUInt32(buf, i * 4),
            64 => (float)BitConverter.ToDouble(buf, i * 8),
            _ => throw new InvalidDataException($"{bits}-bit samples are not read"),
        };
        int bytes = bits / 8;
        if (tiff.IsTiled())
        {
            int tw = tiff.GetField(TiffTag.TILEWIDTH)[0].ToInt(), th = tiff.GetField(TiffTag.TILELENGTH)[0].ToInt();
            var buf = new byte[tiff.TileSize()];
            for (int ty = 0; ty < h; ty += th)
                for (int tx = 0; tx < w; tx += tw)
                {
                    if (tiff.ReadTile(buf, 0, tx, ty, 0, 0) < 0) throw new InvalidDataException("a tile of the elevation file is damaged");
                    for (int y = 0; y < th && ty + y < h; y++)
                    {
                        if ((ty + y) % step != 0) continue;
                        for (int x = 0; x < tw && tx + x < w; x++)
                            if ((tx + x) % step == 0) raw[(ty + y) / step * cols + (tx + x) / step] = Sample(buf, y * tw + x);
                    }
                }
        }
        else
        {
            var buf = new byte[Math.Max(tiff.ScanlineSize(), w * bytes)];
            for (int y = 0; y < h; y += step)
            {
                if (!tiff.ReadScanline(buf, y)) throw new InvalidDataException("a row of the elevation file is damaged");
                for (int x = 0; x < w; x += step) raw[y / step * cols + x / step] = Sample(buf, x);
            }
        }
        double cellX = 1, cellY = 1;
        bool real = false;
        var scale = tiff.GetField((TiffTag)ModelPixelScaleTag);
        if (scale != null && scale.Length >= 2 && scale[1].ToDoubleArray() is { Length: >= 2 } s && s[0] > 0)
        {
            bool degrees = s[0] < 0.01;
            cellX = s[0] * (degrees ? MetresPerDegree : 1) * step;
            cellY = s[1] * (degrees ? MetresPerDegree : 1) * step;
            real = true;
        }
        float? noData = null;
        var nd = tiff.GetField((TiffTag)NoDataTag);
        if (nd != null && nd.Length >= 2 && float.TryParse(nd[1].ToString()?.Trim('\0', ' '), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) noData = v;
        return HeightFiles.FromSamples(name, cols, rows, raw, cellX, cellY, real, noData);
    }

    public static string? Picture(ReliefSource src, ReliefJob job)
    {
        var path = job.Picture ?? (job.OwnPicture ? src.Picture : null);
        if (path == null || ReliefPicture.AsItIs(src)) return path;
        var img = ImageFiles.Load(path) ?? throw new InvalidDataException("the picture could not be read");
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        var (px, w, h) = ReliefPicture.Fitted(src, img.GetData(), img.GetWidth(), img.GetHeight());
        img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, px);
        Directory.CreateDirectory(WorkDir);
        var o = Path.Combine(WorkDir, $"{Guid.NewGuid():N}.png");
        img.SavePng(o);
        return o;
    }

    public static ReliefMade Make(ReliefSource src, ReliefJob job, double zero, string packagesRoot, Action<string>? note, Action<double>? progress)
    {
        var grid = src.ToGrid(zero);
        var (sha, _) = ReliefFiles.Save(Path.Combine(packagesRoot, UploadAsset.ReliefPackage), grid);
        string? image = null; int pw = 0, ph = 0;
        string? temp = null;
        try
        {
            temp = Picture(src, job);
            if (temp != null)
            {
                var (_, picSha, w, h) = SheetImporter.Import(temp, packagesRoot, note, progress);
                image = picSha; pw = w; ph = h;
            }
        }
        finally
        {
            foreach (var t in new[] { temp, src.Picture })
                if (t != null && t.StartsWith(WorkDir, StringComparison.OrdinalIgnoreCase)) BestEffort.DeleteFile(t);
        }
        return new ReliefMade(src.Name, sha, src, image, pw, ph);
    }

    public static (string sha, List<string> written) Save(ReliefGrid g, string packagesRoot) =>
        ReliefFiles.Save(Path.Combine(packagesRoot, UploadAsset.ReliefPackage), g);
}
