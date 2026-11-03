using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

sealed class Captures : HudPart
{
    readonly Func<RectOverlay> _rect;
    public Captures(HudContext ctx, Func<RectOverlay> rect) : base(ctx) { _rect = rect; }

    public async Task Sighting()
    {
        var d = T.Draft;
        if (d == null) return;
        int k = Math.Clamp(Cfg.CaptureScale, 1, 4);
        var img = await T.RenderSighting(d, 620 * k);
        if (img != null) Deliver(img, "sighting");
        if (T.Draft == d) T.EndDraft();
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
        T.LodBoost = k;
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
            KeepAspect = main.KeepAspect, CullMask = Table.WorldLayer,
        };
        vp.AddChild(cam);
        HudNode.AddChild(vp);
        cam.GlobalTransform = main.GlobalTransform;
        await HudNode.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await HudNode.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        T.LodBoost = 1;
        var rr = new Rect2I((int)(r.Position.X * k), (int)(r.Position.Y * k), (int)(r.Size.X * k), (int)(r.Size.Y * k));
        rr = rr.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
        if (rr.Size.X < 2 || rr.Size.Y < 2) return;
        Deliver(img.GetRegion(rr), S.IsUmpire ? "umpire" : "observer");
    }
}
