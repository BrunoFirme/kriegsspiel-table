using KTable.Client.Prefs;
using KTable.Core;

namespace KTable.Client.Tests;

public class KeymapTests
{
    [Fact]
    public void An_action_has_its_default_key_until_bound_and_a_script_hotkey_starts_on_its_number()
    {
        var k = new Keymap();
        Assert.Equal("M", k.Of("ruler"));
        Assert.Equal("Ctrl+Shift+C", k.Of("capture"));
        Assert.Equal("", k.Of("nothing"));
        k.Set("ruler", "Ctrl+R");
        k.Set("flip", "");
        Assert.Equal("Ctrl+R", k.Of("ruler"));
        Assert.Equal("F", k.Of("flip"));
        Assert.Equal("Kp 3", k.Of("script:Scripting button 3"));
        Assert.Equal("Kp 0", k.Of("script:Scripting button 10 "));
        Assert.Equal("3", k.Of("script:Magic Hotkey #3"));
        Assert.Equal("", k.Of("script:Advance"));
        Assert.Equal("", Keymap.ScriptDefault(""));
        k.Set("script:Advance", "Ctrl+N");
        Assert.Equal("Ctrl+N", k.Of("script:Advance"));
        k.Reset();
        Assert.Equal("M", k.Of("ruler"));
        Assert.Equal(46, Keymap.Actions.Length);
        Assert.Equal("Ctrl+M", k.Of("mute"));
        Assert.Equal(Keymap.Actions.Length, Keymap.Actions.Select(a => a.action).Distinct().Count());
    }

    [Fact]
    public void Shift_is_left_out_of_the_fine_step_keys_and_the_keypad_resizes_too()
    {
        var k = new Keymap();
        Assert.True(k.Matches("M", "ruler"));
        Assert.False(k.Matches("Shift+M", "ruler"));
        Assert.True(k.Matches("Shift+Q", "turnLeft"));
        Assert.True(k.Matches("Shift+R", "roll"));
        Assert.True(k.Matches("Kp Add", "larger"));
        Assert.True(k.Matches("Shift+Kp Subtract", "smaller"));
        Assert.False(k.Matches("Kp Add", "smaller"));
        Assert.False(k.Matches("", "ruler"));
        Assert.False(k.Matches("A", "script:Advance"));
        k.Set("turnRight", "Shift+E");
        Assert.True(k.Matches("E", "turnRight"));
        Assert.True(k.Matches("Ctrl+Shift+C", "capture"));
        Assert.False(k.Matches("Ctrl+C", "capture"));
    }

    [Fact]
    public void No_formation_key_is_also_a_key_of_the_table()
    {
        Assert.Empty(new Keymap().FormationClashes());
        var k = new Keymap();
        k.Set("form:line", "Shift+R");
        Assert.Contains(("form:line", "roll"), k.FormationClashes());
    }

    [Fact]
    public void North_up_and_label_plates_have_a_key_each()
    {
        var k = new Keymap();
        Assert.Equal("N", k.Of("north"));
        Assert.Equal("P", k.Of("labelPlates"));
    }

    [Fact]
    public void Keys_are_shown_with_their_signs()
    {
        var k = new Keymap();
        Assert.Equal("[", k.Label("statePrev"));
        Assert.Equal("]", k.Label("stateNext"));
        Assert.Equal("=", k.Label("larger"));
        Assert.Equal("−", k.Label("smaller"));
        Assert.Equal("`", k.Label("scriptConsole"));
        k.Set("copy", "Ctrl+BracketLeft");
        Assert.Equal("Ctrl+[", k.Label("copy"));
    }
}

[Collection("Problems")]
public class SettingsTests
{
    readonly string _file;

    public SettingsTests()
    {
        _file = Path.Combine(TestFolders.New(), "settings.json");
        Settings.FileAt = () => _file;
    }

    [Fact]
    public void Resting_caps_frames_at_thirty_unless_the_limit_is_lower_or_rest_is_off()
    {
        var s = new Settings();
        Assert.Equal(0, s.FramesAtMost(resting: false));
        Assert.Equal(Settings.RestingFrames, s.FramesAtMost(resting: true));
        s.FrameLimit = 144;
        Assert.Equal(144, s.FramesAtMost(resting: false));
        Assert.Equal(Settings.RestingFrames, s.FramesAtMost(resting: true));
        s.FrameLimit = 20;
        Assert.Equal(20, s.FramesAtMost(resting: true));
        s.FrameLimit = 144;
        s.RestWhenIdle = false;
        Assert.Equal(144, s.FramesAtMost(resting: true));
    }

    [Fact]
    public void An_unknown_renderer_in_the_file_reads_as_vulkan()
    {
        File.WriteAllText(_file, "{\"Renderer\":\"metal\"}");
        Assert.Equal(Settings.Vulkan, Settings.Load().Renderer);
        File.WriteAllText(_file, "{\"Renderer\":\"d3d12\"}");
        Assert.Equal(Settings.Direct3D12, Settings.Load().Renderer);
    }

    [Fact]
    public void A_wide_lens_kept_by_an_older_version_opens_at_ninety_degrees_and_a_lens_out_of_reach_is_brought_in()
    {
        File.WriteAllText(_file, "{\"SightingWide\":true}");
        var s = Settings.Load();
        Assert.Equal(SightingDraft.WholeCircleLens, s.SightingLens);
        Assert.Null(s.SightingWide);
        File.WriteAllText(_file, "{\"SightingLens\":500}");
        Assert.Equal(SightingDraft.WidestLens, Settings.Load().SightingLens);
        File.WriteAllText(_file, "{\"SightingLens\":45}");
        Assert.Equal(45, Settings.Load().SightingLens);
    }

    [Fact]
    public void Tool_settings_kept_from_last_time_come_back_and_broken_ones_fall_back()
    {
        var s = Settings.Load();
        s.SightingFacing = "E";
        s.SightingEyeShare = 2;
        s.DrawMode = "erase";
        s.DrawLayer["sc"] = "b2";
        s.HiddenLayers["sc"] = new() { "b1" };
        s.Decal["sc"] = "pic.png";
        s.DecalTurn = 45;
        s.DecalGrowth = 2;
        s.FormationRanks["column"] = 4;
        s.RangeMetres = 800;
        s.DiceHand[6] = 3;
        s.PiecesPack = "";
        s.PiecesTab = 1;
        s.Save();
        var back = Settings.Load();
        Assert.Equal(("E", 2.0, "erase", "b2", "pic.png", 45.0, 2.0, 4, 800.0, 3, "", 1),
            (back.SightingFacing, back.SightingEyeShare, back.DrawMode, back.DrawLayer["sc"], back.Decal["sc"],
                back.DecalTurn, back.DecalGrowth, back.FormationRanks["column"], back.RangeMetres, back.DiceHand[6], back.PiecesPack, back.PiecesTab));
        Assert.Contains("b1", back.HiddenLayers["sc"]);
        File.WriteAllText(_file, "{\"SightingFacing\":\"NE\",\"SightingEyeShare\":99,\"DrawMode\":\"spray\",\"DecalTurn\":-90,\"DecalGrowth\":0,\"RangeMetres\":-5,\"DiceHand\":{\"6\":0}}");
        var broken = Settings.Load();
        Assert.Equal(((string?)null, SightingDraft.EyeHighestShare, "free", 270.0, 1.0, (double?)null, 0),
            (broken.SightingFacing, broken.SightingEyeShare, broken.DrawMode, broken.DecalTurn, broken.DecalGrowth, broken.RangeMetres, broken.DiceHand.Count));
    }

    [Fact]
    public void Tool_memory_holds_the_newest_scenarios_and_only_formations_that_exist()
    {
        var s = Settings.Load();
        for (int i = 0; i < Settings.KeptScenarios + 5; i++)
        {
            s.DrawLayer[$"sc{i}"] = "b";
            s.HiddenLayers[$"sc{i}"] = new() { "b" };
            s.Decal[$"sc{i}"] = "p.png";
        }
        s.FormationRanks["column"] = 3;
        s.FormationRanks["gone-kind"] = 3;
        s.Save();
        var back = Settings.Load();
        Assert.Equal((Settings.KeptScenarios, Settings.KeptScenarios, Settings.KeptScenarios), (back.DrawLayer.Count, back.HiddenLayers.Count, back.Decal.Count));
        Assert.True(back.DrawLayer.ContainsKey($"sc{Settings.KeptScenarios + 4}") && !back.DrawLayer.ContainsKey("sc0"));
        Assert.Equal(new[] { "column" }, back.FormationRanks.Keys);
    }

    [Fact]
    public void A_square_format_kept_by_an_older_version_opens_square_and_an_unknown_format_opens_round()
    {
        File.WriteAllText(_file, "{\"SightingSquare\":true}");
        var s = Settings.Load();
        Assert.Equal("square", s.SightingFormat);
        Assert.Null(s.SightingSquare);
        File.WriteAllText(_file, "{\"SightingSquare\":false}");
        Assert.Equal("round", Settings.Load().SightingFormat);
        File.WriteAllText(_file, "{\"SightingFormat\":\"hexagon\"}");
        Assert.Equal("round", Settings.Load().SightingFormat);
        File.WriteAllText(_file, "{\"SightingFormat\":\"drawn\"}");
        Assert.Equal(SightingFormat.Drawn, SightingDraft.FormatNamed(Settings.Load().SightingFormat));
    }

    [Fact]
    public void New_settings_are_written_with_these_defaults_in_this_order()
    {
        var s = new Settings();
        Assert.Equal(DataPaths.Captures, s.CaptureFolder);
        s.CaptureFolder = "C";
        s.Keys.Set("ruler", "R");
        Assert.Equal(
            "{\"Keys\":{\"Keys\":{\"ruler\":\"R\"}},\"CaptureToClipboard\":true,\"CaptureToFolder\":true,\"CaptureFolder\":\"C\",\"CaptureScale\":4,"
            + "\"SightingFormat\":\"round\",\"SightingFirstPerson\":false,\"SightingLens\":60,\"SightingFacing\":null,\"SightingEyeShare\":1,\"SightingHideText\":false,\"SightingGif\":false,\"CaptureGif\":false,\"GifLarge\":false,\"CaptureProfiles\":{},\"LastName\":\"\",\"LastAddress\":\"127.0.0.1\",\"Port\":7777,\"ShareCursor\":true,\"FaceText\":true,\"LabelPlates\":false,\"WheelTurnsHovered\":false,\"HoverLightsCarrier\":false,\"LiftHeight\":2.4,"
            + "\"LetteringReach\":250,\"ReduceFarDetail\":false,\"MakerTools\":false,\"AgentOn\":false,\"AgentPort\":7796,\"AgentToken\":\"\",\"FaceTextScale\":1,\"ShowCursors\":true,\"ShowZones\":true,\"UiScale\":1,\"Tint\":\"\",\"Antialias\":2,\"SmoothFineEdges\":false,\"ShadowDetail\":1,\"ContactShade\":false,\"WindowMode\":0,\"VSync\":true,\"FrameLimit\":0,\"RestWhenIdle\":true,\"ShowFrameRate\":false,\"Renderer\":\"vulkan\",\"FarDetail\":1,\"RenderScale\":1,\"MapPictureMemory\":0,\"SoundVolume\":0.6,\"SoundMuted\":false,\"SoundInBackground\":false,"
            + "\"CameraAt\":{},\"CameraViews\":{},\"PanelPos\":{},\"PanelSize\":{},\"StripSize\":30,\"StripHidden\":[],\"TopLine\":true,\"TopLineWidth\":1,\"PenInk\":\"#1a1a1a\",\"PenSize\":3,\"PenOpacity\":1,"
            + "\"DrawMode\":\"free\",\"DrawLayer\":{},\"HiddenLayers\":{},\"HiddenSheets\":{},\"Decal\":{},\"RulerPins\":{},\"DecalTurn\":0,\"DecalGrowth\":1,\"FormationRanks\":{},\"RangeMetres\":null,\"RangeUnits\":null,\"DiceHand\":{},\"PiecesPack\":null,\"PiecesTab\":0,"
            + "\"ShapeKind\":\"line\",\"LeftWidth\":300,\"RightWidth\":320,\"LogHeight\":200,\"LeftOpen\":true,\"LeftPage\":\"pieces\","
            + "\"RightOpen\":true,\"LogOpen\":true,\"LogFontSize\":12,\"Favourites\":[],\"FavouritesOnly\":false,\"CompassShown\":true,\"CompassPos\":null,\"BlenderPath\":\"\",\"FolderFavourites\":[],\"RecentFolders\":[],\"CheckUpdates\":true,\"ReliefBrush\":6,\"ReliefStrength\":0.35,\"ReliefRate\":10,\"ReliefUpTo\":0,\"ReliefDownTo\":0,\"ReliefLevel\":0,\"ReliefHardness\":0,\"ReliefFormHeight\":20,\"ReliefProfile\":\"round\",\"ReliefContourHeight\":10,\"ReliefContourStep\":10,\"ReliefRough\":3,\"ReliefGrain\":150,\"ReliefErode\":4,\"ReliefLift\":10,\"ReliefMode\":\"raise\",\"ReliefDensity\":1,\"ReliefPaint\":\"brush\",\"ReliefDetails\":{},\"Build\":{\"Mode\":\"places\",\"Place\":\"settlement\",\"Houses\":0.5,\"Walls\":0,\"Grid\":false,\"Bastions\":5,\"Outworks\":0.5,\"Block\":\"house\",\"Line\":\"road\",\"FindsWay\":true,\"Plots\":0.5,\"Buildings\":0.25,\"Brush\":8},\"FormationGap\":0.1,\"FormationDistance\":null,\"Formation\":\"line\",\"NewBackdrop\":\"\",\"NewTableShade\":0,\"Tour\":0}",
            System.Text.Json.JsonSerializer.Serialize(s));
    }

    [Fact]
    public void Folder_favourites_and_recent_folders_of_the_file_explorer_are_kept_across_starts()
    {
        var s = new Settings();
        Assert.True(s.TakeFolders(["C:/maps/", "c:/MAPS/", " ", "D:/scans/"], Enumerable.Range(0, 30).Select(i => $"E:/r{i}/")));
        Assert.False(s.TakeFolders(["C:/maps/", "D:/scans/"], Enumerable.Range(0, 20).Select(i => $"E:/r{i}/")));
        s.Save();
        var back = Settings.Load();
        Assert.Equal(["C:/maps/", "D:/scans/"], back.FolderFavourites);
        Assert.Equal(Settings.RecentFoldersMost, back.RecentFolders.Count);
        Assert.Equal("E:/r0/", back.RecentFolders[0]);
        File.WriteAllText(_file, "{\"FolderFavourites\":[\"C:/a/\",\"C:/A/\",\"\"],\"RecentFolders\":null}");
        var odd = Settings.Load();
        Assert.Equal(["C:/a/"], odd.FolderFavourites);
        Assert.Empty(odd.RecentFolders);
    }

    [Fact]
    public void Whoever_never_saw_the_tour_takes_it_a_first_start_or_a_settings_file_from_before_it()
    {
        Assert.Equal(0, Settings.Load().Tour);
        File.WriteAllText(_file, "{\"Port\":7777}");
        Assert.Equal(0, Settings.Load().Tour);
        File.WriteAllText(_file, "{\"Tour\":-1}");
        Assert.Equal(Settings.TourOver, Settings.Load().Tour);
        var s = Settings.Load();
        s.Tour = 2;
        s.Save();
        Assert.Equal(2, Settings.Load().Tour);
    }

    [Fact]
    public void Saved_settings_read_back_and_no_file_gives_the_defaults()
    {
        Assert.Equal(7777, Settings.Load().Port);
        var s = new Settings { Port = 8058, LastName = "Bruno", CameraAt = { ["feud"] = new[] { 1f, 2f } } };
        s.Keys.Set("roll", "Ctrl+R");
        s.Save();
        var back = Settings.Load();
        Assert.Equal(8058, back.Port);
        Assert.Equal("Bruno", back.LastName);
        Assert.Equal(new[] { 1f, 2f }, back.CameraAt["feud"]);
        Assert.Equal("Ctrl+R", back.Keys.Of("roll"));
    }

    [Fact]
    public void Yesterdays_defaults_are_brought_up_to_date_as_the_file_is_read()
    {
        var legacy = Path.Combine(DataPaths.LegacyDocuments, "captures");
        File.WriteAllText(_file, System.Text.Json.JsonSerializer.Serialize(new { CaptureFolder = legacy.ToUpperInvariant() + Path.DirectorySeparatorChar, ReduceFarDetail = true }));
        var s = Settings.Load();
        Assert.Equal(DataPaths.Captures, s.CaptureFolder);
        Assert.False(s.ReduceFarDetail);
        Assert.Equal(32f, s.LetteringReach);
        File.WriteAllText(_file, System.Text.Json.JsonSerializer.Serialize(new { CaptureFolder = Path.Combine(DataPaths.Documents, "captures") }));
        Assert.Equal(DataPaths.Captures, Settings.Load().CaptureFolder);

        File.WriteAllText(_file, "{\"CaptureFolder\":\"D:\\\\mine\",\"LetteringReach\":3}");
        s = Settings.Load();
        Assert.Equal("D:\\mine", s.CaptureFolder);
        Assert.Equal(Settings.ReachMin, s.LetteringReach);
        File.WriteAllText(_file, "{\"LetteringReach\":9000}");
        Assert.Equal(Settings.ReachOff, Settings.Load().LetteringReach);
        File.WriteAllText(_file, "{\"UiScale\":9}");
        Assert.Equal(Settings.UiScaleMax, Settings.Load().UiScale);
        File.WriteAllText(_file, "{\"UiScale\":0.1}");
        Assert.Equal(Settings.UiScaleMin, Settings.Load().UiScale);
        File.WriteAllText(_file, "{\"Tint\":\"brown\"}");
        Assert.Equal(InterfaceTint.Charcoal, Settings.Load().Tint);
        File.WriteAllText(_file, "{\"Tint\":\"#805F42\"}");
        Assert.Equal("#805f42", Settings.Load().Tint);
    }

    [Fact]
    public void The_interface_fits_the_screen_in_whole_steps_or_at_Windows_scale_and_the_chosen_size_rides_on_top()
    {
        Assert.Equal(1f, Settings.Fitted(1f, 900));
        Assert.Equal(1f, Settings.Fitted(1f, 1080));
        Assert.Equal(1f, Settings.Fitted(1f, 1369));
        Assert.Equal(1f, Settings.Fitted(1f, 1440));
        Assert.Equal(1f, Settings.Fitted(1f, 1800));
        Assert.Equal(2f, Settings.Fitted(1f, 2100));
        Assert.Equal(2f, Settings.Fitted(1f, 2160));
        Assert.Equal(2f, Settings.Fitted(1f, 2880));
        Assert.Equal(1.5f, Settings.Fitted(1.5f, 900), 3);
        Assert.Equal(2f, Settings.Fitted(1.5f, 2160));
        Assert.Equal(Settings.FittedMost, Settings.Fitted(1f, 8000));
        var s = Settings.Load();
        Assert.Equal(1f, s.UiScale);
        s.UiScale = 1.2f;
        Assert.Equal(1.2f, s.UiScaleFor(1f, 1440), 3);
        File.WriteAllText(_file, "{\"UiScale\":1.3,\"UiScaleFollowsWindows\":false}");
        var before = Settings.Load();
        Assert.Equal(1f, before.UiScale);
        Assert.Null(before.UiScaleFollowsWindows);
        before.Save();
        Assert.DoesNotContain("UiScaleFollowsWindows", File.ReadAllText(_file));
        File.WriteAllText(_file, "{\"UiScale\":1.3}");
        Assert.Equal(1.3f, Settings.Load().UiScale);
    }

    [Fact]
    public void Settings_that_cannot_be_read_are_copied_aside_and_the_defaults_used()
    {
        File.WriteAllText(_file, "{ \"Port\": ");
        Problems.Take();
        var s = Settings.Load();
        Assert.Equal(7777, s.Port);
        var said = Assert.Single(Problems.Take());
        Assert.StartsWith("Your settings could not be read (", said);
        Assert.Matches(@"\), so the defaults are used: keys, camera and panels are as new\. a copy of it is kept as settings\.json\.bad-\d{8}-\d{6}\.$", said);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_file)!, "settings.json.bad-*"));
    }
}
