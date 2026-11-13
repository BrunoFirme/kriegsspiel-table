using KTable.Core.Model;

namespace KTable.Client.Prefs;

public sealed class Settings
{
    public Keymap Keys { get; set; } = new();
    public bool CaptureToClipboard { get; set; } = true;
    public bool CaptureToFolder { get; set; } = true;
    public string CaptureFolder { get; set; } = KTable.Core.DataPaths.Captures;
    public int CaptureScale { get; set; } = 4;
    public const int CaptureScaleMost = 4;
    public string SightingFormat { get; set; } = SightingFormats[0];
    public static readonly string[] SightingFormats = { "round", "square", "drawn" };
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? SightingSquare { get; set; }
    public bool SightingFirstPerson { get; set; }
    public double SightingLens { get; set; } = SightingDraft.NormalLens;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? SightingWide { get; set; }
    public string? SightingFacing { get; set; }
    public double SightingEyeShare { get; set; } = 1;
    public bool SightingHideText { get; set; }
    public bool SightingGif { get; set; }
    public bool CaptureGif { get; set; }
    public bool GifLarge { get; set; }
    public Dictionary<string, List<CaptureProfile>> CaptureProfiles { get; set; } = new();
    public string LastName { get; set; } = "";
    public string LastAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7777;
    public bool ShareCursor { get; set; } = true;
    public bool FaceText { get; set; } = true;
    public bool LabelPlates { get; set; }
    public bool WheelTurnsHovered { get; set; }
    public bool HoverLightsCarrier { get; set; }
    public bool PieceMenuUnfolded { get; set; }
    public float LiftHeight { get; set; } = LiftUsual;
    public const float LiftUsual = 2.4f, LiftLeast = 0.2f, LiftMost = 8f;
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
    public string Tint { get; set; } = InterfaceTint.Charcoal;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? UiScaleFollowsWindows { get; set; }
    public const float UiScaleMin = 0.7f, UiScaleMax = 1.5f, ReferenceHeight = 1080f, FittedMost = 3f, NextStepFrom = 0.75f;
    public static float Fitted(float windowsScale, float windowHeight) =>
        Math.Clamp(Math.Max(windowsScale, MathF.Floor(windowHeight / ReferenceHeight + 1 - NextStepFrom)), 1f, FittedMost);
    public float UiScaleFor(float windowsScale, float windowHeight) => Fitted(windowsScale, windowHeight) * UiScale;
    public int Antialias { get; set; } = 2;
    public bool SmoothFineEdges { get; set; }
    public int ShadowDetail { get; set; } = ShadowDetailDefault;
    public const int ShadowDetailDefault = 1, ShadowDetailMost = 2;
    public bool ContactShade { get; set; }
    public int WindowMode { get; set; }
    public const int Windowed = 0, Borderless = 1, Fullscreen = 2;
    public bool VSync { get; set; } = true;
    public int FrameLimit { get; set; }
    public const int FrameLimitMost = 1000;
    public bool RestWhenIdle { get; set; } = true;
    public bool ShowFrameRate { get; set; }
    public const int RestingFrames = 30;
    public int FramesAtMost(bool resting) => resting && RestWhenIdle && FrameLimit is 0 or > RestingFrames ? RestingFrames : FrameLimit;
    public string Renderer { get; set; } = Vulkan;
    public const string Vulkan = "vulkan", Direct3D12 = "d3d12";
    public static readonly string[] Renderers = { Vulkan, Direct3D12 };
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
    public Dictionary<string, Dictionary<int, float[]>> CameraViews { get; set; } = new();
    public Dictionary<string, float[]> PanelPos { get; set; } = new();
    public Dictionary<string, float[]> PanelSize { get; set; } = new();
    public float StripSize { get; set; } = 30;
    public const float StripSizeMin = 24, StripSizeMax = 56;
    public List<string> StripHidden { get; set; } = new();
    public bool TopLine { get; set; } = true;
    public float TopLineWidth { get; set; } = 1f;
    public const float TopLineWidthMin = 0.25f;
    public string PenInk { get; set; } = "#1a1a1a";
    public float PenSize { get; set; } = 3f;
    public float PenOpacity { get; set; } = 1f;
    public string DrawMode { get; set; } = DrawModes[0];
    public static readonly string[] DrawModes = { "free", "shape", "erase", "name" };
    public Dictionary<string, string> DrawLayer { get; set; } = new();
    public Dictionary<string, HashSet<string>> HiddenLayers { get; set; } = new();
    public Dictionary<string, HashSet<string>> HiddenSheets { get; set; } = new();
    public Dictionary<string, string> Decal { get; set; } = new();
    public Dictionary<string, List<double[]>> RulerPins { get; set; } = new();
    public double DecalTurn { get; set; }
    public double DecalGrowth { get; set; } = 1;
    public Dictionary<string, int> FormationRanks { get; set; } = new();
    public double? RangeMetres { get; set; }
    public double? RangeUnits { get; set; }
    public double? CircleMetres { get; set; }
    public double? CircleUnits { get; set; }
    public Dictionary<int, int> DiceHand { get; set; } = new();
    public string? PiecesPack { get; set; }
    public int PiecesTab { get; set; }
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
    public List<string> FolderFavourites { get; set; } = new();
    public List<string> RecentFolders { get; set; } = new();
    public const int RecentFoldersMost = 20;
    public bool SystemFileDialog { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public const float SlowestReliefRate = 0.5f, FastestReliefRate = 200, HighestReliefLimit = 3000, HardestRelief = 0.9f, MostContourStep = 200;
    public const float LeastRough = 0.2f, MostRough = 50, FinestGrain = 20, CoarsestGrain = 2000, MostLift = 500;
    public float ReliefBrush { get; set; } = 6;
    public float ReliefStrength { get; set; } = 0.35f;
    public float ReliefRate { get; set; } = 10;
    public float ReliefUpTo { get; set; }
    public float ReliefDownTo { get; set; }
    public float ReliefLevel { get; set; }
    public float ReliefHardness { get; set; }
    public float ReliefFormHeight { get; set; } = 20;
    public string ReliefProfile { get; set; } = ReliefForms.Round;
    public float ReliefContourHeight { get; set; } = 10;
    public float ReliefContourStep { get; set; } = 10;
    public float ReliefRough { get; set; } = 3;
    public float ReliefGrain { get; set; } = 150;
    public float ReliefErode { get; set; } = 4;
    public float ReliefLift { get; set; } = 10;
    public string ReliefMode { get; set; } = "raise";
    public float ReliefDensity { get; set; } = 1;
    public string ReliefPaint { get; set; } = "brush";
    public string ReliefTip { get; set; } = "round";
    public Dictionary<string, int[]> ReliefDetails { get; set; } = new();
    public BuildPrefs Build { get; set; } = new();
    public static readonly string[] ReliefPaintings = { "brush", "fill", "rect", "ellipse", "polygon" };

    public int ReliefDetail(byte kind, int which)
    {
        var parameters = GroundKinds.Parameters(kind);
        if (which >= parameters.Count) return 0;
        return ReliefDetails.TryGetValue(GroundKinds.Name(kind), out var levels) && which < levels.Length ? Math.Clamp(levels[which], 0, 100) : parameters[which].Default;
    }

    public void SetReliefDetail(byte kind, int which, int percent)
    {
        var levels = ReliefDetails.TryGetValue(GroundKinds.Name(kind), out var had) && had.Length == 2 ? had : new[] { ReliefDetail(kind, 0), ReliefDetail(kind, 1) };
        levels[which] = Math.Clamp(percent, 0, 100);
        ReliefDetails[GroundKinds.Name(kind)] = levels;
    }

    public byte ReliefLevels(byte kind) => GroundKinds.Levels(ReliefDetail(kind, 0), ReliefDetail(kind, 1));
    public double FormationGap { get; set; } = KTable.Client.Hand.Formations.DefaultGap;
    public double? FormationDistance { get; set; }
    public string Formation { get; set; } = "line";
    public bool StretchKeepsSpacing { get; set; }
    public bool SheetCarries { get; set; }
    public bool SheetKeepsShape { get; set; } = true;
    public string NewBackdrop { get; set; } = "";
    public double NewTableShade { get; set; }
    public int Tour { get; set; }
    public const int TourOver = -1;
    public List<string> SeenNotes { get; set; } = new();
    public const int SeenNotesMost = 20;

    public static Func<string> FileAt { get; set; } = () => throw new InvalidOperationException("the settings file's place was never given");

    static string PathOf => FileAt();

    public static Settings Load()
    {
        try
        {
            if (!System.IO.File.Exists(PathOf)) return new();
            var s = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(PathOf)) ?? new();
            s.Freshen();
            return s;
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

    public const int KeptScenarios = 40;

    static void KeepNewest<T>(Dictionary<string, T> byScenario)
    {
        foreach (var old in byScenario.Keys.Take(Math.Max(0, byScenario.Count - KeptScenarios)).ToList()) byScenario.Remove(old);
    }

    void Freshen()
    {
        static string Bare(string p) => p.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        foreach (var old in new[] { KTable.Core.DataPaths.LegacyDocuments, KTable.Core.DataPaths.Documents })
            if (string.Equals(Bare(CaptureFolder), Bare(System.IO.Path.Combine(old, "captures")), StringComparison.OrdinalIgnoreCase)) CaptureFolder = KTable.Core.DataPaths.Captures;
        if (ReduceFarDetail) { LetteringReach = 32f; ReduceFarDetail = false; }
        LetteringReach = float.IsFinite(LetteringReach) ? Math.Clamp(LetteringReach, ReachMin, ReachOff) : ReachOff;
        LogFontSize = Math.Clamp(LogFontSize, LogFontMin, LogFontMax);
        ShadowDetail = Math.Clamp(ShadowDetail, 0, ShadowDetailMost);
        FrameLimit = Math.Clamp(FrameLimit, 0, FrameLimitMost);
        FarDetail = Math.Clamp(FarDetail, 0, FarDetailMost);
        RenderScale = float.IsFinite(RenderScale) ? Math.Clamp(RenderScale, RenderScaleMin, RenderScaleMax) : RenderScaleMin;
        ReliefDensity = float.IsFinite(ReliefDensity) ? Math.Clamp(ReliefDensity, 0.05f, 1f) : 1f;
        ReliefRate = float.IsFinite(ReliefRate) ? Math.Clamp(ReliefRate, SlowestReliefRate, FastestReliefRate) : 10;
        ReliefUpTo = float.IsFinite(ReliefUpTo) ? Math.Clamp(ReliefUpTo, 0, HighestReliefLimit) : 0;
        ReliefDownTo = float.IsFinite(ReliefDownTo) ? Math.Clamp(ReliefDownTo, 0, HighestReliefLimit) : 0;
        ReliefLevel = float.IsFinite(ReliefLevel) ? Math.Clamp(ReliefLevel, 0, HighestReliefLimit) : 0;
        ReliefHardness = float.IsFinite(ReliefHardness) ? Math.Clamp(ReliefHardness, 0, HardestRelief) : 0;
        ReliefFormHeight = float.IsFinite(ReliefFormHeight) ? Math.Clamp(ReliefFormHeight, 1, HighestReliefLimit) : 20;
        if (!ReliefForms.Profiles.Contains(ReliefProfile)) ReliefProfile = ReliefForms.Round;
        ReliefContourHeight = float.IsFinite(ReliefContourHeight) ? Math.Clamp(ReliefContourHeight, 0, HighestReliefLimit) : 10;
        ReliefContourStep = float.IsFinite(ReliefContourStep) ? Math.Clamp(ReliefContourStep, -MostContourStep, MostContourStep) : 10;
        ReliefRough = float.IsFinite(ReliefRough) ? Math.Clamp(ReliefRough, LeastRough, MostRough) : 3;
        ReliefGrain = float.IsFinite(ReliefGrain) ? Math.Clamp(ReliefGrain, FinestGrain, CoarsestGrain) : 150;
        ReliefErode = float.IsFinite(ReliefErode) ? Math.Clamp(ReliefErode, LeastRough, MostRough) : 4;
        ReliefLift = float.IsFinite(ReliefLift) ? Math.Clamp(ReliefLift, -MostLift, MostLift) : 10;
        if (Array.IndexOf(ReliefPaintings, ReliefPaint) < 0) ReliefPaint = "brush";
        if (Array.IndexOf(KTable.Core.Model.ReliefBrush.Tips, ReliefTip) < 0) ReliefTip = KTable.Core.Model.ReliefBrush.Tips[0];
        if (ReliefMode is "paint:road" or "paint:bridge" or "paint:town") ReliefMode = "raise";
        Build ??= new();
        Build.Normalise();
        if (Array.IndexOf(MapPictureMemoryChoices, MapPictureMemory) < 0) MapPictureMemory = 0;
        if (Array.IndexOf(Renderers, Renderer) < 0) Renderer = Vulkan;
        if (SightingWide == true) SightingLens = SightingDraft.WholeCircleLens;
        SightingWide = null;
        if (SightingSquare == true) SightingFormat = "square";
        SightingSquare = null;
        if (Array.IndexOf(SightingFormats, SightingFormat) < 0) SightingFormat = SightingFormats[0];
        SightingLens = SightingDraft.FitLens(SightingLens);
        SightingEyeShare = double.IsFinite(SightingEyeShare) ? Math.Clamp(SightingEyeShare, 0, SightingDraft.EyeHighestShare) : 1;
        if (SightingFacing is not (null or "N" or "E" or "S" or "W")) SightingFacing = null;
        KTable.Client.Prefs.CaptureProfiles.Fit(CaptureProfiles ??= new());
        if (Array.IndexOf(DrawModes, DrawMode) < 0) DrawMode = DrawModes[0];
        DecalTurn = double.IsFinite(DecalTurn) ? (DecalTurn % 360 + 360) % 360 : 0;
        DecalGrowth = double.IsFinite(DecalGrowth) && DecalGrowth > 0 ? DecalGrowth : 1;
        if (RangeMetres is { } m && !(double.IsFinite(m) && m > 0)) RangeMetres = null;
        if (RangeUnits is { } u && !(double.IsFinite(u) && u > 0)) RangeUnits = null;
        if (CircleMetres is { } cm && !(double.IsFinite(cm) && cm > 0)) CircleMetres = null;
        if (CircleUnits is { } cu && !(double.IsFinite(cu) && cu > 0)) CircleUnits = null;
        foreach (var sides in DiceHand.Keys.ToList()) if (DiceHand[sides] <= 0) DiceHand.Remove(sides);
        foreach (var kind in FormationRanks.Keys.ToList()) if (KTable.Client.Hand.Formations.Find(kind) == null) FormationRanks.Remove(kind);
        KeepNewest(DrawLayer);
        KeepNewest(HiddenLayers);
        KeepNewest(HiddenSheets);
        KeepNewest(Decal);
        foreach (var (scenario, pins) in RulerPins.ToList())
            if (pins.RemoveAll(p => p is not { Length: 4 } || !p.All(double.IsFinite)) >= 0 && pins.Count == 0) RulerPins.Remove(scenario);
        KeepNewest(RulerPins);
        FolderFavourites = Folders(FolderFavourites, int.MaxValue);
        RecentFolders = Folders(RecentFolders, RecentFoldersMost);
        SeenNotes = (SeenNotes ?? []).Where(n => !string.IsNullOrEmpty(n)).Distinct().TakeLast(SeenNotesMost).ToList();
        if (UiScaleFollowsWindows != null) UiScale = 1f;
        UiScaleFollowsWindows = null;
        UiScale = float.IsFinite(UiScale) ? Math.Clamp(UiScale, UiScaleMin, UiScaleMax) : 1f;
        Tint = InterfaceTint.Kept(Tint);
        if (AgentPort is < 1024 or > 65535) AgentPort = AgentPortDefault;
    }

    static List<string> Folders(IEnumerable<string>? paths, int most) =>
        (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(most).ToList();

    public bool TakeFolders(IEnumerable<string> favourites, IEnumerable<string> recent)
    {
        var fav = Folders(favourites, int.MaxValue);
        var rec = Folders(recent, RecentFoldersMost);
        if (fav.SequenceEqual(FolderFavourites) && rec.SequenceEqual(RecentFolders)) return false;
        FolderFavourites = fav;
        RecentFolders = rec;
        return true;
    }

    public bool FirstSight(string note)
    {
        if (SeenNotes.Contains(note)) return false;
        SeenNotes = SeenNotes.Append(note).TakeLast(SeenNotesMost).ToList();
        return true;
    }

    public void Save()
    {
        try { KTable.Core.Json.WriteAtomic(PathOf, System.Text.Json.JsonSerializer.Serialize(this)); }
        catch (Exception e) { Problems.TellOnce("settings", $"Your settings could not be saved ({e.Message}): changes to them last until the app closes."); }
    }
}
