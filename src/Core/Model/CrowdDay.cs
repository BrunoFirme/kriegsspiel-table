using E = KTable.Core.Model.CrowdEntry;
using B = KTable.Core.Model.CrowdBaseline;

namespace KTable.Core.Model;

public readonly record struct CrowdLeg(int To, int Depart, bool Out = false);

public sealed class CrowdPath
{
    public IReadOnlyList<Vec2> Points { get; }
    public IReadOnlyList<float>? Heights { get; }
    readonly double[] _along;

    public CrowdPath(IReadOnlyList<Vec2> points, IReadOnlyList<float>? heights = null)
    {
        Points = points;
        Heights = heights;
        _along = new double[points.Count];
        for (int i = 1; i < points.Count; i++) _along[i] = _along[i - 1] + (points[i] - points[i - 1]).Length;
    }

    public double Length => _along.Length == 0 ? 0 : _along[^1];

    public float HeightAt(double s)
    {
        if (Heights is not { Count: > 0 } h) return float.NaN;
        if (h.Count == 1) return h[0];
        int i = Math.Clamp(Array.BinarySearch(_along, s) is var f && f < 0 ? ~f : f, 1, h.Count - 1);
        double span = _along[i] - _along[i - 1], k = span <= 0 ? 0 : Math.Clamp((s - _along[i - 1]) / span, 0, 1);
        return (float)(h[i - 1] + (h[i] - h[i - 1]) * k);
    }

    public (Vec2 At, double Heading) At(double s)
    {
        if (Points.Count == 1) return (Points[0], 0);
        int i = Array.BinarySearch(_along, s);
        if (i < 0) i = ~i;
        i = Math.Clamp(i, 1, Points.Count - 1);
        var a = Points[i - 1];
        var d = Points[i] - a;
        double span = _along[i] - _along[i - 1], k = span <= 0 ? 0 : Math.Clamp((s - _along[i - 1]) / span, 0, 1);
        return (new Vec2(a.X + d.X * k, a.Z + d.Z * k), Math.Atan2(d.X, d.Z));
    }
}

public readonly record struct CrowdSpan(int Start, int End, int Place, CrowdPath? Path, int To, bool Out = false);

public readonly record struct CrowdWhere(Vec2 At, double Heading, double Stride, bool Moving, int Place, float Height = float.NaN, int To = -1);

public static class CrowdDay
{
    public const int Day = CalendarDef.SecondsInDay;
    const int Hour = CalendarDef.SecondsInHour, Least = 300, PlanStream = 31, Shifts = 3, ShiftHours = 8;
    const double MillRate = 0.05, MillTurn = 0.9, NearPick = 3, LateHour = 23, DoorM = 3, FileM = 1.4, LineM = 1.2;

    public static DayKind KindOf(Nationality flat, string? weekday) =>
        weekday != null && flat.Day?.HolyDay is { Length: > 0 } holy && weekday.Equals(holy, StringComparison.OrdinalIgnoreCase) ? DayKind.Holy
        : weekday != null && flat.Day?.MarketDays?.Any(d => d.Equals(weekday, StringComparison.OrdinalIgnoreCase)) == true ? DayKind.Market
        : DayKind.Working;

    public static List<CrowdLeg> Plan(CrowdTown town, CrowdResident r, DayKind kind, long dayNumber)
    {
        if (r.Leader >= 0 && r.Leader < town.Residents.Count) return Plan(town, town.Residents[r.Leader], kind, dayNumber);
        var dice = new Dice(unchecked(r.Entry.Seed ^ (int)dayNumber * 7919), PlanStream);
        var plan = new Planner(town, r, dice);
        var e = r.Entry;
        var day = town.People.Day ?? new DayRules();
        double holyAt = day.HolyAt ?? 9, dark = day.DarkBy ?? 21;
        if (Timetable(day, e.Calling, kind) is { } stops)
        {
            Follow(plan, r, stops, dice);
            return plan.Legs;
        }
        if (e.Calling == B.Soldier)
        {
            Watch(plan, town, r, dayNumber, dice);
            return plan.Legs;
        }
        if (kind == DayKind.Holy)
        {
            if (!dice.Chance(0.8) || plan.Near(CrowdUse.Worship) is not { } worship) return plan.Legs;
            plan.Go(worship, holyAt - 0.3, 0.1);
            if (dice.Chance(0.4) && (plan.Near(dice.Chance(0.5) ? CrowdUse.Square : CrowdUse.Street) ?? plan.Near(CrowdUse.Market)) is { } after) plan.Go(after, holyAt + 1.5, 0.2);
            plan.Go(r.Home, plan.Last + 1 + dice.Between(0, 1), 0);
            return plan.Legs;
        }
        bool market = kind == DayKind.Market;
        switch (e.Calling)
        {
            case B.Cleric when plan.Near(CrowdUse.Worship) is { } church:
                plan.Go(church, 7, 0.4);
                if (dice.Chance(0.2)) plan.Out(10, 1);
                plan.Go(r.Home, 12, 0.3);
                plan.Go(church, 14, 0.3);
                plan.Go(r.Home, 18, 0.5);
                break;
            case B.Housewife:
                Errands(plan, r, market, dice);
                break;
            case B.Washer when r.Work >= 0:
                plan.Go(r.Work, 8, 0.5);
                plan.Go(r.Home, 12, 0.5);
                break;
            case B.Youngster:
                foreach (var (from, until) in new[] { (9.5, 12.0), (14.0, 17.0) })
                    if (dice.Chance(0.2) && (plan.Near(CrowdUse.Street) ?? plan.Near(CrowdUse.Square)) is { } play)
                    {
                        plan.Go(play, from, 1);
                        plan.Go(r.Home, until, 0.5);
                    }
                break;
            default:
                if (r.Work >= 0 && e.Band is E.Adult or E.Youth) Workday(plan, town, r, day, market, dark, dice);
                else if (e.Band == E.Elder && dice.Chance(0.3)) plan.Out(10, 2);
                else if (dice.Chance(0.2) && (plan.Near(dice.Chance(0.5) ? CrowdUse.Square : CrowdUse.Street) ?? plan.Near(CrowdUse.Worship)) is { } stroll)
                {
                    plan.Go(stroll, 10, 1);
                    plan.Go(r.Home, 12, 0.5);
                }
                break;
        }
        return plan.Legs;
    }

    static List<TimetableStop>? Timetable(DayRules day, string calling, DayKind kind) =>
        day.Timetables is not { } all ? null : all.GetValueOrDefault($"{calling}:{kind.ToString().ToLowerInvariant()}") ?? all.GetValueOrDefault(calling);

    static void Follow(Planner plan, CrowdResident r, List<TimetableStop> stops, Dice dice)
    {
        foreach (var stop in stops.OrderBy(s => s.At))
        {
            if (stop.Chance > 0 && !dice.Chance(stop.Chance)) continue;
            if (stop.To == TimetableStop.Out)
            {
                plan.Out(stop.At, Math.Max(stop.Hours, 0.25));
                continue;
            }
            int? place = stop.To switch
            {
                TimetableStop.Home => r.Home,
                TimetableStop.Work => r.Work >= 0 ? r.Work : null,
                _ => Enum.TryParse<CrowdUse>(stop.To, true, out var use) ? plan.Near(use) : null,
            };
            if (place is { } to) plan.Go(to, stop.At, stop.Play);
        }
    }

    static void Errands(Planner plan, CrowdResident r, bool market, Dice dice)
    {
        double pick = dice.Next();
        var errand = market ? (pick < 0.5 ? plan.Near(CrowdUse.Market) : null)
            : pick < 0.25 ? plan.Near(CrowdUse.Well) : pick < 0.4 ? plan.Near(CrowdUse.Street) : pick < 0.5 ? plan.Near(CrowdUse.Market) : pick < 0.65 ? plan.Near(CrowdUse.Work) : null;
        if (errand is { } go)
        {
            plan.Go(go, market ? 9 : 9.5, 1.5);
            plan.Go(r.Home, plan.Last + (market ? 1.5 : 0.7), 0.4);
        }
        if (dice.Chance(0.2)) plan.Out(15, 1.5);
    }

    static void Workday(Planner plan, CrowdTown town, CrowdResident r, DayRules day, bool market, double dark, Dice dice)
    {
        var e = r.Entry;
        bool farmer = e.Calling == B.Farmer, smith = e.Calling == B.Smith, stall = market && e.Calling == B.Merchant && dice.Chance(0.6);
        int work = stall && plan.Near(CrowdUse.Market) is { } mart ? mart : e.Calling == B.Labourer && dice.Chance(0.15) && plan.Near(CrowdUse.Street) is { } corner ? corner : r.Work;
        plan.Go(work, farmer ? 5.5 : 7, 0.7);
        if (smith) plan.Out(9, 2);
        else if (e.Calling == B.Merchant && !stall && dice.Chance(0.2)) plan.Out(10, 1);
        if (day.Rest is [var restFrom, var restTo, ..])
        {
            plan.Go(r.Home, restFrom, 0.2);
            plan.Go(work, restTo, 0.2);
        }
        else if (farmer)
        {
            plan.Go(r.Home, 12, 0.3);
            plan.Go(work, 13, 0.3);
        }
        if (e.Sex == E.Man && day.Prayers is { Count: > 0 } prayers && plan.Near(CrowdUse.Worship) is { } mosque)
            foreach (double p in prayers.Where(p => p > plan.Last + 0.5 && p < 17))
            {
                plan.Go(mosque, p, 0.1);
                plan.Go(work, p + 0.5, 0.1);
            }
        if (smith) plan.Out(14, 2);
        plan.Go(r.Home, farmer ? 18.5 : 17.5, 0.8);
        if (e.Sex != E.Man || !dice.Chance(0.3) || plan.Near(CrowdUse.Tavern) is not { } tavern) return;
        plan.Go(tavern, plan.Last + 0.5, 0.3);
        if (dice.Chance(0.3)) plan.Out(plan.Last + 0.5, 1);
        plan.Go(r.Home, Math.Min(Math.Max(plan.Last + 1, dark - 1), LateHour), 0.5);
    }

    static void Watch(Planner plan, CrowdTown town, CrowdResident r, long dayNumber, Dice dice)
    {
        int post = r.Work >= 0 ? r.Work : r.Home, shift = (int)((r.Entry.Id + dayNumber) % Shifts);
        double from = 6 + shift * ShiftHours, to = from + ShiftHours;
        var rounds = new[] { CrowdUse.Gate, CrowdUse.Market, CrowdUse.Square, CrowdUse.Well, CrowdUse.Street }
            .SelectMany(u => town.Nearest(post, u, 1)).Where(p => p != post).Distinct().Take(3).ToList();
        if (shift == Shifts - 1) plan.Go(post, 0, 0, true);
        if (shift < Shifts - 1)
        {
            plan.Go(post, from, 0, true);
            foreach (double at in new[] { from + 2, from + 5 })
            {
                foreach (int stop in rounds) plan.Go(stop, Math.Max(at, plan.Last + 0.15), 0, true);
                plan.Go(post, plan.Last + 0.15, 0, true);
            }
        }
        plan.Go(r.Home, shift == Shifts - 1 ? 6 : to, 0);
        if (shift == Shifts - 1) plan.Go(post, 22, 0, true);
    }

    sealed class Planner(CrowdTown town, CrowdResident r, Dice dice)
    {
        public readonly List<CrowdLeg> Legs = new();
        int _at = r.Home;
        bool _out;
        public double Last { get; private set; }

        public int? Near(CrowdUse use)
        {
            var near = town.Nearest(r.Home, use, (int)NearPick);
            return near.Length == 0 ? null : near[dice.Below(near.Length)];
        }

        public void Go(int place, double hour, double play, bool outside = false)
        {
            double when = Math.Max(hour + dice.Between(-play, play), Legs.Count == 0 ? 0 : Last + (double)Least / Hour);
            if (place == _at && outside == _out || when >= LateHour + 1) return;
            Legs.Add(new CrowdLeg(place, (int)(when * Hour), outside));
            (_at, _out, Last) = (place, outside, when);
        }

        public void Out(double hour, double hours)
        {
            int at = _at;
            Go(at, hour, 0.3, true);
            Go(at, Last + hours, 0.2);
        }
    }

    static bool Seen(CrowdPlace place, bool outside) => place.Outdoor || outside;

    public static List<CrowdSpan> Spans(CrowdTown town, CrowdResident r, IReadOnlyList<CrowdLeg> legs, Func<int, int, CrowdPath?> path, double upm)
    {
        var spans = new List<CrowdSpan>();
        double speed = r.SpeedM * Math.Max(upm, 1e-9);
        int at = r.Home, since = 0;
        bool outside = false;
        foreach (var leg in legs)
        {
            int depart = Math.Max(leg.Depart, since);
            if (leg.To == at)
            {
                if (Seen(town.Places[at], outside) && depart > since) spans.Add(new CrowdSpan(since, depart, at, null, at, outside));
                (since, outside) = (depart, leg.Out);
                continue;
            }
            if (path(at, leg.To) is not { } route) continue;
            if (Seen(town.Places[at], outside) && depart > since) spans.Add(new CrowdSpan(since, depart, at, null, leg.To, outside));
            int arrive = depart + (int)Math.Ceiling(route.Length / speed);
            spans.Add(new CrowdSpan(depart, arrive, -1, route, leg.To));
            (at, outside, since) = (leg.To, leg.Out, arrive);
        }
        if (Seen(town.Places[at], outside) && since < Day) spans.Add(new CrowdSpan(since, Day, at, null, at, outside));
        return spans;
    }

    public static bool At(CrowdTown town, CrowdResident r, in CrowdSpan span, int second, double upm, out CrowdWhere where)
    {
        if (span.Path is { } route)
        {
            double walked = (second - span.Start) * r.SpeedM * Math.Max(upm, 1e-9), behind = r.File * FileM * upm, along = Math.Clamp(walked - behind, 0, route.Length);
            var (point, heading) = route.At(along);
            where = new CrowdWhere(point, heading, along, walked > behind, -1, route.HeightAt(along), span.To);
            return true;
        }
        var place = town.Places[span.Place];
        if (r.Entry.Calling == B.Soldier)
        {
            var spot = place.At + new Vec2((r.File - (Squad - 1) / 2.0) * LineM * upm, 0);
            where = new CrowdWhere(spot, Math.PI, 0, false, span.Place);
            return true;
        }
        return Stay(place, r, second, upm, span.Place, span.Out && !place.Outdoor ? DoorM : place.ReachM, out where);
    }

    const int Squad = 4;

    public static bool Where(CrowdTown town, CrowdResident r, IReadOnlyList<CrowdLeg> legs, Func<int, int, CrowdPath?> path, int second, double upm, out CrowdWhere where)
    {
        foreach (var span in Spans(town, r, legs, path, upm))
            if (second >= span.Start && second < span.End) return At(town, r, span, second, upm, out where);
        where = default;
        return false;
    }

    static bool Stay(CrowdPlace place, CrowdResident r, int s, double upm, int index, double reachM, out CrowdWhere where)
    {
        int seed = r.Entry.Seed;
        double a = s * MillRate + seed % 1000, b = s * MillRate * MillTurn + seed % 777, reach = reachM * upm * (0.3 + 0.7 * (seed % 97) / 97.0);
        var at = place.At + new Vec2(Math.Sin(a) * reach, Math.Cos(b) * reach);
        where = new CrowdWhere(at, Math.Atan2(Math.Cos(a), -MillTurn * Math.Sin(b)), s * MillRate * reach, true, index);
        return true;
    }
}
