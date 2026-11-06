using System.Reflection;
using System.Runtime.Loader;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class RepeatableTests
{
    [Fact]
    public void Unset_dice_and_spawned_guids_differ_from_game_to_game()
    {
        if (Environment.GetEnvironmentVariable(Repeatable.Variable) is { Length: > 0 }) return;
        Assert.False(Repeatable.On);
        var one = Probe.Play().Split('|');
        var two = Probe.Play().Split('|');
        Assert.NotEqual(one[0], two[0]);
        Assert.NotEqual(one[1] + one[2], two[1] + two[2]);
    }

    [Fact]
    public void Set_two_games_played_alike_draw_and_stamp_alike()
    {
        const string value = "seed=42,clock=2026-05-06T07:08:09Z";
        var one = PrivateCopy.Run(value, typeof(Probe), nameof(Probe.Play));
        var two = PrivateCopy.Run(value, typeof(Probe), nameof(Probe.Play));
        Assert.Equal(one, two);
        Assert.Contains("2026-05-06T07:08:09", one);
        var other = PrivateCopy.Run("seed=43,clock=2026-05-06T07:08:09Z", typeof(Probe), nameof(Probe.Play));
        Assert.NotEqual(one.Split('|')[0], other.Split('|')[0]);
    }

    public static class Probe
    {
        public static string Play()
        {
            using var t = new Table();
            t.Ok(Table.Ump, new RollDice { Sides = 1000, Count = 20, Label = "fire" });
            var dice = string.Join(",", ((RollDice)t.Server.Log[^1]).Results);
            t.Server.Handle(Table.Host, new SetScript { Layer = Layers.Umpire, Id = "p-000002", Text = """
                function on_load(s)
                  board.spawn{ def = "marker", x = 3, z = 4, label = "made" }
                  print("r " .. math.random(1, 1000000000))
                end
                """ });
            var made = Assert.Single(t.Server.State.Layer(Layers.Umpire), p => p.Label == "made");
            var line = Assert.Single(t.Server.Scripts.Feed, l => l.Text.StartsWith("r "));
            var fresh = new GameServer(GameState.FromScenario(Table.MakeScenario()));
            return string.Join("|", dice, made.Guid, line.Text, line.At, fresh.Created);
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
                try { return (string)t.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!; }
                catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
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
