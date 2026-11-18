namespace KTable.Core.Model;

public enum CrowdStay : byte { Indoors, Milling, Standing }

public readonly record struct CrowdSpot(Vec2 At, CrowdStay Stay, double ReachM);

public sealed record CrowdPerson(int Seed, int Outfit, double HeightM, bool Child, CrowdSpot Home, CrowdSpot Work, CrowdSpot Third, int Leave, int Shift, int Out,
    double SpeedM);

public enum CrowdLeg : byte { ToWork, AtWork, ToThird, AtThird, ToHome }

public readonly record struct CrowdWhere(Vec2 At, double Heading, double StrideM, bool Moving, CrowdLeg Leg, double Along);

public static class CrowdDay
{
    public const int Day = CalendarDef.SecondsInDay;
    const double MillRate = 0.05, MillTurn = 0.9;

    public static bool Where(CrowdPerson p, long clock, double unitsPerMetre, out CrowdWhere where)
    {
        int s = (int)(((clock % Day) + Day) % Day);
        double upm = Math.Max(unitsPerMetre, 1e-9);
        double Walk(Vec2 a, Vec2 b) => (b - a).Length / upm / p.SpeedM;
        int legOne = (int)Walk(p.Home.At, p.Work.At), legTwo = (int)Walk(p.Work.At, p.Third.At), legThree = (int)Walk(p.Third.At, p.Home.At);
        int atWork = p.Leave + legOne, leaveWork = atWork + p.Shift, atThird = leaveWork + legTwo, leaveThird = atThird + p.Out, home = leaveThird + legThree;
        where = default;
        if (s < p.Leave || s >= home) return false;
        if (s < atWork) return Along(p.Home.At, p.Work.At, s - p.Leave, legOne, p.SpeedM * upm, CrowdLeg.ToWork, out where);
        if (s < leaveWork) return At(p.Work, p, s, upm, CrowdLeg.AtWork, out where);
        if (s < atThird) return Along(p.Work.At, p.Third.At, s - leaveWork, legTwo, p.SpeedM * upm, CrowdLeg.ToThird, out where);
        if (s < leaveThird) return At(p.Third, p, s, upm, CrowdLeg.AtThird, out where);
        return Along(p.Third.At, p.Home.At, s - leaveThird, legThree, p.SpeedM * upm, CrowdLeg.ToHome, out where);
    }

    static bool Along(Vec2 from, Vec2 to, int t, int length, double perSecond, CrowdLeg leg, out CrowdWhere where)
    {
        double k = length <= 0 ? 1 : Math.Clamp(t / (double)length, 0, 1);
        var d = to - from;
        where = new(new Vec2(from.X + d.X * k, from.Z + d.Z * k), Math.Atan2(d.X, d.Z), t * perSecond, true, leg, k);
        return true;
    }

    static bool At(CrowdSpot spot, CrowdPerson p, int s, double upm, CrowdLeg leg, out CrowdWhere where)
    {
        where = default;
        if (spot.Stay == CrowdStay.Indoors) return false;
        if (spot.Stay == CrowdStay.Standing)
        {
            where = new(spot.At, p.Seed % 628 / 100.0, 0, false, leg, 0);
            return true;
        }
        double a = s * MillRate + p.Seed % 1000, b = s * MillRate * MillTurn + p.Seed % 777;
        double r = spot.ReachM * upm;
        var at = spot.At + new Vec2(Math.Sin(a) * r, Math.Cos(b) * r);
        where = new(at, Math.Atan2(Math.Cos(a), -MillTurn * Math.Sin(b)), s * MillRate * spot.ReachM * upm, true, leg, 0);
        return true;
    }
}
