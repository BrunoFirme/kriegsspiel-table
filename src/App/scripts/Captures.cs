using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

sealed class Captures : HudPart
{
    readonly Func<RectOverlay> _rect;
    public Captures(HudContext ctx, Func<RectOverlay> rect) : base(ctx) { _rect = rect; }

    const string GifFolder = "ktable-pov-gifs", PartSuffix = ".part";
    const double BytesPerMegabyte = 1e6;

    public PovRecording? Recording { get; private set; }
    public event Action? RecordingTicked;

    public Task Sighting() => T.Draft switch
    {
        null => Task.CompletedTask,
        { Gif: false } => Picture(),
        _ when Recording is { } r => Stopping(r),
        var d => Record(d),
    };

    static Task Stopping(PovRecording r)
    {
        r.Stop();
        return Task.CompletedTask;
    }

    async Task Record(SightingDraft d)
    {
        if (_taking) return;
        var folder = Cfg.CaptureToFolder ? Cfg.CaptureFolder : Path.Combine(Path.GetTempPath(), GifFolder);
        var file = Path.Combine(folder, $"{S.View.ScenarioId}_{S.View.Clock.Turn}_pov_{DateTime.Now:HHmmss}.gif");
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"could not write to {folder}: {e.Message}");
            return;
        }
        var rec = new PovRecording(T.Sighting, d, file + PartSuffix);
        rec.Ticked += () => RecordingTicked?.Invoke();
        Recording = rec;
        T.RefreshSightingPanel();
        bool kept = false;
        try
        {
            var end = await rec.Run();
            Recording = null;
            if (end == PovRecording.End.Dropped) { Say("GIF dropped"); return; }
            if (end == PovRecording.End.Failed || rec.Frames == 0)
            {
                Say("GIF not made: " + (rec.Failure ?? "no picture came"));
                T.RefreshSightingPanel();
                return;
            }
            File.Move(file + PartSuffix, file, overwrite: true);
            kept = true;
            if (T.Draft == d) T.EndDraft();
            DeliverGif(file, rec, end);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"GIF not made: could not write to {folder}: {e.Message}");
        }
        finally
        {
            Recording = null;
            if (!kept) BestEffort.DeleteFile(file + PartSuffix);
        }
    }

    void DeliverGif(string file, PovRecording rec, PovRecording.End end)
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
            PovRecording.End.LongestTime => $", stopped at {PovRecording.MostSeconds:0} s",
            PovRecording.End.LargestFile => $", stopped at {PovRecording.StopAtBytes / BytesPerMegabyte:0} MB",
            _ => "",
        };
        var made = $"GIF {rec.Seconds:0.0} s, {rec.Frames} frames, {rec.Bytes / BytesPerMegabyte:0.0} MB{cut}";
        Log($"{made}: {string.Join(", ", msgs)}");
        Say($"{made}: {string.Join(", ", msgs)} — Ctrl+V into Discord.");
    }

    bool _taking;

    async Task Picture()
    {
        var d = T.Draft;
        if (d == null || _taking) return;
        _taking = true;
        try
        {
            int k = Math.Clamp(Cfg.CaptureScale, 1, 4);
            var img = await T.RenderSighting(d, 620 * k);
            if (T.Draft != d) return;
            if (img != null) Deliver(img, "sighting");
            T.EndDraft();
        }
        finally { _taking = false; }
    }

    void Deliver(Image img, string kind)
    {
        var msgs = new List<string>();
        if (Cfg.CaptureToClipboard)
        {
            string? err;
            try { err = AppClipboard.SetImage(img); } catch (Exception e) { err = e.Message; }
            msgs.Add(err == null ? "copied to clipboard" : "clipboard failed: " + err);
        }
        if (Cfg.CaptureToFolder)
        {
            try
            {
                Directory.CreateDirectory(Cfg.CaptureFolder);
                var file = Path.Combine(Cfg.CaptureFolder, $"{S.View.ScenarioId}_{S.View.Clock.Turn}_{kind}_{DateTime.Now:HHmmss}.png");
                var perr = img.SavePng(file);
                msgs.Add(perr == Error.Ok ? "saved " + Path.GetFileName(file) : $"could not write to {Cfg.CaptureFolder} ({perr})");
            }
            catch (Exception e) { msgs.Add($"could not write to {Cfg.CaptureFolder}: {e.Message}"); }
        }
        Log($"Picture {img.GetWidth()}×{img.GetHeight()}: {string.Join(", ", msgs)}");
        Say(string.Join(", ", msgs) + " — Ctrl+V into Discord.");
    }

    public async Task Region(Rect2 r)
    {
        int k = Math.Clamp(Cfg.CaptureScale, 1, 4);
        _rect().SetRect(null);
        var vpSize = T.GetViewport().GetVisibleRect().Size;
        Say("Capturing…");
        for (int i = 0; i < 30; i++)
        {
            await HudNode.ToSignal(HudNode.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (i >= 2 && !S.Assets.Busy) break;
        }
        var vp = new SubViewport
        {
            Size = new Vector2I(Math.Max(2, (int)(vpSize.X * k)), Math.Max(2, (int)(vpSize.Y * k))), World3D = T.GetViewport().FindWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa2X, TransparentBg = false,
        };
        var main = T.Rig.Cam;
        var cam = new Camera3D
        {
            Projection = main.Projection, Fov = main.Fov, Size = main.Size, Near = main.Near, Far = main.Far,
            KeepAspect = main.KeepAspect, CullMask = Table.WorldLayer | Table.TextLayer,
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
        Deliver(img.GetRegion(rr), S.IsUmpire ? "umpire" : "observer");
    }
}
