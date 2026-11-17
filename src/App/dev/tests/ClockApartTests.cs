using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ClockApartTests
{
    [NodeTest]
    static async Task A_sheet_given_a_clock_apart_shows_its_time_on_its_row_and_the_top_bar_follows_the_place_looked_at(TestRunner r)
    {
        var (h, places) = await PlacesPanelTests.Opened(r);
        var clock = h.S.View.Clock;
        var sheet = PlaceTree.Of(h.S.View, h.S.Def).First(p => p.Kind == PlaceKind.Sheet);
        await PlacesPanelTests.Pick(r, places, sheet.Name, "Clock apart");
        Check.That(clock.Apart?.ContainsKey("s0") == true, "the sheet keeps a clock of its own");
        Check.Equal(0, clock.Of("s0").TurnSeconds, "a clock apart starts with a turn length of none");
        h.S.Do(new StepClock { Seconds = 600, Place = "s0" });
        h.Table.Rig.Target = Coords.ToWorld(sheet.At);
        await r.Seconds(0.6);
        string there = clock.Cal.TimeOfDay(clock.Of("s0").Now);
        Check.Equal("s0", h.Hud.Top.ClockPlace, "the clock follows the place looked at");
        Check.That(h.Hud.Top.ClockBtn.Text.Contains(there) && h.Hud.Top.ClockBtn.Text.Contains(sheet.Name), $"the top bar names the place and its time: {h.Hud.Top.ClockBtn.Text}");
        Check.That(places.Item(sheet.Name)!.GetText(1).Contains(there), "the row shows the place's time");
        long before = clock.Of("s0").Now;
        h.S.Do(new AdvanceClock());
        await r.Frames(3);
        Check.Equal((1, before), (clock.Turn, clock.Of("s0").Now), "next turn counts the turn and leaves the clock apart be");
        h.Table.Rig.Target = Coords.ToWorld(sheet.At + new Vec2(100000, 100000));
        await r.Seconds(0.6);
        Check.That(h.Hud.Top.ClockPlace == null && h.Hud.Top.ClockBtn.Text.Contains(clock.Cal.TimeOfDay(clock.Now)), "away from the place the table's clock shows");
        await PlacesPanelTests.Pick(r, places, sheet.Name, "Give the table this time");
        Check.Equal(clock.Of("s0").Now, clock.Now, "the table takes the place's time");
        await PlacesPanelTests.Pick(r, places, sheet.Name, "Clock apart");
        Check.That(clock.Apart == null, "unticked, the place is on the table's clock again");
    }

    [NodeTest]
    static async Task The_clock_panel_edits_the_clock_of_the_place_looked_at(TestRunner r)
    {
        var (h, places) = await PlacesPanelTests.Opened(r);
        var clock = h.S.View.Clock;
        var sheet = PlaceTree.Of(h.S.View, h.S.Def).First(p => p.Kind == PlaceKind.Sheet);
        await PlacesPanelTests.Pick(r, places, sheet.Name, "Clock apart");
        h.Table.Rig.Target = Coords.ToWorld(sheet.At);
        h.Hud.Top.ClockBtn.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Seconds(0.6);
        long table = clock.Now, place = clock.Of("s0").Now;
        var on = h.Hud.Clock!.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.TooltipText == "On 10′");
        on.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.Equal((table, place + 600), (clock.Now, clock.Of("s0").Now), "the step moved the place's clock alone");
    }
}
