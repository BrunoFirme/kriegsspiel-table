using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class DicePanelTests
{
    [NodeTest]
    static async Task A_die_of_any_sides_joins_the_hand_and_rolls(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Hud.Ctx.Cfg.DiceHand.Clear();
        h.Hud.OpenPanel("dice");
        await r.Frames(2);
        var body = h.Hud.Dice.Panel;
        var sides = body.FindChildren("*", nameof(SpinBox), true, false).OfType<SpinBox>().Single();
        sides.Value = 7;
        Pressed(body, "Add");
        Pressed(body, "Add");
        Check.Equal(2, h.Hud.Ctx.Cfg.DiceHand.GetValueOrDefault(7), "two d7 in the hand");
        Check.That(Labels(body).Any(t => t == "2d7"), "the hand reads 2d7");
        sides.Value = 5000;
        Check.Equal(1000.0, sides.Value, "sides stop at the server's most");
        Pressed(body, "Roll");
        for (int i = 0; i < 30 && !Labels(body).Any(t => t.StartsWith("2d7 ")); i++) await r.Frames(1);
        var line = Labels(body).FirstOrDefault(t => t.StartsWith("2d7 ")) ?? "";
        var rolled = line.Split("   ")[1].Split(" · ").Select(int.Parse).ToList();
        Check.That(rolled.Count == 2 && rolled.All(v => v is >= 1 and <= 7), "two results from 1 to 7: " + line);
    }

    static void Pressed(Node body, string text) =>
        body.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

    static IEnumerable<string> Labels(Node body) =>
        body.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text);
}
