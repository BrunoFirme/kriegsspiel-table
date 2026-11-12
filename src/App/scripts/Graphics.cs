using Godot;

namespace KTable.App;

static class Graphics
{
    public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 240 };
    public static readonly float[] FarDetailBias = { 0f, -0.75f, -1f };
    public static readonly float[] RenderScales = { 1f, 1.25f, 1.5f, 2f };

    const string NextStart = "user://graphics.cfg", RendererSection = "rendering", RendererKey = "rendering_device/driver.windows";

    static int _lastFull = Settings.Borderless;

    static readonly string AskedAtStart = RendererForNextStart();

    public static bool Resting { get; set; }

    public static bool CanChooseRenderer => OS.GetName() == "Windows";

    public static string RendererNow => RenderingServer.GetCurrentRenderingDriverName();

    public static string RendererName(string renderer) => renderer == Settings.Direct3D12 ? "Direct3D 12" : "Vulkan";

    public static string OtherRenderer(string renderer) => renderer == Settings.Direct3D12 ? Settings.Vulkan : Settings.Direct3D12;

    public static event System.Action? WindowChosen;

    public static void ApplyDisplay(Settings cfg)
    {
        ApplyWindow(cfg);
        DisplayServer.WindowSetVsyncMode(cfg.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        ApplyFrames(cfg);
        KeepRendererForNextStart(cfg);
    }

    public static void ApplyFrames(Settings cfg) => Engine.MaxFps = cfg.FramesAtMost(Resting);

    public static string RendererNote(string chosen) =>
        chosen == RendererNow ? ""
        : chosen == AskedAtStart ? $"{RendererName(chosen)} did not start on this card: drawing through {RendererName(RendererNow)}."
        : "From the next start.";

    static string RendererForNextStart()
    {
        var file = new ConfigFile();
        file.Load(NextStart);
        return (string)file.GetValue(RendererSection, RendererKey, Settings.Vulkan);
    }

    static void KeepRendererForNextStart(Settings cfg)
    {
        if (RendererForNextStart() == cfg.Renderer) return;
        var file = new ConfigFile();
        file.Load(NextStart);
        file.SetValue(RendererSection, RendererKey, cfg.Renderer);
        try { KTable.Core.Json.WriteAtomic(ProjectSettings.GlobalizePath(NextStart), file.EncodeToText()); }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { GD.PrintErr("[graphics] the renderer for the next start could not be kept: " + e.Message); }
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
        WindowChosen?.Invoke();
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
            <= 0 => (2048, RenderingServer.ShadowQuality.SoftLow),
            1 => (4096, RenderingServer.ShadowQuality.SoftMedium),
            _ => (8192, RenderingServer.ShadowQuality.SoftHigh),
        };
        RenderingServer.DirectionalShadowAtlasSetSize(side, SixteenBit);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(softness);
    }

    public static bool ShadowsReachFar(Settings cfg) => cfg.ShadowDetail >= Settings.ShadowDetailMost;
}
