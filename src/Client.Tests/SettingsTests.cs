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
        Assert.Equal(45, Keymap.Actions.Length);
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
    public void New_settings_are_written_with_these_defaults_in_this_order()
    {
        var s = new Settings();
        Assert.Equal(Path.Combine(DataPaths.Documents, "captures"), s.CaptureFolder);
        s.CaptureFolder = "C";
        s.Keys.Set("ruler", "R");
        Assert.Equal(
            "{\"Keys\":{\"Keys\":{\"ruler\":\"R\"}},\"CaptureToClipboard\":true,\"CaptureToFolder\":true,\"CaptureFolder\":\"C\",\"CaptureScale\":4,"
            + "\"SightingSquare\":false,\"SightingFirstPerson\":false,\"SightingWide\":false,\"SightingHideText\":false,\"SightingGif\":false,\"LastName\":\"\",\"LastAddress\":\"127.0.0.1\",\"Port\":7777,\"ShareCursor\":true,\"FaceText\":true,\"LabelPlates\":false,"
            + "\"LetteringReach\":250,\"ReduceFarDetail\":false,\"MakerTools\":false,\"AgentOn\":false,\"AgentPort\":7796,\"AgentToken\":\"\",\"FaceTextScale\":1,\"ShowCursors\":true,\"ShowZones\":true,\"UiScale\":1,\"UiScaleFollowsWindows\":null,\"Antialias\":2,\"SmoothFineEdges\":false,\"ShadowDetail\":1,\"ContactShade\":false,\"WindowMode\":0,\"VSync\":true,\"FrameLimit\":0,\"FarDetail\":1,\"RenderScale\":1,\"MapPictureMemory\":0,\"SoundVolume\":0.6,\"SoundMuted\":false,\"SoundInBackground\":false,"
            + "\"CameraAt\":{},\"CameraViews\":{},\"PanelPos\":{},\"PanelSize\":{},\"StripSize\":30,\"TopLine\":true,\"TopLineWidth\":1,\"PenInk\":\"#1a1a1a\",\"PenSize\":3,\"PenOpacity\":1,"
            + "\"ShapeKind\":\"line\",\"LeftWidth\":300,\"RightWidth\":320,\"LogHeight\":200,\"LeftOpen\":true,\"LeftPage\":\"pieces\","
            + "\"RightOpen\":true,\"LogOpen\":true,\"LogFontSize\":12,\"Favourites\":[],\"FavouritesOnly\":false,\"CompassShown\":true,\"CompassPos\":null,\"BlenderPath\":\"\",\"CheckUpdates\":true,\"ReliefBrush\":6,\"ReliefStrength\":0.35,\"ReliefMode\":\"raise\",\"ReliefDensity\":1,\"ReliefPaint\":\"brush\",\"ReliefDetails\":{},\"FormationGap\":0.1,\"Formation\":\"line\",\"NewBackdrop\":\"\",\"NewTableShade\":0,\"Tour\":0}",
            System.Text.Json.JsonSerializer.Serialize(s));
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
        Assert.Equal(Path.Combine(DataPaths.Documents, "captures"), s.CaptureFolder);
        Assert.False(s.ReduceFarDetail);
        Assert.Equal(32f, s.LetteringReach);

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
    }

    [Fact]
    public void The_interface_size_follows_Windows_until_someone_chose_a_size_of_their_own()
    {
        Assert.True(Settings.Load().FollowsWindows);
        Assert.Equal(1.5f, Settings.Load().UiScaleWith(1.5f));
        File.WriteAllText(_file, "{\"UiScale\":1}");
        Assert.True(Settings.Load().FollowsWindows);
        File.WriteAllText(_file, "{\"UiScale\":1.3}");
        var chosen = Settings.Load();
        Assert.False(chosen.FollowsWindows);
        Assert.Equal(1.3f, chosen.UiScaleWith(1.5f));
        chosen.UiScale = 1f;
        chosen.FollowsWindows = false;
        chosen.Save();
        Assert.False(Settings.Load().FollowsWindows);
        var back = Settings.Load();
        back.FollowsWindows = true;
        Assert.Equal(Settings.UiScaleMax, back.UiScaleWith(3f));
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
