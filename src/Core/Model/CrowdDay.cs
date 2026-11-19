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
    const double SwayRate = 0.02, SwayM = 0.12;
    const double MillRate = 0.05, MillTurn = 0.9, NearPick = 3, LateHour = 23, DoorM = 3, FileM = 1.4, LineM = 1.2, PrayersUntil = 17;

    public static DayKind KindOf(Nationality flat, string? weekday) =>
        weekday != null && flat.Day?.HolyDay is { Length: > 0 } holy && weekday.Equals(holy, StringComparison.OrdinalIgnoreCase) ? DayKind.Holy
        : weekday != null && flat.Day?.MarketDays?.Any(d => d.Equals(weekday, StringComparison.OrdinalIgnoreCase)) == true ? DayKind.Market
        : DayKind.Working;

    public static List<CrowdLeg> Plan(CrowdTown town, CrowdResident r, DayKind kind, long dayNumber)
    {
        if (r.Leader >= 0 && r.Leader < town.Residents.Count) return Plan(town, town.Residents[r.Leader], kind, dayNumber);
        if (kind == DayKind.Curfew && r.Entry.Calling != CrowdBaseline.Soldier) return [];
        var dice = new Dice(unchecked(r.Entry.Seed ^ (int)dayNumber * 7919), PlanStream);
        var plan = new Planner(town, r, dice);
        var day = town.People.Day ?? new DayRules();
        if (Timetable(day, r.Outsider ? $"{TimetableStop.Outsider}:" : "", r.Entry.Calling, kind) is { } stops) Follow(plan, town, r, day, stops, kind, dayNumber, dice);
        return plan.Legs;
    }

    static List<TimetableStop>? Timetable(DayRules day, string who, string calling, DayKind kind)
    {
        if (day.Timetables is not { } all) return null;
        string k = kind.ToString().ToLowerInvariant();
        return all.GetValueOrDefault($"{who}{calling}:{k}") ?? all.GetValueOrDefault($"{who}{TimetableStop.Anyone}:{k}") ?? all.GetValueOrDefault(who + calling)
            ?? all.GetValueOrDefault(who + TimetableStop.Anyone);
    }

    static bool Holds(string? when, CrowdResident r, DayRules day, DayKind kind)
    {
        if (string.IsNullOrEmpty(when)) return true;
        foreach (string term in when.Split(','))
        {
            bool not = term.StartsWith('!');
            bool yes = (not ? term[1..] : term) switch
            {
                TimetableStop.Work => r.Work >= 0,
                "man" => r.Entry.Sex == E.Man,
                "woman" => r.Entry.Sex == E.Woman,
                E.Child or E.Youth or E.Adult or E.Elder => r.Entry.Band == (not ? term[1..] : term),
                TimetableStop.Rest => day.Rest is { Count: >= 2 },
                TimetableStop.Prayers => day.Prayers is { Count: > 0 },
                "market" => kind == DayKind.Market,
                "holy" => kind == DayKind.Holy,
                _ => false,
            };
            if (yes == not) return false;
        }
        return true;
    }

    static void Follow(Planner plan, CrowdTown town, CrowdResident r, DayRules day, List<TimetableStop> stops, DayKind kind, long dayNumber, Dice dice)
    {
        bool done = true;
        foreach (var stop in stops)
        {
            bool after = done;
            done = false;
            if (stop.With && !after || !Holds(stop.If, r, day, kind) || stop.Chance > 0 && !dice.Chance(stop.Chance)) continue;
            string to = CrowdDress.Pick(dice, stop.To);
            double at = stop.From switch { TimetableStop.Dark => (day.DarkBy ?? 21) + stop.At, TimetableStop.Holy => (day.HolyAt ?? 9) + stop.At, _ => stop.At };
            if (stop.After > 0) at = Math.Max(at, plan.Last + stop.After);
            if (stop.From == TimetableStop.Dark) at = Math.Min(at, LateHour);
            done = to switch
            {
                TimetableStop.None => false,
                TimetableStop.Out => plan.Out(at, Math.Max(stop.Hours, 0.25)),
                TimetableStop.Rest => day.Rest is [var from, var until, ..] && plan.Go(r.Home, from, stop.Play) | plan.Back(until, stop.Play),
                TimetableStop.Prayers => Pray(plan, day.Prayers ?? []),
                TimetableStop.Watch => Watch(plan, town, r, dayNumber),
                _ => plan.Place(to) is { } place && plan.Go(place, at, stop.Play),
            };
        }
    }

    static bool Pray(Planner plan, List<double> prayers)
    {
        if (plan.Near(CrowdUse.Worship) is not { } mosque) return false;
        foreach (double p in prayers.Where(p => p > plan.Last + 0.5 && p < PrayersUntil))
        {
            plan.Go(mosque, p, 0.1);
            plan.Back(p + 0.5, 0.1);
        }
        return true;
    }

    static bool Watch(Planner plan, CrowdTown town, CrowdResident r, long dayNumber)
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
        return true;
    }

    sealed class Planner(CrowdTown town, CrowdResident r, Dice dice)
    {
        public readonly List<CrowdLeg> Legs = new();
        int _at = r.Home, _was = r.Home;
        bool _out;
        public double Last { get; private set; }

        public int? Near(CrowdUse use)
        {
            var near = town.Nearest(r.Home, use, (int)NearPick);
            return near.Length == 0 ? null : near[dice.Below(near.Length)];
        }

        public int? Place(string to) => to switch
        {
            TimetableStop.Home => r.Home,
            TimetableStop.Work => r.Work >= 0 ? r.Work : null,
            TimetableStop.Back => _was,
            TimetableStop.Away => r.Away >= 0 ? r.Away : null,
            TimetableStop.Shop => Near(CrowdUse.Work),
            _ => Enum.TryParse<CrowdUse>(to, true, out var use) ? Near(use) : null,
        };

        public bool Go(int place, double hour, double play, bool outside = false)
        {
            double when = Math.Max(hour + dice.Between(-play, play), Legs.Count == 0 ? 0 : Last + (double)Least / Hour);
            if (place == _at && outside == _out || when >= LateHour + 1) return false;
            Legs.Add(new CrowdLeg(place, (int)(when * Hour), outside));
            if (place != _at) _was = _at;
            (_at, _out, Last) = (place, outside, when);
            return true;
        }

        public bool Back(double hour, double play) => Go(_was, hour, play);

        public bool Out(double hour, double hours)
        {
            int at = _at;
            bool went = Go(at, hour, 0.3, true);
            Go(at, Last + hours, 0.2);
            return went;
        }
    }

    static bool Seen(CrowdTown town, CrowdResident r, int at, bool outside) => !(r.Outsider && (at == r.Home || at == r.Away)) && (town.Places[at].Outdoor || outside);

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
                if (Seen(town, r, at, outside) && depart > since) spans.Add(new CrowdSpan(since, depart, at, null, at, outside));
                (since, outside) = (depart, leg.Out);
                continue;
            }
            if (path(at, leg.To) is not { } route) continue;
            if (Seen(town, r, at, outside) && depart > since) spans.Add(new CrowdSpan(since, depart, at, null, leg.To, outside));
            int arrive = depart + (int)Math.Ceiling(route.Length / speed);
            spans.Add(new CrowdSpan(depart, arrive, -1, route, leg.To));
            (at, outside, since) = (leg.To, leg.Out, arrive);
        }
        if (Seen(town, r, at, outside) && since < Day) spans.Add(new CrowdSpan(since, Day, at, null, at, outside));
        return spans;
    }

    public static bool At(CrowdTown town, CrowdResident r, in CrowdSpan span, double second, double upm, out CrowdWhere where)
    {
        if (span.Path is { } route)
        {
            double walked = (second - span.Start) * r.SpeedM * Math.Max(upm, 1e-9), behind = r.File * FileM * upm, along = Math.Clamp(walked - behind, 0, route.Length);
            var (point, heading) = route.At(along);
            if (r.Entry.Calling != B.Soldier) point = CrowdSpots.Laned(point, heading, along, route.Length, r.Entry.Seed, upm);
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
        if (span.Out && !place.Outdoor) return Stay(place, r, second, upm, span.Place, DoorM, out where);
        var mine = CrowdSpots.For(town.SpotsOf(span.Place, upm), r.Entry.Id);
        double sway = second * SwayRate + r.Entry.Seed % 1000, face = Math.Atan2(place.At.X - mine.X, place.At.Z - mine.Z);
        var at = new Vec2(mine.X + Math.Sin(sway) * SwayM * upm, mine.Z + Math.Cos(sway * MillTurn) * SwayM * upm);
        where = new CrowdWhere(at, double.IsFinite(face) ? face + Math.Sin(sway * 0.5) * 0.6 : sway, 0, false, span.Place);
        return true;
    }

    const int Squad = 4;

    public static bool Where(CrowdTown town, CrowdResident r, IReadOnlyList<CrowdLeg> legs, Func<int, int, CrowdPath?> path, int second, double upm, out CrowdWhere where)
    {
        foreach (var span in Spans(town, r, legs, path, upm))
            if (second >= span.Start && second < span.End) return At(town, r, span, second, upm, out where);
        where = default;
        return false;
    }

    static bool Stay(CrowdPlace place, CrowdResident r, double s, double upm, int index, double reachM, out CrowdWhere where)
    {
        int seed = r.Entry.Seed;
        double a = s * MillRate + seed % 1000, b = s * MillRate * MillTurn + seed % 777, reach = reachM * upm * (0.3 + 0.7 * (seed % 97) / 97.0);
        var at = place.At + new Vec2(Math.Sin(a) * reach, Math.Cos(b) * reach);
        where = new CrowdWhere(at, Math.Atan2(Math.Cos(a), -MillTurn * Math.Sin(b)), s * MillRate * reach, true, index);
        return true;
    }
}
