namespace KTable.NetRelay.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void EveryNamedProfileReadsBackAsItself()
    {
        foreach (var (name, profile) in Profile.Presets)
        {
            Assert.Equal(profile, Profile.Parse(name));
            Assert.Equal(profile, Profile.Parse(name.ToUpperInvariant()));
        }
    }

    [Fact]
    public void SettingsChangeANamedProfile()
    {
        var mobile = Profile.Named("mobile")!;
        Assert.Equal(mobile with { LossPercent = 5, PingMs = 200 }, Profile.Parse("mobile, loss=5, ping=200"));
    }

    [Fact]
    public void SettingsAloneStartFromLan()
    {
        Assert.Equal(Profile.Lan with { PingMs = 120, LossPercent = 2, BurstLength = 3, DownKbps = 2000, UpKbps = 500, QueueMs = 250, DuplicatePercent = 1, ReorderPercent = 0.5, JitterMs = 10 },
            Profile.Parse("ping=120,loss=2,burst=3,down=2000,up=500,queue=250,dup=1,reorder=0.5,jitter=10"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nowhere")]
    [InlineData("mobile,dsl")]
    [InlineData("ping")]
    [InlineData("ping=fast")]
    [InlineData("ping=-5")]
    [InlineData("ping=NaN")]
    [InlineData("ping=Infinity")]
    [InlineData("loss=101")]
    [InlineData("burst=0.5")]
    [InlineData("speed=10")]
    public void ABadProfileSaysWhatIsWrong(string spec)
    {
        var e = Assert.Throws<FormatException>(() => Profile.Parse(spec));
        Assert.False(string.IsNullOrWhiteSpace(e.Message));
    }

    [Fact]
    public void NumbersReadTheSameInEveryLanguage()
    {
        var kept = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            Assert.Equal(0.5, Profile.Parse("loss=0.5").LossPercent);
            Assert.Contains("loss 0.5%", Profile.Parse("loss=0.5").Describe());
        }
        finally { Thread.CurrentThread.CurrentCulture = kept; }
    }

    [Fact]
    public void ADescriptionNamesWhatIsSet()
    {
        Assert.Equal("ping 90 ms ± 40, loss 2% in bursts of 3, down 5 Mbit/s, up 1.5 Mbit/s", Profile.Named("mobile")!.Describe());
        Assert.Equal("ping 0 ms, down no limit, up no limit", Profile.Lan.Describe());
        Assert.Equal("ping 0 ms, down 256 kbit/s, up no limit, queue 90 ms", Profile.Parse("down=256,queue=90").Describe());
    }

    [Theory]
    [InlineData("7801", 7801, "lan")]
    [InlineData("7801=mobile", 7801, "mobile")]
    [InlineData("7802=dsl,loss=3", 7802, "dsl,loss=3")]
    public void ALaneIsAPortAndAProfile(string text, int port, string spec)
    {
        Assert.Equal(new LaneSetting(port, spec), LaneSetting.Parse(text));
    }

    [Theory]
    [InlineData("mobile")]
    [InlineData("0=mobile")]
    [InlineData("70000=mobile")]
    [InlineData("7801=nowhere")]
    public void ABadLaneSaysWhatIsWrong(string text)
    {
        Assert.Throws<FormatException>(() => LaneSetting.Parse(text));
    }
}
