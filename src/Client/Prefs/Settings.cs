namespace KTable.Client.Prefs;

public sealed class Settings
{
    public Keymap Keys { get; set; } = new();
    public bool CaptureToClipboard { get; set; } = true;
    public bool CaptureToFolder { get; set; } = true;
    public string CaptureFolder { get; set; } = System.IO.Path.Combine(KTable.Core.DataPaths.Documents, "captures");
    public int CaptureScale { get; set; } = 4;
    public const int CaptureScaleMost = 4;
    public bool SightingSquare { get; set; }
    public string LastName { get; set; } = "";
    public string LastAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7777;
    public bool ShareCursor { get; set; } = true;
    public bool FaceText { get; set; } = true;
    public bool LabelPlates { get; set; }
    public float LetteringReach { get; set; } = ReachOff;
    public const float ReachMin = 12f, ReachOff = 250f;
    public bool ReduceFarDetail { get; set; }
    public bool MakerTools { get; set; }
    public bool AgentOn { get; set; }
    public int AgentPort { get; set; } = AgentPortDefault;
    public const int AgentPortDefault = 7796;
    public string AgentToken { get; set; } = "";
    public float FaceTextScale { get; set; } = 1f;
    public const float FaceTextScaleMin = 0.6f, FaceTextScaleMax = 2.5f;
    public bool ShowCursors { get; set; } = true;
    public bool ShowZones { get; set; } = true;
    public float UiScale { get; set; } = 1f;
    public bool? UiScaleFollowsWindows { get; set; }
    public const float UiScaleMin = 0.7f, UiScaleMax = 2f;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool FollowsWindows { get => UiScaleFollowsWindows ?? UiScale == 1f; set => UiScaleFollowsWindows = value; }
    public float UiScaleWith(float windowsScale) => FollowsWindows ? Math.Clamp(windowsScale, UiScaleMin, UiScaleMax) : UiScale;
    public int Antialias { get; set; } = 2;
    public bool SmoothFineEdges { get; set; }
    public int ShadowDetail { get; set; } = ShadowDetailDefault;
    public const int ShadowDetailDefault = 1, ShadowDetailMost = 2;
    public bool ContactShade { get; set; }
    public bool VSync { get; set; } = true;
    public int FrameLimit { get; set; }
    public const int FrameLimitMost = 1000;
    public int FarDetail { get; set; } = FarDetailDefault;
    public const int FarDetailDefault = 1, FarDetailMost = 2;
    public float RenderScale { get; set; } = 1f;
    public const float RenderScaleMin = 1f, RenderScaleMax = 2f;
    public int MapPictureMemory { get; set; }
    public static readonly int[] MapPictureMemoryChoices = { 0, 256, 512, 1024, 2048, 4096, 8192 };
    public float SoundVolume { get; set; } = 0.6f;
    public bool SoundMuted { get; set; }
    public bool SoundInBackground { get; set; }
    public Dictionary<string, float[]> CameraAt { get; set; } = new();
    public Dictionary<string, float[]> PanelPos { get; set; } = new();
    public Dictionary<string, float[]> PanelSize { get; set; } = new();
    public float StripSize { get; set; } = 30;
    public const float StripSizeMin = 24, StripSizeMax = 56;
    public bool TopLine { get; set; } = true;
    public float TopLineWidth { get; set; } = 1f;
    public const float TopLineWidthMin = 0.25f;
    public string PenInk { get; set; } = "#1a1a1a";
    public float PenSize { get; set; } = 3f;
    public float PenOpacity { get; set; } = 1f;
    public string ShapeKind { get; set; } = "line";
    public float LeftWidth { get; set; } = 300;
    public float RightWidth { get; set; } = 320;
    public float LogHeight { get; set; } = 200;
    public bool LeftOpen { get; set; } = true;
    public string LeftPage { get; set; } = "pieces";
    public bool RightOpen { get; set; } = true;
    public bool LogOpen { get; set; } = true;
    public int LogFontSize { get; set; } = LogFontDefault;
    public const int LogFontDefault = 12, LogFontMin = 9, LogFontMax = 24;
    public List<string> Favourites { get; set; } = new();
    public bool FavouritesOnly { get; set; }
    public bool CompassShown { get; set; } = true;
    public float[]? CompassPos { get; set; }
    public string BlenderPath { get; set; } = "";
    public bool CheckUpdates { get; set; } = true;
    public float ReliefBrush { get; set; } = 6;
    public float ReliefStrength { get; set; } = 0.35f;
    public string ReliefMode { get; set; } = "raise";
    public double FormationGap { get; set; } = KTable.Client.Hand.Formations.DefaultGap;
    public string Formation { get; set; } = "line";

    public static Func<string> FileAt { get; set; } = () => throw new InvalidOperationException("the settings file's place was never given");

    static string PathOf => FileAt();

    public static Settings Load()
    {
        try
        {
            if (System.IO.File.Exists(PathOf))
            {
                var s = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(PathOf)) ?? new();
                s.Freshen();
                return s;
            }
        }
        catch (Exception e)
        {
            var copy = $"{PathOf}.bad-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            string kept;
            try { System.IO.File.Copy(PathOf, copy, overwrite: true); kept = $"a copy of it is kept as {System.IO.Path.GetFileName(copy)}"; }
            catch (Exception c) { kept = $"no copy of it could be kept ({c.Message})"; }
            Problems.Tell($"Your settings could not be read ({e.Message}), so the defaults are used: keys, camera and panels are as new. {kept}.");
        }
        return new();
    }

    void Freshen()
    {
        var old = System.IO.Path.Combine(KTable.Core.DataPaths.LegacyDocuments, "captures");
        var now = System.IO.Path.Combine(KTable.Core.DataPaths.Documents, "captures");
        if (string.Equals(CaptureFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar), old.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) CaptureFolder = now;
        if (ReduceFarDetail) { LetteringReach = 32f; ReduceFarDetail = false; }
        LetteringReach = float.IsFinite(LetteringReach) ? Math.Clamp(LetteringReach, ReachMin, ReachOff) : ReachOff;
        LogFontSize = Math.Clamp(LogFontSize, LogFontMin, LogFontMax);
        ShadowDetail = Math.Clamp(ShadowDetail, 0, ShadowDetailMost);
        FrameLimit = Math.Clamp(FrameLimit, 0, FrameLimitMost);
        FarDetail = Math.Clamp(FarDetail, 0, FarDetailMost);
        RenderScale = float.IsFinite(RenderScale) ? Math.Clamp(RenderScale, RenderScaleMin, RenderScaleMax) : RenderScaleMin;
        if (Array.IndexOf(MapPictureMemoryChoices, MapPictureMemory) < 0) MapPictureMemory = 0;
        UiScale = float.IsFinite(UiScale) ? Math.Clamp(UiScale, UiScaleMin, UiScaleMax) : 1f;
        if (AgentPort is < 1024 or > 65535) AgentPort = AgentPortDefault;
    }

    public void Save()
    {
        try { KTable.Core.Json.WriteAtomic(PathOf, System.Text.Json.JsonSerializer.Serialize(this)); }
        catch (Exception e) { Problems.TellOnce("settings", $"Your settings could not be saved ({e.Message}): changes to them last until the app closes."); }
    }
}
