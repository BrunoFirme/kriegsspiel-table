using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Godot;
using KTable.Client;
using KTable.Client.Studio;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class ModelPictures
{
    const int PaintMost = 1024, ListSide = 96, AtOnce = 2, Drawing = 1;

    static readonly Dictionary<string, ImageTexture> Made = new();
    static readonly Dictionary<string, List<(Action<ImageTexture> ready, Action failed)>> Waiting = new();
    static readonly SemaphoreSlim Turns = new(AtOnce);

    public static void Show(Assets assets, PackManifest pack, PieceDef d, Action<ImageTexture> ready, Action failed)
    {
        if (d.Kind != "model" || d.Mesh == null || !pack.Assets.TryGetValue(d.Mesh, out var mesh)) { failed(); return; }
        string? paint = d.Texture != null && pack.Assets.TryGetValue(d.Texture, out var t) ? t.File : null;
        var key = $"{d.Mesh}|{d.Texture}";
        lock (Made)
        {
            if (Made.TryGetValue(key, out var known)) { ready(known); return; }
            if (Waiting.TryGetValue(key, out var queue)) { queue.Add((ready, failed)); return; }
            Waiting[key] = new() { (ready, failed) };
        }
        var baked = ModelBake.ThumbPath(assets.Store, pack.Id, d.Mesh);
        var cached = Cached(d.Mesh, d.Texture);
        void Start(string? paintFile) => System.Threading.Tasks.Task.Run(async () =>
        {
            Image? img = null;
            await Turns.WaitAsync();
            try
            {
                img = Draw(assets.Abs(pack.Id, mesh.File), paintFile, baked, cached);
                img?.Resize(ListSide, ListSide, Image.Interpolation.Lanczos);
            }
            finally
            {
                Turns.Release();
                var made = img;
                Callable.From(() => Done(key, made)).CallDeferred();
            }
        });
        assets.EnsureFile(pack.Id, mesh.File, () =>
        {
            if (paint == null) { Start(null); return; }
            assets.EnsureFile(pack.Id, paint, () => Start(assets.Abs(pack.Id, paint)), () => Start(null));
        }, () => Done(key, null));
    }

    public static Image? Draw(string meshFile, string? paintFile, string? baked, string? cached)
    {
        if (baked != null && File.Exists(baked) && Picture(baked) is { } own) return own;
        if (cached != null && File.Exists(cached) && Picture(cached) is { } kept) return kept;
        try
        {
            var paint = paintFile != null && Picture(paintFile, PaintMost) is { } p ? new ModelPicture(p.GetData(), p.GetWidth(), p.GetHeight()) : null;
            if (ModelThumb.Render(ModelFiles.Obj(meshFile), paint) is not { } rgba) return null;
            var img = Image.CreateFromData(ModelThumb.Out, ModelThumb.Out, false, Image.Format.Rgba8, rgba);
            if (cached != null)
                BestEffort.Try(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                    img.SavePng(cached);
                });
            return img;
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Diagnostics.Write(LogLevel.Warning, $"[pictures] no picture of the model {Path.GetFileName(meshFile)}: {e.Message}");
            return null;
        }
    }

    public static string Cached(string meshSha, string? paintSha) => Path.Combine(Caches.Display, $"model-{Short(meshSha)}-{Short(paintSha ?? "plain")}-{Drawing}.png");

    public static Image? Picture(string path, int most = 0)
    {
        if (!ContentLimits.PictureAllowed(path) || Image.LoadFromFile(path) is not { } p || p.IsEmpty()) return null;
        if (p.IsCompressed()) p.Decompress();
        p.Convert(Image.Format.Rgba8);
        if (most > 0 && Math.Max(p.GetWidth(), p.GetHeight()) > most)
        {
            float k = most / (float)Math.Max(p.GetWidth(), p.GetHeight());
            p.Resize(Math.Max(1, (int)(p.GetWidth() * k)), Math.Max(1, (int)(p.GetHeight() * k)), Image.Interpolation.Bilinear);
        }
        return p;
    }

    static string Short(string sha) => sha.Length > 16 ? sha[..16] : sha;

    static void Done(string key, Image? img)
    {
        var tex = img == null ? null : ImageTexture.CreateFromImage(img);
        List<(Action<ImageTexture> ready, Action failed)>? waiting;
        lock (Made)
        {
            Waiting.Remove(key, out waiting);
            if (tex != null) Made[key] = tex;
        }
        if (waiting == null) return;
        foreach (var (ready, failed) in waiting)
            if (tex != null) ready(tex);
            else failed();
    }
}
