using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ZoomFilm
{
    public const string Prefix = "film";

    public static async Task Shot(Node main, CameraRig rig, string dir, string name, float[] f)
    {
        if (!rig.TopDown) rig.ToggleTopDown();
        int frames = (int)f[4], every = (int)f[5];
        var kept = new List<(int Frame, Image Seen)>();
        for (int i = 0; i <= frames; i++)
        {
            rig.Frame(Coords.ToWorld(new Vec2(f[0], f[1])), f[2] * Mathf.Pow(f[3] / f[2], i / (float)frames));
            await main.ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            if (i % every == 0) kept.Add((i, main.GetViewport().GetTexture().GetImage()));
        }
        foreach (var (frame, seen) in kept) seen.SavePng(System.IO.Path.Combine(dir, $"{name}-{frame:000}.png"));
    }
}
