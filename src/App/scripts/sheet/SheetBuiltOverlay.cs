using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class SheetBuiltOverlay
{
    const int Across = 8;

    public static string TilePath(string folder, int sheet, int tx, int ty) => Path.Combine(folder, $"{sheet}_{tx}_{ty}.rgba");

    static Rect2 Uv(SheetExport.Source s, int tx, int ty)
    {
        float ts = s.Index.TileSize, w = s.Full.W, h = s.Full.H;
        return new Rect2(tx * ts / w, ty * ts / h, ts / w, ts / h);
    }

    public static async Task<int> Render(Node host, Session session, IReadOnlyList<SheetExport.Source> sources, string folder, Func<bool> stopped, Action<double>? progress = null)
    {
        if (!GroundTiles.Enabled) return 0;
        double perMetre = session.View.Calibration.UnitsPerMeter is > 0 and var upm ? upm : 0;
        var jobs = new List<(int Sheet, BuiltPainter Painter, int Tx, int Ty)>();
        for (int i = 0; i < sources.Count; i++)
        {
            var def = sources[i].Sheet;
            var painter = new BuiltPainter();
            painter.Set(session.Relief.Seen(def), Ground.FullWidth(def), BuiltGeometry.FullHeight(def), perMetre);
            if (!painter.Any) continue;
            for (int ty = 0; ty < sources[i].Full.Rows; ty++)
                for (int tx = 0; tx < sources[i].Full.Cols; tx++)
                    if (painter.Touches(Uv(sources[i], tx, ty))) jobs.Add((i, painter, tx, ty));
        }
        if (jobs.Count == 0) return 0;
        Directory.CreateDirectory(folder);
        int side = sources.Max(s => s.Index.TileSize);
        var vp = new SubViewport
        {
            Size = new Vector2I(Across * side, Across * side), TransparentBg = true, Disable3D = true,
            RenderTargetClearMode = SubViewport.ClearMode.Always, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        host.AddChild(vp);
        try
        {
            for (int at = 0; at < jobs.Count; at += Across * Across)
            {
                if (stopped()) throw new OperationCanceledException();
                var batch = jobs.Skip(at).Take(Across * Across).ToList();
                var layers = batch.Select(j => j.Painter.Layer(Uv(sources[j.Sheet], j.Tx, j.Ty), sources[j.Sheet].Index.TileSize, GroundTiles.Style, BuiltLayer.Raising.None)).ToList();
                await Task.WhenAll(layers.OfType<BuiltLayer>().Select(l => Task.Run(l.Make)));
                var cells = new List<Control>();
                for (int k = 0; k < batch.Count; k++)
                {
                    if (layers[k] is not { } layer) continue;
                    int ts = sources[batch[k].Sheet].Index.TileSize;
                    var corner = new Vector2(k % Across * side, k / Across * side);
                    var cell = new Control { Position = corner,Size = new Vector2(ts, ts), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
                    cell.AddChild(layer.Slice(0, layer.Batches));
                    layer.Free();
                    vp.AddChild(cell);
                    cells.Add(cell);
                }
                vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var drawn = vp.GetTexture().GetImage();
                if (drawn.GetFormat() != Image.Format.Rgba8) drawn.Convert(Image.Format.Rgba8);
                for (int k = 0; k < batch.Count; k++)
                {
                    if (layers[k] == null) continue;
                    var (sheet, _, tx, ty) = batch[k];
                    int ts = sources[sheet].Index.TileSize;
                    using var part = drawn.GetRegion(new Rect2I(k % Across * side, k / Across * side, ts, ts));
                    await File.WriteAllBytesAsync(TilePath(folder, sheet, tx, ty), part.GetData());
                }
                foreach (var cell in cells) cell.QueueFree();
                progress?.Invoke((double)(at + batch.Count) / jobs.Count);
            }
        }
        finally { vp.QueueFree(); }
        return jobs.Count;
    }
}
