using System.Reflection;
using System.Runtime.Loader;
using KTable.Core;

namespace KTable.Core.Tests;

public class RepeatableTests
{
    [Fact]
    public void Unset_it_is_the_usual_calls()
    {
        if (Environment.GetEnvironmentVariable(Repeatable.Variable) is { Length: > 0 }) return;
        Assert.False(Repeatable.On);
        Assert.Null(Repeatable.Seed);
        Assert.Same(Random.Shared, Repeatable.Shared);
        Assert.NotEqual(Repeatable.NewGuid(), Repeatable.NewGuid());
        var a = Repeatable.NewRandom();
        var b = Repeatable.NewRandom();
        Assert.NotEqual(Enumerable.Range(0, 8).Select(_ => a.Next()).ToList(), Enumerable.Range(0, 8).Select(_ => b.Next()).ToList());
        var before = DateTime.UtcNow;
        var now = Repeatable.UtcNow;
        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.InRange(now, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Local, Repeatable.Now.Kind);
        Assert.InRange(Repeatable.Now, DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1));
    }

    [Fact]
    public void Set_two_runs_agree_and_another_seed_differs()
    {
        var one = PrivateCopy.Run("seed=7,clock=2026-01-02T03:04:05Z", typeof(Probe), nameof(Probe.Sample));
        var two = PrivateCopy.Run("seed=7,clock=2026-01-02T03:04:05Z", typeof(Probe), nameof(Probe.Sample));
        Assert.Equal(one, two);
        Assert.StartsWith("on 7|", one);
        Assert.NotEqual(one, PrivateCopy.Run("seed=8,clock=2026-01-02T03:04:05Z", typeof(Probe), nameof(Probe.Sample)));
    }

    [Fact]
    public void Set_the_clock_starts_at_the_instant_and_moves_a_millisecond_a_read()
    {
        var s = PrivateCopy.Run("clock=2026-01-02T03:04:05Z", typeof(Probe), nameof(Probe.Clock));
        Assert.Equal("2026-01-02T03:04:05.0000000Z 2026-01-02T03:04:05.0010000Z 2026-01-02T03:04:05.0020000Z", s);
        Assert.Equal(s, PrivateCopy.Run("seed=3, clock=2026-01-02T03:04:05", typeof(Probe), nameof(Probe.Clock)));
        Assert.StartsWith("2000-01-01T00:00:00.0000000Z", PrivateCopy.Run("seed=3", typeof(Probe), nameof(Probe.Clock)));
    }

    [Fact]
    public void A_value_it_cannot_read_leaves_runs_as_usual()
    {
        Assert.StartsWith("off|", PrivateCopy.Run("seed=x", typeof(Probe), nameof(Probe.Sample)));
        Assert.StartsWith("off|", PrivateCopy.Run("speed=3", typeof(Probe), nameof(Probe.Sample)));
    }

    [Fact]
    public void Set_journal_names_repeat_too()
    {
        var one = PrivateCopy.Run("seed=11,clock=2026-03-04T05:06:07Z", typeof(Probe), nameof(Probe.Journal));
        Assert.Equal(one, PrivateCopy.Run("seed=11,clock=2026-03-04T05:06:07Z", typeof(Probe), nameof(Probe.Journal)));
        Assert.StartsWith("20260304-050607-", one);
    }

    public static class Probe
    {
        public static string Sample()
        {
            var r = Repeatable.NewRandom();
            var r2 = Repeatable.NewRandom();
            return string.Join("|", new[]
            {
                Repeatable.On ? $"on {Repeatable.Seed}" : "off",
                string.Join(",", Enumerable.Range(0, 3).Select(_ => Repeatable.NewGuid().ToString("N"))),
                string.Join(",", Enumerable.Range(0, 3).Select(_ => r.Next(1, 1_000_000))),
                string.Join(",", Enumerable.Range(0, 3).Select(_ => r2.Next(1, 1_000_000))),
                string.Join(",", Enumerable.Range(0, 3).Select(_ => Repeatable.Shared.Next(0, 0x1000000))),
                Repeatable.UtcNow.ToString("o"), Repeatable.Now.ToUniversalTime().ToString("o"),
            });
        }

        public static string Clock() => string.Join(" ", Enumerable.Range(0, 3).Select(_ => Repeatable.UtcNow.ToString("o")));

        public static string Journal()
        {
            var root = Path.Combine(Path.GetTempPath(), "ktable-core-tests", "repeatable-" + Repeatable.NewGuid().ToString("N"));
            try
            {
                var saves = new SaveStore(Path.Combine(root, "_saves", "live"), Path.Combine(root, "_saves", "history"), Path.Combine(root, "_saves", "trash"));
                var j = InstallJournal.Begin(root, saves, "a test", Repeatable.UtcNow);
                return Path.GetFileName(j.FilePath);
            }
            finally { BestEffort.DeleteFolder(root); }
        }
    }
}

static class PrivateCopy
{
    static readonly object Gate = new();

    public static string Run(string value, Type probe, string method)
    {
        lock (Gate)
        {
            _ = Repeatable.On;
            var dir = Path.GetDirectoryName(probe.Assembly.Location)!;
            var alc = new Copy(dir, probe.Assembly.GetName().Name!);
            var old = Environment.GetEnvironmentVariable(Repeatable.Variable);
            Environment.SetEnvironmentVariable(Repeatable.Variable, value);
            try
            {
                var asm = alc.LoadFromAssemblyPath(probe.Assembly.Location);
                var t = asm.GetType(probe.FullName!, throwOnError: true)!;
                return (string)t.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            }
            finally
            {
                Environment.SetEnvironmentVariable(Repeatable.Variable, old);
                alc.Unload();
            }
        }
    }

    sealed class Copy : AssemblyLoadContext
    {
        readonly string _dir, _self;
        public Copy(string dir, string self) : base(isCollectible: true) { _dir = dir; _self = self; }
        protected override Assembly? Load(AssemblyName name) =>
            name.Name is "KTable.Core" or "KTable.Server" || name.Name == _self ? LoadFromAssemblyPath(Path.Combine(_dir, name.Name + ".dll")) : null;
    }
}
