using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ClockPanelTests
{
    static async Task<(HudTests.Rig Rig, FloatPanel Panel)> Opened(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Hud.Top.ClockBtn.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var panel = h.Hud.Clock!.Panel;
        Check.That(panel.Visible, "the clock on the top bar opens its panel");
        return (h, panel);
    }

    static Button Pressing(Control under, string tip) =>
        under.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.TooltipText.Split('\n')[0] == tip);

    static LineEdit Field(Control under, string placeholderOrTip) =>
        under.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().First(f => f.PlaceholderText == placeholderOrTip || f.TooltipText.StartsWith(placeholderOrTip));

    static async Task Press(TestRunner r, Button b)
    {
        b.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
    }

    static async Task Type(TestRunner r, LineEdit field, string text)
    {
        field.Text = text;
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, text);
        await r.Frames(2);
    }

    [NodeTest]
    static async Task A_step_chip_moves_the_time_on_and_back_and_ends_no_turn(TestRunner r)
    {
        var (h, panel) = await Opened(r);
        var clock = h.S.View.Clock;
        long start = clock.Now;
        await Press(r, Pressing(panel, "On 10′"));
        Check.Equal(start + 600, clock.Now, "after a step on");
        await Press(r, Pressing(panel, "Back 1′"));
        Check.Equal(start + 540, clock.Now, "after a step back");
        Check.Equal((0, 0), (clock.Turn, h.Net.Server!.Turns.Count), "turn and turns kept");
        Check.That(h.Hud.Top.ClockBtn.Text.Contains(clock.Cal.TimeOfDay(clock.Now)), "the top bar shows the new time");
    }

    [NodeTest]
    static async Task A_typed_time_jumps_the_clock_and_a_name_names_the_turn_in_the_history(TestRunner r)
    {
        var (h, panel) = await Opened(r);
        var clock = h.S.View.Clock;
        await Type(r, Field(panel, "A date and time"), "1812-09-07T06:30");
        Check.Equal("1812-09-07T06:30", clock.GameTime, "after a typed date");
        await Type(r, Field(panel, "A date and time"), "14:00");
        Check.Equal("1812-09-07T14:00", clock.GameTime, "after a typed time of day");
        await Type(r, Field(panel, "A date and time"), "nonsense");
        Check.Equal("1812-09-07T14:00", clock.GameTime, "after words that are no time");
        Check.Equal("7 September 1812 14:00", Field(panel, "A date and time").Text, "the box writes the date day first");
        await Type(r, Field(panel, "A date and time"), "8/9/1812 06:00");
        Check.Equal("1812-09-08T06:00", clock.GameTime, "a date typed with slashes is read day first");
        var opener = panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.TooltipText == CalendarPick.Tip);
        await Press(r, opener);
        var pop = opener.GetChildren().OfType<PopupPanel>().Single();
        Check.That(pop.Visible, "the calendar beside the time opens");
        var twentieth = pop.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == "20");
        await Press(r, twentieth);
        Check.Equal("1812-09-20T06:00", clock.GameTime, "a day picked on it moves the clock there, at the same time of day");
        Check.That(!GodotObject.IsInstanceValid(pop) || !pop.Visible, "and the calendar closes");
        Check.That(NewBlankScenarioDialog.Takes("") && NewBlankScenarioDialog.Takes("22/07/1812 06:00") && !NewBlankScenarioDialog.Takes("07/22/1812"),
            "a new scenario starts at a date read day first, or the usual start when left empty, and refuses one it cannot read");
        await Type(r, Field(panel, "This turn"), "The Tavern");
        Check.Equal("The Tavern", clock.Name, "the turn's name");
        Check.That(h.Hud.Top.ClockBtn.Text.Contains("The Tavern"), "the top bar names the turn");
        await Press(r, h.Hud.Top.NextTurnBtn);
        Check.Equal(("The Tavern", (string?)null), (h.Net.Server!.Turns[^1].Name, clock.Name), "the name stays with the ended turn");
    }

    [NodeTest]
    static async Task A_timer_is_added_shown_beside_the_clock_told_when_due_and_removed(TestRunner r)
    {
        var (h, panel) = await Opened(r);
        var clock = h.S.View.Clock;
        Field(panel, "New timer").Text = "Reinforcements";
        await Type(r, Field(panel, "14:00 · 2 h"), "20 min");
        Check.Equal(clock.Now + 1200, clock.Timer("Reinforcements")!.Due, "the timer's due time");
        Check.That(h.Hud.Top.ClockBtn.Text.Contains("Reinforcements"), "the next timer shows beside the clock");
        await Press(r, Pressing(panel, "Only umpires see this timer"));
        Check.That(clock.Timer("Reinforcements")!.Everyone, "shown to players");
        h.S.Do(new StepClock { Seconds = 1500 });
        await r.Seconds(0.5);
        Check.That(h.Hud.Top.Said!.Text.Contains("Reinforcements"), "the top line tells it is due");
        Check.That(!h.Hud.Top.ClockBtn.Text.Contains("Reinforcements"), "a timer that is past is no longer the next one");
        await Press(r, Pressing(panel, "Remove this timer"));
        Check.That(clock.Timers == null, "removed");
    }

    [NodeTest]
    static async Task The_clock_runs_at_a_rate_and_pauses_from_the_top_bar(TestRunner r)
    {
        var (h, panel) = await Opened(r);
        var clock = h.S.View.Clock;
        long start = clock.Now;
        var flow = panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == "×600");
        await Press(r, flow);
        Check.Equal(600d, clock.Rate, "rate");
        await r.Seconds(1.2);
        Check.That(clock.Now >= start + 600, $"a second at ×600 moved the time ten minutes or more, it moved {clock.Now - start} s");
        Check.That(h.Hud.Top.ClockBtn.Text.Contains("×600"), "the top bar shows the rate");
        await Press(r, h.Hud.Top.RunBtn);
        long paused = clock.Now;
        Check.That(!clock.Runs, "paused from the top bar");
        await r.Seconds(0.3);
        Check.Equal(paused, clock.Now, "the time holds while paused");
    }

    [NodeTest]
    static async Task The_turn_length_takes_none_and_next_turn_then_only_counts_the_turn(TestRunner r)
    {
        var (h, panel) = await Opened(r);
        var clock = h.S.View.Clock;
        long start = clock.Now;
        var unit = panel.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().First(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == "none"));
        int none = Enumerable.Range(0, unit.ItemCount).First(i => unit.GetItemText(i) == "none");
        unit.Select(none);
        unit.EmitSignal(OptionButton.SignalName.ItemSelected, none);
        await r.Frames(2);
        Check.Equal(0, clock.TurnSeconds, "turn length none");
        await Press(r, h.Hud.Top.NextTurnBtn);
        Check.Equal((1, start), (clock.Turn, clock.Now), "the turn is counted and the time kept");
    }
}
