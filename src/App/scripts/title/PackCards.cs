using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class PackCards
{
    const int W = 452, H = 260, Most = 6, Margin = 10, SmallestPicture = 64;

    static readonly Dictionary<string, (DateTime at, ImageTexture tex)> _thumbs = new();
    static readonly Dictionary<string, List<Action<ImageTexture>>> _waiting = new();

    public static void Thumb(PackageStore store, PackManifest pack, Action<ImageTexture> show)
    {
        var manifest = Path.Combine(store.PackageDir(pack.Id), "pack.json");
        if (!File.Exists(manifest)) return;
        var at = File.GetLastWriteTimeUtc(manifest);
        lock (_thumbs)
        {
            if (_thumbs.TryGetValue(pack.Id, out var had) && had.at == at) { show(had.tex); return; }
            if (_waiting.TryGetValue(pack.Id, out var waiting)) { waiting.Add(show); return; }
            _waiting[pack.Id] = new List<Action<ImageTexture>> { show };
        }
        var pictures = Pictures(store, pack);
        var id = pack.Id;
        System.Threading.Tasks.Task.Run(() =>
        {
            Image? img = null;
            try { img = pictures.Count == 0 ? null : CachedMontage(id, at, pictures); }
            catch (Exception e) { Diagnostics.Write(LogLevel.Warning, $"[cards] no picture for the pack {id}: {e.Message}"); }
            Callable.From(() => Ready(id, at, img)).CallDeferred();
        });
    }

    static void Ready(string id, DateTime at, Image? img)
    {
        List<Action<ImageTexture>>? waiting;
        ImageTexture? tex = img == null ? null : ImageTexture.CreateFromImage(img);
        lock (_thumbs)
        {
            _waiting.Remove(id, out waiting);
            if (tex != null) _thumbs[id] = (at, tex);
        }
        if (tex == null || waiting == null) return;
        foreach (var show in waiting) show(tex);
    }

    static List<string> Pictures(PackageStore store, PackManifest pack) =>
        pack.Pieces.Select(d => d.Image)
            .Where(h => h != null && pack.Assets.TryGetValue(h, out var a) && Math.Max(a.W, a.H) > SmallestPicture)
            .Distinct().Take(Most)
            .Select(h => store.AssetPath(pack, h)!)
            .ToList();

    static Image? CachedMontage(string id, DateTime at, List<string> pictures)
    {
        var dir = Caches.Display;
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"pack-{id}-{at.Ticks}.png");
        if (File.Exists(file) && Image.LoadFromFile(file) is { } kept && !kept.IsEmpty()) return kept;
        var img = Montage(pictures);
        if (img == null) return null;
        foreach (var old in Directory.EnumerateFiles(dir, $"pack-{id}-*.png")) BestEffort.Try(() => File.Delete(old));
        BestEffort.Try(() => img.SavePng(file));
        return img;
    }

    static Image? Montage(List<string> pictures)
    {
        int n = pictures.Count;
        int cols = n <= 1 ? 1 : n <= 2 ? 2 : 3, rows = n <= 3 ? 1 : 2;
        int cw = W / cols, ch = H / rows;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        bool drawn = false;
        for (int i = 0; i < n; i++)
        {
            if (!ContentLimits.PictureAllowed(pictures[i]) || Image.LoadFromFile(pictures[i]) is not { } p || p.IsEmpty()) continue;
            if (p.IsCompressed()) p.Decompress();
            p.Convert(Image.Format.Rgba8);
            float k = Math.Min((cw - 2 * Margin) / (float)p.GetWidth(), (ch - 2 * Margin) / (float)p.GetHeight());
            p.Resize(Math.Max(1, (int)(p.GetWidth() * k)), Math.Max(1, (int)(p.GetHeight() * k)), Image.Interpolation.Lanczos);
            int cx = (i % cols) * cw + (cw - p.GetWidth()) / 2, cy = (i / cols) * ch + (ch - p.GetHeight()) / 2;
            img.BlendRect(p, new Rect2I(0, 0, p.GetWidth(), p.GetHeight()), new Vector2I(cx, cy));
            drawn = true;
        }
        return drawn ? img : null;
    }
}
