using Godot;

namespace KTable.App;

static class Graphics
{
    public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 240 };
    public static readonly float[] FarDetailBias = { 0f, -0.75f, -1f };
    public static readonly float[] RenderScales = { 1f, 1.25f, 1.5f, 2f };

    static int _lastFull = Settings.Borderless;

    public static void ApplyDisplay(Settings cfg)
    {
        ApplyWindow(cfg);
        DisplayServer.WindowSetVsyncMode(cfg.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = cfg.FrameLimit;
    }

    public static DisplayServer.WindowMode ModeOf(Settings cfg) => cfg.WindowMode switch
    {
        Settings.Borderless => DisplayServer.WindowMode.Fullscreen,
        Settings.Fullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
        _ => DisplayServer.WindowMode.Windowed,
    };

    static bool FillsScreen(DisplayServer.WindowMode mode) => mode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;

    public static void ApplyWindow(Settings cfg)
    {
        if (DisplayServer.GetName() == "headless") return;
        if (cfg.WindowMode != Settings.Windowed) _lastFull = cfg.WindowMode;
        var want = ModeOf(cfg);
        var now = DisplayServer.WindowGetMode();
        if (now == want || (!FillsScreen(want) && !FillsScreen(now))) return;
        DisplayServer.WindowSetMode(want);
    }

    public static void ToggleFullScreen(Settings cfg)
    {
        cfg.WindowMode = cfg.WindowMode == Settings.Windowed ? _lastFull : Settings.Windowed;
        cfg.Save();
        ApplyWindow(cfg);
    }

    public static void ApplyEdges(Viewport view, Settings cfg)
    {
        view.Msaa3D = cfg.Antialias switch
        {
            <= 0 => Viewport.Msaa.Disabled,
            1 => Viewport.Msaa.Msaa2X,
            2 => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Msaa8X,
        };
        view.ScreenSpaceAA = cfg.SmoothFineEdges ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled;
        view.TextureMipmapBias = FarDetailBias[Mathf.Clamp(cfg.FarDetail, 0, FarDetailBias.Length - 1)];
        view.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        view.Scaling3DScale = Mathf.Clamp(cfg.RenderScale, Settings.RenderScaleMin, Settings.RenderScaleMax);
    }

    public static void ApplyShadows(Settings cfg)
    {
        const bool SixteenBit = true;
        var (side, softness) = cfg.ShadowDetail switch
        {
            <= 0 => (2048, RenderingServer.ShadowQuality.SoftVeryLow),
            1 => (4096, RenderingServer.ShadowQuality.SoftLow),
            _ => (8192, RenderingServer.ShadowQuality.SoftMedium),
        };
        RenderingServer.DirectionalShadowAtlasSetSize(side, SixteenBit);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(softness);
    }

    public static bool ShadowsReachFar(Settings cfg) => cfg.ShadowDetail >= Settings.ShadowDetailMost;
}
