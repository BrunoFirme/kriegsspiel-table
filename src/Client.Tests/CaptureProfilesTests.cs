using KTable.Client.Prefs;

namespace KTable.Client.Tests;

[Collection("Problems")]
public class CaptureProfilesTests
{
    readonly string _file;

    public CaptureProfilesTests()
    {
        _file = Path.Combine(TestFolders.New(), "settings.json");
        Settings.FileAt = () => _file;
    }

    static CaptureProfile Reports() => new() { Format = "square", Radius = 120, Sharp = 0.6, Blur = 0.05, HideText = true };
    static CaptureProfile Aar() => new() { SightingGif = true, GifLarge = true, FirstPerson = true, Lens = 90, Radius = 900 };

    [Fact]
    public void Profiles_are_kept_per_scenario_numbered_in_turn_and_the_one_matching_the_settings_is_lit()
    {
        var s = new Settings();
        Assert.Equal("Profile 1", CaptureProfiles.Add(s, "feud", Reports())!.Name);
        Assert.Equal("Profile 2", CaptureProfiles.Add(s, "feud", Aar())!.Name);
        Assert.Equal("Profile 1", CaptureProfiles.Add(s, "seki", Aar())!.Name);
        var feud = CaptureProfiles.Of(s, "feud");
        Assert.Equal(2, feud.Count);
        Assert.Empty(CaptureProfiles.Of(s, "kolin"));
        Assert.Equal(1, CaptureProfiles.Lit(feud, Aar()));
        Assert.Equal(0, CaptureProfiles.Lit(feud, Reports()));
        var nudged = Reports();
        nudged.Radius = 121;
        Assert.Equal(-1, CaptureProfiles.Lit(feud, nudged));
        Assert.True(CaptureProfiles.Keep(s, "feud", 0, nudged));
        Assert.Equal(("Profile 1", 0), (CaptureProfiles.Of(s, "feud")[0].Name, CaptureProfiles.Lit(CaptureProfiles.Of(s, "feud"), nudged)));
    }

    [Fact]
    public void A_name_is_tidied_and_a_taken_or_empty_name_is_refused_and_six_is_the_most()
    {
        var s = new Settings();
        CaptureProfiles.Add(s, "feud", Reports());
        CaptureProfiles.Add(s, "feud", Aar());
        Assert.True(CaptureProfiles.Rename(s, "feud", 0, "  Player   reports "));
        Assert.Equal("Player reports", CaptureProfiles.Of(s, "feud")[0].Name);
        Assert.False(CaptureProfiles.Rename(s, "feud", 1, "player REPORTS"));
        Assert.False(CaptureProfiles.Rename(s, "feud", 1, "   "));
        Assert.True(CaptureProfiles.Rename(s, "feud", 1, new string('A', 40)));
        Assert.Equal(CaptureProfiles.LongestName, CaptureProfiles.Of(s, "feud")[1].Name.Length);
        Assert.True(CaptureProfiles.Rename(s, "feud", 1, "Profile 3"));
        Assert.Equal("Profile 4", CaptureProfiles.Add(s, "feud", Aar())!.Name);
        while (CaptureProfiles.Of(s, "feud").Count < CaptureProfiles.Most) CaptureProfiles.Add(s, "feud", Aar());
        Assert.Null(CaptureProfiles.Add(s, "feud", Aar()));
        while (CaptureProfiles.Of(s, "feud").Count > 0) Assert.True(CaptureProfiles.Remove(s, "feud", 0));
        Assert.False(s.CaptureProfiles.ContainsKey("feud"));
        Assert.False(CaptureProfiles.Remove(s, "feud", 0));
    }

    [Fact]
    public void Profiles_come_back_from_the_file_and_broken_ones_are_mended_or_dropped()
    {
        var s = Settings.Load();
        CaptureProfiles.Add(s, "feud", Reports());
        CaptureProfiles.Rename(s, "feud", 0, "Reports");
        CaptureProfiles.Add(s, "feud", Aar());
        CaptureProfiles.Rename(s, "feud", 1, "AAR");
        s.Save();
        var back = CaptureProfiles.Of(Settings.Load(), "feud");
        Assert.Equal(new[] { "Reports", "AAR" }, back.Select(p => p.Name));
        Assert.True(back[0].SameAs(Reports()) && back[1].SameAs(Aar()));
        File.WriteAllText(_file, """
            {"CaptureProfiles":{
              "feud":[{"Name":"Odd","Format":"hex","Lens":500,"EyeShare":99,"Radius":50,"Sharp":7,"Blur":-1},
                      {"Name":"odd","Radius":50},{"Name":"No reach","Radius":0},{"Name":"  ","Radius":10},null],
              "empty":[{"Name":"Gone","Radius":-3}]}}
            """);
        var mended = Settings.Load();
        var p = Assert.Single(CaptureProfiles.Of(mended, "feud"));
        Assert.Equal(("Odd", "round", SightingDraft.WidestLens, SightingDraft.EyeHighestShare, 1.0, SightingDraft.LeastBlur),
            (p.Name, p.Format, p.Lens, p.EyeShare, p.Sharp, p.Blur));
        Assert.False(mended.CaptureProfiles.ContainsKey("empty"));
    }
}
