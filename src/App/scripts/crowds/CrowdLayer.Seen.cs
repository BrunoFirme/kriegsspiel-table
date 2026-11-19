using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class CrowdLayer
{
    CrowdWatch? _watch;

    public CrowdWatch Watch => _watch ??= new CrowdWatch(_c, this);

    DayKind DayOf(CrowdTown roster, long day) =>
        _c.S.View.Crowds?.Towns?.GetValueOrDefault(roster.Thing)?.Kind ?? CrowdDay.KindOf(roster.People, _c.S.View.Clock.Cal.WeekDay(day * CrowdDay.Day));

    int Riders(Town t) => _c.S.View.Crowds?.Towns?.GetValueOrDefault(t.Roster.Thing)?.NoTraffic == true ? 0 : t.Shown.Count(s => s);

    public bool Lives(int ti, int i) => _towns[ti].Shown[i];

    public CrowdSeen? Observed(int ti, int i)
    {
        var t = _towns[ti];
        long now = _c.S.View.Clock.Seconds;
        if (!t.Shown[i] || FateAt(ti, i, now) is CrowdFates.Gone or CrowdFates.Kept or CrowdFates.Dead || _asPieces.Contains((ti, i))) return null;
        var r = t.Roster.Residents[i];
        float upm = (float)(_c.S.View.Calibration.UnitsPerMeter ?? 1);
        string? place = null;
        bool known = Ordered(ti, i, now, upm, out var w, out _, out _);
        if (!known && t.Stands is { } stands) (w, known) = (stands[i], true);
        if (!known && !Outdoors(t, r, i, now, upm, out w, out place)) return null;
        var e = r.Entry;
        return new CrowdSeen(CrowdPiece.Key(t.Roster.Thing, i), e.Name, e.Calling, e.Sex, e.Band, e.Wealth, t.Roster.Thing, place, w.At, w.Heading, !w.Moving);
    }

    bool Outdoors(Town t, CrowdResident r, int i, long now, float upm, out CrowdWhere w, out string? place)
    {
        (w, place) = (default, null);
        long day = (long)Math.Floor(now / (double)CrowdDay.Day);
        int second = (int)(now - day * CrowdDay.Day);
        var spans = day == _day ? t.Spans[i] ??= CrowdDay.Spans(t.Roster, r, t.Plans[i] ??= CrowdDay.Plan(t.Roster, r, t.Kind, day), t.PathOf, upm)
            : CrowdDay.Spans(t.Roster, r, DayPlan(t, r, day), t.PathOf, upm);
        foreach (var span in spans)
        {
            if (second < span.Start || second >= span.End) continue;
            place = span.Path == null ? t.Roster.Places[span.Place].Use.ToString().ToLowerInvariant() : null;
            return CrowdDay.At(t.Roster, r, span, second, upm, out w);
        }
        return false;
    }
}
