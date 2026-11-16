namespace KTable.Core.Model;

public sealed record Wealth(Spot Core, Spot? Keep, double Within, int Seed, Spot? Stench = null, double? Base = null)
{
    public const double RichFrom = 0.68, PoorBelow = 0.3;
    const double CellM = 70, Play = 0.34, AtCore = 0.95, Fall = 0.78, KeepPull = 0.3, KeepReach = 0.45, Beyond = 1.6, StenchPull = 0.45, StenchReach = 0.35, WaterPull = 0.2, WaterNearM =
        45, HeightPull = 0.15, RiseM = 25;

    public double At(Spot p, BuiltSite site)
    {
        double from = Math.Clamp((p - Core).Length / Math.Max(Within, 1e-9), 0, Beyond), w = AtCore - from * Fall;
        if (Keep is { } keep) w += KeepPull * Math.Max(0, 1 - (p - keep).Length / (Within * KeepReach));
        if (Stench is { } stench) w -= StenchPull * Math.Max(0, 1 - (p - stench).Length / (Within * StenchReach));
        if (site.NearestWater(p) is var (_, reach) && reach < site.M(WaterNearM)) w -= WaterPull * (1 - reach / site.M(WaterNearM));
        if (Base is { } floor) w += HeightPull * Math.Clamp((site.HeightAt(p) - floor) / site.M(RiseM), -1, 1);
        var cell = new Spot(Math.Floor(p.X / site.M(CellM)), Math.Floor(p.Y / site.M(CellM))) * site.M(CellM);
        return Math.Clamp(w + (BuiltSite.Roll(cell, Seed).Next() - 0.5) * Play, 0, 1);
    }
}
