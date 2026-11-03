using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

static class ScenarioCards
{
    static System.Threading.Tasks.Task? _cardRead;

    public static void ReadSoon(PackageStore store) => _cardRead = System.Threading.Tasks.Task.Run(() =>
    {
        try
        {
            bool backdrop = false;
            foreach (var (id, path) in store.ListScenarios())
            {
                var s = Card(path).s;
                if (s?.Sheets.Count > 0)
                {
                    const int CardWidth = 512, BackdropWidth = 1400;
                    ThumbImage(store, s, CardWidth);
                    if (!backdrop) { backdrop = true; ThumbImage(store, s, BackdropWidth); }
                }
                var live = Main.AutosavePath(id);
                if (File.Exists(live)) Save(live);
            }
            foreach (var mp in Main.Saves.Mirrors()) Save(mp);
        }
        catch (Exception e) { GD.PrintErr("[lobby] reading the scenarios' cards: " + e.Message); }
    });

    public static SaveCard Save(string path) => CardReader.Save(path);

    static readonly Dictionary<string, (DateTime at, Image? img, ImageTexture? tex)> _thumbs = new();

    public static void WaitForEarlyRead()
    {
        BestEffort.Try(() => _cardRead?.Wait());
        _cardRead = null;
    }

    public static (Scenario? s, int pieces) Card(string path) => CardReader.Card(path);

    public static ImageTexture? Thumb(PackageStore store, Scenario s, int maxW)
    {
        var key = ThumbKey(store, s, maxW, out var at);
        if (key == null) return null;
        lock (_thumbs)
            if (_thumbs.TryGetValue(key, out var c) && c.at == at && c.tex != null) return c.tex;
        var img = ThumbImage(store, s, maxW);
        if (img == null) return null;
        var tex = ImageTexture.CreateFromImage(img);
        lock (_thumbs) _thumbs[key] = (at, img, tex);
        return tex;
    }

    static string? ThumbKey(PackageStore store, Scenario s, int maxW, out DateTime at)
    {
        at = default;
        var sh = s.Sheets.FirstOrDefault();
        if (sh == null) return null;
        var dir = store.TilesDir(sh);
        var index = Path.Combine(dir, "index.json");
        if (!File.Exists(index)) return null;
        at = File.GetLastWriteTimeUtc(index);
        return $"{dir}|{maxW}|{sh.RotDeg}";
    }

    static Image? ThumbImage(PackageStore store, Scenario s, int maxW)
    {
        var key = ThumbKey(store, s, maxW, out var at);
        if (key == null) return null;
        lock (_thumbs)
            if (_thumbs.TryGetValue(key, out var c) && c.at == at) return c.img;
        var img = StitchThumb(store, s, maxW);
        lock (_thumbs) _thumbs[key] = (at, img, null);
        return img;
    }

    static Image? StitchThumb(PackageStore store, Scenario s, int maxW)
    {
        try
        {
            var sh = s.Sheets.FirstOrDefault();
            if (sh == null) return null;
            var dir = store.TilesDir(sh);
            var idx = Json.Load<TileIndex>(Path.Combine(dir, "index.json"));
            var lv = idx.Levels.FirstOrDefault(l => l.W <= maxW) ?? idx.Levels[^1];
            var img = Image.CreateEmpty(lv.W, lv.H, false, Image.Format.Rgba8);
            for (int y = 0; y < lv.Rows; y++)
                for (int x = 0; x < lv.Cols; x++)
                {
                    var tile = Path.Combine(dir, lv.Level.ToString(), $"{x}_{y}.{idx.Ext}");
                    if (!ContentLimits.PictureAllowed(tile) || Image.LoadFromFile(tile) is not { } t) continue;
                    t.Convert(Image.Format.Rgba8);
                    img.BlitRect(t, new Rect2I(0, 0, t.GetWidth(), t.GetHeight()), new Vector2I(x * idx.TileSize, y * idx.TileSize));
                }
            if (Math.Abs(((sh.RotDeg % 360) + 360) % 360 - 180) < 45) { img.FlipX(); img.FlipY(); }
            return img;
        }
        catch { return null; }
    }
}
