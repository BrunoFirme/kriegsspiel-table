using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CalendarPickTests
{
    static readonly CalendarDef Real = CalendarDef.Gregorian;

    [NodeTest]
    static async Task The_calendar_lays_out_a_month_on_its_weeks_turns_the_pages_and_a_day_click_sets_the_date(TestRunner r)
    {
        long salamanca = Real.Read("1812-07-22T06:00")!.Value;
        var picked = new List<long>();
        bool done = false;
        var pick = new CalendarPick(Real, () => salamanca, picked.Add) { Done = () => done = true };
        r.AddChild(pick.Content);
        await r.Frame();
        Check.Equal("July 1812", pick.Shown, "it opens on the month of the date");
        Check.Equal(2, pick.Lead, "1 July 1812 is a Wednesday, two places after Monday");
        Check.Equal(31, pick.Days.Length, "July has 31 days");
        pick.Turn(-5);
        Check.Equal("February 1812", pick.Shown, "back five months");
        Check.Equal(29, pick.Days.Length, "1812 was a leap year");
        pick.Turn(-2);
        Check.Equal("December 1811", pick.Shown, "back over the turn of the year");
        pick.Show(1, 1);
        pick.Turn(-1);
        Check.Equal("December -1", pick.Shown, "the year before 1 is 1 BC, with no year 0");
        pick.Turn(1);
        Check.Equal("January 1", pick.Shown, "and on again");
        pick.Retime("14:30");
        Check.Equal(Real.Read("1812-07-22T14:30"), picked.Single(), "a time typed below sets the time of day at once");
        Check.That(!done, "and leaves the calendar open");
        pick.Retime("nonsense");
        Check.Equal(1, picked.Count, "words that are no time change nothing");
        pick.Retime("14:30");
        pick.Show(1811, 12);
        pick.PickDay(14);
        Check.Equal(Real.Read("1811-12-14T14:30"), picked[^1], "a day click keeps the time typed below");
        Check.That(done, "and closes the calendar");
        pick.Content.QueueFree();
        await r.Frame();
    }

    [NodeTest]
    static async Task The_month_name_zooms_out_to_the_months_and_the_year_to_the_years_and_a_pick_zooms_back_in(TestRunner r)
    {
        long salamanca = Real.Read("1812-07-22T06:00")!.Value;
        var pick = new CalendarPick(Real, () => salamanca, _ => { });
        r.AddChild(pick.Content);
        await r.Frame();
        pick.ZoomOut();
        Check.Equal(CalendarPick.Zoom.Months, pick.View, "the month name opens the months of the year");
        Check.Equal(12, pick.Cells.Length, "all twelve months");
        Check.Equal("January", pick.Cells[0], "named in full");
        pick.StepOn(1);
        Check.Equal("1813 1813", pick.Shown, "the arrows turn a year at a time");
        pick.PickCell("March");
        Check.Equal(CalendarPick.Zoom.Days, pick.View, "a month picked opens its days");
        Check.Equal("March 1813", pick.Shown, "of that year");
        pick.ZoomOut();
        pick.ZoomOut();
        Check.Equal(CalendarPick.Zoom.Years, pick.View, "the year opens a page of years");
        Check.Equal("1808,1809,1810,1811,1812,1813,1814,1815,1816,1817,1818,1819", string.Join(",", pick.Cells), "twelve years round the one shown");
        pick.StepOn(1);
        Check.Equal("1820", pick.Cells[0], "the arrows turn twelve years at a time");
        pick.PickCell("1825");
        Check.Equal(CalendarPick.Zoom.Months, pick.View, "a year picked opens its months");
        Check.Equal("1825 1825", pick.Shown, "of that year");
        pick.Show(3, 1);
        pick.ZoomOut();
        pick.ZoomOut();
        Check.Equal("3 BC,2 BC,1 BC,1,2,3,4,5,6,7,8,9", string.Join(",", pick.Cells), "a page of years across the turn of the era has no year 0");
        pick.Content.QueueFree();
        await r.Frame();
    }
}
