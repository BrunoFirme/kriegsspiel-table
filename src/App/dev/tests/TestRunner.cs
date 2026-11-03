using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

[AttributeUsage(AttributeTargets.Method)]
sealed class NodeTestAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
sealed class ScreenTestAttribute : Attribute { }

sealed class TestFailure(string message) : Exception(message);

static class Check
{
    public static void That(bool ok, string what) { if (!ok) throw new TestFailure(what); }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new TestFailure($"{what}: expected {expected}, got {actual}");
    }
}

public partial class TestRunner : Node
{
    public override async void _Ready()
    {
        int passed = 0, failed = 0;
        bool screen = DisplayServer.GetName() != "headless";
        var kind = screen ? "screen tests" : "node tests";
        try
        {
            var tests = typeof(TestRunner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => screen ? m.GetCustomAttribute<ScreenTestAttribute>() != null : m.GetCustomAttribute<NodeTestAttribute>() != null)
                .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var m in tests)
            {
                var name = $"{m.DeclaringType!.Name}.{m.Name}";
                try
                {
                    await (Task)m.Invoke(null, new object[] { this })!;
                    passed++;
                    GD.Print("PASS " + name);
                }
                catch (Exception e)
                {
                    failed++;
                    var why = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                    GD.PrintErr($"FAIL {name}: {(why is TestFailure ? why.Message : why.ToString())}");
                }
                foreach (var child in GetChildren()) child.QueueFree();
                await Frame();
            }
        }
        catch (Exception e) { failed++; GD.PrintErr("FAIL the runner itself: " + e); }
        GD.Print($"{kind}: {passed} passed, {failed} failed");
        GetTree().Quit(failed == 0 && passed > 0 ? 0 : 1);
    }

    public async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    public async Task Frames(int n) { for (int i = 0; i < n; i++) await Frame(); }

    public async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
