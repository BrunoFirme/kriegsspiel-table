using E = KTable.Core.Model.CrowdEntry;
using B = KTable.Core.Model.CrowdBaseline;

namespace KTable.Core.Model;

public readonly record struct CrowdLeg(int To, int Depart);

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

public readonly record struct CrowdSpan(int Start, int End, int Place, CrowdPath? Path, int To);

public readonly record struct CrowdWhere(Vec2 At, double Heading, double Stride, bool Moving, int Place, float Height = float.NaN, int To = -1);

public static class CrowdDay
{
    public const int Day = CalendarDef.SecondsInDay;
    const int Hour = CalendarDef.SecondsInHour, Least = 300, PlanStream = 31;
    const double MillRate = 0.05, MillTurn = 0.9, NearPick = 3, LateHour = 23;

    public static DayKind KindOf(Nationality flat, string? weekday) =>
        weekday != null && flat.Day?.HolyDay is { Length: > 0 } holy && weekday.Equals(holy, StringComparison.OrdinalIgnoreCase) ? DayKind.Holy
        : weekday != null && flat.Day?.MarketDays?.Any(d => d.Equals(weekday, StringComparison.OrdinalIgnoreCase)) == true ? DayKind.Market
        : DayKind.Working;

    public static List<CrowdLeg> Plan(CrowdTown town, CrowdResident r, DayKind kind, long dayNumber)
    {
        var dice = new Dice(unchecked(r.Entry.Seed ^ (int)dayNumber * 7919), PlanStream);
        var plan = new Planner(town, r, dice);
        var e = r.Entry;
        var day = town.People.Day ?? new DayRules();
        double holyAt = day.HolyAt ?? 9, dark = day.DarkBy ?? 21;
        if (kind == DayKind.Holy && !(e.Calling == B.Soldier && town.Places[r.Home].Use == CrowdUse.Barracks))
        {
            if (!dice.Chance(0.8) || plan.Near(CrowdUse.Worship) is not { } worship) return plan.Legs;
            plan.Go(worship, holyAt - 0.3, 0.1);
            if (dice.Chance(0.4) && (plan.Near(dice.Chance(0.5) ? CrowdUse.Square : CrowdUse.Tavern) ?? plan.Near(CrowdUse.Market)) is { } after) plan.Go(after, holyAt + 1.5, 0.2);
            plan.Go(r.Home, plan.Last + 1 + dice.Between(0, 1), 0);
            return plan.Legs;
        }
        bool market = kind == DayKind.Market;
        switch (e.Calling)
        {
            case B.Soldier when town.Places[r.Home].Use == CrowdUse.Barracks:
                if (dice.Chance(0.3) && (plan.Near(CrowdUse.Square) ?? plan.Near(CrowdUse.Market)) is { } post)
                {
                    double from = dice.Between(6, 18);
                    plan.Go(post, from, 0);
                    plan.Go(r.Home, from + 2, 0.2);
                }
                break;
            case B.Cleric when plan.Near(CrowdUse.Worship) is { } church:
                plan.Go(church, 7, 0.4);
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
                if (dice.Chance(0.3) && (plan.Near(CrowdUse.Square) ?? plan.Near(CrowdUse.Market)) is { } play)
                {
                    plan.Go(play, 9.5, 1);
                    plan.Go(r.Home, 12, 0.5);
                }
                break;
            default:
                if (r.Work >= 0 && e.Band is E.Adult or E.Youth) Workday(plan, r, day, market, dark, dice);
                else if (dice.Chance(0.3) && (plan.Near(dice.Chance(0.5) ? CrowdUse.Square : CrowdUse.Worship) ?? plan.Near(CrowdUse.Tavern)) is { } stroll)
                {
                    plan.Go(stroll, 10, 1);
                    plan.Go(r.Home, 12, 0.5);
                }
                break;
        }
        return plan.Legs;
    }

    static void Errands(Planner plan, CrowdResident r, bool market, Dice dice)
    {
        if (dice.Chance(market ? 0.45 : 0.25) && (market ? plan.Near(CrowdUse.Market) : plan.Near(dice.Chance(0.5) ? CrowdUse.Well : CrowdUse.Market)) is { } errand)
        {
            plan.Go(errand, market ? 9 : 9.5, 1);
            plan.Go(r.Home, plan.Last + (market ? 2 : 1), 0.5);
        }
        if (dice.Chance(0.15) && (plan.Near(CrowdUse.Well) ?? plan.Near(CrowdUse.Square)) is { } later)
        {
            plan.Go(later, 15, 0.5);
            plan.Go(r.Home, plan.Last + 1, 0.3);
        }
    }

    static void Workday(Planner plan, CrowdResident r, DayRules day, bool market, double dark, Dice dice)
    {
        bool farmer = r.Entry.Calling == B.Farmer;
        plan.Go(r.Work, farmer ? 5.5 : 7, 0.7);
        if (day.Rest is [var restFrom, var restTo, ..])
        {
            plan.Go(r.Home, restFrom, 0.2);
            plan.Go(r.Work, restTo, 0.2);
        }
        else if (farmer)
        {
            plan.Go(r.Home, 12, 0.3);
            plan.Go(r.Work, 13, 0.3);
        }
        if (r.Entry.Sex == E.Man && day.Prayers is { Count: > 0 } prayers && plan.Near(CrowdUse.Worship) is { } mosque)
            foreach (double p in prayers.Where(p => p > plan.Last + 0.5 && p < 17))
            {
                plan.Go(mosque, p, 0.1);
                plan.Go(r.Work, p + 0.5, 0.1);
            }
        plan.Go(r.Home, farmer ? 18.5 : 17.5, 0.8);
        if (r.Entry.Sex != E.Man) return;
        var after = market && dice.Chance(0.5) ? plan.Near(CrowdUse.Market) : dice.Chance(0.35) ? plan.Near(CrowdUse.Tavern) : null;
        if (after is not { } evening) return;
        plan.Go(evening, plan.Last + 0.5, 0.3);
        plan.Go(r.Home, Math.Min(Math.Max(plan.Last + 1, dark - 1), LateHour), 0.5);
    }

    sealed class Planner(CrowdTown town, CrowdResident r, Dice dice)
    {
        public readonly List<CrowdLeg> Legs = new();
        int _at = r.Home;
        public double Last { get; private set; }

        public int? Near(CrowdUse use)
        {
            var near = town.Nearest(r.Home, use, (int)NearPick);
            return near.Length == 0 ? null : near[dice.Below(near.Length)];
        }

        public void Go(int place, double hour, double play)
        {
            double when = Math.Max(hour + dice.Between(-play, play), Last + (double)Least / Hour);
            if (place == _at || when >= LateHour + 1) return;
            Legs.Add(new CrowdLeg(place, (int)(when * Hour)));
            (_at, Last) = (place, when);
        }
    }

    public static List<CrowdSpan> Spans(CrowdTown town, CrowdResident r, IReadOnlyList<CrowdLeg> legs, Func<int, int, CrowdPath?> path, double upm)
    {
        var spans = new List<CrowdSpan>();
        double speed = r.SpeedM * Math.Max(upm, 1e-9);
        int at = r.Home, since = 0;
        foreach (var leg in legs)
        {
            int depart = Math.Max(leg.Depart, since);
            if (leg.To == at || path(at, leg.To) is not { } route) continue;
            if (town.Places[at].Outdoor && depart > since) spans.Add(new CrowdSpan(since, depart, at, null, leg.To));
            int arrive = depart + (int)Math.Ceiling(route.Length / speed);
            spans.Add(new CrowdSpan(depart, arrive, -1, route, leg.To));
            (at, since) = (leg.To, arrive);
        }
        if (town.Places[at].Outdoor && since < Day) spans.Add(new CrowdSpan(since, Day, at, null, at));
        return spans;
    }

    public static bool At(CrowdTown town, CrowdResident r, in CrowdSpan span, int second, double upm, out CrowdWhere where)
    {
        if (span.Path is { } route)
        {
            double walked = Math.Min((second - span.Start) * r.SpeedM * Math.Max(upm, 1e-9), route.Length);
            var (point, heading) = route.At(walked);
            where = new CrowdWhere(point, heading, walked, true, -1, route.HeightAt(walked), span.To);
            return true;
        }
        return Stay(town.Places[span.Place], r, second, upm, span.Place, out where);
    }

    public static bool Where(CrowdTown town, CrowdResident r, IReadOnlyList<CrowdLeg> legs, Func<int, int, CrowdPath?> path, int second, double upm, out CrowdWhere where)
    {
        foreach (var span in Spans(town, r, legs, path, upm))
            if (second >= span.Start && second < span.End) return At(town, r, span, second, upm, out where);
        where = default;
        return false;
    }

    static bool Stay(CrowdPlace place, CrowdResident r, int s, double upm, int index, out CrowdWhere where)
    {
        where = default;
        if (!place.Outdoor) return false;
        int seed = r.Entry.Seed;
        double a = s * MillRate + seed % 1000, b = s * MillRate * MillTurn + seed % 777, reach = place.ReachM * upm * (0.3 + 0.7 * (seed % 97) / 97.0);
        var at = place.At + new Vec2(Math.Sin(a) * reach, Math.Cos(b) * reach);
        where = new CrowdWhere(at, Math.Atan2(Math.Cos(a), -MillTurn * Math.Sin(b)), s * MillRate * reach, true, index);
        return true;
    }
}
