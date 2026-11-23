using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

sealed class Captures : HudPart
{
    readonly Func<RectOverlay> _rect;
    public Captures(HudContext ctx, Func<RectOverlay> rect) : base(ctx) { _rect = rect; }

    internal const string GifFolder = "ktable-gifs";
    internal const string PictureWord = "Picture";
    const string PartSuffix = ".part";
    const double BytesPerMegabyte = 1e6;

    public GifRecording? Recording { get; private set; }
    public event Action? RecordingTicked;

    public GifRecording? RegionRecording { get; private set; }
    public event Action? RegionChanged, RegionTicked;

    public Task Sighting() => T.Draft switch
    {
        null => Task.CompletedTask,
        { Gif: false } => Picture(),
        _ when Recording is { } r => Stopping(r),
        var d => _povRun = Gif(new PovFrames(T.Sighting, d, Cfg.PovScale > 1), "pov", rec =>
        {
            Recording = rec;
            if (rec != null) rec.Ticked += () => RecordingTicked?.Invoke();
            T.RefreshSightingPanel();
        }, () => { if (T.Draft == d) T.EndDraft(); }),
    };

    static Task Stopping(GifRecording r)
    {
        r.Stop();
        return Task.CompletedTask;
    }

    public Task Capture(Rect2 r, bool? gif = null) => gif ?? Cfg.CaptureGif ? RecordRegion(r) : Region(r);

    internal string? Handed;

    void Told(string said)
    {
        Handed = said;
        Say(said);
    }

    Task RecordRegion(Rect2 r)
    {
        if (RegionRecording != null) return Task.CompletedTask;
        _rect().SetRect(null);
        return _regionRun = Gif(new RegionFrames(T, HudNode, r, Cfg.GifLarge), S.IsUmpire ? "umpire" : "observer", rec =>
        {
            RegionRecording = rec;
            _rect().SetRecording(rec == null ? null : r);
            if (rec != null) rec.Ticked += () => RegionTicked?.Invoke();
            RegionChanged?.Invoke();
        }, () => { });
    }

    Task? _povRun, _regionRun;

    public async Task StopRecordings()
    {
        StopRegion();
        Recording?.Stop();
        foreach (var run in new[] { _povRun, _regionRun })
            if (run != null) await run;
    }

    public bool StopRegion()
    {
        if (RegionRecording is not { } r) return false;
        r.Stop();
        return true;
    }

    public void CancelRegion() => RegionRecording?.Drop();

    async Task Gif(IGifFrames frames, string kind, Action<GifRecording?> holds, Action kept)
    {
        if (_taking) return;
        var folder = Cfg.CaptureToFolder ? Cfg.CaptureFolder : Path.Combine(Path.GetTempPath(), GifFolder);
        var file = Path.Combine(folder, $"{S.View.ScenarioId}_{S.View.Clock.Turn}_{kind}_{DateTime.Now:HHmmss}.gif");
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Told($"could not write to {folder}: {e.Message}");
            return;
        }
        var rec = new GifRecording(frames, file + PartSuffix);
        holds(rec);
        bool done = false;
        try
        {
            var end = await rec.Run();
            holds(null);
            if (end == GifRecording.End.Dropped) { Told("GIF dropped"); return; }
            if (end == GifRecording.End.Failed || rec.Frames == 0)
            {
                Told("GIF not made: " + (rec.Failure ?? "no picture came"));
                return;
            }
            File.Move(file + PartSuffix, file, overwrite: true);
            done = true;
            kept();
            DeliverGif(file, rec, end);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Told($"GIF not made: could not write to {folder}: {e.Message}");
        }
        finally
        {
            holds(null);
            if (!done) BestEffort.DeleteFile(file + PartSuffix);
        }
    }

    void DeliverGif(string file, GifRecording rec, GifRecording.End end)
    {
        var msgs = new List<string>();
        if (Cfg.CaptureToClipboard)
        {
            string? err;
            try { err = AppClipboard.SetFile(file); } catch (Exception e) { err = e.Message; }
            msgs.Add(err == null ? "copied to clipboard" : "clipboard failed: " + err);
        }
        if (Cfg.CaptureToFolder) msgs.Add("saved " + Path.GetFileName(file));
        var cut = end switch
        {
            GifRecording.End.LongestTime => $", stopped at {GifRecording.MostSeconds:0} s",
            GifRecording.End.LargestFile => $", stopped at {GifRecording.MostBytes / BytesPerMegabyte:0} MB",
            _ => "",
        };
        var made = $"GIF {rec.Seconds:0.0} s, {rec.Frames} frames, {rec.Bytes / BytesPerMegabyte:0.0} MB{cut}";
        Handed = $"{made}: {string.Join(", ", msgs)}";
        Log(Handed);
        Say($"{made}: {string.Join(", ", msgs)} — Ctrl+V into Discord.");
    }

    bool _taking;

    public const int SightingSide = 620;

    async Task Picture()
    {
        var d = T.Draft;
        if (d == null || _taking) return;
        _taking = true;
        try
        {
            int k = Cfg.PovScale;
            var img = await T.RenderSighting(d, SightingSide * k);
            if (T.Draft != d) return;
            if (img != null) await Deliver(img, "sighting");
            T.EndDraft();
        }
        finally { _taking = false; }
    }

    async Task Deliver(Image img, string kind)
    {
        var msgs = new List<string>();
        byte[] png = Cfg.CaptureToClipboard || Cfg.CaptureToFolder ? await Task.Run(() => img.SavePngToBuffer()) : Array.Empty<byte>();
        if (Cfg.CaptureToClipboard)
        {
            string? err;
            try { err = await AppClipboard.SetImage(img, png); } catch (Exception e) { err = e.Message; }
            msgs.Add(err == null ? "copied to clipboard" : "clipboard failed: " + err);
        }
        if (Cfg.CaptureToFolder)
        {
            try
            {
                var folder = Cfg.CaptureFolder;
                Directory.CreateDirectory(folder);
                var file = FreeFile(folder, $"{S.View.ScenarioId}_{S.View.Clock.Turn}_{kind}_{DateTime.Now:HHmmss}");
                if (png.Length > 0) await Task.Run(() => WriteWhole(file, png));
                msgs.Add(png.Length > 0 ? "saved " + Path.GetFileName(file) : $"could not write to {Cfg.CaptureFolder} (no picture)");
            }
            catch (Exception e) { msgs.Add($"could not write to {Cfg.CaptureFolder}: {e.Message}"); }
        }
        Handed = $"{PictureWord} {img.GetWidth()}×{img.GetHeight()}: {string.Join(", ", msgs)}";
        Log(Handed);
        Say(string.Join(", ", msgs) + " — Ctrl+V into Discord.");
    }

    static void WriteWhole(string file, byte[] bytes)
    {
        try
        {
            File.WriteAllBytes(file + PartSuffix, bytes);
            File.Move(file + PartSuffix, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            BestEffort.DeleteFile(file + PartSuffix);
            throw;
        }
    }

    const int MostFramesForFiles = 30, LeastFramesForFiles = 2;

    async Task FilesIn()
    {
        Say("Capturing…");
        for (int i = 0; i < MostFramesForFiles; i++)
        {
            await HudNode.ToSignal(HudNode.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (i >= LeastFramesForFiles && !S.Assets.Busy) break;
        }
    }

    public Task Boxed() => T.Box.Area is { } area ? Boxed(area, T.Box.Pixels) : Task.CompletedTask;

    public async Task Boxed(MapArea area, int pixels)
    {
        if (_taking) return;
        if (T.Walk.Immersed && !S.IsUmpire)
        {
            Told("No map box while walking");
            return;
        }
        _taking = true;
        try
        {
            await FilesIn();
            var img = await FromAbove(area, pixels);
            if (img == null) Told("No picture: the window is not drawing");
            else await Deliver(img, S.IsUmpire ? "umpire" : "observer");
        }
        finally { _taking = false; }
    }

    public async Task AllBoxes()
    {
        if (_taking || S.View.ScenarioId is not { Length: > 0 } scenario) return;
        var kept = BoxProfiles(Cfg, scenario);
        if (kept.Count == 0)
        {
            Told("No map box profiles");
            return;
        }
        if (T.Walk.Immersed && !S.IsUmpire)
        {
            Told("No map box while walking");
            return;
        }
        _taking = true;
        try
        {
            await FilesIn();
            var taken = new List<(string name, Image img)>();
            foreach (var p in kept)
            {
                Say($"Capturing {taken.Count + 1} of {kept.Count}…");
                var b = p.Box!;
                if (await FromAbove(new MapArea(new Vec2(b[0], b[1]), b[2], b[3]), MapBox.FitPixels(b[4])) is not { } img)
                {
                    Told("No pictures: the window is not drawing");
                    return;
                }
                taken.Add((p.Name, img));
            }
            await SaveAll(taken, S.IsUmpire ? "umpire" : "observer");
        }
        finally { _taking = false; }
    }

    internal static List<KTable.Client.Prefs.CaptureProfile> BoxProfiles(KTable.Client.Prefs.Settings cfg, string scenario) =>
        KTable.Client.Prefs.CaptureProfiles.Of(cfg, scenario, boxes: true).Where(p => p.Box is { Length: KTable.Client.Prefs.CaptureProfile.BoxNumbers }).ToList();

    internal static string FreeFile(string folder, string stem)
    {
        var file = Path.Combine(folder, stem + ".png");
        for (int n = 2; File.Exists(file); n++) file = Path.Combine(folder, $"{stem}_{n.ToString(CultureInfo.InvariantCulture)}.png");
        return file;
    }

    async Task SaveAll(List<(string name, Image img)> taken, string kind)
    {
        var at = DateTime.Now;
        var folder = Cfg.CaptureFolder;
        int saved = 0;
        string? failed = null;
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { failed = e.Message; }
        for (int i = 0; i < taken.Count && failed == null; i++)
        {
            Say($"Saving {i + 1} of {taken.Count}…");
            var (name, img) = taken[i];
            var stem = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();
            if (stem.Length == 0) stem = (i + 1).ToString(CultureInfo.InvariantCulture);
            var file = FreeFile(folder, $"{S.View.ScenarioId}_{S.View.Clock.Turn}_{kind}_{stem}_{at:HHmmss}");
            var err = await Task.Run(() => img.SavePng(file));
            if (err == Error.Ok) saved++;
            else failed = err.ToString();
        }
        if (failed != null)
        {
            Told($"{saved} of {taken.Count} pictures saved: could not write to {folder} ({failed})");
            return;
        }
        Handed = $"{PictureWord}s: {saved} saved to {folder}";
        Log(Handed);
        Say($"{saved} pictures saved to {folder}");
    }

    async Task<Image?> FromAbove(MapArea area, int pixels)
    {
        bool lettered = T.Draft == null;
        if (lettered)
            T.Pieces.Lettering.HoldNearest(
                T.Pieces.Nodes.Where(n => n.Visible && area.Reaches(Coords.FromWorld(n.Position), n.Footprint.Length() * Math.Max(1, n.Piece.Scale))), Coords.ToWorld(area.Centre));
        try { return await MapView.Shot(HudNode, T, area.Centre, area.Deep, MapView.Picture(area.Wide, area.Deep, pixels)); }
        finally
        {
            if (lettered) T.Pieces.Lettering.Release();
        }
    }

    public async Task Region(Rect2 r)
    {
        int k = Math.Clamp(Cfg.CaptureScale, 1, KTable.Client.Prefs.Settings.CaptureScaleMost);
        _rect().SetRect(null);
        var vpSize = T.GetViewport().GetVisibleRect().Size;
        await FilesIn();
        var vp = new SubViewport
        {
            Size = new Vector2I(Math.Max(2, (int)(vpSize.X * k)), Math.Max(2, (int)(vpSize.Y * k))), World3D = T.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa2X, TransparentBg = false,
        };
        var main = T.Rig.Cam;
        var cam = new Camera3D
        {
            Projection = main.Projection, Fov = main.Fov, Size = main.Size, Near = main.Near, Far = main.Far,
            KeepAspect = main.KeepAspect, CullMask = Table.CaptureLayers,
        };
        vp.AddChild(cam);
        HudNode.AddChild(vp);
        cam.GlobalTransform = main.GlobalTransform;
        await HudNode.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await HudNode.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        var rr = new Rect2I((int)(r.Position.X * k), (int)(r.Position.Y * k), (int)(r.Size.X * k), (int)(r.Size.Y * k));
        rr = rr.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
        if (rr.Size.X < 2 || rr.Size.Y < 2) return;
        await Deliver(img.GetRegion(rr), S.IsUmpire ? "umpire" : "observer");
    }
}
